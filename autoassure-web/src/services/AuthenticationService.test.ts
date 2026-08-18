import type { Api } from "autoassure-server-sdk";
import { AxiosError, type AxiosResponse } from "axios";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { deepEqual, instance, mock, verify, when } from "ts-mockito";
import { ServiceError } from "../models/ServiceError";
import {
  AuthenticationService,
  deriveCodeChallenge,
  generateCodeVerifier,
  toBase64Url,
  type GoogleAuthConfig,
} from "./AuthenticationService";
import type { Clock } from "./Clock";

// Minimal in-memory Storage so tests don't depend on a browser/jsdom environment.
class InMemoryStorage implements Storage {
  private readonly data = new Map<string, string>();

  get length(): number {
    return this.data.size;
  }

  clear(): void {
    this.data.clear();
  }

  getItem(key: string): string | null {
    return this.data.get(key) ?? null;
  }

  key(index: number): string | null {
    return Array.from(this.data.keys())[index] ?? null;
  }

  removeItem(key: string): void {
    this.data.delete(key);
  }

  setItem(key: string, value: string): void {
    this.data.set(key, value);
  }
}

function fakeJwt(payload: Record<string, unknown>): string {
  const header = toBase64Url(
    new TextEncoder().encode(JSON.stringify({ alg: "none" })),
  );
  const body = toBase64Url(new TextEncoder().encode(JSON.stringify(payload)));
  return `${header}.${body}.`;
}

type AuthModule = Api<unknown>["auth"];
type GoogleTokenCreateResponse = Awaited<
  ReturnType<AuthModule["googleTokenCreate"]>
>;

describe("toBase64Url", () => {
  it.each([
    { bytes: [], expected: "" },
    { bytes: [0], expected: "AA" },
    // 255 -> std base64 "/w==": exercises '/' replacement and padding strip.
    { bytes: [255], expected: "_w" },
    // Chosen so the std base64 output is "+++/" -- exercises both '+' and '/'
    // replacement with no padding.
    { bytes: [251, 239, 190], expected: "----" },
  ])("encodes byte(s) $bytes as $expected", ({ bytes, expected }) => {
    expect(toBase64Url(new Uint8Array(bytes))).toBe(expected);
  });

  it("never emits standard-base64 padding or unsafe characters", () => {
    for (let length = 0; length <= 16; length++) {
      const bytes = Uint8Array.from({ length }, (_, i) => (i * 37) % 256);
      expect(toBase64Url(bytes)).not.toMatch(/[+/=]/);
    }
  });
});

describe("generateCodeVerifier", () => {
  it("returns a 43-character unpadded base64url string for 32 random bytes", () => {
    // 32 bytes -> 256 bits -> ceil(256/6) = 43 base64 chars once the '=' padding is stripped.
    expect(generateCodeVerifier()).toMatch(/^[A-Za-z0-9_-]{43}$/);
  });

  it("does not repeat across calls", () => {
    expect(generateCodeVerifier()).not.toBe(generateCodeVerifier());
  });
});

describe("deriveCodeChallenge", () => {
  it.each([
    // RFC 7636 A.2 example verifier; expected value computed independently via
    // Node's crypto.subtle to avoid trusting a copied constant.
    {
      name: "RFC 7636 example verifier",
      verifier: "dbjftaDrZs6q8vLE2waPB4WCVpX8FiJVFdc6M2FF5x8",
      expected: "BWmu6ej2abueHsDdMtkCrsZFbfDtbhLLBsprhRxAUMk",
    },
    // SHA-256 of the empty string is a well-known constant, useful as a sanity check.
    {
      name: "empty verifier",
      verifier: "",
      expected: "47DEQpj8HBSa-_TImW-5JCeuQeRkm5NMpJWZG3hSuFU",
    },
    // Non-ASCII input: confirms the verifier is UTF-8 encoded before hashing,
    // not treated as raw code units.
    {
      name: "non-ASCII verifier",
      verifier: "café-🎉",
      expected: "Le3cbOO5Cv2XAqYFG_r5JypcjCbmq4JXECe_j1fP_5s",
    },
  ])("hashes the $name correctly", async ({ verifier, expected }) => {
    await expect(deriveCodeChallenge(verifier)).resolves.toBe(expected);
  });

  it("is deterministic for the same verifier", async () => {
    const verifier = generateCodeVerifier();
    const [first, second] = await Promise.all([
      deriveCodeChallenge(verifier),
      deriveCodeChallenge(verifier),
    ]);
    expect(first).toBe(second);
  });
});

describe("AuthenticationService", () => {
  const googleAuthConfig: GoogleAuthConfig = {
    clientId: "test-client-id",
    redirectUri: "https://example.test/callback",
  };

  let localStorageStub: InMemoryStorage;
  let sessionStorageStub: InMemoryStorage;

  beforeEach(() => {
    // setup: a fresh, isolated Storage per test so no state leaks between tests.
    localStorageStub = new InMemoryStorage();
    sessionStorageStub = new InMemoryStorage();
    vi.stubGlobal("localStorage", localStorageStub);
    vi.stubGlobal("sessionStorage", sessionStorageStub);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function newAuthenticationService(clock: Clock, client: Api<unknown>) {
    return new AuthenticationService(client, clock, googleAuthConfig);
  }

  describe("loading a session from storage", () => {
    it("restores a logged-in session that was persisted by a previous instance", () => {
      // setup: a previous instance's session, as it would be left in storage.
      const jwt = fakeJwt({ sub: "user-1", email: "user1@example.test" });
      localStorageStub.setItem("auth.jwt", jwt);
      localStorageStub.setItem("auth.issuedAt", "1700000000");

      // act: construct a new instance, simulating a page reload.
      const service = newAuthenticationService(
        instance(mock<Clock>()),
        instance(mock<Api<unknown>>()),
      );

      // verify
      expect(service.isLoggedIn()).toBe(true);
      expect(service.getUserInfo()).toEqual({
        id: "user-1",
        email: "user1@example.test",
      });
    });

    it.each([
      { name: "storage is empty" },
      { name: "only the JWT is present", jwt: "some.jwt.token" },
      { name: "only the issued-at timestamp is present", issuedAt: "1700000000" },
      {
        name: "the issued-at timestamp is corrupted",
        jwt: "some.jwt.token",
        issuedAt: "not-a-number",
      },
    ])("treats the session as logged out when $name", ({ jwt, issuedAt }) => {
      // setup
      if (jwt !== undefined) {
        localStorageStub.setItem("auth.jwt", jwt);
      }
      if (issuedAt !== undefined) {
        localStorageStub.setItem("auth.issuedAt", issuedAt);
      }

      // act
      const service = newAuthenticationService(
        instance(mock<Clock>()),
        instance(mock<Api<unknown>>()),
      );

      // verify
      expect(service.isLoggedIn()).toBe(false);
      expect(service.getUserInfo()).toBeNull();
    });
  });

  describe("starting a Google login", () => {
    it("stashes a PKCE code verifier and redirects to Google with a matching challenge", async () => {
      // setup
      const assign = vi.fn();
      vi.stubGlobal("window", { location: { assign } });
      const service = newAuthenticationService(
        instance(mock<Clock>()),
        instance(mock<Api<unknown>>()),
      );

      // act
      await service.startGoogleLogin();

      // verify
      expect(assign).toHaveBeenCalledOnce();
      const url = new URL(assign.mock.calls[0]?.[0] as string);
      expect(url.origin + url.pathname).toBe(
        "https://accounts.google.com/o/oauth2/v2/auth",
      );
      expect(url.searchParams.get("client_id")).toBe(
        googleAuthConfig.clientId,
      );
      expect(url.searchParams.get("redirect_uri")).toBe(
        googleAuthConfig.redirectUri,
      );
      expect(url.searchParams.get("response_type")).toBe("code");
      expect(url.searchParams.get("code_challenge_method")).toBe("S256");

      const codeVerifier = sessionStorageStub.getItem(
        "auth.googleCodeVerifier",
      );
      expect(codeVerifier).not.toBeNull();
      expect(url.searchParams.get("code_challenge")).toBe(
        await deriveCodeChallenge(codeVerifier as string),
      );
    });
  });

  describe("completing a Google login", () => {
    it("saves the session to storage, consumes the code verifier, and updates in-memory state", async () => {
      // setup
      sessionStorageStub.setItem("auth.googleCodeVerifier", "verifier-abc");
      const jwt = fakeJwt({ sub: "user-2", email: "user2@example.test" });

      const mockedAuthModule = mock<AuthModule>();
      when(
        mockedAuthModule.googleTokenCreate(
          deepEqual({ code: "auth-code-123", codeVerifier: "verifier-abc" }),
        ),
      ).thenResolve({
        data: { token: jwt },
      } as GoogleTokenCreateResponse);
      const mockedClient = mock<Api<unknown>>();
      when(mockedClient.auth).thenReturn(instance(mockedAuthModule));

      const mockedClock = mock<Clock>();
      when(mockedClock.now()).thenReturn(1_800_000_000);

      const service = newAuthenticationService(
        instance(mockedClock),
        instance(mockedClient),
      );

      // act
      await service.completeGoogleLogin("auth-code-123");

      // verify: black-box via a fresh instance reading back what was persisted,
      // rather than reaching into AuthenticationService's private state.
      const reloaded = newAuthenticationService(
        instance(mock<Clock>()),
        instance(mock<Api<unknown>>()),
      );
      expect(reloaded.isLoggedIn()).toBe(true);
      expect(reloaded.getUserInfo()).toEqual({
        id: "user-2",
        email: "user2@example.test",
      });
      expect(localStorageStub.getItem("auth.issuedAt")).toBe("1800000000");

      // The verifier is single-use and tied to this attempt: it must be sent
      // to the backend, and cleared so a replayed callback can't reuse it.
      verify(
        mockedAuthModule.googleTokenCreate(
          deepEqual({ code: "auth-code-123", codeVerifier: "verifier-abc" }),
        ),
      ).once();
      expect(sessionStorageStub.getItem("auth.googleCodeVerifier")).toBeNull();
    });

    it("throws a classified ServiceError when the backend rejects the code", async () => {
      // setup
      sessionStorageStub.setItem("auth.googleCodeVerifier", "verifier-abc");
      const mockedAuthModule = mock<AuthModule>();
      const axiosError = new AxiosError(
        "Request failed with status code 400",
        "ERR_BAD_REQUEST",
        undefined,
        undefined,
        { status: 400 } as AxiosResponse,
      );
      when(
        mockedAuthModule.googleTokenCreate(
          deepEqual({ code: "auth-code-123", codeVerifier: "verifier-abc" }),
        ),
      ).thenReject(axiosError);
      const mockedClient = mock<Api<unknown>>();
      when(mockedClient.auth).thenReturn(instance(mockedAuthModule));

      const service = newAuthenticationService(
        instance(mock<Clock>()),
        instance(mockedClient),
      );

      // act & verify
      const error = await service
        .completeGoogleLogin("auth-code-123")
        .catch((caught: unknown) => caught);
      expect(error).toBeInstanceOf(ServiceError);
      expect((error as ServiceError).kind).toBe("client");
      expect(service.isLoggedIn()).toBe(false);
    });

    it("throws and does not touch storage when no login was started", async () => {
      // setup: no code verifier stashed in sessionStorage.
      const service = newAuthenticationService(
        instance(mock<Clock>()),
        instance(mock<Api<unknown>>()),
      );

      // act & verify
      await expect(service.completeGoogleLogin("auth-code-123")).rejects.toThrow();
      expect(service.isLoggedIn()).toBe(false);
      expect(localStorageStub.getItem("auth.jwt")).toBeNull();
    });
  });

  describe("logging out", () => {
    it("clears the persisted session and in-memory state", () => {
      // setup: a logged-in session, both persisted and reflected in-memory.
      const jwt = fakeJwt({ sub: "user-3", email: "user3@example.test" });
      localStorageStub.setItem("auth.jwt", jwt);
      localStorageStub.setItem("auth.issuedAt", "1700000000");
      const service = newAuthenticationService(
        instance(mock<Clock>()),
        instance(mock<Api<unknown>>()),
      );
      expect(service.isLoggedIn()).toBe(true);

      // act
      service.logout();

      // verify
      expect(service.isLoggedIn()).toBe(false);
      expect(localStorageStub.getItem("auth.jwt")).toBeNull();
      expect(localStorageStub.getItem("auth.issuedAt")).toBeNull();
    });
  });
});

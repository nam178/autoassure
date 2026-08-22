import type { Api } from "autoassure-server-sdk";
import { AxiosError, type AxiosResponse } from "axios";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { deepEqual, instance, mock, verify, when } from "ts-mockito";
import {
  AUTH_EXPIRY_SAFETY_MARGIN_SECONDS,
  AUTH_REFRESH_THRESHOLD_SECONDS,
} from "../common/Config";
import { ServiceError } from "../models/ServiceError";
import {
  AuthenticationService,
  deriveCodeChallenge,
  generateCodeVerifier,
  toBase64Url,
  type GoogleAuthConfig,
} from "./AuthenticationService";
import type { Clock } from "./Clock";
import type { Scheduler } from "./Scheduler";

// Captures the callback passed to `setInterval` so tests can trigger a check
// tick deterministically instead of depending on real timers.
class FakeScheduler implements Scheduler {
  private callback: (() => void) | null = null;

  setInterval(callback: () => void): number {
    this.callback = callback;
    return 1;
  }

  clearInterval(): void {
    this.callback = null;
  }

  // Fires the registered tick without waiting for any refresh it kicks off to settle.
  trigger(): void {
    this.callback?.();
  }

  // Fires a tick and waits for any refresh it kicks off to settle.
  async tick(): Promise<void> {
    this.trigger();
    // The tick's refresh call is fire-and-forget from the scheduler's
    // perspective; flush microtasks so the resulting promise resolves.
    await Promise.resolve();
    await Promise.resolve();
  }
}

const googleUser = {
  id: "google-user-1",
  firstName: "Ada",
  lastName: "Lovelace",
  email: "user@example.test",
  emailVerified: true,
};

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

// A 401 the backend would return for a refresh token it no longer accepts
// (expired/revoked) — a "client" ServiceError, distinct from a transient
// network/server failure.
function unauthorizedError(): AxiosError {
  return new AxiosError(
    "Request failed with status code 401",
    "ERR_BAD_REQUEST",
    undefined,
    undefined,
    { status: 401 } as AxiosResponse,
  );
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
type RefreshCreateResponse = Awaited<ReturnType<AuthModule["refreshCreate"]>>;

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

  function newAuthenticationService(
    clock: Clock,
    client: Api<unknown>,
    scheduler: Scheduler = new FakeScheduler(),
  ) {
    const service = new AuthenticationService(
      client,
      clock,
      scheduler,
      googleAuthConfig,
    );
    // Mirrors the provider's useEffect: the background check only runs once started.
    service.start();
    return service;
  }

  // Persists a full, valid session as a previous instance would have left it.
  function storeFullSession({
    jwt,
    issuedAt = "1700000000",
    expiresAt = "1700003600",
    refreshTokenSecret = "refresh-secret",
  }: {
    jwt: string;
    issuedAt?: string;
    expiresAt?: string;
    refreshTokenSecret?: string;
  }) {
    localStorageStub.setItem("auth.jwt", jwt);
    localStorageStub.setItem("auth.issuedAt", issuedAt);
    localStorageStub.setItem("auth.expiresAt", expiresAt);
    localStorageStub.setItem("auth.refreshTokenSecret", refreshTokenSecret);
  }

  describe("loading a session from storage", () => {
    it("restores a logged-in session that was persisted by a previous instance", () => {
      // setup: a previous instance's session, as it would be left in storage.
      const jwt = fakeJwt({ sub: "user-1", email: "user1@example.test" });
      storeFullSession({ jwt });

      // act: construct a new instance, simulating a page reload.
      const service = newAuthenticationService(
        instance(mock<Clock>()),
        instance(mock<Api<unknown>>()),
      );

      // verify
      expect(service.isLoggedIn()).toBe(true);
      expect(service.isSessionExpired()).toBe(false);
      expect(service.getUserInfo()).toEqual({
        id: "user-1",
        email: "user1@example.test",
      });
    });

    it.each([
      { name: "storage is empty" },
      { name: "only the JWT is present", jwt: "some.jwt.token" },
      { name: "the issued-at timestamp is missing", jwt: "some.jwt.token", expiresAt: "1700003600", refreshTokenSecret: "secret" },
      { name: "the expires-at timestamp is missing", jwt: "some.jwt.token", issuedAt: "1700000000", refreshTokenSecret: "secret" },
      { name: "the refresh token secret is missing", jwt: "some.jwt.token", issuedAt: "1700000000", expiresAt: "1700003600" },
      {
        name: "the issued-at timestamp is corrupted",
        jwt: "some.jwt.token",
        issuedAt: "not-a-number",
        expiresAt: "1700003600",
        refreshTokenSecret: "secret",
      },
      {
        name: "the expires-at timestamp is corrupted",
        jwt: "some.jwt.token",
        issuedAt: "1700000000",
        expiresAt: "not-a-number",
        refreshTokenSecret: "secret",
      },
    ])(
      "treats the session as logged out when $name",
      ({ jwt, issuedAt, expiresAt, refreshTokenSecret }) => {
        // setup
        if (jwt !== undefined) {
          localStorageStub.setItem("auth.jwt", jwt);
        }
        if (issuedAt !== undefined) {
          localStorageStub.setItem("auth.issuedAt", issuedAt);
        }
        if (expiresAt !== undefined) {
          localStorageStub.setItem("auth.expiresAt", expiresAt);
        }
        if (refreshTokenSecret !== undefined) {
          localStorageStub.setItem("auth.refreshTokenSecret", refreshTokenSecret);
        }

        // act
        const service = newAuthenticationService(
          instance(mock<Clock>()),
          instance(mock<Api<unknown>>()),
        );

        // verify
        expect(service.isLoggedIn()).toBe(false);
        expect(service.getUserInfo()).toBeNull();
      },
    );
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
        data: {
          token: jwt,
          expiresInSeconds: 3600,
          refreshTokenSecret: "refresh-secret-1",
          user: googleUser,
        },
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
      expect(reloaded.isSessionExpired()).toBe(false);
      expect(reloaded.getUserInfo()).toEqual({
        id: "user-2",
        email: "user2@example.test",
      });
      expect(localStorageStub.getItem("auth.issuedAt")).toBe("1800000000");
      // expiresAt is anchored to this client's issuedAt + the server's
      // relative duration, not to any server-side timestamp.
      expect(localStorageStub.getItem("auth.expiresAt")).toBe("1800003600");
      expect(localStorageStub.getItem("auth.refreshTokenSecret")).toBe(
        "refresh-secret-1",
      );

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
      storeFullSession({ jwt });
      const service = newAuthenticationService(
        instance(mock<Clock>()),
        instance(mock<Api<unknown>>()),
      );
      expect(service.isLoggedIn()).toBe(true);

      // act
      service.logout();

      // verify
      expect(service.isLoggedIn()).toBe(false);
      expect(service.isSessionExpired()).toBe(false);
      expect(localStorageStub.getItem("auth.jwt")).toBeNull();
      expect(localStorageStub.getItem("auth.issuedAt")).toBeNull();
      expect(localStorageStub.getItem("auth.expiresAt")).toBeNull();
      expect(localStorageStub.getItem("auth.refreshTokenSecret")).toBeNull();
    });

    it("resets a session-expired flag", async () => {
      // setup: a session that has already failed a refresh.
      const now = 1_800_000_000;
      const jwt = fakeJwt({ sub: "user-3b", email: "user3b@example.test" });
      storeFullSession({
        jwt,
        issuedAt: String(now - 3600),
        expiresAt: String(now + AUTH_REFRESH_THRESHOLD_SECONDS - 1),
        refreshTokenSecret: "secret",
      });
      const mockedAuthModule = mock<AuthModule>();
      when(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "secret" })),
      ).thenReject(unauthorizedError());
      const mockedClient = mock<Api<unknown>>();
      when(mockedClient.auth).thenReturn(instance(mockedAuthModule));
      const mockedClock = mock<Clock>();
      when(mockedClock.now()).thenReturn(now);

      const scheduler = new FakeScheduler();
      const service = newAuthenticationService(
        instance(mockedClock),
        instance(mockedClient),
        scheduler,
      );
      await scheduler.tick();
      expect(service.isSessionExpired()).toBe(true);

      // act
      service.logout();

      // verify
      expect(service.isSessionExpired()).toBe(false);
    });
  });

  describe("auto-refreshing the session", () => {
    it("stops checking for a due refresh once disposed", async () => {
      // setup: a stored session already due for refresh.
      const now = 1_800_000_000;
      storeFullSession({
        jwt: fakeJwt({ sub: "user-3c", email: "user3c@example.test" }),
        issuedAt: String(now - 3600),
        expiresAt: String(now + AUTH_REFRESH_THRESHOLD_SECONDS - 1),
        refreshTokenSecret: "secret",
      });
      const mockedAuthModule = mock<AuthModule>();
      const mockedClient = mock<Api<unknown>>();
      when(mockedClient.auth).thenReturn(instance(mockedAuthModule));
      const mockedClock = mock<Clock>();
      when(mockedClock.now()).thenReturn(now);

      const scheduler = new FakeScheduler();
      const service = newAuthenticationService(
        instance(mockedClock),
        instance(mockedClient),
        scheduler,
      );

      // act: dispose before the scheduler ever fires -- mirrors the provider
      // unmounting (e.g. React StrictMode's mount/cleanup/remount in dev).
      service.dispose();
      await scheduler.tick();

      // verify: no refresh was attempted, since the check was torn down.
      verify(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "secret" })),
      ).never();
    });

    it("refreshes the token once it is within the threshold of expiring", async () => {
      // setup: a stored session already due for refresh.
      const now = 1_800_000_000;
      const jwt = fakeJwt({ sub: "user-4", email: "user4@example.test" });
      storeFullSession({
        jwt,
        issuedAt: String(now - 3600),
        expiresAt: String(now + AUTH_REFRESH_THRESHOLD_SECONDS - 1),
        refreshTokenSecret: "old-secret",
      });

      const newJwt = fakeJwt({ sub: "user-4", email: "user4@example.test" });
      const mockedAuthModule = mock<AuthModule>();
      when(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "old-secret" })),
      ).thenResolve({
        data: {
          token: newJwt,
          expiresInSeconds: 3600,
          refreshTokenSecret: "new-secret",
        },
      } as RefreshCreateResponse);
      const mockedClient = mock<Api<unknown>>();
      when(mockedClient.auth).thenReturn(instance(mockedAuthModule));
      const mockedClock = mock<Clock>();
      when(mockedClock.now()).thenReturn(now);

      const scheduler = new FakeScheduler();
      const service = newAuthenticationService(
        instance(mockedClock),
        instance(mockedClient),
        scheduler,
      );

      // act
      await scheduler.tick();

      // verify
      verify(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "old-secret" })),
      ).once();
      expect(service.isLoggedIn()).toBe(true);
      expect(service.isSessionExpired()).toBe(false);
      expect(localStorageStub.getItem("auth.jwt")).toBe(newJwt);
      expect(localStorageStub.getItem("auth.refreshTokenSecret")).toBe(
        "new-secret",
      );
      // expiresAt is anchored to this client's clock at refresh time (`now`)
      // plus the server's relative duration.
      expect(localStorageStub.getItem("auth.expiresAt")).toBe(
        String(now + 3600),
      );
    });

    it("does not refresh when the token is not yet near expiry", async () => {
      // setup
      const now = 1_800_000_000;
      storeFullSession({
        jwt: fakeJwt({ sub: "user-5", email: "user5@example.test" }),
        issuedAt: String(now - 60),
        expiresAt: String(now + AUTH_REFRESH_THRESHOLD_SECONDS + 3600),
        refreshTokenSecret: "secret",
      });
      const mockedAuthModule = mock<AuthModule>();
      const mockedClient = mock<Api<unknown>>();
      when(mockedClient.auth).thenReturn(instance(mockedAuthModule));
      const mockedClock = mock<Clock>();
      when(mockedClock.now()).thenReturn(now);

      const scheduler = new FakeScheduler();
      const service = newAuthenticationService(
        instance(mockedClock),
        instance(mockedClient),
        scheduler,
      );

      // act
      await scheduler.tick();

      // verify
      verify(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "secret" })),
      ).never();
      expect(service.isSessionExpired()).toBe(false);
    });

    it("does not start a second refresh while one is already in flight", async () => {
      // setup: a refresh request that won't settle until we say so.
      const now = 1_800_000_000;
      storeFullSession({
        jwt: fakeJwt({ sub: "user-6", email: "user6@example.test" }),
        issuedAt: String(now - 3600),
        expiresAt: String(now + AUTH_REFRESH_THRESHOLD_SECONDS - 1),
        refreshTokenSecret: "secret",
      });
      let resolveRefresh: ((value: RefreshCreateResponse) => void) | undefined;
      const refreshPromise = new Promise<RefreshCreateResponse>((resolve) => {
        resolveRefresh = resolve;
      });
      const mockedAuthModule = mock<AuthModule>();
      when(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "secret" })),
      ).thenReturn(refreshPromise);
      const mockedClient = mock<Api<unknown>>();
      when(mockedClient.auth).thenReturn(instance(mockedAuthModule));
      const mockedClock = mock<Clock>();
      when(mockedClock.now()).thenReturn(now);

      const scheduler = new FakeScheduler();
      newAuthenticationService(
        instance(mockedClock),
        instance(mockedClient),
        scheduler,
      );

      // act: a second tick fires while the first refresh is still in flight.
      scheduler.trigger();
      scheduler.trigger();
      resolveRefresh?.({
        data: {
          token: fakeJwt({ sub: "user-6", email: "user6@example.test" }),
          expiresInSeconds: 3600,
          refreshTokenSecret: "new-secret",
        },
      } as RefreshCreateResponse);
      await refreshPromise;
      await Promise.resolve();

      // verify
      verify(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "secret" })),
      ).once();
    });

    it("marks the session expired without logging the user out when the backend rejects the refresh token", async () => {
      // setup: the backend responds 401, meaning the refresh token itself is no longer valid.
      const now = 1_800_000_000;
      const jwt = fakeJwt({ sub: "user-7", email: "user7@example.test" });
      storeFullSession({
        jwt,
        issuedAt: String(now - 3600),
        expiresAt: String(now + AUTH_REFRESH_THRESHOLD_SECONDS - 1),
        refreshTokenSecret: "secret",
      });
      const mockedAuthModule = mock<AuthModule>();
      when(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "secret" })),
      ).thenReject(unauthorizedError());
      const mockedClient = mock<Api<unknown>>();
      when(mockedClient.auth).thenReturn(instance(mockedAuthModule));
      const mockedClock = mock<Clock>();
      when(mockedClock.now()).thenReturn(now);

      const scheduler = new FakeScheduler();
      const service = newAuthenticationService(
        instance(mockedClock),
        instance(mockedClient),
        scheduler,
      );

      // act
      await scheduler.tick();

      // verify: the stale session is kept (so the UI doesn't unmount) but flagged expired.
      expect(service.isSessionExpired()).toBe(true);
      expect(service.isLoggedIn()).toBe(true);
      expect(service.getUserInfo()).toEqual({
        id: "user-7",
        email: "user7@example.test",
      });
      expect(localStorageStub.getItem("auth.jwt")).toBe(jwt);
    });

    it("does not retry an already-expired session on the next tick", async () => {
      // setup
      const now = 1_800_000_000;
      storeFullSession({
        jwt: fakeJwt({ sub: "user-7b", email: "user7b@example.test" }),
        issuedAt: String(now - 3600),
        expiresAt: String(now + AUTH_REFRESH_THRESHOLD_SECONDS - 1),
        refreshTokenSecret: "secret",
      });
      const mockedAuthModule = mock<AuthModule>();
      when(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "secret" })),
      ).thenReject(unauthorizedError());
      const mockedClient = mock<Api<unknown>>();
      when(mockedClient.auth).thenReturn(instance(mockedAuthModule));
      const mockedClock = mock<Clock>();
      when(mockedClock.now()).thenReturn(now);

      const scheduler = new FakeScheduler();
      newAuthenticationService(
        instance(mockedClock),
        instance(mockedClient),
        scheduler,
      );
      await scheduler.tick();

      // act: a second tick, still past the threshold.
      await scheduler.tick();

      // verify: only the first tick attempted a refresh.
      verify(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "secret" })),
      ).once();
    });

    it("retries on the next tick, without flagging the session expired, when the failure is transient", async () => {
      // setup: the refresh call fails with a plain (non-HTTP) error, e.g. offline
      // -- classified as "network" by ServiceError, not the refresh token being rejected.
      const now = 1_800_000_000;
      storeFullSession({
        jwt: fakeJwt({ sub: "user-7c", email: "user7c@example.test" }),
        issuedAt: String(now - 3600),
        expiresAt: String(now + AUTH_REFRESH_THRESHOLD_SECONDS - 1),
        refreshTokenSecret: "secret",
      });
      const mockedAuthModule = mock<AuthModule>();
      when(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "secret" })),
      ).thenReject(new Error("network down"));
      const mockedClient = mock<Api<unknown>>();
      when(mockedClient.auth).thenReturn(instance(mockedAuthModule));
      const mockedClock = mock<Clock>();
      when(mockedClock.now()).thenReturn(now);

      const scheduler = new FakeScheduler();
      const service = newAuthenticationService(
        instance(mockedClock),
        instance(mockedClient),
        scheduler,
      );

      // act: two ticks, both hitting the same transient failure.
      await scheduler.tick();
      expect(service.isSessionExpired()).toBe(false);
      await scheduler.tick();

      // verify: neither tick gave up on the session, and both retried the refresh.
      expect(service.isSessionExpired()).toBe(false);
      expect(service.isLoggedIn()).toBe(true);
      verify(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "secret" })),
      ).times(2);
    });

    it("does not resurrect the session if the user logs out while a refresh is in flight", async () => {
      // setup: a refresh request that won't settle until we say so.
      const now = 1_800_000_000;
      storeFullSession({
        jwt: fakeJwt({ sub: "user-6b", email: "user6b@example.test" }),
        issuedAt: String(now - 3600),
        expiresAt: String(now + AUTH_REFRESH_THRESHOLD_SECONDS - 1),
        refreshTokenSecret: "secret",
      });
      let resolveRefresh: ((value: RefreshCreateResponse) => void) | undefined;
      const refreshPromise = new Promise<RefreshCreateResponse>((resolve) => {
        resolveRefresh = resolve;
      });
      const mockedAuthModule = mock<AuthModule>();
      when(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "secret" })),
      ).thenReturn(refreshPromise);
      const mockedClient = mock<Api<unknown>>();
      when(mockedClient.auth).thenReturn(instance(mockedAuthModule));
      const mockedClock = mock<Clock>();
      when(mockedClock.now()).thenReturn(now);

      const scheduler = new FakeScheduler();
      const service = newAuthenticationService(
        instance(mockedClock),
        instance(mockedClient),
        scheduler,
      );

      // act: a check tick kicks off a refresh, the user logs out before it
      // settles, then the stale refresh finally resolves.
      scheduler.trigger();
      service.logout();
      resolveRefresh?.({
        data: {
          token: fakeJwt({ sub: "user-6b", email: "user6b@example.test" }),
          expiresInSeconds: 3600,
          refreshTokenSecret: "new-secret",
        },
      } as RefreshCreateResponse);
      await refreshPromise;
      await Promise.resolve();

      // verify: the logout sticks -- the late refresh response must not
      // resurrect the session.
      expect(service.isLoggedIn()).toBe(false);
      expect(localStorageStub.getItem("auth.jwt")).toBeNull();
      expect(localStorageStub.getItem("auth.refreshTokenSecret")).toBeNull();
    });

    it("does not flag the session expired if the user logs out while a failing refresh is in flight", async () => {
      // setup: a refresh request that won't settle until we say so, and will
      // then reject as the refresh token being rejected (401).
      const now = 1_800_000_000;
      storeFullSession({
        jwt: fakeJwt({ sub: "user-6c", email: "user6c@example.test" }),
        issuedAt: String(now - 3600),
        expiresAt: String(now + AUTH_REFRESH_THRESHOLD_SECONDS - 1),
        refreshTokenSecret: "secret",
      });
      let rejectRefresh: ((error: unknown) => void) | undefined;
      const refreshPromise = new Promise<RefreshCreateResponse>((_, reject) => {
        rejectRefresh = reject;
      });
      const mockedAuthModule = mock<AuthModule>();
      when(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "secret" })),
      ).thenReturn(refreshPromise);
      const mockedClient = mock<Api<unknown>>();
      when(mockedClient.auth).thenReturn(instance(mockedAuthModule));
      const mockedClock = mock<Clock>();
      when(mockedClock.now()).thenReturn(now);

      const scheduler = new FakeScheduler();
      const service = newAuthenticationService(
        instance(mockedClock),
        instance(mockedClient),
        scheduler,
      );

      // act: a check tick kicks off a refresh, the user logs out before it
      // settles, then the stale refresh finally rejects.
      scheduler.trigger();
      service.logout();
      rejectRefresh?.(unauthorizedError());
      await refreshPromise.catch(() => undefined);
      await Promise.resolve();

      // verify: the logout sticks -- the late failure must not resurrect the
      // session just to flag it expired.
      expect(service.isLoggedIn()).toBe(false);
      expect(service.isSessionExpired()).toBe(false);
    });

    it("marks the session expired on a transient failure once the token is within the safety margin of actually expiring", async () => {
      // setup: the token's real deadline is now closer than
      // AUTH_EXPIRY_SAFETY_MARGIN_SECONDS -- even though this refresh failure
      // is transient, there's no time left for a retry to land.
      const now = 1_800_000_000;
      const jwt = fakeJwt({ sub: "user-7d", email: "user7d@example.test" });
      storeFullSession({
        jwt,
        issuedAt: String(now - 3600),
        expiresAt: String(now + AUTH_EXPIRY_SAFETY_MARGIN_SECONDS - 1),
        refreshTokenSecret: "secret",
      });
      const mockedAuthModule = mock<AuthModule>();
      when(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "secret" })),
      ).thenReject(new Error("network down"));
      const mockedClient = mock<Api<unknown>>();
      when(mockedClient.auth).thenReturn(instance(mockedAuthModule));
      const mockedClock = mock<Clock>();
      when(mockedClock.now()).thenReturn(now);

      const scheduler = new FakeScheduler();
      const service = newAuthenticationService(
        instance(mockedClock),
        instance(mockedClient),
        scheduler,
      );

      // act
      await scheduler.tick();

      // verify: flagged expired despite the failure being transient, and the
      // stale session (not a hard logout) is kept, same as a rejected refresh token.
      expect(service.isSessionExpired()).toBe(true);
      expect(service.isLoggedIn()).toBe(true);
      expect(localStorageStub.getItem("auth.jwt")).toBe(jwt);
    });
  });

  describe("the login -> session-expired -> login-again lifecycle", () => {
    it("resets isSessionExpired once the user logs in again after a failed refresh", async () => {
      // setup: an in-flight login.
      sessionStorageStub.setItem("auth.googleCodeVerifier", "verifier-1");
      const firstJwt = fakeJwt({ sub: "user-8", email: "user8@example.test" });
      const mockedAuthModule = mock<AuthModule>();
      when(
        mockedAuthModule.googleTokenCreate(
          deepEqual({ code: "auth-code-1", codeVerifier: "verifier-1" }),
        ),
      ).thenResolve({
        data: {
          token: firstJwt,
          // Already within the refresh threshold at issuance, so the very
          // next check tick is due to attempt a refresh.
          expiresInSeconds: AUTH_REFRESH_THRESHOLD_SECONDS - 1,
          refreshTokenSecret: "secret-1",
          user: googleUser,
        },
      } as GoogleTokenCreateResponse);
      when(
        mockedAuthModule.refreshCreate(deepEqual({ refreshTokenSecret: "secret-1" })),
      ).thenReject(unauthorizedError());
      const mockedClient = mock<Api<unknown>>();
      when(mockedClient.auth).thenReturn(instance(mockedAuthModule));

      const now = 1_800_000_000;
      const mockedClock = mock<Clock>();
      when(mockedClock.now()).thenReturn(now);

      const scheduler = new FakeScheduler();
      const service = newAuthenticationService(
        instance(mockedClock),
        instance(mockedClient),
        scheduler,
      );

      // act: log in.
      await service.completeGoogleLogin("auth-code-1");
      expect(service.isLoggedIn()).toBe(true);
      expect(service.isSessionExpired()).toBe(false);

      // act: the background check runs and the refresh fails.
      await scheduler.tick();
      expect(service.isLoggedIn()).toBe(true);
      expect(service.isSessionExpired()).toBe(true);

      // act: the user logs in again.
      sessionStorageStub.setItem("auth.googleCodeVerifier", "verifier-2");
      const secondJwt = fakeJwt({ sub: "user-8", email: "user8@example.test" });
      when(
        mockedAuthModule.googleTokenCreate(
          deepEqual({ code: "auth-code-2", codeVerifier: "verifier-2" }),
        ),
      ).thenResolve({
        data: {
          token: secondJwt,
          expiresInSeconds: 3600,
          refreshTokenSecret: "secret-2",
          user: googleUser,
        },
      } as GoogleTokenCreateResponse);
      await service.completeGoogleLogin("auth-code-2");

      // verify
      expect(service.isLoggedIn()).toBe(true);
      expect(service.isSessionExpired()).toBe(false);
      expect(service.getUserInfo()).toEqual({
        id: "user-8",
        email: "user8@example.test",
      });
    });
  });
});

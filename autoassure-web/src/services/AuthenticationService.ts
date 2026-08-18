import type { Api } from "autoassure-server-sdk";
import { EventEmitter } from "eventemitter3";
import { ServiceError } from "../models/ServiceError";
import type { UserInfo } from "../models/UserInfo";
import type { Clock } from "./Clock";

// localStorage key for the session JWT.
const JWT_STORAGE_KEY = "auth.jwt";
// localStorage key for when the JWT was issued, used to judge when it's due for refresh.
const ISSUED_AT_STORAGE_KEY = "auth.issuedAt";
// sessionStorage key for the in-flight Google login's PKCE code verifier.
const CODE_VERIFIER_STORAGE_KEY = "auth.googleCodeVerifier";

// Google's OAuth 2.0 authorization endpoint.
const GOOGLE_AUTHORIZATION_ENDPOINT =
  "https://accounts.google.com/o/oauth2/v2/auth";

interface AuthenticationEvents {
  readonly change: [];
}

interface Session {
  readonly jwt: string;
  // Unix timestamp (seconds) the JWT was issued, used to judge when it's due for refresh.
  readonly issuedAt: number;
}

/** Config for Google OAuth (PKCE) login, sourced from env by the caller. */
export interface GoogleAuthConfig {
  readonly clientId: string;
  readonly redirectUri: string;
}

export function toBase64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary)
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "");
}

// PKCE code verifier for the in-flight Google login (RFC 7636).
export function generateCodeVerifier(): string {
  return toBase64Url(crypto.getRandomValues(new Uint8Array(32)));
}

// PKCE code challenge derived from `generateCodeVerifier`'s output (RFC 7636).
export async function deriveCodeChallenge(codeVerifier: string): Promise<string> {
  const digest = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(codeVerifier),
  );
  return toBase64Url(new Uint8Array(digest));
}

/** Manages the user's login session and Google OAuth (PKCE) login flow. */
export class AuthenticationService {
  private readonly emitter = new EventEmitter<AuthenticationEvents>();
  private readonly autoAssureClient: Api<unknown>;
  private readonly clock: Clock;
  private readonly googleAuthConfig: GoogleAuthConfig;
  // In-memory cache of the session so hot-path reads (isLoggedIn, getUserInfo)
  // don't hit localStorage; localStorage only exists to survive a page reload.
  private session: Session | null;

  constructor(
    autoAssureClient: Api<unknown>,
    clock: Clock,
    googleAuthConfig: GoogleAuthConfig,
  ) {
    this.autoAssureClient = autoAssureClient;
    this.clock = clock;
    this.googleAuthConfig = googleAuthConfig;
    this.session = this.loadSessionFromStorage();
  }

  /** Subscribes to login/logout changes; call the returned function to unsubscribe. */
  on(event: "change", listener: () => void): () => void {
    this.emitter.on(event, listener);
    return () => {
      this.emitter.off(event, listener);
    };
  }

  /** Whether the user currently has a session. */
  isLoggedIn(): boolean {
    return this.getJwt() !== null;
  }

  /** The current session's JWT, or null if not logged in. */
  private getJwt(): string | null {
    return this.session?.jwt ?? null;
  }

  /** The logged-in user's identity, or null if not logged in. */
  getUserInfo(): UserInfo | null {
    const jwt = this.getJwt();
    if (jwt === null) {
      return null;
    }
    // The JWT's signature is verified server-side on every request; the client
    // only needs the claims, so decoding the payload without verification is fine.
    const encodedPayload = jwt.split(".")[1] ?? "";
    const payload = JSON.parse(atob(encodedPayload)) as {
      sub: string;
      email: string;
    };
    return { id: payload.sub, email: payload.email };
  }

  /** Redirects the browser to Google to start a login. */
  async startGoogleLogin(): Promise<void> {
    // Generate a fresh PKCE pair per attempt and stash the verifier so the
    // callback step can prove to the backend it's the same client that started this flow.
    const codeVerifier = generateCodeVerifier();
    const codeChallenge = await deriveCodeChallenge(codeVerifier);
    sessionStorage.setItem(CODE_VERIFIER_STORAGE_KEY, codeVerifier);

    const params = new URLSearchParams({
      client_id: this.googleAuthConfig.clientId,
      redirect_uri: this.googleAuthConfig.redirectUri,
      response_type: "code",
      scope: "openid email profile",
      code_challenge: codeChallenge,
      code_challenge_method: "S256",
    });

    // Hand off to Google; it will redirect back to our callback route with a `code`.
    window.location.assign(
      `${GOOGLE_AUTHORIZATION_ENDPOINT}?${params.toString()}`,
    );
  }

  /**
   * @throws {Error} if no login was started with `startGoogleLogin`.
   * @throws {ServiceError} if the backend rejects the code or the request
   * otherwise fails — caller should let the user retry.
   */
  async completeGoogleLogin(code: string): Promise<void> {
    const codeVerifier = sessionStorage.getItem(CODE_VERIFIER_STORAGE_KEY);
    if (codeVerifier === null) {
      throw new Error(
        "Missing PKCE code verifier. Please try logging in again.",
      );
    }
    // The verifier is single-use; drop it now so a duplicate callback can't replay it.
    sessionStorage.removeItem(CODE_VERIFIER_STORAGE_KEY);

    const response = await this.autoAssureClient.auth
      .googleTokenCreate({ code, codeVerifier })
      .catch((error: unknown) => {
        throw ServiceError.fromSdkError(error);
      });
    this.storeSession(response.data.token);
  }

  /** Clears the current session. */
  logout(): void {
    this.session = null;
    localStorage.removeItem(JWT_STORAGE_KEY);
    localStorage.removeItem(ISSUED_AT_STORAGE_KEY);
    this.notifyListeners();
  }

  private storeSession(jwt: string): void {
    const issuedAt = this.clock.now();
    this.session = { jwt, issuedAt };
    localStorage.setItem(JWT_STORAGE_KEY, jwt);
    localStorage.setItem(ISSUED_AT_STORAGE_KEY, String(issuedAt));
    this.notifyListeners();
  }

  // Requires the JWT and its issued-at timestamp to both be present; a
  // partial pair (e.g. cleared by another tab, or corrupted) is treated as
  // no session rather than trusted with a made-up age.
  private loadSessionFromStorage(): Session | null {
    const jwt = localStorage.getItem(JWT_STORAGE_KEY);
    const rawIssuedAt = localStorage.getItem(ISSUED_AT_STORAGE_KEY);
    if (jwt === null || rawIssuedAt === null) {
      return null;
    }
    const issuedAt = Number(rawIssuedAt);
    if (Number.isNaN(issuedAt)) {
      return null;
    }
    return { jwt, issuedAt };
  }

  private notifyListeners(): void {
    this.emitter.emit("change");
  }
}

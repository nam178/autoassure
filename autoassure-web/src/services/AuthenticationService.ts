import type { Api, RefreshTokenResponse } from "autoassure-server-sdk";
import { EventEmitter } from "eventemitter3";
import {
  AUTH_EXPIRY_SAFETY_MARGIN_SECONDS,
  AUTH_REFRESH_CHECK_INTERVAL_MS,
  AUTH_REFRESH_THRESHOLD_SECONDS,
} from "../common/Config";
import { ServiceError } from "../models/ServiceError";
import type { UserInfo } from "../models/UserInfo";
import type { Clock } from "./Clock";
import type { Scheduler } from "./Scheduler";

/** localStorage key for the session JWT. */
const JWT_STORAGE_KEY = "auth.jwt";
/** localStorage key for when the JWT was issued, used to judge refresh timing. */
const ISSUED_AT_STORAGE_KEY = "auth.issuedAt";
/** localStorage key for when the JWT expires (Unix seconds), per the backend. */
const EXPIRES_AT_STORAGE_KEY = "auth.expiresAt";
/** localStorage key for the secret used to exchange for a new JWT via /auth/refresh. */
const REFRESH_TOKEN_STORAGE_KEY = "auth.refreshTokenSecret";
/** sessionStorage key for the in-flight Google login's PKCE code verifier. */
const CODE_VERIFIER_STORAGE_KEY = "auth.googleCodeVerifier";

/** Google's OAuth 2.0 authorization endpoint. */
const GOOGLE_AUTHORIZATION_ENDPOINT =
  "https://accounts.google.com/o/oauth2/v2/auth";

interface AuthenticationEvents {
  readonly change: [];
}

interface Session {
  readonly jwt: string;
  // Unix timestamp (seconds) the JWT was issued, per this client's clock.
  readonly issuedAt: number;
  // Unix timestamp (seconds) the JWT expires, per the backend's response.
  readonly expiresAt: number;
  // Secret to exchange for a new JWT via /auth/refresh.
  readonly refreshTokenSecret: string;
}

/** Config for Google OAuth (PKCE) login, sourced from env by the caller. */
export interface GoogleAuthConfig {
  readonly clientId: string;
  readonly redirectUri: string;
}

/** Encodes bytes as a base64url string (RFC 4648 §5, no padding). */
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

/** Generates a fresh PKCE code verifier for a Google login attempt (RFC 7636). */
export function generateCodeVerifier(): string {
  return toBase64Url(crypto.getRandomValues(new Uint8Array(32)));
}

/** Derives the PKCE code challenge to send Google for a given code verifier (RFC 7636). */
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
  private readonly scheduler: Scheduler;
  private readonly googleAuthConfig: GoogleAuthConfig;
  // In-memory cache of the session so hot-path reads (isLoggedIn, getUserInfo)
  // don't hit localStorage; localStorage only exists to survive a page reload.
  private session: Session | null;
  // True once a refresh attempt has failed for the current session; a substate
  // of being logged in, not a separate logged-out state, so the app doesn't
  // unmount — callers should show a re-login prompt instead.
  private sessionExpired = false;
  // Guards against overlapping refresh calls if a check tick fires while a
  // previous refresh request is still in flight.
  private refreshInFlight = false;
  // Id of the scheduled refresh check, set by `start()`; null when not running.
  private refreshCheckIntervalId: number | null = null;

  constructor(
    autoAssureClient: Api<unknown>,
    clock: Clock,
    scheduler: Scheduler,
    googleAuthConfig: GoogleAuthConfig,
  ) {
    this.autoAssureClient = autoAssureClient;
    this.clock = clock;
    this.scheduler = scheduler;
    this.googleAuthConfig = googleAuthConfig;
    this.session = this.loadSessionFromStorage();
  }

  /**
   * Starts the background check that refreshes the session as it nears
   * expiry. Callers own the lifecycle: call once the service is mounted, and
   * `dispose()` on unmount so the timer doesn't outlive its owner.
   */
  start(): void {
    if (this.refreshCheckIntervalId !== null) {
      return;
    }
    this.refreshCheckIntervalId = this.scheduler.setInterval(() => {
      void this.checkAndMaybeRefresh();
    }, AUTH_REFRESH_CHECK_INTERVAL_MS);
  }

  /** Stops the background refresh check started by `start()`. */
  dispose(): void {
    if (this.refreshCheckIntervalId === null) {
      return;
    }
    this.scheduler.clearInterval(this.refreshCheckIntervalId);
    this.refreshCheckIntervalId = null;
  }

  /** Subscribes to login/logout/session-expiry changes; call the returned function to unsubscribe. */
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

  /**
   * Whether the current session's last refresh attempt failed. Only ever
   * true while `isLoggedIn()` is also true; resets on `logout()` or on the
   * next successful login.
   */
  isSessionExpired(): boolean {
    return this.sessionExpired;
  }

  /** The current session's JWT, or null if not logged in. */
  private getJwt(): string | null {
    return this.session?.jwt ?? null;
  }

  /**
   * The logged-in user's identity, or null if not logged in.
   * @throws {Error} if the stored JWT's payload is malformed or not valid base64/JSON.
   */
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
    this.storeSession(response.data);
  }

  /** Clears the current session. */
  logout(): void {
    this.session = null;
    this.sessionExpired = false;
    localStorage.removeItem(JWT_STORAGE_KEY);
    localStorage.removeItem(ISSUED_AT_STORAGE_KEY);
    localStorage.removeItem(EXPIRES_AT_STORAGE_KEY);
    localStorage.removeItem(REFRESH_TOKEN_STORAGE_KEY);
    this.notifyListeners();
  }

  private async checkAndMaybeRefresh(): Promise<void> {
    // Nothing to refresh if logged out, already flagged expired, or a
    // previous check's refresh is still in flight.
    if (this.session === null || this.sessionExpired || this.refreshInFlight) {
      return;
    }
    // Captured up front rather than re-read from `this.session` after the
    // request settles: `logout()` can null out `this.session` (or a fresh
    // login can replace it) while the request is in flight, and `expiresAt`
    // doesn't change mid-attempt anyway.
    const sessionAtRequestStart = this.session;
    const { expiresAt, refreshTokenSecret } = sessionAtRequestStart;
    const remainingSeconds = expiresAt - this.clock.now();
    if (remainingSeconds > AUTH_REFRESH_THRESHOLD_SECONDS) {
      return;
    }
    this.refreshInFlight = true;
    let response: { data: RefreshTokenResponse };
    try {
      response = await this.autoAssureClient.auth.refreshCreate({
        refreshTokenSecret,
      });
    } catch (error: unknown) {
      this.refreshInFlight = false;
      // Discard a stale failure the same way a stale success is discarded:
      // the user logged out or into a different account while this request
      // was in flight, so it must not flag the (unrelated) current session.
      if (this.session !== sessionAtRequestStart) {
        return;
      }
      const serviceError = ServiceError.fromSdkError(error);
      // Re-measure rather than reusing the pre-request `remainingSeconds`:
      // the failed request itself took time, and that time counts against
      // the token's real deadline.
      const secondsUntilActualExpiry = expiresAt - this.clock.now();
      if (
        serviceError.kind === "client" ||
        secondsUntilActualExpiry <= AUTH_EXPIRY_SAFETY_MARGIN_SECONDS
      ) {
        this.sessionExpired = true;
        this.notifyListeners();
      } else {
        console.error(
          "Session refresh failed; will retry on the next check.",
          serviceError,
        );
      }
      return;
    }
    this.refreshInFlight = false;
    // Discard a response that's no longer relevant: the user logged out or
    // logged into a different account while this request was in flight, so
    // applying it now would resurrect a stale session or clobber the new one.
    if (this.session !== sessionAtRequestStart) {
      return;
    }
    this.storeSession(response.data);
  }

  private storeSession(grant: RefreshTokenResponse): void {
    const issuedAt = this.clock.now();
    // The backend returns a relative duration, not an absolute timestamp,
    // specifically so the client isn't comparing the server's clock against
    // its own; anchoring that duration to this client's own `issuedAt` keeps
    // refresh timing correct even if this device's wall clock is off.
    const expiresAt = issuedAt + Number(grant.expiresInSeconds);
    this.session = {
      jwt: grant.token,
      issuedAt,
      expiresAt,
      refreshTokenSecret: grant.refreshTokenSecret,
    };
    this.sessionExpired = false;
    localStorage.setItem(JWT_STORAGE_KEY, grant.token);
    localStorage.setItem(ISSUED_AT_STORAGE_KEY, String(issuedAt));
    localStorage.setItem(EXPIRES_AT_STORAGE_KEY, String(expiresAt));
    localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, grant.refreshTokenSecret);
    this.notifyListeners();
  }

  // Requires the JWT, issued-at, expires-at, and refresh secret to all be
  // present; a partial set (e.g. cleared by another tab, or corrupted) is
  // treated as no session rather than trusted with made-up values.
  private loadSessionFromStorage(): Session | null {
    const jwt = localStorage.getItem(JWT_STORAGE_KEY);
    const rawIssuedAt = localStorage.getItem(ISSUED_AT_STORAGE_KEY);
    const rawExpiresAt = localStorage.getItem(EXPIRES_AT_STORAGE_KEY);
    const refreshTokenSecret = localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY);
    if (
      jwt === null ||
      rawIssuedAt === null ||
      rawExpiresAt === null ||
      refreshTokenSecret === null
    ) {
      return null;
    }
    const issuedAt = Number(rawIssuedAt);
    const expiresAt = Number(rawExpiresAt);
    if (Number.isNaN(issuedAt) || Number.isNaN(expiresAt)) {
      return null;
    }
    return { jwt, issuedAt, expiresAt, refreshTokenSecret };
  }

  private notifyListeners(): void {
    this.emitter.emit("change");
  }
}

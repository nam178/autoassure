import type { UserInfo } from "./UserInfo";

/** The user's current authentication state: logged out, or logged in (optionally with an expired session needing re-login). */
export type AuthState =
  | { readonly status: "logged-out" }
  | { readonly status: "logged-in"; readonly user: UserInfo; readonly isExpired: boolean };

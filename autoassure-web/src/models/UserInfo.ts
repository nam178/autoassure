/** The logged-in user's identity, decoded from the session JWT's claims. */
export interface UserInfo {
  readonly id: string;
  readonly email: string;
}

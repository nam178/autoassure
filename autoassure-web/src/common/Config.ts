// Application-level constants, grouped by the feature/area that owns them
// (constant names are prefixed accordingly, e.g. `AUTH_*`).

/** Refresh the session once its token is within this many seconds of expiring. */
export const AUTH_REFRESH_THRESHOLD_SECONDS = 5 * 60;

/** How often to check whether the session is due for a refresh. */
export const AUTH_REFRESH_CHECK_INTERVAL_MS = 5_000;

/**
 * Once the token is within this many seconds of actually expiring, a failed
 * refresh marks the session expired even if the failure looked transient
 * (network/server/unknown) -- the token will stop working regardless, so
 * there's no point waiting for a retry that can no longer land in time.
 */
export const AUTH_EXPIRY_SAFETY_MARGIN_SECONDS = 2 * 60;

/** Path of the home page, rendered inside the app shell. */
export const ROUTE_HOME = "/";

/** Path of the scenarios page, rendered inside the app shell. */
export const ROUTE_SCENARIOS = "/scenarios";

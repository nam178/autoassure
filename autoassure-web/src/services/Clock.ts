/** Provides the current time; inject so callers can fake it in tests. */
export interface Clock {
  /** Current time as a Unix timestamp (seconds). */
  now(): number;
}

/** Clock backed by the real system time. */
export class SystemClock implements Clock {
  now(): number {
    return Math.floor(Date.now() / 1000);
  }
}

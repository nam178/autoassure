/** Schedules recurring work; inject so callers can fake it in tests. */
export interface Scheduler {
  /** Invokes `callback` roughly every `intervalMs`; returns an id for `clearInterval`. */
  setInterval(callback: () => void, intervalMs: number): number;
  clearInterval(id: number): void;
}

/** Scheduler backed by the browser's real `setInterval`. */
export class SystemScheduler implements Scheduler {
  setInterval(callback: () => void, intervalMs: number): number {
    return window.setInterval(callback, intervalMs);
  }

  clearInterval(id: number): void {
    window.clearInterval(id);
  }
}

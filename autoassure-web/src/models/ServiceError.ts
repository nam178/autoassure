import { isAxiosError } from "axios";

/** Coarse-grained cause of a service call failure. */
export type ServiceErrorKind = "client" | "server" | "network";

/** Generic error services throw when a backend call fails, classified so callers can react without inspecting transport details. */
export class ServiceError extends Error {
  readonly kind: ServiceErrorKind;
  // HTTP status code; present for "client" and "server" kinds only.
  readonly status?: number;

  constructor(
    kind: ServiceErrorKind,
    message: string,
    options: { status?: number; cause?: unknown } = {},
  ) {
    super(message, { cause: options.cause });
    this.name = "ServiceError";
    this.kind = kind;
    if (options.status !== undefined) {
      this.status = options.status;
    }
  }

  /**
   * Classifies an error thrown by an SDK call into a `ServiceError`:
   * "client" for 4xx, "server" for 5xx, "network" for anything else
   * (timeout, no response, non-HTTP error).
   */
  static fromSdkError(error: unknown): ServiceError {
    if (isAxiosError(error)) {
      const status = error.response?.status;
      if (status !== undefined && status < 500) {
        return new ServiceError("client", error.message, { status, cause: error });
      }
      if (status !== undefined) {
        return new ServiceError("server", error.message, { status, cause: error });
      }
      return new ServiceError("network", error.message, { cause: error });
    }
    return new ServiceError(
      "network",
      error instanceof Error ? error.message : String(error),
      { cause: error },
    );
  }
}

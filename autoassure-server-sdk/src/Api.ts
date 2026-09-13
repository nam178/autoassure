/* eslint-disable */
/* tslint:disable */
// @ts-nocheck
/*
 * ---------------------------------------------------------------
 * ## THIS FILE WAS GENERATED VIA SWAGGER-TYPESCRIPT-API        ##
 * ##                                                           ##
 * ## AUTHOR: acacode                                           ##
 * ## SOURCE: https://github.com/acacode/swagger-typescript-api ##
 * ---------------------------------------------------------------
 */

/** A single step within a Scenario, as returned to the client. */
export interface ActivityResponse {
  /** @format uuid */
  id: string;
  /** @format uuid */
  scenarioId: string;
  description: string;
  /**
   * @format int32
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  order: number | string;
  preconditionIds: string[];
  evidenceIds: string[];
  /** @format uuid */
  createdByUserId: string;
  /** @format uuid */
  updatedByUserId: string;
  /** @format date-time */
  createdAt: string;
  /** @format date-time */
  updatedAt: string;
}

/**
 * The outcome of one Activity within a Run, as sent by a worker appending a status update and
 *     as returned back to the client reading the log. ScenarioId and ActivityId identify rows in the Run's
 *     OWN snapshot -- the ids the Get Run response's Scenario/Activity snapshots carry on their Source --
 *     not the live Scenario or Activity, which may since have changed or been deleted.
 */
export interface ActivityResult {
  /** @format uuid */
  scenarioId: string;
  /** @format uuid */
  activityId: string;
  /**
   * What became of an Activity by the time its result was appended to a Run's status update log,
   *     as returned to the client. Only Passed, Failed or Skipped are legal on an appended result -- Pending
   *     and Running name the states before an Activity has concluded.
   */
  status: ActivityResultStatus;
  /** @maxLength 50 */
  resolvedPreconditions?: null | Record<string, string>;
  /** @maxLength 50 */
  evidence?: null | Record<string, string>;
  /**
   * Why this Activity was chosen for execution despite an earlier Activity failing.
   * @maxLength 2000
   */
  continuationReasoning?: null | string;
}

/**
 * What became of an Activity by the time its result was appended to a Run's status update log,
 *     as returned to the client. Only Passed, Failed or Skipped are legal on an appended result -- Pending
 *     and Running name the states before an Activity has concluded.
 */
export type ActivityResultStatus = number;

/**
 * Request body to append one entry to a Run's status update log, at the caller's own sequence
 *      number. The owning worker allocates Seq in memory -- this API never invents one. Sequence numbers are
 *      1-based and dense: the first update of a Run's log has Seq 1.
 *
 *      AppendActivityResult is the only kind of status update that exists today, so this always carries an
 *      ActivityResult.
 */
export interface AppendRunStatusUpdateRequest {
  /**
   * @format int64
   * @min 1
   * @max 9223372036854776000
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  seq: number | string;
  /**
   * The outcome of one Activity within a Run, as sent by a worker appending a status update and
   *     as returned back to the client reading the log. ScenarioId and ActivityId identify rows in the Run's
   *     OWN snapshot -- the ids the Get Run response's Scenario/Activity snapshots carry on their Source --
   *     not the live Scenario or Activity, which may since have changed or been deleted.
   */
  activityResult: ActivityResult;
}

/** An Application, as returned to the client. */
export interface ApplicationResponse {
  /** @format uuid */
  id: string;
  name: string;
  description: string;
}

/** Returned after a successful Google sign-in: the issued tokens and the signed-in user. */
export interface AuthTokenResponse {
  token: string;
  /**
   * Number of seconds until Token expires, measured from when the response is sent.
   * A relative duration is used instead of an absolute timestamp because the client's clock may be offset
   * from the server's.
   * @format int32
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  expiresInSeconds: number | string;
  refreshTokenSecret: string;
  /** The signed-in AutoAssure user, as returned to the client after authentication. */
  user: UserResponse;
}

/**
 * Request body to append a new Activity to a Scenario. PreconditionIds/EvidenceIds must
 *     each reference existing library rows in the Scenario's Application.
 */
export interface CreateActivityRequest {
  /** @maxLength 2000 */
  description: string;
  /** @maxItems 15 */
  preconditionIds?: null | string[];
  /** @maxItems 15 */
  evidenceIds?: null | string[];
}

/** Request body to create a new Application in the caller's Organization. */
export interface CreateApplicationRequest {
  /**
   * Must not be empty or whitespace.
   * @maxLength 100
   */
  name: string;
  /** @maxLength 1000 */
  description: string;
}

/**
 * Request body to Try a single Scenario against an Environment. The Scenario comes from the
 *     URL (`POST /scenarios/{id}/runs`); this only supplies the Environment to run against.
 */
export interface CreateAuthoringRunRequest {
  /** @format uuid */
  environmentId: string;
}

/**
 * Request body to create a new Environment for an Application. No Variables at creation —
 *     set those afterward via `PUT /environments/{id}/variables/{key}`.
 */
export interface CreateEnvironmentRequest {
  /**
   * Must not be empty or whitespace.
   * @maxLength 100
   */
  name: string;
  /** Whether an Environment is a live Production system or a non-production one (staging, dev, ...). */
  classification: EnvironmentClassification;
}

/** Request body to add an EvidenceDefinition to an Application's library. */
export interface CreateEvidenceDefinitionRequest {
  /**
   * Must not be empty or whitespace.
   * @maxLength 200
   */
  name: string;
  /** @maxLength 500 */
  description: string;
  /** @maxLength 10000 */
  exampleValue: string;
}

/** Request body to add a Precondition to an Application's library. */
export interface CreatePreconditionRequest {
  /**
   * Must not be empty or whitespace.
   * @maxLength 200
   */
  name: string;
  /** Where a Precondition's value comes from at execution time. */
  valueSource: PreconditionValueSource;
  /** @maxLength 10000 */
  exampleValue: string;
}

/**
 * Request body to start a Manual Run of one or more Scenarios against an Environment. Every id
 *     in ScenarioIds must reference a Scenario belonging to the Application named in the URL.
 */
export interface CreateRunRequest {
  /**
   * @maxItems 96
   * @minItems 1
   */
  scenarioIds: string[];
  /** @format uuid */
  environmentId: string;
}

/**
 * Request body to create a new Scenario for an Application. Folder defaults to "/" when
 *     not given; Tags default to empty.
 */
export interface CreateScenarioRequest {
  /**
   * Must not be empty or whitespace.
   * @maxLength 200
   */
  title: string;
  /** @maxLength 2000 */
  description: string;
  /** @maxLength 300 */
  folder?: null | string;
  /** @maxItems 20 */
  tags?: null | string[];
}

/**
 * Request body to end a Running Run. TerminalStatus must be Completed, Cancelled or Abandoned --
 *     Pending and Running are rejected, since those are states the server itself moves a Run through, never
 *     an outcome a caller declares. StatusReason may only be given alongside Abandoned; Completed and
 *     Cancelled are self-explanatory and must leave it null.
 */
export interface EndRunRequest {
  /**
   * A Run's execution state, as returned to the client. Carries no pass/fail judgment: a Run
   *      whose every Activity failed is still Completed, and the activity counts say how it went.
   *
   *      Only Completed, Cancelled and Abandoned are legal terminal values to send on End Run -- Pending and
   *      Running name states the server itself moves a Run through and are rejected there.
   *
   *      Running does not by itself mean the owning worker is still alive -- it may have crashed or been
   *      killed without anything having noticed yet. Treat Running as "not yet terminal," and check
   *      LastHeartbeatAt (on RunResponse/RunSummaryResponse) to tell whether it is actually making
   *      progress.
   */
  terminalStatus: RunStatus;
  statusReason?: null | RunStatusReason;
}

/** Whether an Environment is a live Production system or a non-production one (staging, dev, ...). */
export type EnvironmentClassification = number;

/** An Environment and its assembled Variables, as returned to the client. */
export interface EnvironmentResponse {
  /** @format uuid */
  id: string;
  name: string;
  /** Whether an Environment is a live Production system or a non-production one (staging, dev, ...). */
  classification: EnvironmentClassification;
  variables: EnvironmentVariableResponse[];
}

/**
 * A single Environment variable, as returned to the client. When IsSensitive is true, Value
 *     is masked: only its first 30% of characters, the rest replaced by a fixed-length run of dots.
 */
export interface EnvironmentVariableResponse {
  key: string;
  value: string;
  isSensitive: boolean;
}

/** An error, as returned to the client on a non-success response. */
export interface ErrorResponse {
  /** User-friendly message for displaying to the caller. */
  message: string;
}

/** An EvidenceDefinition library item, as returned to the client. */
export interface EvidenceDefinitionResponse {
  /** @format uuid */
  id: string;
  name: string;
  description: string;
  exampleValue: string;
}

/**
 * An OAuth 2.0 PKCE authorization code from Google's consent screen, to be exchanged for
 *     the user's Google identity.
 */
export interface ExchangeGoogleCodeRequest {
  /**
   * The authorization code returned by Google after the user consents.
   * @maxLength 2000
   */
  code: string;
  /**
   * The PKCE code verifier the client generated for this authorization request.
   * @maxLength 200
   */
  codeVerifier: string;
}

/** A Precondition library item, as returned to the client. */
export interface PreconditionResponse {
  /** @format uuid */
  id: string;
  name: string;
  /** Where a Precondition's value comes from at execution time. */
  valueSource: PreconditionValueSource;
  exampleValue: string;
}

/** Where a Precondition's value comes from at execution time. */
export type PreconditionValueSource = number;

export interface ProblemDetails {
  type?: null | string;
  title?: null | string;
  /**
   * @format int32
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  status?: null | number | string;
  detail?: null | string;
  instance?: null | string;
}

/** Requests a new access token using a previously issued refresh token. */
export interface RefreshTokenRequest {
  /**
   * The raw refresh token secret previously issued to the client, to be exchanged for a new access token.
   * @maxLength 500
   */
  refreshTokenSecret: string;
}

/** Returned after a successful token refresh: the newly issued tokens. */
export interface RefreshTokenResponse {
  token: string;
  /**
   * Number of seconds until Token expires, measured from when the response is sent.
   * A relative duration is used instead of an absolute timestamp because the client's clock may be offset
   * from the server's.
   * @format int32
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  expiresInSeconds: number | string;
  refreshTokenSecret: string;
}

/**
 * Request body to reorder a Scenario's Activities. Must contain exactly one entry per
 *     Activity currently in the Scenario, as a permutation of their ids.
 */
export interface ReorderActivitiesRequest {
  /** @maxItems 90 */
  orderedActivityIds: string[];
}

/**
 * An Activity as it was when a Run was created, together with the Preconditions and
 *     EvidenceDefinitions it referenced at that moment, as returned to the client.
 */
export interface RunActivitySnapshotResponse {
  /**
   * Where a Run snapshot was copied from, as returned to the client: provenance only. Id may no
   *     longer resolve to a live row -- the source can have been edited, archived or deleted since this Run
   *     was created -- so it should be treated as a hint for "open the current version, if it still exists",
   *     never as a live reference.
   */
  source: SnapshotSourceResponse;
  /**
   * @format int32
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  order: number | string;
  description: string;
  preconditions: RunPreconditionSnapshotResponse[];
  evidenceDefinitions: RunEvidenceDefinitionSnapshotResponse[];
}

/**
 * The Environment a Run ran against, as it was when the Run was created, as returned to the
 *     client.
 */
export interface RunEnvironmentSnapshotResponse {
  /**
   * Where a Run snapshot was copied from, as returned to the client: provenance only. Id may no
   *     longer resolve to a live row -- the source can have been edited, archived or deleted since this Run
   *     was created -- so it should be treated as a hint for "open the current version, if it still exists",
   *     never as a live reference.
   */
  source: SnapshotSourceResponse;
  name: string;
  /** Whether an Environment is a live Production system or a non-production one (staging, dev, ...). */
  classification: EnvironmentClassification;
  variables: RunEnvironmentVariableSnapshotResponse[];
}

/**
 * An Environment variable as it was when a Run was created, as returned to the client. When
 *     IsSensitive is true, Value is already masked -- more heavily than the live Environment API masks it,
 *     since a Run snapshot lives for three years.
 */
export interface RunEnvironmentVariableSnapshotResponse {
  key: string;
  value: string;
  isSensitive: boolean;
  /** @format uuid */
  createdByUserId: string;
  /** @format uuid */
  updatedByUserId: string;
  /** @format date-time */
  createdAt: string;
  /** @format date-time */
  updatedAt: string;
}

/** An EvidenceDefinition as it was when a Run was created, as returned to the client. */
export interface RunEvidenceDefinitionSnapshotResponse {
  /**
   * Where a Run snapshot was copied from, as returned to the client: provenance only. Id may no
   *     longer resolve to a live row -- the source can have been edited, archived or deleted since this Run
   *     was created -- so it should be treated as a hint for "open the current version, if it still exists",
   *     never as a live reference.
   */
  source: SnapshotSourceResponse;
  name: string;
  description: string;
  exampleValue: string;
}

export interface RunningRunResponse {
  /** @format uuid */
  id: string;
  /** @format date-time */
  startedAt: string;
}

/** A Precondition as it was when a Run was created, as returned to the client. */
export interface RunPreconditionSnapshotResponse {
  /**
   * Where a Run snapshot was copied from, as returned to the client: provenance only. Id may no
   *     longer resolve to a live row -- the source can have been edited, archived or deleted since this Run
   *     was created -- so it should be treated as a hint for "open the current version, if it still exists",
   *     never as a live reference.
   */
  source: SnapshotSourceResponse;
  name: string;
  /** Where a Precondition's value comes from at execution time. */
  valueSource: PreconditionValueSource;
  exampleValue: string;
}

/**
 * A Run's identity, execution state and what it ran, as returned to the client. Never carries
 *     the status update log -- LastSeq and Status are what tell a client whether it is worth polling List
 *     Run Status Updates and when to stop. ApplicationId is included even under the nested
 *     `/applications/{applicationId}/runs/{runId}` route because the authoring create route
 *     (`POST /scenarios/{scenarioId}/runs`) is flat and returns this same shape -- without it, a client
 *     following an authoring Run would have no way to build its polling URLs.
 */
export interface RunResponse {
  /** @format uuid */
  id: string;
  /** @format uuid */
  applicationId: string;
  /**
   * Where a Run came from, as returned to the client. Affects retention and whether the Run
   *     shows up in the Application's Runs panel -- nothing about how it executes.
   */
  trigger: RunTrigger;
  /**
   * A Run's execution state, as returned to the client. Carries no pass/fail judgment: a Run
   *      whose every Activity failed is still Completed, and the activity counts say how it went.
   *
   *      Only Completed, Cancelled and Abandoned are legal terminal values to send on End Run -- Pending and
   *      Running name states the server itself moves a Run through and are rejected there.
   *
   *      Running does not by itself mean the owning worker is still alive -- it may have crashed or been
   *      killed without anything having noticed yet. Treat Running as "not yet terminal," and check
   *      LastHeartbeatAt (on RunResponse/RunSummaryResponse) to tell whether it is actually making
   *      progress.
   */
  status: RunStatus;
  /** Only set when Status is Abandoned. */
  statusReason?: null | RunStatusReason;
  /**
   * @format int32
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  totalActivityCount: number | string;
  /**
   * @format int32
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  passedActivityCount: number | string;
  /**
   * @format int32
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  failedActivityCount: number | string;
  /**
   * @format int32
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  skippedActivityCount: number | string;
  /**
   * The Environment a Run ran against, as it was when the Run was created, as returned to the
   *     client.
   */
  environment: RunEnvironmentSnapshotResponse;
  /** One snapshot per Scenario the Run ran, in no particular order. */
  scenarios: RunScenarioSnapshotResponse[];
  /**
   * The highest sequence number appended to this Run's status update log so far. A client
   *     that already holds up to this sequence has nothing new to poll for.
   * @format int64
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  lastSeq: number | string;
  /**
   * Who triggered this Run. Null for a Scheduled Run -- a system timer has no user id.
   * @format uuid
   */
  triggeredByUserId?: null | string;
  /** @format date-time */
  createdAt: string;
  /** @format date-time */
  startedAt?: null | string;
  /** @format date-time */
  completedAt?: null | string;
  /**
   * When the owning worker last proved it was alive. Null while Status is Pending. A Run
   *     stuck on Running with an old LastHeartbeatAt has likely lost its worker -- Status alone does not
   *     tell you that.
   * @format date-time
   */
  lastHeartbeatAt?: null | string;
}

/**
 * A Scenario as it was when a Run was created, together with its Activities in order, as
 *     returned to the client. Never changes when the live Scenario is edited or deleted afterward.
 */
export interface RunScenarioSnapshotResponse {
  /**
   * Where a Run snapshot was copied from, as returned to the client: provenance only. Id may no
   *     longer resolve to a live row -- the source can have been edited, archived or deleted since this Run
   *     was created -- so it should be treated as a hint for "open the current version, if it still exists",
   *     never as a live reference.
   */
  source: SnapshotSourceResponse;
  title: string;
  description: string;
  folder: string;
  tags: string[];
  activities: RunActivitySnapshotResponse[];
}

/**
 * A Run's execution state, as returned to the client. Carries no pass/fail judgment: a Run
 *      whose every Activity failed is still Completed, and the activity counts say how it went.
 *
 *      Only Completed, Cancelled and Abandoned are legal terminal values to send on End Run -- Pending and
 *      Running name states the server itself moves a Run through and are rejected there.
 *
 *      Running does not by itself mean the owning worker is still alive -- it may have crashed or been
 *      killed without anything having noticed yet. Treat Running as "not yet terminal," and check
 *      LastHeartbeatAt (on RunResponse/RunSummaryResponse) to tell whether it is actually making
 *      progress.
 */
export type RunStatus = number;

export type RunStatusReason = number;

/**
 * What a Run status update row records, as returned to the client. AppendActivityResult is
 *     the only kind that exists today -- see the server's design notes for what earns a new one.
 */
export type RunStatusUpdateKind = number;

/**
 * One entry of a Run's status update log, as returned to the client. The API hands these back
 *     exactly as appended, in sequence order -- it never folds or interprets them; only the client
 *     does.
 */
export interface RunStatusUpdateResponse {
  /**
   * @format int64
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  seq: number | string;
  /**
   * What a Run status update row records, as returned to the client. AppendActivityResult is
   *     the only kind that exists today -- see the server's design notes for what earns a new one.
   */
  kind: RunStatusUpdateKind;
  /** @format date-time */
  createdAt: string;
  /**
   * Set when Kind is AppendActivityResult -- the only kind today, so always set in
   *     practice.
   */
  activityResult?: null | ActivityResult;
}

/**
 * A Run's identity and execution state only -- no Environment, no Scenarios -- exactly what the
 *     Application's Runs panel shows for one row of the list. Authoring Runs never appear here; fetch one by
 *     id instead.
 */
export interface RunSummaryResponse {
  /** @format uuid */
  id: string;
  /**
   * Where a Run came from, as returned to the client. Affects retention and whether the Run
   *     shows up in the Application's Runs panel -- nothing about how it executes.
   */
  trigger: RunTrigger;
  /**
   * A Run's execution state, as returned to the client. Carries no pass/fail judgment: a Run
   *      whose every Activity failed is still Completed, and the activity counts say how it went.
   *
   *      Only Completed, Cancelled and Abandoned are legal terminal values to send on End Run -- Pending and
   *      Running name states the server itself moves a Run through and are rejected there.
   *
   *      Running does not by itself mean the owning worker is still alive -- it may have crashed or been
   *      killed without anything having noticed yet. Treat Running as "not yet terminal," and check
   *      LastHeartbeatAt (on RunResponse/RunSummaryResponse) to tell whether it is actually making
   *      progress.
   */
  status: RunStatus;
  /** Only set when Status is Abandoned. */
  statusReason?: null | RunStatusReason;
  /**
   * @format int32
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  totalActivityCount: number | string;
  /**
   * @format int32
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  passedActivityCount: number | string;
  /**
   * @format int32
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  failedActivityCount: number | string;
  /**
   * @format int32
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  skippedActivityCount: number | string;
  /** @format date-time */
  createdAt: string;
  /** @format date-time */
  startedAt?: null | string;
  /** @format date-time */
  completedAt?: null | string;
  /**
   * When the owning worker last proved it was alive. Null while Status is Pending. A Run
   *     stuck on Running with an old LastHeartbeatAt has likely lost its worker -- Status alone does not
   *     tell you that.
   * @format date-time
   */
  lastHeartbeatAt?: null | string;
}

/**
 * Where a Run came from, as returned to the client. Affects retention and whether the Run
 *     shows up in the Application's Runs panel -- nothing about how it executes.
 */
export type RunTrigger = number;

/** A Scenario, as returned to the client. */
export interface ScenarioResponse {
  /** @format uuid */
  id: string;
  title: string;
  description: string;
  folder: string;
  tags: string[];
}

/** Request body to upsert a single Environment variable's value. */
export interface SetEnvironmentVariableRequest {
  /** @maxLength 4000 */
  value: string;
  /** When true, the API masks this variable's value on every future read. */
  isSensitive: boolean;
}

/**
 * Where a Run snapshot was copied from, as returned to the client: provenance only. Id may no
 *     longer resolve to a live row -- the source can have been edited, archived or deleted since this Run
 *     was created -- so it should be treated as a hint for "open the current version, if it still exists",
 *     never as a live reference.
 */
export interface SnapshotSourceResponse {
  /** @format uuid */
  id: string;
  /** @format uuid */
  createdByUserId: string;
  /** @format uuid */
  updatedByUserId: string;
  /** @format date-time */
  createdAt: string;
  /** @format date-time */
  updatedAt: string;
}

/**
 * Request body to edit an existing Activity's Description/PreconditionIds/EvidenceIds.
 *     Does not change the Activity's Order -- use the reorder endpoint for that.
 */
export interface UpdateActivityRequest {
  /** @maxLength 2000 */
  description: string;
  /** @maxItems 15 */
  preconditionIds?: null | string[];
  /** @maxItems 15 */
  evidenceIds?: null | string[];
}

/** Request body to update an existing Environment's Name/Classification. */
export interface UpdateEnvironmentRequest {
  /**
   * Must not be empty or whitespace.
   * @maxLength 100
   */
  name: string;
  /** Whether an Environment is a live Production system or a non-production one (staging, dev, ...). */
  classification: EnvironmentClassification;
}

/** Request body to edit an existing EvidenceDefinition. */
export interface UpdateEvidenceDefinitionRequest {
  /**
   * Must not be empty or whitespace.
   * @maxLength 200
   */
  name: string;
  /** @maxLength 500 */
  description: string;
  /** @maxLength 10000 */
  exampleValue: string;
}

/** Request body to edit an existing Precondition. */
export interface UpdatePreconditionRequest {
  /**
   * Must not be empty or whitespace.
   * @maxLength 200
   */
  name: string;
  /** Where a Precondition's value comes from at execution time. */
  valueSource: PreconditionValueSource;
  /** @maxLength 10000 */
  exampleValue: string;
}

/**
 * Request body to overwrite a Running Run's four activity counts with absolute values -- never
 *     an increment, so a retried call does no harm.
 */
export interface UpdateRunStatsRequest {
  /**
   * @format int32
   * @min 0
   * @max 2147483647
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  totalActivityCount: number | string;
  /**
   * @format int32
   * @min 0
   * @max 2147483647
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  passedActivityCount: number | string;
  /**
   * @format int32
   * @min 0
   * @max 2147483647
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  failedActivityCount: number | string;
  /**
   * @format int32
   * @min 0
   * @max 2147483647
   * @pattern ^-?(?:0|[1-9]\d*)$
   */
  skippedActivityCount: number | string;
}

/** Request body to edit an existing Scenario's Title/Description/Folder/Tags. */
export interface UpdateScenarioRequest {
  /**
   * Must not be empty or whitespace.
   * @maxLength 200
   */
  title: string;
  /** @maxLength 2000 */
  description: string;
  /** @maxLength 300 */
  folder: string;
  /** @maxItems 20 */
  tags?: null | string[];
}

/** The signed-in AutoAssure user, as returned to the client after authentication. */
export interface UserResponse {
  /** @format uuid */
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  emailVerified: boolean;
}

import type {
  AxiosInstance,
  AxiosRequestConfig,
  AxiosResponse,
  HeadersDefaults,
  ResponseType,
} from "axios";
import axios from "axios";

export type QueryParamsType = Record<string | number, any>;

export interface FullRequestParams
  extends Omit<AxiosRequestConfig, "data" | "params" | "url" | "responseType"> {
  /** set parameter to `true` for call `securityWorker` for this request */
  secure?: boolean;
  /** request path */
  path: string;
  /** content type of request body */
  type?: ContentType;
  /** query params */
  query?: QueryParamsType;
  /** format of response (i.e. response.json() -> format: "json") */
  format?: ResponseType;
  /** request body */
  body?: unknown;
}

export type RequestParams = Omit<
  FullRequestParams,
  "body" | "method" | "query" | "path"
>;

export interface ApiConfig<SecurityDataType = unknown>
  extends Omit<AxiosRequestConfig, "data" | "cancelToken"> {
  securityWorker?: (
    securityData: SecurityDataType | null,
  ) => Promise<AxiosRequestConfig | void> | AxiosRequestConfig | void;
  secure?: boolean;
  format?: ResponseType;
}

export enum ContentType {
  Json = "application/json",
  JsonApi = "application/vnd.api+json",
  FormData = "multipart/form-data",
  UrlEncoded = "application/x-www-form-urlencoded",
  Text = "text/plain",
}

export class HttpClient<SecurityDataType = unknown> {
  public instance: AxiosInstance;
  private securityData: SecurityDataType | null = null;
  private securityWorker?: ApiConfig<SecurityDataType>["securityWorker"];
  private secure?: boolean;
  private format?: ResponseType;

  constructor({
    securityWorker,
    secure,
    format,
    ...axiosConfig
  }: ApiConfig<SecurityDataType> = {}) {
    this.instance = axios.create({
      ...axiosConfig,
      baseURL: axiosConfig.baseURL || "",
    });
    this.secure = secure;
    this.format = format;
    this.securityWorker = securityWorker;
  }

  public setSecurityData = (data: SecurityDataType | null) => {
    this.securityData = data;
  };

  protected mergeRequestParams(
    params1: AxiosRequestConfig,
    params2?: AxiosRequestConfig,
  ): AxiosRequestConfig {
    const method = params1.method || (params2 && params2.method);

    return {
      ...this.instance.defaults,
      ...params1,
      ...(params2 || {}),
      headers: {
        ...((method &&
          this.instance.defaults.headers[
            method.toLowerCase() as keyof HeadersDefaults
          ]) ||
          {}),
        ...(params1.headers || {}),
        ...((params2 && params2.headers) || {}),
      },
    };
  }

  protected stringifyFormItem(formItem: unknown) {
    if (typeof formItem === "object" && formItem !== null) {
      return JSON.stringify(formItem);
    } else {
      return `${formItem}`;
    }
  }

  protected createFormData(input: Record<string, unknown>): FormData {
    if (input instanceof FormData) {
      return input;
    }
    return Object.keys(input || {}).reduce((formData, key) => {
      const property = input[key];
      const propertyContent: any[] =
        property instanceof Array ? property : [property];

      for (const formItem of propertyContent) {
        const isFileType = formItem instanceof Blob || formItem instanceof File;
        formData.append(
          key,
          isFileType ? formItem : this.stringifyFormItem(formItem),
        );
      }

      return formData;
    }, new FormData());
  }

  public request = async <T = any, _E = any>({
    secure,
    path,
    type,
    query,
    format,
    body,
    ...params
  }: FullRequestParams): Promise<AxiosResponse<T>> => {
    const secureParams =
      ((typeof secure === "boolean" ? secure : this.secure) &&
        this.securityWorker &&
        (await this.securityWorker(this.securityData))) ||
      {};
    const requestParams = this.mergeRequestParams(params, secureParams);
    const responseFormat = format || this.format || undefined;

    if (
      type === ContentType.FormData &&
      body &&
      body !== null &&
      typeof body === "object"
    ) {
      body = this.createFormData(body as Record<string, unknown>);
    }

    if (
      type === ContentType.Text &&
      body &&
      body !== null &&
      typeof body !== "string"
    ) {
      body = JSON.stringify(body);
    }

    return this.instance.request({
      ...requestParams,
      headers: {
        ...(requestParams.headers || {}),
        ...(type ? { "Content-Type": type } : {}),
      },
      params: query,
      responseType: responseFormat,
      data: body,
      url: path,
    });
  };
}

/**
 * @title A2.Server | v1
 * @version 1.0.0
 */
export class Api<SecurityDataType extends unknown> {
  http: HttpClient<SecurityDataType>;

  constructor(http: HttpClient<SecurityDataType>) {
    this.http = http;
  }

  scenarios = {
    /**
     * No description
     *
     * @tags Activities
     * @name CreateActivity
     * @request POST:/scenarios/{scenarioId}/activities
     * @response `200` `ActivityResponse` OK
     * @response `400` `ErrorResponse` PreconditionIds/EvidenceIds do not reference existing library rows in the Scenario's Application, or the Scenario already has the maximum number of Activities. Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Scenario with the given scenarioId exists in the caller's Organization, or it no longer exists (deleted after this request started).
     */
    createActivity: (
      scenarioId: string,
      data: CreateActivityRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<ActivityResponse, ErrorResponse | ProblemDetails>({
        path: `/scenarios/${scenarioId}/activities`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Activities
     * @name ListActivities
     * @request GET:/scenarios/{scenarioId}/activities
     * @response `200` `(ActivityResponse)[]` OK
     */
    listActivities: (scenarioId: string, params: RequestParams = {}) =>
      this.http.request<ActivityResponse[], any>({
        path: `/scenarios/${scenarioId}/activities`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Activities
     * @name ReorderActivities
     * @request PATCH:/scenarios/{scenarioId}/activities/order
     * @response `200` `(ActivityResponse)[]` OK
     * @response `400` `ErrorResponse` OrderedActivityIds is not exactly a permutation of the Scenario's current Activity ids. Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Scenario with the given scenarioId exists in the caller's Organization.
     */
    reorderActivities: (
      scenarioId: string,
      data: ReorderActivitiesRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<ActivityResponse[], ErrorResponse | ProblemDetails>({
        path: `/scenarios/${scenarioId}/activities/order`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags AuthoringRuns
     * @name CreateAuthoringRun
     * @request POST:/scenarios/{scenarioId}/runs
     * @response `200` `RunResponse` OK
     * @response `400` `ErrorResponse` EnvironmentId does not reference an Environment belonging to the Scenario's Application. Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Scenario with the given scenarioId exists in the caller's Organization.
     */
    createAuthoringRun: (
      scenarioId: string,
      data: CreateAuthoringRunRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<RunResponse, ErrorResponse | ProblemDetails>({
        path: `/scenarios/${scenarioId}/runs`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Scenarios
     * @name GetScenarioById
     * @request GET:/scenarios/{scenarioId}
     * @response `200` `ScenarioResponse` OK
     * @response `404` `ProblemDetails` No Scenario with the given scenarioId exists in the caller's Organization.
     */
    getScenarioById: (scenarioId: string, params: RequestParams = {}) =>
      this.http.request<ScenarioResponse, ProblemDetails>({
        path: `/scenarios/${scenarioId}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Scenarios
     * @name UpdateScenario
     * @request PATCH:/scenarios/{scenarioId}
     * @response `200` `ScenarioResponse` OK
     * @response `400` `ErrorResponse` A tag in Tags is longer than 50 characters. Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Scenario with the given scenarioId exists in the caller's Organization.
     * @response `409` `ErrorResponse` The Scenario's Application no longer exists (deleted after this request started).
     */
    updateScenario: (
      scenarioId: string,
      data: UpdateScenarioRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<ScenarioResponse, ErrorResponse | ProblemDetails>({
        path: `/scenarios/${scenarioId}`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Scenarios
     * @name DeleteScenario
     * @request DELETE:/scenarios/{scenarioId}
     * @response `204` `void` No Content
     * @response `404` `ProblemDetails` No Scenario with the given scenarioId exists in the caller's Organization.
     */
    deleteScenario: (scenarioId: string, params: RequestParams = {}) =>
      this.http.request<void, ProblemDetails>({
        path: `/scenarios/${scenarioId}`,
        method: "DELETE",
        ...params,
      }),
  };
  activities = {
    /**
     * No description
     *
     * @tags Activities
     * @name UpdateActivity
     * @request PATCH:/activities/{activityId}
     * @response `200` `ActivityResponse` OK
     * @response `400` `ErrorResponse` PreconditionIds/EvidenceIds do not reference existing library rows in the Scenario's Application. Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Activity with the given activityId exists in the caller's Organization.
     */
    updateActivity: (
      activityId: string,
      data: UpdateActivityRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<ActivityResponse, ErrorResponse | ProblemDetails>({
        path: `/activities/${activityId}`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Activities
     * @name DeleteActivity
     * @request DELETE:/activities/{activityId}
     * @response `204` `void` No Content
     */
    deleteActivity: (activityId: string, params: RequestParams = {}) =>
      this.http.request<void, any>({
        path: `/activities/${activityId}`,
        method: "DELETE",
        ...params,
      }),
  };
  applications = {
    /**
     * No description
     *
     * @tags Applications
     * @name CreateApplication
     * @request POST:/applications
     * @response `200` `ApplicationResponse` OK
     * @response `400` `ErrorResponse` The caller's Organization could not be found or has been deleted. Returns 400 when the request fails a validation constraint.
     */
    createApplication: (
      data: CreateApplicationRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<ApplicationResponse, ErrorResponse>({
        path: `/applications`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Applications
     * @name ListApplications
     * @request GET:/applications
     * @response `200` `(ApplicationResponse)[]` OK
     */
    listApplications: (params: RequestParams = {}) =>
      this.http.request<ApplicationResponse[], any>({
        path: `/applications`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Applications
     * @name GetApplicationById
     * @request GET:/applications/{applicationId}
     * @response `200` `ApplicationResponse` OK
     * @response `404` `ProblemDetails` No Application with the given applicationId exists in the caller's Organization.
     */
    getApplicationById: (applicationId: string, params: RequestParams = {}) =>
      this.http.request<ApplicationResponse, ProblemDetails>({
        path: `/applications/${applicationId}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Environments
     * @name CreateEnvironment
     * @request POST:/applications/{applicationId}/environments
     * @response `200` `EnvironmentResponse` OK
     * @response `400` `void` Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Application with the given applicationId exists in the caller's Organization.
     */
    createEnvironment: (
      applicationId: string,
      data: CreateEnvironmentRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<EnvironmentResponse, void | ProblemDetails>({
        path: `/applications/${applicationId}/environments`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Environments
     * @name ListEnvironments
     * @request GET:/applications/{applicationId}/environments
     * @response `200` `(EnvironmentResponse)[]` OK
     */
    listEnvironments: (applicationId: string, params: RequestParams = {}) =>
      this.http.request<EnvironmentResponse[], any>({
        path: `/applications/${applicationId}/environments`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags EvidenceDefinitions
     * @name CreateEvidenceDefinition
     * @request POST:/applications/{applicationId}/evidence-definitions
     * @response `200` `EvidenceDefinitionResponse` OK
     * @response `400` `void` Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Application with the given applicationId exists in the caller's Organization.
     */
    createEvidenceDefinition: (
      applicationId: string,
      data: CreateEvidenceDefinitionRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<EvidenceDefinitionResponse, void | ProblemDetails>({
        path: `/applications/${applicationId}/evidence-definitions`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags EvidenceDefinitions
     * @name ListEvidenceDefinitions
     * @request GET:/applications/{applicationId}/evidence-definitions
     * @response `200` `(EvidenceDefinitionResponse)[]` OK
     */
    listEvidenceDefinitions: (
      applicationId: string,
      params: RequestParams = {},
    ) =>
      this.http.request<EvidenceDefinitionResponse[], any>({
        path: `/applications/${applicationId}/evidence-definitions`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Preconditions
     * @name CreatePrecondition
     * @request POST:/applications/{applicationId}/preconditions
     * @response `200` `PreconditionResponse` OK
     * @response `400` `void` Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Application with the given applicationId exists in the caller's Organization.
     */
    createPrecondition: (
      applicationId: string,
      data: CreatePreconditionRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<PreconditionResponse, void | ProblemDetails>({
        path: `/applications/${applicationId}/preconditions`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Preconditions
     * @name ListPreconditions
     * @request GET:/applications/{applicationId}/preconditions
     * @response `200` `(PreconditionResponse)[]` OK
     */
    listPreconditions: (applicationId: string, params: RequestParams = {}) =>
      this.http.request<PreconditionResponse[], any>({
        path: `/applications/${applicationId}/preconditions`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Runs
     * @name CreateRun
     * @request POST:/applications/{applicationId}/runs
     * @response `200` `RunResponse` OK
     * @response `400` `ErrorResponse` EnvironmentId does not reference an Environment belonging to this Application, ScenarioIds contains a duplicate, or ScenarioIds contains an id that does not reference a Scenario belonging to this Application. Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Application with the given applicationId exists in the caller's Organization, or it no longer exists (deleted after this request started).
     */
    createRun: (
      applicationId: string,
      data: CreateRunRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<RunResponse, ErrorResponse | ProblemDetails>({
        path: `/applications/${applicationId}/runs`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Runs
     * @name ListRuns
     * @summary Never returns Authoring Runs -- those are scratch runs against a Scenario under construction, not runs of the Application's saved Scenarios.
     * @request GET:/applications/{applicationId}/runs
     * @response `200` `(RunSummaryResponse)[]` OK
     */
    listRuns: (applicationId: string, params: RequestParams = {}) =>
      this.http.request<RunSummaryResponse[], any>({
        path: `/applications/${applicationId}/runs`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Runs
     * @name ListRunningRuns
     * @summary Lists the Runs currently Running for this Application, strongly consistent -- a Run that just started is never briefly missing from this result, unlike Task&lt;ActionResult&lt;IReadOnlyList&lt;RunSummaryResponse&gt;&gt;&gt; RunsController.List(Guid applicationId).
     * @request GET:/applications/{applicationId}/runs/running
     * @response `200` `(RunningRunResponse)[]` OK
     */
    listRunningRuns: (applicationId: string, params: RequestParams = {}) =>
      this.http.request<RunningRunResponse[], any>({
        path: `/applications/${applicationId}/runs/running`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Runs
     * @name GetRunById
     * @request GET:/applications/{applicationId}/runs/{runId}
     * @response `200` `RunResponse` OK
     * @response `404` `ProblemDetails` No Run with the given runId exists in this Application, in the caller's Organization.
     */
    getRunById: (
      applicationId: string,
      runId: string,
      params: RequestParams = {},
    ) =>
      this.http.request<RunResponse, ProblemDetails>({
        path: `/applications/${applicationId}/runs/${runId}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Runs
     * @name StartRun
     * @summary Claims a Pending Run for execution and returns it, unmasked, to the winning caller only -- the one time in this API's life a sensitive Environment variable's real value is ever returned. Every other response (Create Run, Get Run) always masks sensitive values regardless of what storage currently holds; see RunResponse ContractMapper.ToResponse(Run run, bool maskSensitiveValues = true).
     * @request POST:/applications/{applicationId}/runs/{runId}/start
     * @response `200` `RunResponse` OK
     * @response `404` `ProblemDetails` No Run with the given runId exists in this Application, in the caller's Organization.
     * @response `409` `ErrorResponse` The Run's Status is not Pending.
     */
    startRun: (
      applicationId: string,
      runId: string,
      params: RequestParams = {},
    ) =>
      this.http.request<RunResponse, ProblemDetails | ErrorResponse>({
        path: `/applications/${applicationId}/runs/${runId}/start`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Runs
     * @name EndRun
     * @request POST:/applications/{applicationId}/runs/{runId}/end
     * @response `204` `void` No Content
     * @response `400` `ErrorResponse` TerminalStatus is Pending or Running, or StatusReason is set while TerminalStatus is not Abandoned. Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Run with the given runId exists in this Application, in the caller's Organization.
     * @response `409` `ErrorResponse` The Run's Status is not Running.
     */
    endRun: (
      applicationId: string,
      runId: string,
      data: EndRunRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<void, ErrorResponse | ProblemDetails>({
        path: `/applications/${applicationId}/runs/${runId}/end`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags Runs
     * @name UpdateRunHeartbeat
     * @request POST:/applications/{applicationId}/runs/{runId}/heartbeat
     * @response `204` `void` No Content
     * @response `404` `ProblemDetails` No Run with the given runId exists in this Application, in the caller's Organization.
     * @response `409` `ErrorResponse` The Run's Status is not Running.
     */
    updateRunHeartbeat: (
      applicationId: string,
      runId: string,
      params: RequestParams = {},
    ) =>
      this.http.request<void, ProblemDetails | ErrorResponse>({
        path: `/applications/${applicationId}/runs/${runId}/heartbeat`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Runs
     * @name UpdateRunStats
     * @request POST:/applications/{applicationId}/runs/{runId}/stats
     * @response `204` `void` No Content
     * @response `400` `void` Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Run with the given runId exists in this Application, in the caller's Organization.
     * @response `409` `ErrorResponse` The Run's Status is not Running.
     */
    updateRunStats: (
      applicationId: string,
      runId: string,
      data: UpdateRunStatsRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<void, void | ProblemDetails | ErrorResponse>({
        path: `/applications/${applicationId}/runs/${runId}/stats`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags RunStatusUpdates
     * @name AppendRunStatusUpdate
     * @request POST:/applications/{applicationId}/runs/{runId}/status-updates
     * @response `200` `RunStatusUpdateResponse` OK
     * @response `400` `ErrorResponse` ActivityResult.Status is Pending or Running. Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Run with the given runId exists in this Application, in the caller's Organization.
     * @response `409` `ErrorResponse` Seq is not greater than the Run's current LastSeq, or the Run's Status is not Running.
     */
    appendRunStatusUpdate: (
      applicationId: string,
      runId: string,
      data: AppendRunStatusUpdateRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<
        RunStatusUpdateResponse,
        ErrorResponse | ProblemDetails
      >({
        path: `/applications/${applicationId}/runs/${runId}/status-updates`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags RunStatusUpdates
     * @name ListRunStatusUpdates
     * @request GET:/applications/{applicationId}/runs/{runId}/status-updates
     * @response `200` `(RunStatusUpdateResponse)[]` OK
     * @response `400` `ErrorResponse` after is negative.
     */
    listRunStatusUpdates: (
      applicationId: string,
      runId: string,
      query?: {
        /**
         * Return only updates with a higher Seq than this. Pass 0 (the default) to read
         *     from the start of the log.
         * @format int64
         * @default 0
         * @pattern ^-?(?:0|[1-9]\d*)$
         */
        after?: number | string;
      },
      params: RequestParams = {},
    ) =>
      this.http.request<RunStatusUpdateResponse[], ErrorResponse>({
        path: `/applications/${applicationId}/runs/${runId}/status-updates`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Scenarios
     * @name CreateScenario
     * @request POST:/applications/{applicationId}/scenarios
     * @response `200` `ScenarioResponse` OK
     * @response `400` `ErrorResponse` A tag in Tags is longer than 50 characters. Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Application with the given applicationId exists in the caller's Organization, or it no longer exists (deleted after this request started).
     */
    createScenario: (
      applicationId: string,
      data: CreateScenarioRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<ScenarioResponse, ErrorResponse | ProblemDetails>({
        path: `/applications/${applicationId}/scenarios`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Scenarios
     * @name ListScenarios
     * @request GET:/applications/{applicationId}/scenarios
     * @response `200` `(ScenarioResponse)[]` OK
     * @response `400` `ErrorResponse` Both folder and tag were provided; they are mutually exclusive.
     */
    listScenarios: (
      applicationId: string,
      query?: {
        folder?: string;
        tag?: string;
      },
      params: RequestParams = {},
    ) =>
      this.http.request<ScenarioResponse[], ErrorResponse>({
        path: `/applications/${applicationId}/scenarios`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
  };
  auth = {
    /**
     * No description
     *
     * @tags Auth
     * @name ExchangeGoogleCode
     * @request POST:/auth/google/token
     * @response `200` `AuthTokenResponse` OK
     * @response `400` `void` Returns 400 when the request fails a validation constraint.
     * @response `401` `ErrorResponse` The Google authorization code or PKCE verifier is invalid or expired.
     */
    exchangeGoogleCode: (
      data: ExchangeGoogleCodeRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<AuthTokenResponse, void | ErrorResponse>({
        path: `/auth/google/token`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Auth
     * @name RefreshToken
     * @request POST:/auth/refresh
     * @response `200` `RefreshTokenResponse` OK
     * @response `400` `void` Returns 400 when the request fails a validation constraint.
     * @response `401` `ErrorResponse` The refresh token is invalid, expired, or revoked.
     */
    refreshToken: (data: RefreshTokenRequest, params: RequestParams = {}) =>
      this.http.request<RefreshTokenResponse, void | ErrorResponse>({
        path: `/auth/refresh`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  environments = {
    /**
     * No description
     *
     * @tags Environments
     * @name GetEnvironmentById
     * @request GET:/environments/{environmentId}
     * @response `200` `EnvironmentResponse` OK
     * @response `404` `ProblemDetails` No Environment with the given environmentId exists in the caller's Organization.
     */
    getEnvironmentById: (environmentId: string, params: RequestParams = {}) =>
      this.http.request<EnvironmentResponse, ProblemDetails>({
        path: `/environments/${environmentId}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Environments
     * @name UpdateEnvironment
     * @request PATCH:/environments/{environmentId}
     * @response `200` `EnvironmentResponse` OK
     * @response `400` `void` Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Environment with the given environmentId exists in the caller's Organization.
     */
    updateEnvironment: (
      environmentId: string,
      data: UpdateEnvironmentRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<EnvironmentResponse, void | ProblemDetails>({
        path: `/environments/${environmentId}`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Environments
     * @name SetEnvironmentVariable
     * @request PUT:/environments/{environmentId}/variables/{key}
     * @response `204` `void` No Content
     * @response `400` `void` Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Environment with the given environmentId exists in the caller's Organization, or it no longer exists (deleted after this request started).
     */
    setEnvironmentVariable: (
      environmentId: string,
      key: string,
      data: SetEnvironmentVariableRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<void, void | ProblemDetails>({
        path: `/environments/${environmentId}/variables/${key}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags Environments
     * @name DeleteEnvironmentVariable
     * @request DELETE:/environments/{environmentId}/variables/{key}
     * @response `204` `void` No Content
     * @response `400` `void` Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Environment with the given environmentId exists in the caller's Organization.
     */
    deleteEnvironmentVariable: (
      environmentId: string,
      key: string,
      params: RequestParams = {},
    ) =>
      this.http.request<void, void | ProblemDetails>({
        path: `/environments/${environmentId}/variables/${key}`,
        method: "DELETE",
        ...params,
      }),
  };
  evidenceDefinitions = {
    /**
     * No description
     *
     * @tags EvidenceDefinitions
     * @name UpdateEvidenceDefinition
     * @request PATCH:/evidence-definitions/{evidenceDefinitionId}
     * @response `200` `EvidenceDefinitionResponse` OK
     * @response `400` `void` Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No EvidenceDefinition with the given evidenceDefinitionId exists in the caller's Organization.
     */
    updateEvidenceDefinition: (
      evidenceDefinitionId: string,
      data: UpdateEvidenceDefinitionRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<EvidenceDefinitionResponse, void | ProblemDetails>({
        path: `/evidence-definitions/${evidenceDefinitionId}`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags EvidenceDefinitions
     * @name DeleteEvidenceDefinition
     * @request DELETE:/evidence-definitions/{evidenceDefinitionId}
     * @response `204` `void` No Content
     */
    deleteEvidenceDefinition: (
      evidenceDefinitionId: string,
      params: RequestParams = {},
    ) =>
      this.http.request<void, any>({
        path: `/evidence-definitions/${evidenceDefinitionId}`,
        method: "DELETE",
        ...params,
      }),
  };
  preconditions = {
    /**
     * No description
     *
     * @tags Preconditions
     * @name UpdatePrecondition
     * @request PATCH:/preconditions/{preconditionId}
     * @response `200` `PreconditionResponse` OK
     * @response `400` `void` Returns 400 when the request fails a validation constraint.
     * @response `404` `ProblemDetails` No Precondition with the given preconditionId exists in the caller's Organization.
     */
    updatePrecondition: (
      preconditionId: string,
      data: UpdatePreconditionRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<PreconditionResponse, void | ProblemDetails>({
        path: `/preconditions/${preconditionId}`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Preconditions
     * @name DeletePrecondition
     * @request DELETE:/preconditions/{preconditionId}
     * @response `204` `void` No Content
     */
    deletePrecondition: (preconditionId: string, params: RequestParams = {}) =>
      this.http.request<void, any>({
        path: `/preconditions/${preconditionId}`,
        method: "DELETE",
        ...params,
      }),
  };
}

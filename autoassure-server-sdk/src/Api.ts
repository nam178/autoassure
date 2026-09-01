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

/**
 * A single step within a Scenario, as submitted by the client. PreconditionIds/EvidenceIds
 *     must reference existing library rows in the same Application.
 */
export interface ActivityRequest {
  description: string;
  preconditionIds: null | string[];
  evidenceIds: null | string[];
}

/** A single step within a Scenario, as returned to the client. */
export interface ActivityResponse {
  /** @format uuid */
  id: string;
  description: string;
  preconditionIds: string[];
  evidenceIds: string[];
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

/** Request body to create a new Application in the caller's Organization. */
export interface CreateApplicationRequest {
  name: string;
  description: string;
}

/**
 * Request body to create a new Environment for an Application. No Variables at creation —
 *     set those afterward via `PUT /environments/{id}/variables/{key}`.
 */
export interface CreateEnvironmentRequest {
  name: string;
  /** Whether an Environment is a live Production system or a non-production one (staging, dev, ...). */
  classification: EnvironmentClassification;
}

/** Request body to add an EvidenceDefinition to an Application's library. */
export interface CreateEvidenceDefinitionRequest {
  name: string;
  description: string;
  exampleValue: string;
}

/** Request body to add a Precondition to an Application's library. */
export interface CreatePreconditionRequest {
  name: string;
  /** Where a Precondition's value comes from at execution time. */
  valueSource: PreconditionValueSource;
  exampleValue: string;
}

/** Request body to start a Run of one or more Scenarios against an Environment. */
export interface CreateRunRequest {
  scenarioIds: string[];
  /** @format uuid */
  environmentId: string;
}

/**
 * Request body to create a new Scenario for an Application. Folder defaults to "/" when
 *     not given; Tags default to empty.
 */
export interface CreateScenarioRequest {
  title: string;
  description: string;
  folder: null | string;
  tags: null | string[];
  activities: null | ActivityRequest[];
}

/** Request body to Try a single Scenario against an Environment. */
export interface CreateTryRequest {
  /** @format uuid */
  environmentId: string;
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

/** A single Environment variable, as returned to the client. */
export interface EnvironmentVariableResponse {
  key: string;
  value: string;
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
  /** The authorization code returned by Google after the user consents. */
  code: string;
  /** The PKCE code verifier the client generated for this authorization request. */
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
  /** The raw refresh token secret previously issued to the client, to be exchanged for a new access token. */
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
 * A Run over one or more Scenarios, as returned to the client. Created in Pending status --
 *     nothing here executes anything yet.
 */
export interface RunResponse {
  /** @format uuid */
  id: string;
  scenarioIds: string[];
  /** @format uuid */
  environmentId: string;
  /** A Try/Run's lifecycle state only -- carries no pass/fail judgment; see the activity counts for that. */
  status: RunStatus;
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
  startedAt: null | string;
  /** @format date-time */
  completedAt: null | string;
}

/** A Try/Run's lifecycle state only -- carries no pass/fail judgment; see the activity counts for that. */
export type RunStatus = number;

/** A Scenario, as returned to the client. */
export interface ScenarioResponse {
  /** @format uuid */
  id: string;
  title: string;
  description: string;
  folder: string;
  tags: string[];
  activities: ActivityResponse[];
}

/** Request body to upsert a single Environment variable's value. */
export interface SetEnvironmentVariableRequest {
  value: string;
}

/**
 * A single-Scenario Try, as returned to the client. Created in Pending status -- nothing
 *     here executes anything yet.
 */
export interface TryScenarioResponse {
  /** @format uuid */
  id: string;
  /** @format uuid */
  scenarioId: string;
  /** @format uuid */
  environmentId: string;
  /** A Try/Run's lifecycle state only -- carries no pass/fail judgment; see the activity counts for that. */
  status: RunStatus;
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
  startedAt: null | string;
  /** @format date-time */
  completedAt: null | string;
}

/** Request body to update an existing Environment's Name/Classification. */
export interface UpdateEnvironmentRequest {
  name: string;
  /** Whether an Environment is a live Production system or a non-production one (staging, dev, ...). */
  classification: EnvironmentClassification;
}

/** Request body to edit an existing EvidenceDefinition. */
export interface UpdateEvidenceDefinitionRequest {
  name: string;
  description: string;
  exampleValue: string;
}

/** Request body to edit an existing Precondition. */
export interface UpdatePreconditionRequest {
  name: string;
  /** Where a Precondition's value comes from at execution time. */
  valueSource: PreconditionValueSource;
  exampleValue: string;
}

/** Request body to edit an existing Scenario's Title/Description/Folder/Tags/Activities. */
export interface UpdateScenarioRequest {
  title: string;
  description: string;
  folder: string;
  tags: null | string[];
  activities: null | ActivityRequest[];
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

  applications = {
    /**
     * No description
     *
     * @tags Applications
     * @name CreateApplication
     * @request POST:/applications
     * @response `200` `ApplicationResponse` OK
     * @response `400` `ProblemDetails` The caller's Organization could not be found or has been deleted.
     */
    createApplication: (
      data: CreateApplicationRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<ApplicationResponse, ProblemDetails>({
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
     * @request GET:/applications/{id}
     * @response `200` `ApplicationResponse` OK
     * @response `404` `ProblemDetails` No Application with the given id exists in the caller's Organization.
     */
    getApplicationById: (id: string, params: RequestParams = {}) =>
      this.http.request<ApplicationResponse, ProblemDetails>({
        path: `/applications/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Environments
     * @name CreateEnvironment
     * @request POST:/applications/{appId}/environments
     * @response `200` `EnvironmentResponse` OK
     * @response `404` `ProblemDetails` No Application with the given appId exists in the caller's Organization.
     */
    createEnvironment: (
      appId: string,
      data: CreateEnvironmentRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<EnvironmentResponse, ProblemDetails>({
        path: `/applications/${appId}/environments`,
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
     * @request GET:/applications/{appId}/environments
     * @response `200` `(EnvironmentResponse)[]` OK
     */
    listEnvironments: (appId: string, params: RequestParams = {}) =>
      this.http.request<EnvironmentResponse[], any>({
        path: `/applications/${appId}/environments`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags EvidenceDefinitions
     * @name CreateEvidenceDefinition
     * @request POST:/applications/{appId}/evidence-definitions
     * @response `200` `EvidenceDefinitionResponse` OK
     * @response `400` `ProblemDetails` The Application no longer exists (deleted after this request started).
     * @response `404` `ProblemDetails` No Application with the given appId exists in the caller's Organization.
     */
    createEvidenceDefinition: (
      appId: string,
      data: CreateEvidenceDefinitionRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<EvidenceDefinitionResponse, ProblemDetails>({
        path: `/applications/${appId}/evidence-definitions`,
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
     * @request GET:/applications/{appId}/evidence-definitions
     * @response `200` `(EvidenceDefinitionResponse)[]` OK
     */
    listEvidenceDefinitions: (appId: string, params: RequestParams = {}) =>
      this.http.request<EvidenceDefinitionResponse[], any>({
        path: `/applications/${appId}/evidence-definitions`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Preconditions
     * @name CreatePrecondition
     * @request POST:/applications/{appId}/preconditions
     * @response `200` `PreconditionResponse` OK
     * @response `400` `ProblemDetails` The Application no longer exists (deleted after this request started).
     * @response `404` `ProblemDetails` No Application with the given appId exists in the caller's Organization.
     */
    createPrecondition: (
      appId: string,
      data: CreatePreconditionRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<PreconditionResponse, ProblemDetails>({
        path: `/applications/${appId}/preconditions`,
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
     * @request GET:/applications/{appId}/preconditions
     * @response `200` `(PreconditionResponse)[]` OK
     */
    listPreconditions: (appId: string, params: RequestParams = {}) =>
      this.http.request<PreconditionResponse[], any>({
        path: `/applications/${appId}/preconditions`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Runs
     * @name CreateRun
     * @request POST:/applications/{appId}/runs
     * @response `200` `RunResponse` OK
     * @response `400` `ProblemDetails` EnvironmentId does not reference an Environment belonging to this Application, or the Application/Environment no longer exists (deleted after this request started).
     * @response `404` `ProblemDetails` No Application with the given appId exists in the caller's Organization.
     */
    createRun: (
      appId: string,
      data: CreateRunRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<RunResponse, ProblemDetails>({
        path: `/applications/${appId}/runs`,
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
     * @request GET:/applications/{appId}/runs
     * @response `200` `(RunResponse)[]` OK
     */
    listRuns: (appId: string, params: RequestParams = {}) =>
      this.http.request<RunResponse[], any>({
        path: `/applications/${appId}/runs`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Scenarios
     * @name CreateScenario
     * @request POST:/applications/{appId}/scenarios
     * @response `200` `ScenarioResponse` OK
     * @response `400` `ProblemDetails` Tags are invalid, an Activity's PreconditionIds/EvidenceIds do not reference existing library rows, the total number of unique references exceeds the allowed maximum, or the Application no longer exists (deleted after this request started).
     * @response `404` `ProblemDetails` No Application with the given appId exists in the caller's Organization.
     */
    createScenario: (
      appId: string,
      data: CreateScenarioRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<ScenarioResponse, ProblemDetails>({
        path: `/applications/${appId}/scenarios`,
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
     * @request GET:/applications/{appId}/scenarios
     * @response `200` `(ScenarioResponse)[]` OK
     * @response `400` `ProblemDetails` Both folder and tag were provided; they are mutually exclusive.
     */
    listScenarios: (
      appId: string,
      query?: {
        folder?: string;
        tag?: string;
      },
      params: RequestParams = {},
    ) =>
      this.http.request<ScenarioResponse[], ProblemDetails>({
        path: `/applications/${appId}/scenarios`,
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
     * @response `401` `ProblemDetails` The Google authorization code or PKCE verifier is invalid or expired.
     */
    exchangeGoogleCode: (
      data: ExchangeGoogleCodeRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<AuthTokenResponse, ProblemDetails>({
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
     * @response `401` `ProblemDetails` The refresh token is invalid, expired, or revoked.
     */
    refreshToken: (data: RefreshTokenRequest, params: RequestParams = {}) =>
      this.http.request<RefreshTokenResponse, ProblemDetails>({
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
     * @request GET:/environments/{id}
     * @response `200` `EnvironmentResponse` OK
     * @response `404` `ProblemDetails` No Environment with the given id exists in the caller's Organization.
     */
    getEnvironmentById: (id: string, params: RequestParams = {}) =>
      this.http.request<EnvironmentResponse, ProblemDetails>({
        path: `/environments/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Environments
     * @name UpdateEnvironment
     * @request PATCH:/environments/{id}
     * @response `200` `EnvironmentResponse` OK
     * @response `404` `ProblemDetails` No Environment with the given id exists in the caller's Organization.
     */
    updateEnvironment: (
      id: string,
      data: UpdateEnvironmentRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<EnvironmentResponse, ProblemDetails>({
        path: `/environments/${id}`,
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
     * @request PUT:/environments/{id}/variables/{key}
     * @response `204` `void` No Content
     * @response `400` `ProblemDetails` key exceeds the maximum allowed length, key contains characters other than letters, digits, or underscores, or the Environment no longer exists (deleted after this request started).
     * @response `404` `ProblemDetails` No Environment with the given id exists in the caller's Organization.
     */
    setEnvironmentVariable: (
      id: string,
      key: string,
      data: SetEnvironmentVariableRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<void, ProblemDetails>({
        path: `/environments/${id}/variables/${key}`,
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
     * @request DELETE:/environments/{id}/variables/{key}
     * @response `204` `void` No Content
     * @response `404` `ProblemDetails` No Environment with the given id exists in the caller's Organization.
     */
    deleteEnvironmentVariable: (
      id: string,
      key: string,
      params: RequestParams = {},
    ) =>
      this.http.request<void, ProblemDetails>({
        path: `/environments/${id}/variables/${key}`,
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
     * @request PATCH:/evidence-definitions/{id}
     * @response `200` `EvidenceDefinitionResponse` OK
     * @response `404` `ProblemDetails` No EvidenceDefinition with the given id exists in the caller's Organization.
     */
    updateEvidenceDefinition: (
      id: string,
      data: UpdateEvidenceDefinitionRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<EvidenceDefinitionResponse, ProblemDetails>({
        path: `/evidence-definitions/${id}`,
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
     * @request DELETE:/evidence-definitions/{id}
     * @response `204` `void` No Content
     */
    deleteEvidenceDefinition: (id: string, params: RequestParams = {}) =>
      this.http.request<void, any>({
        path: `/evidence-definitions/${id}`,
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
     * @request PATCH:/preconditions/{id}
     * @response `200` `PreconditionResponse` OK
     * @response `404` `ProblemDetails` No Precondition with the given id exists in the caller's Organization.
     */
    updatePrecondition: (
      id: string,
      data: UpdatePreconditionRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<PreconditionResponse, ProblemDetails>({
        path: `/preconditions/${id}`,
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
     * @request DELETE:/preconditions/{id}
     * @response `204` `void` No Content
     */
    deletePrecondition: (id: string, params: RequestParams = {}) =>
      this.http.request<void, any>({
        path: `/preconditions/${id}`,
        method: "DELETE",
        ...params,
      }),
  };
  runs = {
    /**
     * No description
     *
     * @tags Runs
     * @name GetRunById
     * @request GET:/runs/{id}
     * @response `200` `RunResponse` OK
     * @response `404` `ProblemDetails` No Run with the given id exists in the caller's Organization.
     */
    getRunById: (id: string, params: RequestParams = {}) =>
      this.http.request<RunResponse, ProblemDetails>({
        path: `/runs/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),
  };
  scenarios = {
    /**
     * No description
     *
     * @tags Scenarios
     * @name GetScenarioById
     * @request GET:/scenarios/{id}
     * @response `200` `ScenarioResponse` OK
     * @response `404` `ProblemDetails` No Scenario with the given id exists in the caller's Organization.
     */
    getScenarioById: (id: string, params: RequestParams = {}) =>
      this.http.request<ScenarioResponse, ProblemDetails>({
        path: `/scenarios/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Scenarios
     * @name UpdateScenario
     * @request PATCH:/scenarios/{id}
     * @response `200` `ScenarioResponse` OK
     * @response `400` `ProblemDetails` Tags are invalid, an Activity's PreconditionIds/EvidenceIds do not reference existing library rows, the total number of unique references exceeds the allowed maximum, or the Application no longer exists (deleted after this request started).
     * @response `404` `ProblemDetails` No Scenario with the given id exists in the caller's Organization.
     */
    updateScenario: (
      id: string,
      data: UpdateScenarioRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<ScenarioResponse, ProblemDetails>({
        path: `/scenarios/${id}`,
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
     * @request DELETE:/scenarios/{id}
     * @response `204` `void` No Content
     * @response `404` `ProblemDetails` No Scenario with the given id exists in the caller's Organization.
     */
    deleteScenario: (id: string, params: RequestParams = {}) =>
      this.http.request<void, ProblemDetails>({
        path: `/scenarios/${id}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Tries
     * @name CreateTry
     * @request POST:/scenarios/{id}/try
     * @response `200` `TryScenarioResponse` OK
     * @response `400` `ProblemDetails` EnvironmentId does not reference an Environment belonging to the Scenario's Application, or the Application/Environment no longer exists (deleted after this request started).
     * @response `404` `ProblemDetails` No Scenario with the given id exists in the caller's Organization.
     */
    createTry: (
      id: string,
      data: CreateTryRequest,
      params: RequestParams = {},
    ) =>
      this.http.request<TryScenarioResponse, ProblemDetails>({
        path: `/scenarios/${id}/try`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  tries = {
    /**
     * No description
     *
     * @tags Tries
     * @name GetTryById
     * @request GET:/tries/{id}
     * @response `200` `TryScenarioResponse` OK
     * @response `404` `ProblemDetails` No Try (Run) with the given id exists in the caller's Organization.
     */
    getTryById: (id: string, params: RequestParams = {}) =>
      this.http.request<TryScenarioResponse, ProblemDetails>({
        path: `/tries/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),
  };
}

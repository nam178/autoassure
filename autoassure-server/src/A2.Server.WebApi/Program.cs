using System.Reflection;
using System.Text;
using A2.Server.Common;
using A2.Server.Engine.AsyncProcessing;
using A2.Server.Engine.Repositories;
using A2.Server.Engine.Services;
using A2.Server.UserManagement;
using A2.Server.UserManagement.Repositories;
using A2.Server.UserManagement.Services;
using A2.Server.WebApi.Common;
using A2.Server.WebApi.Contracts;
using A2.Server.WebApi.Services;
using Amazon.DynamoDBv2;
using Amazon.SQS;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// During `dotnet build`, the OpenAPI generator boots this app under another entry assembly
// just to read routes, with no AWS access. Skip anything that needs AWS in that case.
var isDesignTimeBuild =
    Assembly.GetEntryAssembly()?.GetName().Name != "A2.Server.WebApi";

var ssmParameterPath = builder.Configuration["Ssm:ParameterPath"];
if (!isDesignTimeBuild && !string.IsNullOrEmpty(ssmParameterPath))
    builder.Configuration.AddSystemsManager(ssmParameterPath);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    options.AddSchemaTransformer<NotBlankSchemaTransformer>();
    options.AddSchemaTransformer<NoNullItemsSchemaTransformer>();
    options.AddOperationTransformer<RequestValidationOperationTransformer>();
    options.AddOperationTransformer<ArchivedOrganizationOperationTransformer>();
});
builder
    .Services.AddControllers(options =>
    {
        // Register the global filter that blocks writes to archived organizations
        options.Filters.Add<RequireActiveOrganizationFilter>();
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // When automatic DataAnnotations validation (e.g. [NotBlank]) fails, then respond with the
        // same ErrorResponse shape used by every other 400 in this API, instead of ASP.NET's
        // default ValidationProblemDetails, so API consumers only handle one error shape.
        options.InvalidModelStateResponseFactory = context =>
        {
            var message =
                context
                    .ModelState.Values.SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault(m => !string.IsNullOrEmpty(m))
                ?? "Request is invalid.";
            return new BadRequestObjectResult(new ErrorResponse(message));
        };
    })
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.RespectNullableAnnotations = true
    );
builder.Services.Configure<GoogleAuthOptions>(
    builder.Configuration.GetSection("OAuth:Google")
);
builder.Services.Configure<AuthTokenOptions>(
    builder.Configuration.GetSection("Auth")
);
builder.Services.Configure<DynamoDbOptions>(
    builder.Configuration.GetSection("DynamoDb")
);
builder.Services.AddSingleton<
    IGoogleIdTokenValidator,
    GoogleIdTokenValidator
>();
builder.Services.AddHttpClient<
    IGoogleTokenExchangeService,
    GoogleTokenExchangeService
>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<
    IRefreshTokenRepository,
    DynamoDbRefreshTokenRepository
>();
builder.Services.AddScoped<IUserRepository, DynamoDbUserRepository>();
builder.Services.AddScoped<
    IOrganizationRepository,
    DynamoDbOrganizationRepository
>();
builder.Services.AddScoped<
    IOrganizationUserRepository,
    DynamoDbOrganizationUserRepository
>();
builder.Services.AddScoped<IGoogleUserSyncService, GoogleUserSyncService>();
builder.Services.AddScoped<IAuthTokenService, AuthTokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<
    ICallerOrganizationService,
    CallerOrganizationService
>();
builder.Services.AddScoped<
    IApplicationRepository,
    DynamoDbApplicationRepository
>();
builder.Services.AddScoped<
    IEnvironmentRepository,
    DynamoDbEnvironmentRepository
>();
builder.Services.AddScoped<
    IEnvironmentVariableRepository,
    DynamoDbEnvironmentVariableRepository
>();
builder.Services.AddScoped<
    IPreconditionRepository,
    DynamoDbPreconditionRepository
>();
builder.Services.AddScoped<
    IEvidenceDefinitionRepository,
    DynamoDbEvidenceDefinitionRepository
>();
builder.Services.AddScoped<IScenarioRepository, DynamoDbScenarioRepository>();
builder.Services.AddScoped<IActivityRepository, DynamoDbActivityRepository>();
builder.Services.AddScoped<IRunRepository, DynamoDbRunRepository>();
builder.Services.AddScoped<IRunSnapshotBuilder, RunSnapshotBuilder>();
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.Configure<WorkerQueueOptions>(
    builder.Configuration.GetSection("WorkerQueue")
);
builder.Services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient());
builder.Services.AddSingleton<IWorkerQueuePublisher, SqsWorkerQueuePublisher>();
if (!isDesignTimeBuild)
    builder.Services.AddHostedService<ConfigValidationHostedService>();

// Allow CORS, allowing the web page to invoke this APIs directly
var allowedOrigins =
    builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()
    );
});

// Add Authorization
builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(jwtOptions =>
    {
        // Keep JWT claim types as-issued (e.g. "sub") instead of ASP.NET's default remapping to
        // long-form ClaimTypes URIs, so ClaimsPrincipalExtensions.GetUserId() can read Sub directly.
        jwtOptions.MapInboundClaims = false;
        var tokenOptions = builder
            .Configuration.GetSection("Auth")
            .Get<AuthTokenOptions>()!;
        jwtOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = tokenOptions.Issuer,
            ValidAudience = tokenOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(tokenOptions.SigningKey)
            ),
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public abstract partial class Program { }

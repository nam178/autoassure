using A2.Server.Common;
using A2.Server.Engine.AsyncProcessing;
using A2.Server.Engine.AsyncProcessing.Messages;
using A2.Server.Workers;
using A2.Server.Workers.Handlers;
using Amazon.DynamoDBv2;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

var ssmParameterPath = builder.Configuration["Ssm:ParameterPath"];
if (!string.IsNullOrEmpty(ssmParameterPath))
    builder.Configuration.AddSystemsManager(ssmParameterPath);

builder.Services.Configure<DynamoDbOptions>(
    builder.Configuration.GetSection("DynamoDb")
);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder
    .Services.AddOptions<WorkerQueueOptions>()
    .Bind(builder.Configuration.GetSection("WorkerQueue"))
    .Validate(
        options => !string.IsNullOrEmpty(options.QueueUrl),
        "WorkerQueue:QueueUrl is not configured"
    )
    .ValidateOnStart();
builder
    .Services.AddOptions<WorkerVisibilityOptions>()
    .Bind(builder.Configuration.GetSection("WorkerVisibility"))
    .Validate(
        options => options.VisibilityTimeoutSeconds > 0,
        "WorkerVisibility:VisibilityTimeoutSeconds must be a positive number"
    )
    .ValidateOnStart();
builder
    .Services.AddOptions<WorkerConcurrencyOptions>()
    .Bind(builder.Configuration.GetSection("WorkerConcurrency"))
    .Validate(
        options => options.IsValid(),
        "WorkerConcurrency options are invalid: min must be at least 1, max must not be below min, sample interval must be at least 1 second, and each low threshold must be below its high threshold"
    )
    .ValidateOnStart();
builder.Services.AddSingleton(serviceProvider => new ConcurrencyGate(
    serviceProvider
        .GetRequiredService<IOptions<WorkerConcurrencyOptions>>()
        .Value.MinConcurrentMessages
));
if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows())
{
    builder.Services.AddResourceMonitoring();
    builder.Services.AddSingleton<
        IHostLoadReader,
        ResourceMonitoringHostLoadReader
    >();
}
else
{
    builder.Services.AddSingleton<IHostLoadReader, FakeHostLoadReader>();
}
builder.Services.AddHostedService<WorkerConcurrencyAdjuster>();
builder.Services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient());
builder.Services.AddSingleton<WorkerMessageDispatcher>();
builder.Services.AddWorkerMessageHandler<
    DeleteApplicationMessage,
    DeleteApplicationMessageHandler
>(TimeSpan.FromMinutes(30));
builder.Services.AddWorkerMessageHandler<
    DeleteScenarioMessage,
    DeleteScenarioMessageHandler
>(TimeSpan.FromMinutes(30));
builder.Services.AddWorkerMessageHandler<
    ExecuteRunMessage,
    ExecuteRunMessageHandler
>(TimeSpan.FromMinutes(30));
builder.Services.AddHostedService<SqsWorkerQueueConsumer>();

var host = builder.Build();
await host.RunAsync();

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

var builder = Host.CreateApplicationBuilder(args);

var ssmParameterPath = builder.Configuration["AUTOASSURE_SSM_PARAMETER_PATH"];
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

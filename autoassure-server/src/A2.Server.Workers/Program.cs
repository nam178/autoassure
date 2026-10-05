using A2.Server.Common;
using A2.Server.Workers;
using Amazon.DynamoDBv2;
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
builder.Services.AddHostedService<WorkerService>();

var host = builder.Build();
await host.RunAsync();

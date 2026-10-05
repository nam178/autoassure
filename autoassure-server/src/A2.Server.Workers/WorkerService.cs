using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace A2.Server.Workers;

public sealed partial class WorkerService(ILogger<WorkerService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogWorkerStarted(logger);
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "worker started")]
    private static partial void LogWorkerStarted(ILogger logger);
}

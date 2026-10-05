using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace A2.Server.UserManagement.Services;

// Fails startup immediately if any secret we expect to come from SSM Parameter Store
// (declared as "" in appsettings.json) is still empty once configuration is loaded.
public partial class ConfigValidationHostedService(
    IConfiguration configuration,
    ILogger<ConfigValidationHostedService> logger
) : IHostedService
{
    private static readonly string[] RequiredSecretKeys =
    [
        "OAuth:Google:ClientId",
        "OAuth:Google:ClientSecret",
        "Auth:SigningKey",
    ];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var missingKeys = RequiredSecretKeys
            .Where(key => string.IsNullOrEmpty(configuration[key]))
            .ToList();

        if (missingKeys.Count > 0)
        {
#pragma warning disable CA1873
            LogMissingConfigurationKeys(logger, string.Join(", ", missingKeys));
#pragma warning restore CA1873
            Environment.Exit(1);
        }

        foreach (var key in RequiredSecretKeys)
            LogValidatedConfigurationKey(logger, key);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "Startup aborted: required configuration values are missing or empty: {MissingKeys}"
    )]
    private static partial void LogMissingConfigurationKeys(
        ILogger logger,
        string missingKeys
    );

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Validated configuration key {Key} is present"
    )]
    private static partial void LogValidatedConfigurationKey(
        ILogger logger,
        string key
    );
}

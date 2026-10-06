using Microsoft.Extensions.DependencyInjection;

namespace A2.Server.Workers;

public sealed class WorkerMessageDispatcher
{
    private readonly Dictionary<string, WorkerMessageRoute> _routesByKind;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public WorkerMessageDispatcher(
        IEnumerable<WorkerMessageRoute> routes,
        IServiceScopeFactory serviceScopeFactory
    )
    {
        _routesByKind = routes.ToDictionary(route => route.Kind);
        _serviceScopeFactory = serviceScopeFactory;
    }

    /// <summary>
    /// Runs the handler for <paramref name="kind" /> in its own DI scope, with
    /// the time limit of that kind. Handler exceptions, and
    /// <see cref="System.Text.Json.JsonException" /> for a body that does not
    /// match the kind, are not caught. <paramref name="shutdownToken" />
    /// cancelling surfaces as <see cref="OperationCanceledException" />, not as
    /// a time limit.
    /// </summary>
    public async Task<WorkerMessageDispatchResult> DispatchMessage(
        string kind,
        string body,
        CancellationToken shutdownToken
    )
    {
        if (!_routesByKind.TryGetValue(kind, out var route))
            return WorkerMessageDispatchResult.UnknownKind;

        using var timeLimitSource =
            CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        timeLimitSource.CancelAfter(route.TimeLimit);

        using var scope = _serviceScopeFactory.CreateScope();
        try
        {
            await route.HandleBody(
                scope.ServiceProvider,
                body,
                timeLimitSource.Token
            );
            return WorkerMessageDispatchResult.Handled;
        }
        catch (OperationCanceledException)
            when (timeLimitSource.IsCancellationRequested
                && !shutdownToken.IsCancellationRequested
            )
        {
            return WorkerMessageDispatchResult.TimeLimitExceeded;
        }
    }
}

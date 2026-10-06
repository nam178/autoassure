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
    /// Runs the handler for <paramref name="kind" /> on one message. The handler
    /// gets its own DI scope and the time limit of that kind.
    /// <list type="bullet">
    /// <item>When <paramref name="kind" /> has no handler, it returns
    /// <see cref="WorkerMessageDispatchResult.UnknownKind" />.</item>
    /// <item>When the handler finishes, it returns
    /// <see cref="WorkerMessageDispatchResult.Handled" />.</item>
    /// <item>When the time limit of the kind runs out, it returns
    /// <see cref="WorkerMessageDispatchResult.TimeLimitExceeded" />.</item>
    /// <item>When <paramref name="programCancellationToken" /> is cancelled, it throws
    /// <see cref="OperationCanceledException" />. This is not a time
    /// limit.</item>
    /// <item>When the handler throws, it rethrows the same exception.</item>
    /// <item>When <paramref name="body" /> does not match the kind, it throws
    /// <see cref="System.Text.Json.JsonException" />.</item>
    /// </list>
    /// </summary>
    public async Task<WorkerMessageDispatchResult> DispatchMessage(
        string kind,
        string body,
        CancellationToken programCancellationToken
    )
    {
        if (!_routesByKind.TryGetValue(kind, out var route))
            return WorkerMessageDispatchResult.UnknownKind;

        using var timeLimitCancellationToken =
            CancellationTokenSource.CreateLinkedTokenSource(
                programCancellationToken
            );
        timeLimitCancellationToken.CancelAfter(route.TimeLimit);

        using var scope = _serviceScopeFactory.CreateScope();
        try
        {
            await route.HandleBody(
                scope.ServiceProvider,
                body,
                timeLimitCancellationToken.Token
            );
            return WorkerMessageDispatchResult.Handled;
        }
        catch (OperationCanceledException)
            when (timeLimitCancellationToken.IsCancellationRequested
                && !programCancellationToken.IsCancellationRequested
            )
        {
            return WorkerMessageDispatchResult.TimeLimitExceeded;
        }
    }
}

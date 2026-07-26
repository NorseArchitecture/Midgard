using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// Converts any exception the chain doesn't already model as data into <see cref="ErrorCategory.Fault"/>
/// — as a returned <see cref="Outcome{T}"/>, never rethrown past this point (spec §2.5, §2.6).
/// <see cref="OperationCanceledException"/> on the caller's own token is never caught; it propagates
/// so the channel's native cancellation handling takes over.
///
/// Stays <c>internal</c> (2026-07-25): see <see cref="TelemetryBehavior{TRequest,TResponse}"/>'s
/// remark — visible to InProcessHost-mode consumers via this project's <c>InternalsVisibleTo</c>
/// grant, not by widening to <c>public</c>.
/// </summary>
sealed class ExceptionTranslationBehavior<TRequest, TResponse>(ILogger<ExceptionTranslationBehavior<TRequest, TResponse>> logger)
	: IBehavior<TRequest, TResponse>
	where TResponse : notnull
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, CancellationToken cancellationToken, BehaviorDelegate<TResponse> next)
	{
		try
		{
			return await next().ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			var correlationId = Guid.NewGuid();
#pragma warning disable CA1848 // Use LoggerMessage delegates
			logger.LogError(ex, "Unhandled exception, correlation id {CorrelationId}", correlationId);
#pragma warning restore CA1848
			return Outcome<TResponse>.Err(ErrorCategory.Fault, correlationId: correlationId);
		}
	}
}

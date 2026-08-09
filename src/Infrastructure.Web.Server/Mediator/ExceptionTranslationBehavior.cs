using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
///     Converts any exception the chain doesn't already model as data into <see cref="ErrorCategory.Fault" />
///     — as a returned <see cref="Outcome{T}" />, never rethrown past this point (spec §2.5, §2.6).
///     <see cref="OperationCanceledException" /> on the caller's own token is never caught; it propagates
///     so the channel's native cancellation handling takes over.
/// </summary>
sealed partial class ExceptionTranslationBehavior<TRequest, TResponse>(
	ILogger<ExceptionTranslationBehavior<TRequest, TResponse>> logger) :
	IBehavior<TRequest, TResponse>
	where TResponse : notnull
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, BehaviorDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
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
			LogUnhandledException(logger, ex, correlationId);
			return Outcome<TResponse>.Err(ErrorCategory.Fault, correlationId: correlationId);
		}
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception, correlation id {CorrelationId}")]
	static partial void LogUnhandledException(ILogger logger, Exception ex, Guid correlationId);
}

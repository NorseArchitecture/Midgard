using System.Diagnostics;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// Outermost behavior in the standard chain (spec §2.5) — sits outside
/// <see cref="ExceptionTranslationBehavior{TRequest,TResponse}"/> specifically so it reads the
/// finished outcome problem details directly off the return value rather than watching an exception
/// fly past unlabeled. Trusted not to throw — it is not itself further wrapped.
/// </summary>
sealed partial class TelemetryBehavior<TRequest, TResponse>(ILogger<TelemetryBehavior<TRequest, TResponse>> logger) :
	IBehavior<TRequest, TResponse>
	where TResponse : notnull
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, BehaviorDelegate<TResponse> next, CancellationToken cancellationToken = default)
	{
		var stopwatch = Stopwatch.StartNew();
		var outcome = await next().ConfigureAwait(false);
		stopwatch.Stop();

		switch (outcome)
		{
			case Success<TResponse>:
				LogSucceeded(logger, typeof(TRequest).Name, stopwatch.ElapsedMilliseconds);
				break;
			case Failed({ Category: ErrorCategory.Fault } problem):
				LogFaulted(logger, typeof(TRequest).Name, stopwatch.ElapsedMilliseconds, problem.CorrelationId);
				break;
			case Failed(var problem):
				LogFailed(logger, typeof(TRequest).Name, stopwatch.ElapsedMilliseconds, problem.Category);
				break;
		}

		return outcome;
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "{RequestType} succeeded in {ElapsedMs}ms")]
	static partial void LogSucceeded(ILogger logger, string requestType, long elapsedMs);

	[LoggerMessage(Level = LogLevel.Warning, Message = "{RequestType} faulted in {ElapsedMs}ms, correlation id {CorrelationId}")]
	static partial void LogFaulted(ILogger logger, string requestType, long elapsedMs, Guid? correlationId);

	[LoggerMessage(Level = LogLevel.Information, Message = "{RequestType} failed in {ElapsedMs}ms with {Category}")]
	static partial void LogFailed(ILogger logger, string requestType, long elapsedMs, ErrorCategory category);
}

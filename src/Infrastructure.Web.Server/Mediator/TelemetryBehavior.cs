using System.Diagnostics;
#pragma warning disable IDE0005 // Using directive is unnecessary
using Microsoft.Extensions.Logging;
#pragma warning restore IDE0005
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// Outermost behavior in the standard chain (spec §2.5) — sits outside
/// <see cref="ExceptionTranslationBehavior{TRequest,TResponse}"/> specifically so it reads the
/// finished outcome problem details directly off the return value rather than watching an exception
/// fly past unlabeled. Trusted not to throw — it is not itself further wrapped.
/// </summary>
sealed class TelemetryBehavior<TRequest, TResponse>(ILogger<TelemetryBehavior<TRequest, TResponse>> logger)
	: IBehavior<TRequest, TResponse>
	where TResponse : notnull
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, CancellationToken cancellationToken, BehaviorDelegate<TResponse> next)
	{
		var stopwatch = Stopwatch.StartNew();
		var outcome = await next().ConfigureAwait(false);
		stopwatch.Stop();

		switch (outcome)
		{
			case Norse.Primitives.Success<TResponse>:
#pragma warning disable CA1848 // Use LoggerMessage delegates
#pragma warning disable CA1873 // Avoid unnecessary logging
				logger.LogInformation("{RequestType} succeeded in {ElapsedMs}ms", typeof(TRequest).Name, stopwatch.ElapsedMilliseconds);
#pragma warning restore CA1873
#pragma warning restore CA1848
				break;
			case Failed(var problem) when problem.Category == ErrorCategory.Fault:
#pragma warning disable CA1848 // Use LoggerMessage delegates
#pragma warning disable CA1873 // Avoid unnecessary logging
				logger.LogWarning("{RequestType} faulted in {ElapsedMs}ms, correlation id {CorrelationId}",
					typeof(TRequest).Name, stopwatch.ElapsedMilliseconds, problem.CorrelationId);
#pragma warning restore CA1873
#pragma warning restore CA1848
				break;
			case Failed(var problem):
#pragma warning disable CA1848 // Use LoggerMessage delegates
#pragma warning disable CA1873 // Avoid unnecessary logging
				logger.LogInformation("{RequestType} failed in {ElapsedMs}ms with {Category}",
					typeof(TRequest).Name, stopwatch.ElapsedMilliseconds, problem.Category);
#pragma warning restore CA1873
#pragma warning restore CA1848
				break;
		}

		return outcome;
	}
}

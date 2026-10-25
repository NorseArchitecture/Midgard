using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class TelemetryBehaviorTests
{
	[Fact]
	async Task Chain_UnhandledException_BecomesFaultOutcome_NotRethrown()
	{
		TelemetryBehavior<string, bool> telemetry = new(NullLogger<TelemetryBehavior<string, bool>>.Instance);
		ExceptionTranslationBehavior<string, bool> translation =
			new(NullLogger<ExceptionTranslationBehavior<string, bool>>.Instance);

#pragma warning disable IDE0062 // Local function can be made static
		Outcome<bool> Result() => throw new InvalidOperationException("boom");
#pragma warning restore IDE0062

		var outcome = await telemetry.Handle("request",
			() => translation.Handle("request", () => ValueTask.FromResult(Result()),
				TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Fault);
		failed.Problem.CorrelationId.ShouldNotBeNull();
	}

	[Fact]
	async Task Chain_SuccessfulCall_PassesThroughUnchanged()
	{
		TelemetryBehavior<string, bool> telemetry = new(NullLogger<TelemetryBehavior<string, bool>>.Instance);
		ExceptionTranslationBehavior<string, bool> translation =
			new(NullLogger<ExceptionTranslationBehavior<string, bool>>.Instance);

		var outcome = await telemetry.Handle("request",
			() => translation.Handle("request",
				() => ValueTask.FromResult(Outcome<bool>.Ok(true)), TestContext.Current.CancellationToken),
			TestContext.Current.CancellationToken);

		outcome.TryGetValue(out Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
	}

	[Fact]
	async Task Chain_CooperativeCancellation_PropagatesAsOperationCanceledException()
	{
		TelemetryBehavior<string, bool> telemetry = new(NullLogger<TelemetryBehavior<string, bool>>.Instance);
		ExceptionTranslationBehavior<string, bool> translation =
			new(NullLogger<ExceptionTranslationBehavior<string, bool>>.Instance);
		using CancellationTokenSource cts = new();
		await cts.CancelAsync();
		await Should.ThrowAsync<OperationCanceledException>(async () =>
			await telemetry.Handle("request",
				() => translation.Handle("request",
					() => throw new OperationCanceledException(cts.Token), cts.Token), cts.Token)
		);
	}
	[Fact]
	async Task Indeterminate_failure_logs_at_warning_with_correlation_id()
	{
		CapturingLogger<TelemetryBehavior<string, bool>> logger = new();
		TelemetryBehavior<string, bool> telemetry = new(logger);
		var correlationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

		await telemetry.Handle("request",
			() => ValueTask.FromResult(Outcome<bool>.Err(ErrorCategory.Indeterminate, correlationId: correlationId)),
			TestContext.Current.CancellationToken);

		var entry = logger.Entries.ShouldHaveSingleItem();
		entry.Level.ShouldBe(LogLevel.Warning);
		entry.Message.ShouldContain(correlationId.ToString());
		entry.Message.ShouldContain("indeterminate");
	}

	sealed class CapturingLogger<T> : ILogger<T>
	{
		public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = [];

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter) =>
			Entries.Add((logLevel, eventId, formatter(state, exception)));
	}
}

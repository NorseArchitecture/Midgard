using Microsoft.Extensions.Logging.Abstractions;
using Norse.Abstractions.Contracts;
#pragma warning disable IDE0005 // Using directive is unnecessary
using Norse.Abstractions.Web.Server.Mediator;
#pragma warning restore IDE0005
using Norse.Infrastructure.Web.Server.Mediator;
#pragma warning disable IDE0005 // Using directive is unnecessary
using Shouldly;
#pragma warning restore IDE0005

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class TelemetryBehaviorTests
{
	[Fact]
	async Task Chain_UnhandledException_BecomesFaultOutcome_NotRethrown()
	{
		var telemetry = new TelemetryBehavior<string, bool>(NullLogger<TelemetryBehavior<string, bool>>.Instance);
		var translation = new ExceptionTranslationBehavior<string, bool>(NullLogger<ExceptionTranslationBehavior<string, bool>>.Instance);

#pragma warning disable IDE0062 // Local function can be made static
		Outcome<bool> Result() => throw new InvalidOperationException("boom");
#pragma warning restore IDE0062

		var outcome = await telemetry.Handle("request", CancellationToken.None,
			() => translation.Handle("request", CancellationToken.None,
				() => ValueTask.FromResult(Result())));

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Fault);
		failed.Problem.CorrelationId.ShouldNotBeNull();
	}

	[Fact]
	async Task Chain_SuccessfulCall_PassesThroughUnchanged()
	{
		var telemetry = new TelemetryBehavior<string, bool>(NullLogger<TelemetryBehavior<string, bool>>.Instance);
		var translation = new ExceptionTranslationBehavior<string, bool>(NullLogger<ExceptionTranslationBehavior<string, bool>>.Instance);

		var outcome = await telemetry.Handle("request", CancellationToken.None,
			() => translation.Handle("request", CancellationToken.None,
				() => ValueTask.FromResult(Outcome<bool>.Ok(true))));

		outcome.TryGetValue(out Norse.Primitives.Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
	}

	[Fact]
	async Task Chain_CooperativeCancellation_PropagatesAsOperationCanceledException()
	{
		var telemetry = new TelemetryBehavior<string, bool>(NullLogger<TelemetryBehavior<string, bool>>.Instance);
		var translation = new ExceptionTranslationBehavior<string, bool>(NullLogger<ExceptionTranslationBehavior<string, bool>>.Instance);
		using var cts = new CancellationTokenSource();
#pragma warning disable CA1849 // Cancel is not blocking
		cts.Cancel();
#pragma warning restore CA1849
		await Should.ThrowAsync<OperationCanceledException>(async () =>
#pragma warning disable CA2007 // Test context does not require ConfigureAwait
			await telemetry.Handle("request", cts.Token,
				() => translation.Handle("request", cts.Token,
					() => throw new OperationCanceledException(cts.Token)))
#pragma warning restore CA2007
			);
	}
}

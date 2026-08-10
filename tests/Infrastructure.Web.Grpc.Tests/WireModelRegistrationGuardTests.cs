using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc.Tests;

public sealed class WireModelRegistrationGuardTests
{
	[Fact]
	void Runs_the_register_action_exactly_once_for_repeated_calls_with_the_same_key()
	{
		var model = RuntimeTypeModel.Create();
		var runCount = 0;

		model.EnsureRegistered(typeof(WireModelRegistrationGuardTests), () => Interlocked.Increment(ref runCount));
		model.EnsureRegistered(typeof(WireModelRegistrationGuardTests), () => Interlocked.Increment(ref runCount));
		model.EnsureRegistered(typeof(WireModelRegistrationGuardTests), () => Interlocked.Increment(ref runCount));

		runCount.ShouldBe(1);
	}

	[Fact]
	void Treats_different_keys_on_the_same_model_as_independent()
	{
		var model = RuntimeTypeModel.Create();
		var firstRuns = 0;
		var secondRuns = 0;

		model.EnsureRegistered(typeof(string), () => Interlocked.Increment(ref firstRuns));
		model.EnsureRegistered(typeof(int), () => Interlocked.Increment(ref secondRuns));

		firstRuns.ShouldBe(1);
		secondRuns.ShouldBe(1);
	}

	[Fact]
	void Treats_the_same_key_on_different_models_as_independent()
	{
		var firstModel = RuntimeTypeModel.Create();
		var secondModel = RuntimeTypeModel.Create();
		var runCount = 0;

		firstModel.EnsureRegistered(typeof(WireModelRegistrationGuardTests), () => Interlocked.Increment(ref runCount));
		secondModel.EnsureRegistered(typeof(WireModelRegistrationGuardTests),
			() => Interlocked.Increment(ref runCount));

		runCount.ShouldBe(2);
	}

	[Fact]
	void Throws_on_a_null_model() =>
		Should.Throw<ArgumentNullException>(() =>
			WireModelRegistrationGuard.EnsureRegistered(null!, typeof(string), () => { }));

	[Fact]
	void Throws_on_a_null_key()
	{
		var model = RuntimeTypeModel.Create();
		Should.Throw<ArgumentNullException>(() => model.EnsureRegistered(null!, () => { }));
	}

	[Fact]
	void Throws_on_a_null_register()
	{
		var model = RuntimeTypeModel.Create();
		Should.Throw<ArgumentNullException>(() => model.EnsureRegistered(typeof(string), null!));
	}

	[Fact]
	async Task Every_concurrent_first_touch_caller_blocks_until_registration_completes()
	{
		// Regression coverage for the general primitive, generalizing the site-specific concurrency
		// tests already shipped for IdentifierSerializers/ResultSerializers
		// (../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md and its follow-up,
		// ../../../Glitnir/docs/Midgard/specs/2026-08-06-wire-model-registration-guard-design.md).
		const int ModelCount = 500;
		const int CallersPerModel = 8;

		await Task.WhenAll(Enumerable.Range(0, ModelCount).Select(async _ =>
		{
			var model = RuntimeTypeModel.Create();
			var registeredFlag = 0;
			using Barrier barrier = new(CallersPerModel);

			await Task.WhenAll(Enumerable.Range(0, CallersPerModel).Select(_ => Task.Run(() =>
			{
				barrier.SignalAndWait();
				model.EnsureRegistered(typeof(WireModelRegistrationGuardTests),
					() => Volatile.Write(ref registeredFlag, 1));
				Volatile.Read(ref registeredFlag).ShouldBe(1);
			})));
		}));
	}

	[Fact]
	void A_throwing_register_action_surfaces_the_same_exception_to_every_caller_not_just_the_first()
	{
		var model = RuntimeTypeModel.Create();

		var firstException = Should.Throw<InvalidOperationException>(() =>
			model.EnsureRegistered(typeof(WireModelRegistrationGuardTests),
				() => throw new InvalidOperationException("registration failed")));
		var secondException = Should.Throw<InvalidOperationException>(() =>
			model.EnsureRegistered(typeof(WireModelRegistrationGuardTests),
				() => throw new InvalidOperationException("a different message -- never reached")));

		secondException.ShouldBeSameAs(firstException);
	}
}

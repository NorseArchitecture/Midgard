using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Grpc.Analyzers.Tests;

public sealed class WireModelGuardAnalyzerTests
{
	const string DirectAddOutsideGuard =
		"""
		using ProtoBuf.Meta;

		namespace App;

		static class Leak
		{
			public static void Register(RuntimeTypeModel model) =>
				model.Add(typeof(string), applyDefaultBehaviour: false);
		}
		""";

	[Fact]
	async Task Strikes_norse080_on_a_direct_Add_call_outside_the_guard()
	{
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App", [], DirectAddOutsideGuard);
		diagnostics.ShouldContain(d =>
			d.Id == "NORSE080" && d.GetMessage(CultureInfo.InvariantCulture).Contains("Add", StringComparison.Ordinal));
	}

	[Fact]
	async Task Does_not_strike_on_a_bare_IsDefined_read_with_no_paired_Add()
	{
		// A read-only check never mutates the model, so it can't itself cause the TOCTOU race this rule
		// exists to close -- and since Add is banned outside the guard, nothing can write unguarded either
		// way, so a preceding IsDefined read is inert regardless. Mirrors the real, legitimate pattern in
		// Yggdrasil's CompositionTests.cs (RuntimeTypeModel.Default.IsDefined(...).ShouldBeTrue()).
		const string BareReadOnlyIsDefined =
			"""
			using ProtoBuf.Meta;

			namespace App;

			static class ReadOnly
			{
				public static bool CheckOnly(RuntimeTypeModel model) =>
					model.IsDefined(typeof(string));
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App", [], BareReadOnlyIsDefined);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task Stays_silent_for_Add_and_IsDefined_called_inside_the_EnsureRegistered_callback_instance_call_form()
	{
		// The sanctioned pattern — Add/IsDefined called from inside the register callback passed to
		// model.EnsureRegistered(...), the instance-extension-call form used by IdentifierSerializers and
		// ResultSerializers. This replaces a since-removed fixture that had WireModelRegistrationGuard's
		// own type calling Add/IsDefined directly, which never represented anything real: the actual guard
		// body never calls either member itself, only the caller-supplied delegate does.
		const string ThroughTheGuardInstanceForm =
			"""
			using System;
			using ProtoBuf.Meta;
			using Norse.Infrastructure.Web.Grpc;

			namespace App;

			static class Correct
			{
				public static void Register(RuntimeTypeModel model) =>
					model.EnsureRegistered(typeof(Correct), () =>
					{
						if (!model.IsDefined(typeof(string)))
							model.Add(typeof(string), applyDefaultBehaviour: false);
					});
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App",
			[MetadataReference.CreateFromFile(typeof(WireModelRegistrationGuard).Assembly.Location)],
			ThroughTheGuardInstanceForm);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task Stays_silent_for_Add_and_IsDefined_called_inside_the_EnsureRegistered_callback_static_invocation_form()
	{
		// Same sanctioned pattern, fully-qualified static-invocation form — what both generator emitters
		// (ServerRegistrationEmitter/ClientRegistrationEmitter) actually produce. Proven separately from the
		// instance-call form above because the two forms resolve TargetMethod.ContainingType completely
		// differently (extension-block wrapper vs. WireModelRegistrationGuard directly) and both must exempt.
		const string ThroughTheGuardStaticForm =
			"""
			using System;
			using ProtoBuf.Meta;
			using Norse.Infrastructure.Web.Grpc;

			namespace App;

			static class Correct
			{
				public static void Register(RuntimeTypeModel model) =>
					WireModelRegistrationGuard.EnsureRegistered(model, typeof(Correct), () =>
					{
						if (!model.IsDefined(typeof(string)))
							model.Add(typeof(string), applyDefaultBehaviour: false);
					});
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App",
			[MetadataReference.CreateFromFile(typeof(WireModelRegistrationGuard).Assembly.Location)],
			ThroughTheGuardStaticForm);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task Strikes_on_an_Add_nested_in_the_key_argument_not_the_register_callback()
	{
		// 2026-08-06 review fold-in: the exemption is scoped to the register callback specifically, not
		// "anywhere lexically under an EnsureRegistered invocation" — an Add evaluated while building the
		// key argument runs BEFORE the guard takes hold, so it is an unguarded mutation like any other.
		const string AddInsideKeyArgument =
			"""
			using System;
			using ProtoBuf.Meta;
			using Norse.Infrastructure.Web.Grpc;

			namespace App;

			static class Sneaky
			{
				public static void Register(RuntimeTypeModel model) =>
					model.EnsureRegistered(model.Add(typeof(string), applyDefaultBehaviour: false).Type, () => { });
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App",
			[MetadataReference.CreateFromFile(typeof(WireModelRegistrationGuard).Assembly.Location)],
			AddInsideKeyArgument);
		diagnostics.ShouldContain(d => d.Id == "NORSE080");
	}

	[Fact]
	async Task Strikes_when_the_callback_mutates_a_different_model_than_the_guard_protects()
	{
		// 2026-08-06 review fold-in: the guard synchronizes the (model, key) pair it is invoked on, so an
		// Add against a provably different model inside the callback is unprotected — the guard records
		// completion against firstModel while secondModel mutates with no synchronization at all.
		const string CrossedModels =
			"""
			using System;
			using ProtoBuf.Meta;
			using Norse.Infrastructure.Web.Grpc;

			namespace App;

			static class Crossed
			{
				public static void Register(RuntimeTypeModel firstModel, RuntimeTypeModel secondModel) =>
					firstModel.EnsureRegistered(typeof(Crossed), () =>
						secondModel.Add(typeof(string), applyDefaultBehaviour: false));
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App",
			[MetadataReference.CreateFromFile(typeof(WireModelRegistrationGuard).Assembly.Location)],
			CrossedModels);
		diagnostics.ShouldContain(d => d.Id == "NORSE080");
	}

	[Fact]
	async Task Stays_silent_when_the_callback_Adds_through_a_local_copy_of_the_guarded_model()
	{
		// Pins the shape both generator emitters actually produce: the guard is invoked on
		// RuntimeTypeModel.Default and the callback re-reads Default into a local before mutating it.
		// The receiver-match check must chase that one-level local initializer, or NORSE080 convicts the
		// platform's own generated registration code.
		const string LocalCopyOfGuardedModel =
			"""
			using System;
			using ProtoBuf.Meta;
			using Norse.Infrastructure.Web.Grpc;

			namespace App;

			static class Generated
			{
				public static void Register() =>
					WireModelRegistrationGuard.EnsureRegistered(
						RuntimeTypeModel.Default,
						typeof(Generated),
						() =>
						{
							var model = RuntimeTypeModel.Default;
							model.Add(typeof(string), applyDefaultBehaviour: false);
						});
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App",
			[MetadataReference.CreateFromFile(typeof(WireModelRegistrationGuard).Assembly.Location)],
			LocalCopyOfGuardedModel);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task Does_not_strike_on_unrelated_Add_members_only_the_real_RuntimeTypeModel_Add()
	{
		// Pins the receiver-type boundary: List<T>.Add and MetaType.Add share the banned member NAME but
		// are not RuntimeTypeModel, so a future loosening of the receiver-type check shouldn't silently
		// start flagging every collection .Add() on the platform.
		const string MixedAddCalls =
			"""
			using System.Collections.Generic;
			using ProtoBuf.Meta;

			namespace App;

			static class Mixed
			{
				public static void Register(RuntimeTypeModel model, List<string> list, MetaType metaType)
				{
					list.Add("not the droid you're looking for");
					metaType.Add("AlsoNotIt");
					model.Add(typeof(string), applyDefaultBehaviour: false);
				}
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App", [], MixedAddCalls);
		diagnostics.Count(d => d.Id == "NORSE080").ShouldBe(1);
	}

	[Fact]
	async Task Strikes_regardless_of_assembly_name_not_realm_scoped()
	{
		// Unlike WireFormatAnalyzer, this rule is not realm-scoped — the defect it closes was found
		// live in a Yggdrasil TEST project, squarely inside the wire-format-blessed zone. Prove it
		// strikes in a Tests-suffixed assembly name, which WireFormatAnalyzer's RealmIdentity would
		// treat as exempt.
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "Norse.Hosting.Web.Server.Tests", [], DirectAddOutsideGuard);
		diagnostics.ShouldContain(d => d.Id == "NORSE080");
	}
}

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

	const string DirectIsDefinedOutsideGuard =
		"""
		using ProtoBuf.Meta;

		namespace App;

		static class Leak
		{
			public static bool Register(RuntimeTypeModel model) =>
				model.IsDefined(typeof(string));
		}
		""";

	[Fact]
	async Task Strikes_norse080_on_a_direct_Add_call_outside_the_guard()
	{
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App", [], DirectAddOutsideGuard);
		diagnostics.ShouldContain(d => d.Id == "NORSE080" && d.GetMessage(CultureInfo.InvariantCulture).Contains("Add", StringComparison.Ordinal));
	}

	[Fact]
	async Task Strikes_norse080_on_a_direct_IsDefined_call_outside_the_guard()
	{
		// Split from the Add case (rather than asserting an aggregate count of 2 against a fixture that
		// calls both) so a regression that breaks IsDefined binding specifically — e.g. a future protobuf-net
		// upgrade moving it to yet another base type — fails with a message naming IsDefined, not a bare
		// count mismatch.
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App", [], DirectIsDefinedOutsideGuard);
		diagnostics.ShouldContain(d => d.Id == "NORSE080" && d.GetMessage(CultureInfo.InvariantCulture).Contains("IsDefined", StringComparison.Ordinal));
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

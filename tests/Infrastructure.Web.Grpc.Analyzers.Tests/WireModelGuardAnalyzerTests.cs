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
			public static void Register(RuntimeTypeModel model)
			{
				if (!model.IsDefined(typeof(string)))
					model.Add(typeof(string), applyDefaultBehaviour: false);
			}
		}
		""";

	[Fact]
	async Task Strikes_norse080_on_a_direct_Add_call_outside_the_guard()
	{
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App", [], DirectAddOutsideGuard);
		diagnostics.ShouldContain(d => d.Id == "NORSE080");
	}

	[Fact]
	async Task Strikes_norse080_on_a_direct_IsDefined_call_outside_the_guard()
	{
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App", [], DirectAddOutsideGuard);
		diagnostics.Count(d => d.Id == "NORSE080").ShouldBe(2); // one for IsDefined, one for Add
	}

	[Fact]
	async Task Stays_silent_inside_WireModelRegistrationGuard_itself()
	{
		const string GuardImplementation =
			"""
			using ProtoBuf.Meta;

			namespace Norse.Infrastructure.Web.Grpc;

			public static class WireModelRegistrationGuard
			{
				public static void Touch(RuntimeTypeModel model)
				{
					if (!model.IsDefined(typeof(string)))
						model.Add(typeof(string), applyDefaultBehaviour: false);
				}
			}
			""";
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "Norse.Infrastructure.Web.Grpc", [], GuardImplementation);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task Stays_silent_for_code_that_only_calls_EnsureRegistered()
	{
		const string ThroughTheGuard =
			"""
			using System;
			using ProtoBuf.Meta;
			using Norse.Infrastructure.Web.Grpc;

			namespace App;

			static class Correct
			{
				public static void Register(RuntimeTypeModel model) =>
					WireModelRegistrationGuard.EnsureRegistered(model, typeof(Correct), () => { });
			}
			""";
		// This fixture references the real Infrastructure.Web.Grpc assembly (the actual
		// WireModelRegistrationGuard.EnsureRegistered signature) via the extra-references parameter --
		// see Step 4's harness note on threading MetadataReference.CreateFromFile(typeof(WireModelRegistrationGuard).Assembly.Location)
		// through for this test specifically.
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "App",
			[MetadataReference.CreateFromFile(typeof(WireModelRegistrationGuard).Assembly.Location)],
			ThroughTheGuard);
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	async Task Strikes_regardless_of_assembly_name_not_realm_scoped()
	{
		// Unlike WireFormatAnalyzer, this rule is not realm-scoped -- the defect it closes was found
		// live in a Yggdrasil TEST project, squarely inside the wire-format-blessed zone. Prove it
		// strikes in a Tests-suffixed assembly name, which WireFormatAnalyzer's RealmIdentity would
		// treat as exempt.
		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(
			new WireModelGuardAnalyzer(), "Norse.Hosting.Web.Server.Tests", [], DirectAddOutsideGuard);
		diagnostics.ShouldContain(d => d.Id == "NORSE080");
	}
}

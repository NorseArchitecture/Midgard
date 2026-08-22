using System.Collections.Immutable;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Norse.Abstractions.Web.Server.Authorization;
using Norse.Infrastructure.Web.Server.Generator.Policies;

namespace Norse.Infrastructure.Web.Server.Generator.Tests.Policies;

public sealed class PolicyRegistrationGeneratorTests
{
	// Every assembly under the .NET / ASP.NET Core shared framework directories the FrameworkReference
	// resolves against -- same shape GrpcServerRegistrationGeneratorTests uses (object lives in
	// Microsoft.NETCore.App, WebApplication/AuthorizationPolicyBuilder in Microsoft.AspNetCore.App).
	static readonly MetadataReference[] _sharedFrameworks =
	[
		.. SharedFrameworkReferences.In(Path.GetDirectoryName(typeof(object).Assembly.Location)!),
		.. SharedFrameworkReferences.In(Path.GetDirectoryName(typeof(WebApplication).Assembly.Location)!)
	];

	static readonly MetadataReference[] _baseReferences =
	[
		.. ReferenceAssemblies.Net110,
		.. _sharedFrameworks,
		MetadataReference.CreateFromFile(typeof(NorsePolicyAttribute).Assembly.Location)
	];

	const string EmptySource = "namespace Norse.Hosting.Web.Server;";

	const string AuthNPolicySource = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Web.Server.Authorization;

		namespace Norse.AuthN.Services;

		public static class AuthNPolicies
		{
			[NorsePolicy("AuthN.Public")]
			public static void Public(AuthorizationPolicyBuilder builder) => builder.RequireAssertion(_ => true);
		}
		""";

	const string OtherPolicySameNameSource = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Web.Server.Authorization;

		namespace Norse.Other.Services;

		public static class OtherPolicies
		{
			[NorsePolicy("AuthN.Public")]
			public static void Public(AuthorizationPolicyBuilder builder) => builder.RequireAssertion(_ => true);
		}
		""";

	// Package A: the far end of the two-hop chain, declares the one real policy.
	static readonly MetadataReference _imageA = Compile("Norse.PolicyPackage.A", AuthNPolicySource);

	// Package B: a pass-through package -- references A, declares nothing of its own. Mirrors what
	// MSBuild's @(ReferencePath) flattening actually produces for a two-hop project graph.
	static readonly MetadataReference _imageB = Compile("Norse.PolicyPackage.B", EmptySource, _imageA);

	static readonly MetadataReference _imageOther = Compile("Norse.PolicyPackage.Other", OtherPolicySameNameSource);

	// --- Composition facts -----------------------------------------------------------------------

	[Fact]
	void Direct_declaration_in_a_referenced_assembly_is_registered()
	{
		var (_, outputCompilation) = Run("Norse.Hosting.Web.Server", EmptySource, _imageA);

		Generated(outputCompilation).ShouldContain(
			"""AddPolicy("AuthN.Public", global::Norse.AuthN.Services.AuthNPolicies.Public)""");
	}

	[Fact]
	void Two_hops_composed_as_MSBuild_composes_registers_the_far_declaration_and_the_output_compiles_clean()
	{
		// C references BOTH B and A directly -- exactly what @(ReferencePath) flattening produces for
		// an SDK-style two-hop project graph, even though B itself never surfaces A's types publicly.
		var (_, outputCompilation) = Run("Norse.Hosting.Web.Server", EmptySource, _imageB, _imageA);

		Generated(outputCompilation).ShouldContain(
			"""AddPolicy("AuthN.Public", global::Norse.AuthN.Services.AuthNPolicies.Public)""");

		// Compiling the output is the fact that matters here: it is what proves the emitter only ever
		// names types this compilation can actually resolve.
		var errors = outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.ToArray();
		errors.ShouldBeEmpty(string.Join("\n", errors.Select(e => e.ToString())));
	}

	[Fact]
	void Two_hops_unflattened_registers_nothing_from_the_unreferenced_assembly_and_still_compiles_clean()
	{
		// C references B ONLY -- A is never in C's own reference list. B's public surface (EmptySource)
		// never names anything from A, so Roslyn never needs A to resolve C either: this is the honest
		// edge of the reference-set-scoped contract, not a closure walk that would reach A anyway.
		var (_, outputCompilation) = Run("Norse.Hosting.Web.Server", EmptySource, _imageB);

		var generated = Generated(outputCompilation);
		generated.ShouldNotContain("AuthN.Public");
		generated.ShouldNotContain("AuthNPolicies");

		var errors = outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.ToArray();
		errors.ShouldBeEmpty(string.Join("\n", errors.Select(e => e.ToString())));
	}

	[Fact]
	void Duplicate_policy_name_across_two_metadata_packages_fires_NORSE014_and_registers_neither()
	{
		var (diagnostics, outputCompilation) = Run("Norse.Hosting.Web.Server", EmptySource, _imageA, _imageOther);

		diagnostics.ShouldContain(d => d.Id == "NORSE014" && d.Severity == DiagnosticSeverity.Error);
		var duplicate = diagnostics.Single(d => d.Id == "NORSE014");
		duplicate.GetMessage(CultureInfo.InvariantCulture).ShouldContain("AuthN.Public");
		duplicate.GetMessage(CultureInfo.InvariantCulture).ShouldContain("AuthNPolicies.Public");
		duplicate.GetMessage(CultureInfo.InvariantCulture).ShouldContain("OtherPolicies.Public");

		Generated(outputCompilation).ShouldNotContain("AuthN.Public");
	}

	// --- NORSE015 rejection matrix -----------------------------------------------------------------
	//
	// Each class below is exercised twice:
	//   * "...arriving_as_metadata" compiles the malformed declaration into its own assembly and runs
	//     the generator against a compilation that only references that assembly -- the normal case, a
	//     realm's declaration arriving as a published package. NORSE015 fires with Location.None and
	//     the fully qualified method name.
	//   * "...declared_in_the_compilations_own_source" runs the generator directly against a
	//     compilation whose OWN source carries the same malformed declaration. Confirmed empirically
	//     (see the task report): AttributeData.ApplicationSyntaxReference is non-null for a symbol
	//     declared in the compilation currently being built, and PolicyDeclarationDiscovery.Collect()
	//     deliberately steps aside for exactly that case, deferring to Asgard's bundled
	//     NorsePolicyDeclarationAnalyzer (Task 2, already covered by its own test suite) so the same
	//     mistake is never struck twice in the same build. This half proves that deference holds --
	//     Midgard's generator reports nothing for what Asgard's half already owns.

	const string PrivateMethodSource = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Web.Server.Authorization;

		namespace Norse.Realm.Rejections;

		public static class PrivatePolicies
		{
			[NorsePolicy("Realm.Private")]
			private static void Configure(AuthorizationPolicyBuilder builder) => builder.RequireAssertion(_ => true);
		}
		""";

	[Fact]
	void NORSE015_fires_for_an_attributed_private_method_arriving_as_metadata()
	{
		var diagnostic = SingleNorse015(RunAgainstMetadata(PrivateMethodSource).Diagnostics);
		diagnostic.Location.ShouldBe(Location.None);
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("PrivatePolicies.Configure");
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("must be public");
	}

	[Fact]
	void NORSE015_does_not_double_report_an_attributed_private_method_declared_in_the_compilations_own_source() =>
		RunAgainstSource(PrivateMethodSource).Diagnostics.ShouldNotContain(d => d.Id == "NORSE015");

	const string InternalMethodSource = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Web.Server.Authorization;

		namespace Norse.Realm.Rejections;

		public static class InternalPolicies
		{
			[NorsePolicy("Realm.Internal")]
			internal static void Configure(AuthorizationPolicyBuilder builder) => builder.RequireAssertion(_ => true);
		}
		""";

	[Fact]
	void NORSE015_fires_for_an_attributed_internal_method_arriving_as_metadata()
	{
		var diagnostic = SingleNorse015(RunAgainstMetadata(InternalMethodSource).Diagnostics);
		diagnostic.Location.ShouldBe(Location.None);
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("InternalPolicies.Configure");
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("must be public");
	}

	[Fact]
	void NORSE015_does_not_double_report_an_attributed_internal_method_declared_in_the_compilations_own_source() =>
		RunAgainstSource(InternalMethodSource).Diagnostics.ShouldNotContain(d => d.Id == "NORSE015");

	const string InstanceMethodSource = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Web.Server.Authorization;

		namespace Norse.Realm.Rejections;

		public sealed class InstancePolicies
		{
			[NorsePolicy("Realm.Instance")]
			public void Configure(AuthorizationPolicyBuilder builder) => builder.RequireAssertion(_ => true);
		}
		""";

	[Fact]
	void NORSE015_fires_for_an_attributed_instance_method_arriving_as_metadata()
	{
		var diagnostic = SingleNorse015(RunAgainstMetadata(InstanceMethodSource).Diagnostics);
		diagnostic.Location.ShouldBe(Location.None);
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("InstancePolicies.Configure");
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("must be static");
	}

	[Fact]
	void NORSE015_does_not_double_report_an_attributed_instance_method_declared_in_the_compilations_own_source() =>
		RunAgainstSource(InstanceMethodSource).Diagnostics.ShouldNotContain(d => d.Id == "NORSE015");

	const string InaccessibleTypeSource = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Web.Server.Authorization;

		namespace Norse.Realm.Rejections;

		public static class InaccessibleOuter
		{
			private static class Inaccessible
			{
				[NorsePolicy("Realm.Inaccessible")]
				public static void Configure(AuthorizationPolicyBuilder builder) => builder.RequireAssertion(_ => true);
			}
		}
		""";

	[Fact]
	void NORSE015_fires_for_an_attributed_method_on_an_inaccessible_type_arriving_as_metadata()
	{
		var diagnostic = SingleNorse015(RunAgainstMetadata(InaccessibleTypeSource).Diagnostics);
		diagnostic.Location.ShouldBe(Location.None);
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("Inaccessible.Configure");
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("accessible from the consuming compilation");
	}

	[Fact]
	void NORSE015_does_not_double_report_an_attributed_method_on_an_inaccessible_type_declared_in_the_compilations_own_source() =>
		RunAgainstSource(InaccessibleTypeSource).Diagnostics.ShouldNotContain(d => d.Id == "NORSE015");

	const string NonVoidReturnSource = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Web.Server.Authorization;

		namespace Norse.Realm.Rejections;

		public static class NonVoidPolicies
		{
			[NorsePolicy("Realm.NonVoid")]
			public static bool Configure(AuthorizationPolicyBuilder builder)
			{
				builder.RequireAssertion(_ => true);
				return true;
			}
		}
		""";

	[Fact]
	void NORSE015_fires_for_a_non_void_returning_attributed_method_arriving_as_metadata()
	{
		var diagnostic = SingleNorse015(RunAgainstMetadata(NonVoidReturnSource).Diagnostics);
		diagnostic.Location.ShouldBe(Location.None);
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("NonVoidPolicies.Configure");
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("must return void");
	}

	[Fact]
	void NORSE015_does_not_double_report_a_non_void_returning_attributed_method_declared_in_the_compilations_own_source() =>
		RunAgainstSource(NonVoidReturnSource).Diagnostics.ShouldNotContain(d => d.Id == "NORSE015");

	const string WrongParametersSource = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Web.Server.Authorization;

		namespace Norse.Realm.Rejections;

		public static class WrongParametersPolicies
		{
			[NorsePolicy("Realm.WrongParameters")]
			public static void Configure(AuthorizationPolicyBuilder builder, int extra) => builder.RequireAssertion(_ => true);
		}
		""";

	[Fact]
	void NORSE015_fires_for_an_attributed_method_with_extra_parameters_arriving_as_metadata()
	{
		var diagnostic = SingleNorse015(RunAgainstMetadata(WrongParametersSource).Diagnostics);
		diagnostic.Location.ShouldBe(Location.None);
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("WrongParametersPolicies.Configure");
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("exactly one AuthorizationPolicyBuilder parameter");
	}

	[Fact]
	void NORSE015_does_not_double_report_an_attributed_method_with_extra_parameters_declared_in_the_compilations_own_source() =>
		RunAgainstSource(WrongParametersSource).Diagnostics.ShouldNotContain(d => d.Id == "NORSE015");

	const string GenericMethodSource = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Web.Server.Authorization;

		namespace Norse.Realm.Rejections;

		public static class GenericMethodPolicies
		{
			[NorsePolicy("Realm.GenericMethod")]
			public static void Configure<T>(AuthorizationPolicyBuilder builder) => builder.RequireAssertion(_ => true);
		}
		""";

	[Fact]
	void NORSE015_fires_for_an_attributed_generic_method_arriving_as_metadata()
	{
		var diagnostic = SingleNorse015(RunAgainstMetadata(GenericMethodSource).Diagnostics);
		diagnostic.Location.ShouldBe(Location.None);
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("GenericMethodPolicies.Configure");
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("may be generic");
	}

	[Fact]
	void NORSE015_does_not_double_report_an_attributed_generic_method_declared_in_the_compilations_own_source() =>
		RunAgainstSource(GenericMethodSource).Diagnostics.ShouldNotContain(d => d.Id == "NORSE015");

	const string GenericContainingTypeSource = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Web.Server.Authorization;

		namespace Norse.Realm.Rejections;

		public static class GenericContainingTypePolicies<T>
		{
			[NorsePolicy("Realm.GenericContainingType")]
			public static void Configure(AuthorizationPolicyBuilder builder) => builder.RequireAssertion(_ => true);
		}
		""";

	[Fact]
	void NORSE015_fires_for_an_attributed_method_on_a_generic_containing_type_arriving_as_metadata()
	{
		var diagnostic = SingleNorse015(RunAgainstMetadata(GenericContainingTypeSource).Diagnostics);
		diagnostic.Location.ShouldBe(Location.None);
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("GenericContainingTypePolicies<T>.Configure");
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("may be generic");
	}

	[Fact]
	void NORSE015_does_not_double_report_an_attributed_method_on_a_generic_containing_type_declared_in_the_compilations_own_source() =>
		RunAgainstSource(GenericContainingTypeSource).Diagnostics.ShouldNotContain(d => d.Id == "NORSE015");

	[Theory]
	[InlineData("null")]
	[InlineData("\"\"")]
	void NORSE015_fires_for_a_null_or_empty_policy_name_arriving_as_metadata(string literal)
	{
		var diagnostic = SingleNorse015(RunAgainstMetadata(NamelessSource(literal)).Diagnostics);
		diagnostic.Location.ShouldBe(Location.None);
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("NamelessPolicies.Configure");
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("non-empty constant string");
	}

	[Theory]
	[InlineData("null")]
	[InlineData("\"\"")]
	void NORSE015_does_not_double_report_a_null_or_empty_policy_name_declared_in_the_compilations_own_source(string literal) =>
		RunAgainstSource(NamelessSource(literal)).Diagnostics.ShouldNotContain(d => d.Id == "NORSE015");

	static string NamelessSource(string literal) =>
		$$"""
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Web.Server.Authorization;

		namespace Norse.Realm.Rejections;

		public static class NamelessPolicies
		{
			[NorsePolicy({{literal}})]
			public static void Configure(AuthorizationPolicyBuilder builder) => builder.RequireAssertion(_ => true);
		}
		""";

	// --- Harness -------------------------------------------------------------------------------

	static Diagnostic SingleNorse015(ImmutableArray<Diagnostic> diagnostics)
	{
		var diagnostic = diagnostics.Single(d => d.Id == "NORSE015");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		return diagnostic;
	}

	static (ImmutableArray<Diagnostic> Diagnostics, Compilation OutputCompilation) RunAgainstMetadata(
		string malformedSource) =>
		Run("Norse.Hosting.Web.Server", EmptySource, Compile("Norse.PolicyPackage.Malformed", malformedSource));

	static (ImmutableArray<Diagnostic> Diagnostics, Compilation OutputCompilation) RunAgainstSource(
		string malformedSource) =>
		Run("Norse.Hosting.Web.Server", malformedSource);

	static MetadataReference Compile(string assemblyName, string source, params MetadataReference[] extraReferences)
	{
		var compilation = CSharpCompilation.Create(
			assemblyName,
			[CSharpSyntaxTree.ParseText(source)],
			[.. _baseReferences, .. extraReferences],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		using MemoryStream stream = new();
		var emitResult = compilation.Emit(stream);
		emitResult.Success.ShouldBeTrue(string.Join('\n', emitResult.Diagnostics));
		return MetadataReference.CreateFromImage(stream.ToArray());
	}

	static (ImmutableArray<Diagnostic> Diagnostics, Compilation OutputCompilation) Run(
		string assemblyName, string source, params MetadataReference[] extraReferences)
	{
		var compilation = CSharpCompilation.Create(
			assemblyName,
			[CSharpSyntaxTree.ParseText(source)],
			[.. _baseReferences, .. extraReferences],
			// MetadataImportOptions.All, not the CSharpCompilation default of Public: confirmed
			// empirically (see the task report) that Roslyn hides non-public members of a REFERENCED
			// assembly from every symbol-table walk -- GetMembers() included -- under the default a
			// real `dotnet build` uses. A private or internal [NorsePolicy] method arriving as
			// metadata is invisible to Collect() entirely at that default; this is the only way to
			// exercise the private/internal rejection classes' metadata half at all. Changes nothing
			// for the public-only fixtures elsewhere in this file, since Public is the floor every
			// level includes.
			new CSharpCompilationOptions(
				OutputKind.DynamicallyLinkedLibrary, metadataImportOptions: MetadataImportOptions.All));

		_ = CSharpGeneratorDriver.Create(new PolicyRegistrationGenerator().AsSourceGenerator())
			.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

		return (diagnostics, outputCompilation);
	}

	static string Generated(Compilation outputCompilation) =>
		outputCompilation.SyntaxTrees.Skip(1).Select(t => t.ToString()).Single();
}

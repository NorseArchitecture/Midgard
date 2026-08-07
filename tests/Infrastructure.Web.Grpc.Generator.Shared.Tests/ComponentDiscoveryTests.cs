using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Norse.Infrastructure.Web.Grpc.Generator.Shared.Tests;

public sealed class ComponentDiscoveryTests
{
	// Field declaration order matters here: static field initializers run top-to-bottom, and the
	// fixture MetadataReference fields below call BuildReferenceAssembly, which reads _extraReferences
	// -- so it must be declared first, not just textually convenient.
	static readonly MetadataReference[] _extraReferences =
	[
		.. ReferenceAssemblies.Net110,
		MetadataReference.CreateFromFile(typeof(FluentValidation.IValidator<>).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Components.RouteAttribute).Assembly.Location),
	];

	const string ValidatorSource = """
		using FluentValidation;

		namespace Own;

		public sealed record FakeRequest;

		public sealed class FakeValidator : AbstractValidator<FakeRequest>;
		""";

	// A genuinely separate assembly (CompilationReference via ToMetadataReference -- no IL emission
	// needed) so ComponentDiscovery.Discover sees it as a referenced IAssemblySymbol distinct from the
	// harness's own compilation, the way a real consuming project sees FluentValidation validators
	// declared in an upstream package.
	static readonly MetadataReference _referencedValidatorAssembly = BuildReferenceAssembly(
		"Referenced.Validators",
		"""
		using FluentValidation;

		namespace Referenced;

		public sealed record OtherRequest;

		public sealed class OtherValidator : AbstractValidator<OtherRequest>;
		""");

	static readonly MetadataReference _routableAssembly = BuildReferenceAssembly(
		"Routable.Pages",
		"""
		using Microsoft.AspNetCore.Components;

		namespace RoutableAsm;

		[Route("/widget")]
		public sealed class WidgetPage;
		""");

	static readonly MetadataReference _plainAssembly = BuildReferenceAssembly(
		"Plain.Library",
		"""
		namespace PlainAsm;

		public sealed class Nothing;
		""");

	// Carries its own [Route]-attributed type (HomePage) alongside Routes itself, so the exclusion
	// test below actually exercises the exclusion -- if ComponentDiscovery fell back to treating this
	// assembly like any other routable assembly instead of excluding it outright, HomePage would leak
	// into RoutableAssemblyMarkers even though RoutesHolderMarker itself (a different string) would
	// still pass a ShouldNotContain-only check.
	static readonly MetadataReference _routesHolderAssembly = BuildReferenceAssembly(
		"Hosting.Web.Components",
		"""
		using Microsoft.AspNetCore.Components;

		namespace Norse.Hosting.Web.Components;

		[Route("/")]
		public sealed class Routes;

		[Route("/home")]
		public sealed class HomePage;
		""");

	[Fact]
	void Discovers_concrete_validators_in_own_and_referenced_assemblies()
	{
		var compilation = HarnessCompilation(sources: [ValidatorSource], references: [_referencedValidatorAssembly]);

		var result = ComponentDiscovery.Discover(compilation);

		// Ascending ordinal by ValidatorTypeName, per ComponentDiscoveryResult's own contract ("ordered
		// by ValidatorTypeName, ordinal") -- "Own" < "Referenced" ordinally, so Own sorts first.
		result.Validators.Select(v => v.ValidatorTypeName)
			.ShouldBe(["global::Own.FakeValidator", "global::Referenced.OtherValidator"]);
		result.Validators.Select(v => v.RequestTypeName)
			.ShouldBe(["global::Own.FakeRequest", "global::Referenced.OtherRequest"]);
	}

	[Fact]
	void Records_one_routable_marker_per_assembly_and_skips_assemblies_without_routes()
	{
		var compilation = HarnessCompilation(references: [_routableAssembly, _plainAssembly]);

		var result = ComponentDiscovery.Discover(compilation);

		result.RoutableAssemblyMarkers.ShouldHaveSingleItem();
		result.RoutableAssemblyMarkers.ShouldContain("global::RoutableAsm.WidgetPage");
	}

	[Fact]
	void Identifies_the_routes_holder_assembly_separately()
	{
		var compilation = HarnessCompilation(references: [_routesHolderAssembly]);

		var result = ComponentDiscovery.Discover(compilation);

		result.RoutesHolderMarker.ShouldBe("global::Norse.Hosting.Web.Components.Routes");
		result.RoutableAssemblyMarkers.ShouldNotContain(result.RoutesHolderMarker);
		result.RoutableAssemblyMarkers.ShouldBeEmpty();
	}

	[Fact]
	void RoutesHolderMarker_is_null_when_Routes_is_unreferenced()
	{
		var compilation = HarnessCompilation(references: [_routableAssembly]);

		var result = ComponentDiscovery.Discover(compilation);

		result.RoutesHolderMarker.ShouldBeNull();
	}

	[Fact]
	void RoutesAdditionalAssembliesTypeExists_is_false_when_the_type_is_unreferenced()
	{
		var compilation = HarnessCompilation();

		var result = ComponentDiscovery.Discover(compilation);

		result.RoutesAdditionalAssembliesTypeExists.ShouldBeFalse();
	}

	static MetadataReference BuildReferenceAssembly(string assemblyName, string source) =>
		CSharpCompilation.Create(
			assemblyName,
			[CSharpSyntaxTree.ParseText(source)],
			_extraReferences,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
			.ToMetadataReference();

	static Compilation HarnessCompilation(string[]? sources = null, MetadataReference[]? references = null) =>
		CSharpCompilation.Create(
			"Norse.Hosting.Web.Server",
			[.. (sources ?? []).Select(s => CSharpSyntaxTree.ParseText(s))],
			[.. _extraReferences, .. references ?? []],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}

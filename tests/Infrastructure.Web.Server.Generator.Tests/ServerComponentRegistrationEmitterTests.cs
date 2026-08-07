using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Norse.Infrastructure.Web.Server.Generator.Tests;

public sealed class ServerComponentRegistrationEmitterTests
{
	const string ValidatorSource = """
		using FluentValidation;

		namespace Fake;

		public sealed record LoginRequest;

		public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>;
		""";

	const string RoutesAdditionalAssembliesSource = """
		using System.Collections.Generic;
		using System.Reflection;

		namespace Norse.Hosting.Web.Components;

		public sealed class RoutesAdditionalAssemblies(IEnumerable<Assembly> assemblies)
		{
			public IEnumerable<Assembly> Assemblies { get; } = assemblies;
		}
		""";

	// A [Route]-attributed type living in the consuming compilation itself, not a separate
	// referenced assembly -- the fixture the "never the compilation's own assembly" endpoint
	// exclusion rule needs to actually exercise it. The router side has no equivalent exception, so
	// this same marker should still show up in the router's AdditionalAssemblies list.
	const string OwnRoutablePageSource = """
		using Microsoft.AspNetCore.Components;

		namespace Fake;

		[Route("/own")]
		public sealed class OwnPage;
		""";

	// Norse.Hosting.Web.Components.Routes declared in the harness compilation's OWN sources, not a
	// separate referenced assembly -- the fixture the "an in-compilation Routes holder is excluded
	// from the endpoint list too" regression needs. Deliberately a standalone const, not combined with
	// _routesHolderAssembly in the same Run(): having both in play at once would give the compilation
	// two distinct "Norse.Hosting.Web.Components.Routes" candidates (one from source, one referenced)
	// and GetTypeByMetadataName would see that as ambiguous.
	const string OwnRoutesHolderSource = """
		using Microsoft.AspNetCore.Components;

		namespace Norse.Hosting.Web.Components;

		[Route("/")]
		public sealed class Routes;
		""";

	// Field declaration order matters here: static field initializers run top-to-bottom, and
	// _routableAssembly's initializer calls BuildReferenceAssembly, which reads _extraReferences --
	// so _sharedFrameworks/_extraReferences must be declared first (mirrors Task 4's
	// ClientComponentRegistrationEmitterTests.cs's own note on the same pitfall).
	static readonly MetadataReference[] _sharedFrameworks =
	[
		.. Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll")
			.Select(f => MetadataReference.CreateFromFile(f)),
		.. Directory.GetFiles(Path.GetDirectoryName(typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly.Location)!, "*.dll")
			.Select(f => MetadataReference.CreateFromFile(f)),
	];

	static readonly MetadataReference[] _extraReferences =
	[
		MetadataReference.CreateFromFile(typeof(FluentValidation.IValidator<>).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Components.RouteAttribute).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Builder.RazorComponentsEndpointConventionBuilder).Assembly.Location),
		.. _sharedFrameworks,
	];

	// A genuinely separate referenced assembly, not a second source file in the same compilation --
	// the routable marker in the brief's expected output (typeof(Fake.RoutablePage).Assembly) is only
	// meaningful once RoutablePage lives somewhere other than the consuming compilation itself.
	static readonly MetadataReference _routableAssembly = BuildReferenceAssembly(
		"Routable.Fixture",
		"""
		using Microsoft.AspNetCore.Components;

		namespace Fake;

		[Route("/routable")]
		public sealed class RoutablePage;
		""");

	// Also a genuinely separate referenced assembly, not a compilation source -- if Routes lived in
	// the harness compilation's own sources, that compilation *is* the routes-holder assembly, and
	// ComponentDiscovery excludes the entire routes-holder assembly from the routable walk. That
	// would swallow OwnRoutablePageSource's own-assembly exclusion test below along with it, testing
	// the wrong exclusion rule entirely.
	static readonly MetadataReference _routesHolderAssembly = BuildReferenceAssembly(
		"Hosting.Web.Components",
		"""
		using Microsoft.AspNetCore.Components;

		namespace Norse.Hosting.Web.Components;

		[Route("/")]
		public sealed class Routes;
		""");

	[Fact]
	void Emits_the_validator_block_before_the_route_block_when_both_are_discovered()
	{
		var generated = Generate(ValidatorSource, RoutesAdditionalAssembliesSource);

		var validatorIndex = generated.IndexOf(
			"global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(",
			StringComparison.Ordinal);
		var routeIndex = generated.IndexOf(
			"global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(",
			StringComparison.Ordinal);

		validatorIndex.ShouldBeGreaterThan(-1);
		routeIndex.ShouldBeGreaterThan(-1);
		validatorIndex.ShouldBeLessThan(routeIndex);
	}

	[Fact]
	void Emits_an_idempotent_TryAddEnumerable_registration_per_discovered_validator()
	{
		var generated = Generate(ValidatorSource, RoutesAdditionalAssembliesSource);

		generated.ShouldContain("// validator (idempotent, pairs with the server-side generator's identical shape)");
		generated.ShouldContain("global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(");
		generated.ShouldContain("services, global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped(");
		generated.ShouldContain("typeof(global::FluentValidation.IValidator<global::Fake.LoginRequest>), typeof(global::Fake.LoginRequestValidator)));");
	}

	[Fact]
	void Emits_a_single_RoutesAdditionalAssemblies_registration_carrying_every_routable_marker()
	{
		var generated = Generate(ValidatorSource, RoutesAdditionalAssembliesSource);

		generated.ShouldContain("// router discovery");
		generated.ShouldContain("global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(");
		generated.ShouldContain("services, new global::Norse.Hosting.Web.Components.RoutesAdditionalAssemblies([");
		generated.ShouldContain("typeof(global::Fake.RoutablePage).Assembly,");
	}

	[Fact]
	void Emits_the_validator_block_only_when_RoutesAdditionalAssemblies_is_unreferenced()
	{
		var generated = Generate(ValidatorSource);

		generated.ShouldContain("// validator (idempotent, pairs with the server-side generator's identical shape)");
		generated.ShouldNotContain("// router discovery");
		generated.ShouldNotContain("RoutesAdditionalAssemblies");
	}

	[Fact]
	void Emits_nothing_when_no_validator_or_routing_seam_is_discovered()
	{
		const string Empty = """
			namespace Norse.Empty.Web.Server;

			public sealed class NotAValidator;
			""";
		var (_, outputCompilation) = Run(Empty);
		outputCompilation.SyntaxTrees.Count().ShouldBe(1); // no generated tree added
	}

	[Fact]
	void Emits_the_consuming_assemblys_root_namespace()
	{
		var generated = Generate(ValidatorSource, RoutesAdditionalAssembliesSource);
		generated.ShouldContain("namespace Norse.Hosting.Web.Server;");
	}

	// An AssemblyName is not guaranteed to be a legal C# namespace token -- a hyphenated package id
	// (common: "My-App") or a leading digit would otherwise land verbatim in `namespace {{...}};` and
	// fail to compile for the consumer. No build_property.RootNamespace is configured here, so the
	// fallback path (AssemblyName) is what gets sanitized.
	[Fact]
	void Sanitizes_an_assembly_name_that_is_not_a_legal_namespace_token()
	{
		var generated = GenerateWithAssemblyName("My-App.Web-Server", ValidatorSource);

		generated.ShouldContain("namespace My_App.Web_Server;");
	}

	// build_property.RootNamespace -- MSBuild's actual RootNamespace property, read via the standard
	// AnalyzerConfigOptionsProvider interop -- wins over AssemblyName when both are present, since the
	// two can diverge freely (a renamed assembly, a hyphenated package id). The configured value is
	// itself sanitized too, not just the AssemblyName fallback.
	[Fact]
	void Prefers_the_configured_RootNamespace_build_property_over_the_assembly_name_and_sanitizes_it_too()
	{
		var generated = GenerateWithRootNamespaceProperty("Configured.Root-Namespace", "My-App", ValidatorSource);

		generated.ShouldContain("namespace Configured.Root_Namespace;");
		generated.ShouldNotContain("namespace My_App;");
	}

	// The brief's exact expected shape: the Routes-holder assembly and the referenced routable
	// assembly, in that order, fed to Razor endpoint discovery via AddAdditionalAssemblies.
	[Fact]
	void Emits_the_AddNorseComponentAssemblies_endpoint_extension_per_the_brief_template()
	{
		var generated = Generate(RoutesAdditionalAssembliesSource);

		generated.ShouldContain(
			"""
				/// <summary>Feeds every discovered routable component assembly to Razor endpoint discovery — the render-mode half of discovery, distinct from the Router's.</summary>
				public static global::Microsoft.AspNetCore.Builder.RazorComponentsEndpointConventionBuilder AddNorseComponentAssemblies(
					this global::Microsoft.AspNetCore.Builder.RazorComponentsEndpointConventionBuilder builder) =>
					global::Microsoft.AspNetCore.Builder.RazorComponentsEndpointConventionBuilderExtensions.AddAdditionalAssemblies(builder,
						typeof(global::Norse.Hosting.Web.Components.Routes).Assembly,
						typeof(global::Fake.RoutablePage).Assembly);
			""");
	}

	// Exclusion rule 1: the Routes-holder assembly is excluded from the router's
	// RoutesAdditionalAssemblies list (Router's AppAssembly already covers it) but included in the
	// endpoint list.
	[Fact]
	void Router_list_excludes_the_Routes_holder_assembly_but_the_endpoint_list_includes_it()
	{
		var generated = Generate(RoutesAdditionalAssembliesSource);

		var routerBlockStart = generated.IndexOf("new global::Norse.Hosting.Web.Components.RoutesAdditionalAssemblies([", StringComparison.Ordinal);
		var routerBlockEnd = generated.IndexOf("]));", routerBlockStart, StringComparison.Ordinal);
		var routerBlock = generated[routerBlockStart..routerBlockEnd];
		routerBlock.ShouldNotContain("typeof(global::Norse.Hosting.Web.Components.Routes).Assembly");

		generated.ShouldContain("typeof(global::Norse.Hosting.Web.Components.Routes).Assembly,\n\t\t\ttypeof(global::Fake.RoutablePage).Assembly);");
	}

	// Exclusion rule 2: the compilation's own assembly is excluded from the endpoint list
	// (MapRazorComponents<App>'s implicit root already covers it) but the router has no equivalent
	// exception, so the same own-assembly marker still shows up in RoutesAdditionalAssemblies.
	[Fact]
	void Endpoint_list_excludes_the_compilations_own_assembly_but_the_router_list_may_include_it()
	{
		var generated = Generate(RoutesAdditionalAssembliesSource, OwnRoutablePageSource);

		var endpointCallStart = generated.IndexOf("AddAdditionalAssemblies(builder,", StringComparison.Ordinal);
		var endpointCallEnd = generated.IndexOf(");", endpointCallStart, StringComparison.Ordinal);
		var endpointArgs = generated[endpointCallStart..endpointCallEnd];
		endpointArgs.ShouldNotContain("typeof(global::Fake.OwnPage).Assembly");

		var routerBlockStart = generated.IndexOf("new global::Norse.Hosting.Web.Components.RoutesAdditionalAssemblies([", StringComparison.Ordinal);
		var routerBlockEnd = generated.IndexOf("]));", routerBlockStart, StringComparison.Ordinal);
		var routerBlock = generated[routerBlockStart..routerBlockEnd];
		routerBlock.ShouldContain("typeof(global::Fake.OwnPage).Assembly");
	}

	// Regression: when Norse.Hosting.Web.Components.Routes lives in the compilation's OWN assembly
	// (not a referenced one), the routes-holder assembly IS the compilation's own assembly --
	// DiscoverRoutes excludes the routes-holder assembly from the per-assembly walk before
	// OwnAssemblyRoutableMarker is computed, so that marker comes back null with nothing left to
	// filter RoutesHolderMarker against. Unconditionally including RoutesHolderMarker in the endpoint
	// list would slip the compilation's own assembly through -- redundant with (and a potential
	// double-discovery source alongside) MapRazorComponents<App>'s implicit root.
	[Fact]
	void Endpoint_list_excludes_an_in_compilation_Routes_holder_the_same_way_it_excludes_an_out_of_compilation_one()
	{
		var generated = GenerateWithOwnRoutesHolder();

		generated.ShouldNotContain("typeof(global::Norse.Hosting.Web.Components.Routes).Assembly");
		generated.ShouldContain("AddAdditionalAssemblies(builder);");
	}

	[Fact]
	void Emitted_source_compiles_cleanly_against_real_ASP_NET_Core_FluentValidation_and_DependencyInjection_references()
	{
		var (_, outputCompilation) = Run(RoutesAdditionalAssembliesSource);
		var errors = outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.ToArray();
		errors.ShouldBeEmpty(string.Join("\n", errors.Select(e => e.ToString())));
	}

	static MetadataReference BuildReferenceAssembly(string assemblyName, string source) =>
		CSharpCompilation.Create(
			assemblyName,
			[CSharpSyntaxTree.ParseText(source)],
			[.. ReferenceAssemblies.Net110, .. _extraReferences],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
			.ToMetadataReference();

	static (ImmutableArray<Diagnostic> Diagnostics, Compilation OutputCompilation) Run(params string[] sources)
	{
		MetadataReference[] references = sources.Contains(RoutesAdditionalAssembliesSource)
			? [.. ReferenceAssemblies.Net110, .. _extraReferences, _routableAssembly, _routesHolderAssembly]
			: [.. ReferenceAssemblies.Net110, .. _extraReferences];

		var compilation = CSharpCompilation.Create(
			"Norse.Hosting.Web.Server",
			[.. sources.Select(s => CSharpSyntaxTree.ParseText(s))],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		_ = CSharpGeneratorDriver.Create([new ServerComponentRegistrationGenerator().AsSourceGenerator()])
			.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

		return (diagnostics, outputCompilation);
	}

	// Deliberately not routed through Run(): that helper adds _routesHolderAssembly (a SEPARATE
	// referenced assembly also declaring Norse.Hosting.Web.Components.Routes) whenever
	// RoutesAdditionalAssembliesSource is present, which would give the compilation two distinct
	// candidates for that metadata name and make GetTypeByMetadataName ambiguous -- this fixture needs
	// Routes to live only in the compilation's own sources.
	static string GenerateWithOwnRoutesHolder()
	{
		MetadataReference[] references = [.. ReferenceAssemblies.Net110, .. _extraReferences];
		var compilation = CSharpCompilation.Create(
			"Norse.Hosting.Web.Server",
			[CSharpSyntaxTree.ParseText(RoutesAdditionalAssembliesSource), CSharpSyntaxTree.ParseText(OwnRoutesHolderSource)],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		_ = CSharpGeneratorDriver.Create([new ServerComponentRegistrationGenerator().AsSourceGenerator()])
			.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

		return outputCompilation.SyntaxTrees.Skip(2).Select(tree => tree.ToString()).Single();
	}

	static string Generate(params string[] sources)
	{
		var (_, outputCompilation) = Run(sources);
		return outputCompilation.SyntaxTrees.Skip(sources.Length).Select(tree => tree.ToString()).Single();
	}

	// Deliberately not routed through Run()/Generate(): those hardcode the harness compilation's
	// AssemblyName to "Norse.Hosting.Web.Server" (always a legal token), which is exactly what these
	// two sanitization fixtures need to vary.
	static string GenerateWithAssemblyName(string assemblyName, params string[] sources)
	{
		MetadataReference[] references = sources.Contains(RoutesAdditionalAssembliesSource)
			? [.. ReferenceAssemblies.Net110, .. _extraReferences, _routableAssembly, _routesHolderAssembly]
			: [.. ReferenceAssemblies.Net110, .. _extraReferences];

		var compilation = CSharpCompilation.Create(
			assemblyName,
			[.. sources.Select(s => CSharpSyntaxTree.ParseText(s))],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		_ = CSharpGeneratorDriver.Create([new ServerComponentRegistrationGenerator().AsSourceGenerator()])
			.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

		return outputCompilation.SyntaxTrees.Skip(sources.Length).Select(tree => tree.ToString()).Single();
	}

	static string GenerateWithRootNamespaceProperty(string rootNamespace, string assemblyName, params string[] sources)
	{
		MetadataReference[] references = sources.Contains(RoutesAdditionalAssembliesSource)
			? [.. ReferenceAssemblies.Net110, .. _extraReferences, _routableAssembly, _routesHolderAssembly]
			: [.. ReferenceAssemblies.Net110, .. _extraReferences];

		var compilation = CSharpCompilation.Create(
			assemblyName,
			[.. sources.Select(s => CSharpSyntaxTree.ParseText(s))],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		_ = CSharpGeneratorDriver.Create(
				[new ServerComponentRegistrationGenerator().AsSourceGenerator()],
				optionsProvider: new TestAnalyzerConfigOptionsProvider(rootNamespace))
			.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

		return outputCompilation.SyntaxTrees.Skip(sources.Length).Select(tree => tree.ToString()).Single();
	}
}

/// <summary>Minimal test double for MSBuild's build_property.* interop -- reports a single configured <c>build_property.RootNamespace</c> value from AnalyzerConfigOptionsProvider.GlobalOptions and nothing else.</summary>
sealed class TestAnalyzerConfigOptionsProvider(string rootNamespace) : AnalyzerConfigOptionsProvider
{
	public override AnalyzerConfigOptions GlobalOptions { get; } = new TestAnalyzerConfigOptions(rootNamespace);
	public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
	public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
}

sealed class TestAnalyzerConfigOptions(string rootNamespace) : AnalyzerConfigOptions
{
	public override bool TryGetValue(string key, out string value)
	{
		if (key == "build_property.RootNamespace")
		{
			value = rootNamespace;
			return true;
		}

		value = "";
		return false;
	}
}

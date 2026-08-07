using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Norse.Infrastructure.Web.Client.Generator.Tests;

public sealed class ClientComponentRegistrationEmitterTests
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

	// Field declaration order matters here: static field initializers run top-to-bottom, and
	// _routableAssembly's initializer calls BuildReferenceAssembly, which reads _extraReferences --
	// so _sharedFramework/_extraReferences must be declared first (mirrors
	// ComponentDiscoveryTests.cs's own note on the same pitfall).
	static readonly MetadataReference[] _sharedFramework =
	[
		.. Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll")
			.Select(f => MetadataReference.CreateFromFile(f)),
	];

	static readonly MetadataReference[] _extraReferences =
	[
		MetadataReference.CreateFromFile(typeof(FluentValidation.IValidator<>).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Components.RouteAttribute).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
		.. _sharedFramework,
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
			namespace Norse.Empty.Web.Client;

			public sealed class NotAValidator;
			""";
		var (_, outputCompilation) = Run(Empty);
		outputCompilation.SyntaxTrees.Count().ShouldBe(1); // no generated tree added
	}

	[Fact]
	void Emits_the_consuming_assemblys_root_namespace()
	{
		var generated = Generate(ValidatorSource, RoutesAdditionalAssembliesSource);
		generated.ShouldContain("namespace Norse.Hosting.Web.Client;");
	}

	// An AssemblyName is not guaranteed to be a legal C# namespace token -- a hyphenated package id
	// (common: "My-App") or a leading digit would otherwise land verbatim in `namespace {{...}};` and
	// fail to compile for the consumer. No build_property.RootNamespace is configured here, so the
	// fallback path (AssemblyName) is what gets sanitized.
	[Fact]
	void Sanitizes_an_assembly_name_that_is_not_a_legal_namespace_token()
	{
		var generated = GenerateWithAssemblyName("My-App.Web-Client", ValidatorSource);

		generated.ShouldContain("namespace My_App.Web_Client;");
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

	[Fact]
	void Emitted_source_compiles_cleanly_against_real_FluentValidation_and_DependencyInjection_references()
	{
		var (_, outputCompilation) = Run(ValidatorSource, RoutesAdditionalAssembliesSource);
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
			? [.. ReferenceAssemblies.Net110, .. _extraReferences, _routableAssembly]
			: [.. ReferenceAssemblies.Net110, .. _extraReferences];

		var compilation = CSharpCompilation.Create(
			"Norse.Hosting.Web.Client",
			[.. sources.Select(s => CSharpSyntaxTree.ParseText(s))],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		_ = CSharpGeneratorDriver.Create([new ClientComponentRegistrationGenerator().AsSourceGenerator()])
			.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

		return (diagnostics, outputCompilation);
	}

	static string Generate(params string[] sources)
	{
		var (_, outputCompilation) = Run(sources);
		return outputCompilation.SyntaxTrees.Skip(sources.Length).Select(tree => tree.ToString()).Single();
	}

	// Deliberately not routed through Run()/Generate(): those hardcode the harness compilation's
	// AssemblyName to "Norse.Hosting.Web.Client" (always a legal token), which is exactly what these
	// two sanitization fixtures need to vary.
	static string GenerateWithAssemblyName(string assemblyName, params string[] sources)
	{
		MetadataReference[] references = sources.Contains(RoutesAdditionalAssembliesSource)
			? [.. ReferenceAssemblies.Net110, .. _extraReferences, _routableAssembly]
			: [.. ReferenceAssemblies.Net110, .. _extraReferences];

		var compilation = CSharpCompilation.Create(
			assemblyName,
			[.. sources.Select(s => CSharpSyntaxTree.ParseText(s))],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		_ = CSharpGeneratorDriver.Create([new ClientComponentRegistrationGenerator().AsSourceGenerator()])
			.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

		return outputCompilation.SyntaxTrees.Skip(sources.Length).Select(tree => tree.ToString()).Single();
	}

	static string GenerateWithRootNamespaceProperty(string rootNamespace, string assemblyName, params string[] sources)
	{
		MetadataReference[] references = sources.Contains(RoutesAdditionalAssembliesSource)
			? [.. ReferenceAssemblies.Net110, .. _extraReferences, _routableAssembly]
			: [.. ReferenceAssemblies.Net110, .. _extraReferences];

		var compilation = CSharpCompilation.Create(
			assemblyName,
			[.. sources.Select(s => CSharpSyntaxTree.ParseText(s))],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		_ = CSharpGeneratorDriver.Create(
				[new ClientComponentRegistrationGenerator().AsSourceGenerator()],
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

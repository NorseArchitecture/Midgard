using System.Collections.Immutable;
using Microsoft.AspNetCore.Authorization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Components.Authorization;
using ClientPolicyRegistrationGenerator = Norse.Infrastructure.Web.Client.Generator.Policies.PolicyRegistrationGenerator;
using ServerPolicyRegistrationGenerator = Norse.Infrastructure.Web.Server.Generator.Policies.PolicyRegistrationGenerator;

namespace Norse.Infrastructure.Web.Client.Generator.Tests.Policies;

public sealed class PolicyRegistrationGeneratorTests
{
	const string ClientBootstrap =
		"new global::Microsoft.AspNetCore.Authorization.AuthorizationBuilder(services.AddAuthorizationCore())";

	const string ServerBootstrap = "services.AddAuthorizationBuilder()";

	// WASM-reachable references only -- Microsoft.NETCore.App plus the standalone packages a Blazor
	// WebAssembly host can actually reference -- never the ASP.NET Core shared framework. That is what
	// makes the "compiles clean" fact below a proof the client output compiles where it will be called.
	static readonly MetadataReference[] _withoutAuthorization =
	[
		.. ReferenceAssemblies.Net110,
		.. SharedFrameworkReferences.In(Path.GetDirectoryName(typeof(object).Assembly.Location)!),
		MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(NorsePolicyAttribute).Assembly.Location)
	];

	static readonly MetadataReference[] _baseReferences =
	[
		.. _withoutAuthorization,
		MetadataReference.CreateFromFile(typeof(AuthorizationBuilder).Assembly.Location)
	];

	const string EmptySource = "namespace Norse.Hosting.Web.Client;";

	const string AuthNPolicySource = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Components.Authorization;

		namespace Norse.AuthN.Services;

		public static class AuthNPolicies
		{
			[NorsePolicy("AuthN.Public")]
			public static void Public(AuthorizationPolicyBuilder builder) => builder.RequireAssertion(_ => true);
		}
		""";

	const string OtherPolicySameNameSource = """
		using Microsoft.AspNetCore.Authorization;
		using Norse.Abstractions.Components.Authorization;

		namespace Norse.Other.Services;

		public static class OtherPolicies
		{
			[NorsePolicy("AuthN.Public")]
			public static void Public(AuthorizationPolicyBuilder builder) => builder.RequireAssertion(_ => true);
		}
		""";

	static readonly MetadataReference _imageA = Compile("Norse.PolicyPackage.A", AuthNPolicySource);

	static readonly MetadataReference _imageOther = Compile("Norse.PolicyPackage.Other", OtherPolicySameNameSource);

	[Fact]
	void Direct_declaration_in_a_referenced_assembly_is_registered()
	{
		var (_, output) = Run("Norse.Hosting.Web.Client", EmptySource, _imageA);

		Generated(output).ShouldContain("""AddPolicy("AuthN.Public", global::Norse.AuthN.Services.AuthNPolicies.Public)""");
	}

	[Fact]
	void The_output_is_identical_to_the_server_generators_output_except_for_the_bootstrap_line()
	{
		var (_, clientOutput) = Run("Norse.Hosting.Web.Client", EmptySource, _imageA);
		var errors = clientOutput.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.ToArray();
		errors.ShouldBeEmpty(string.Join("\n", errors.Select(e => e.ToString())));

		var client = Lines(Generated(clientOutput));
		var server = Lines(Generated(RunServer("Norse.Hosting.Web.Client", EmptySource, _imageA).Output));

		var clientRest = client.Where(line => !line.Contains(ClientBootstrap, StringComparison.Ordinal)).ToArray();
		var serverRest = server.Where(line => !line.Contains(ServerBootstrap, StringComparison.Ordinal)).ToArray();

		(client.Length - clientRest.Length).ShouldBe(1);
		(server.Length - serverRest.Length).ShouldBe(1);
		clientRest.ShouldBe(serverRest);
	}

	[Fact]
	void The_client_output_never_names_AddAuthorizationBuilder()
	{
		var generated = Generated(Run("Norse.Hosting.Web.Client", EmptySource, _imageA).Output);

		generated.ShouldNotContain("AddAuthorizationBuilder");
		generated.ShouldContain("AddAuthorizationCore()");
	}

	[Fact]
	void An_empty_reference_set_still_emits_a_compilable_AddNorsePolicies()
	{
		var (_, output) = Run("Norse.Hosting.Web.Client", EmptySource);

		Generated(output).ShouldContain("AddNorsePolicies");
		var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.ToArray();
		errors.ShouldBeEmpty(string.Join("\n", errors.Select(e => e.ToString())));
	}

	[Fact]
	void Duplicate_policy_name_across_two_packages_fires_NORSE014_and_registers_neither()
	{
		var (diagnostics, output) = Run("Norse.Hosting.Web.Client", EmptySource, _imageA, _imageOther);

		diagnostics.ShouldContain(d => d.Id == "NORSE014" && d.Severity == DiagnosticSeverity.Error);
		Generated(output).ShouldNotContain("AuthN.Public");
	}

	[Fact]
	void Nothing_is_emitted_when_the_authorization_package_is_not_referenced()
	{
		// The policy image still carries a [NorsePolicy] declaration, so discovery would fire -- only the
		// missing authorization seam keeps the generator silent.
		var (diagnostics, output) = Drive(
			new ClientPolicyRegistrationGenerator(), "Norse.Hosting.Web.Client", EmptySource,
			[.. _withoutAuthorization, _imageA, _imageOther]);

		output.SyntaxTrees.ShouldNotContain(t => t.FilePath.EndsWith("NorsePolicyRegistration.g.cs", StringComparison.Ordinal));
		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	void Nothing_is_emitted_when_the_shared_framework_is_visible()
	{
		// The shared framework carries AuthorizationBuilder too, so the authorization-seam gate alone
		// would let the generator emit -- a second NorsePolicyRegistration beside the server generator's
		// (CS0101). Only the WebApplication gate keeps it silent.
		var (diagnostics, output) = Drive(
			new ClientPolicyRegistrationGenerator(), "Norse.Hosting.Web.Server", EmptySource,
			[.. _withoutAuthorization, .. SharedFrameworkReferences.In(AspNetCoreAppDirectory()), _imageA]);

		output.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.WebApplication").ShouldNotBeNull();
		output.SyntaxTrees.ShouldNotContain(t => t.FilePath.EndsWith("NorsePolicyRegistration.g.cs", StringComparison.Ordinal));
		diagnostics.ShouldBeEmpty();
	}

	// --- Harness -------------------------------------------------------------------------------

	// The ASP.NET Core shared framework sits beside Microsoft.NETCore.App under the dotnet root
	// (shared/Microsoft.AspNetCore.App/{version}). This project deliberately carries no FrameworkReference to
	// it (NU1510, see the csproj), so the directory is located from the runtime's own, preferring the
	// matching version and otherwise the newest installed.
	static string AspNetCoreAppDirectory()
	{
		var runtimeDirectory = new DirectoryInfo(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
		var aspNetRoot = Path.Combine(runtimeDirectory.Parent!.Parent!.FullName, "Microsoft.AspNetCore.App");
		var matching = Path.Combine(aspNetRoot, runtimeDirectory.Name);
		return Directory.Exists(matching)
			? matching
			: Directory.GetDirectories(aspNetRoot)
				.OrderByDescending(path => path, StringComparer.Ordinal)
				.First();
	}

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

	static (ImmutableArray<Diagnostic> Diagnostics, Compilation Output) Run(
		string assemblyName, string source, params MetadataReference[] extraReferences) =>
		Drive(new ClientPolicyRegistrationGenerator(), assemblyName, source, [.. _baseReferences, .. extraReferences]);

	static (ImmutableArray<Diagnostic> Diagnostics, Compilation Output) RunServer(
		string assemblyName, string source, params MetadataReference[] extraReferences) =>
		Drive(new ServerPolicyRegistrationGenerator(), assemblyName, source, [.. _baseReferences, .. extraReferences]);

	static (ImmutableArray<Diagnostic> Diagnostics, Compilation Output) Drive(
		IIncrementalGenerator generator, string assemblyName, string source, MetadataReference[] references)
	{
		var compilation = CSharpCompilation.Create(
			assemblyName,
			[CSharpSyntaxTree.ParseText(source)],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		_ = CSharpGeneratorDriver.Create(generator.AsSourceGenerator())
			.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

		return (diagnostics, output);
	}

	static string Generated(Compilation output) =>
		output.SyntaxTrees
			.Single(t => t.FilePath.EndsWith("NorsePolicyRegistration.g.cs", StringComparison.Ordinal))
			.ToString();

	static string[] Lines(string text) => text.Split('\n');
}

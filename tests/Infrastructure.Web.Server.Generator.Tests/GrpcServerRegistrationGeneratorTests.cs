using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Norse.Infrastructure.Web.Server.Generator.Tests;

public sealed class GrpcServerRegistrationGeneratorTests
{
	const string Contract = """
		using System.ServiceModel;
		using System.Threading.Tasks;
		using Norse.Abstractions.Contracts;

		namespace Norse.Identity.Web.Server;

		[ServiceContract]
		public interface IAuthenticationService
		{
			Task<Outcome<LoginResult>> Login(LoginRequest request);
			Task<Outcome<Unit>> Register(RegisterRequest request);
		}

		public sealed record LoginRequest;
		public sealed record RegisterRequest;
		public sealed record LoginResult;

		public sealed class AuthenticationService : IAuthenticationService
		{
			public Task<Outcome<LoginResult>> Login(LoginRequest request) =>
				Task.FromResult(Outcome<LoginResult>.Ok(new LoginResult()));

			public Task<Outcome<Unit>> Register(RegisterRequest request) =>
				Task.FromResult(Outcome<Unit>.Ok(Unit.Value));
		}
		""";

	[Fact]
	void Emits_MapGrpcService_and_EnableGrpcWeb_for_the_discovered_implementation()
	{
		var generated = Generate(Contract);
		generated.ShouldContain("global::Microsoft.AspNetCore.Builder.GrpcEndpointRouteBuilderExtensions.MapGrpcService<global::Norse.Identity.Web.Server.AuthenticationService>(app)");
		generated.ShouldContain("global::Microsoft.AspNetCore.Builder.GrpcWebEndpointConventionBuilderExtensions.EnableGrpcWeb(");
	}

	[Fact]
	void Emits_the_consuming_assemblys_root_namespace()
	{
		var generated = Generate(Contract);
		generated.ShouldContain("namespace Norse.Hosting.Web.Server;");
	}

	[Fact]
	void Emits_one_guarded_SetSurrogate_per_distinct_payload_including_Unit()
	{
		var generated = Generate(Contract);
		generated.ShouldContain("SetSurrogate(typeof(global::Norse.Identity.Web.Server.LoginResult))");
		generated.ShouldContain("SetSurrogate(typeof(global::Norse.Abstractions.Contracts.Unit))");

		var isDefinedCount = CountOccurrences(generated, "model.IsDefined(");
		var setSurrogateCount = CountOccurrences(generated, ".SetSurrogate(");
		isDefinedCount.ShouldBe(setSurrogateCount);
		isDefinedCount.ShouldBe(2); // LoginResult, Unit
	}

	[Fact]
	void RegisterNorseOutcomeSurrogates_is_called_first_inside_MapNorseGrpcServices()
	{
		var generated = Generate(Contract);
		var mapMethodIndex = generated.IndexOf("MapNorseGrpcServices", StringComparison.Ordinal);
		var callIndex = generated.IndexOf("RegisterNorseOutcomeSurrogates();", mapMethodIndex, StringComparison.Ordinal);
		var mapGrpcServiceIndex = generated.IndexOf("MapGrpcService<", StringComparison.Ordinal);
		callIndex.ShouldBeGreaterThan(-1);
		callIndex.ShouldBeLessThan(mapGrpcServiceIndex);
	}

	[Fact]
	void Registers_the_identifier_serializers_before_the_Outcome_surrogates()
	{
		var generated = Generate(Contract);
		var registerIndex = generated.IndexOf(
			"global::Norse.Infrastructure.Web.Grpc.IdentifierSerializers.Register(model);",
			StringComparison.Ordinal);
		var surrogateIndex = generated.IndexOf(".SetSurrogate(", StringComparison.Ordinal);
		registerIndex.ShouldBeGreaterThan(-1);
		registerIndex.ShouldBeLessThan(surrogateIndex);
	}

	[Fact]
	void NORSE020_fires_when_the_implementation_is_absent()
	{
		var withoutImplementation = Contract.Replace(
			"""
			public sealed class AuthenticationService : IAuthenticationService
			{
				public Task<Outcome<LoginResult>> Login(LoginRequest request) =>
					Task.FromResult(Outcome<LoginResult>.Ok(new LoginResult()));

				public Task<Outcome<Unit>> Register(RegisterRequest request) =>
					Task.FromResult(Outcome<Unit>.Ok(Unit.Value));
			}
			""",
			"");
		var diagnostics = GenerateDiagnostics(withoutImplementation);
		diagnostics.ShouldContain(d => d.Id == "NORSE020" && d.Severity == DiagnosticSeverity.Error);
	}

	[Fact]
	void NORSE021_fires_on_a_payload_short_name_collision_across_namespaces()
	{
		// Two distinct Widget payload types, same short name, different namespaces — each its own
		// file-scoped namespace, so each lives in its own syntax tree (a file-scoped namespace
		// declaration is the only namespace-related content its file may carry).
		const string FirstWidgetContract = """
			using System.ServiceModel;
			using System.Threading.Tasks;
			using Norse.Abstractions.Contracts;

			namespace Norse.Identity.Web.Server;

			[ServiceContract]
			public interface IWidgetService
			{
				Task<Outcome<Widget>> Get(LoginRequest request);
			}

			public sealed record Widget;
			""";
		const string SecondWidgetContract = """
			using System.ServiceModel;
			using System.Threading.Tasks;
			using Norse.Abstractions.Contracts;

			namespace Norse.Other.Web.Server;

			[ServiceContract]
			public interface IOtherService
			{
				Task<Outcome<Widget>> Get(OtherRequest request);
			}

			public sealed record OtherRequest;
			public sealed record Widget;
			""";
		var diagnostics = GenerateDiagnostics(Contract, FirstWidgetContract, SecondWidgetContract);
		diagnostics.ShouldContain(d => d.Id == "NORSE021" && d.Severity == DiagnosticSeverity.Error);
	}

	[Fact]
	void Emitted_source_compiles_cleanly_against_real_ASP_NET_Core_and_protobuf_net_references()
	{
		var (_, outputCompilation) = Run(Contract);
		var errors = outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.ToArray();
		errors.ShouldBeEmpty(string.Join("\n", errors.Select(e => e.ToString())));
	}

	static int CountOccurrences(string haystack, string needle)
	{
		var count = 0;
		var index = 0;
		while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
		{
			count++;
			index += needle.Length;
		}
		return count;
	}

	// Every assembly under the .NET / ASP.NET Core shared framework directories the
	// FrameworkReference resolves against — enumerated from already-loaded types' own assembly
	// directories rather than hardcoding an SDK path, so this survives an SDK bump untouched.
	// Two directories: object/IHost live in Microsoft.NETCore.App, WebApplication et al. in
	// Microsoft.AspNetCore.App.
	static readonly MetadataReference[] _sharedFrameworks =
	[
		.. Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll")
			.Select(f => MetadataReference.CreateFromFile(f)),
		.. Directory.GetFiles(Path.GetDirectoryName(typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly.Location)!, "*.dll")
			.Select(f => MetadataReference.CreateFromFile(f)),
	];

	static readonly MetadataReference[] _extraReferences =
	[
		MetadataReference.CreateFromFile(typeof(System.ServiceModel.ServiceContractAttribute).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Norse.Abstractions.Contracts.Outcome<>).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(ProtoBuf.Meta.RuntimeTypeModel).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(ProtoBuf.Meta.TypeModel).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Builder.GrpcEndpointRouteBuilderExtensions).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Builder.GrpcWebEndpointConventionBuilderExtensions).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Norse.Infrastructure.Web.Grpc.IdentifierSerializers).Assembly.Location),
		.. _sharedFrameworks,
	];

	static (ImmutableArray<Diagnostic> Diagnostics, Compilation OutputCompilation) Run(params string[] sources)
	{
		var compilation = CSharpCompilation.Create(
			"Norse.Hosting.Web.Server",
			[.. sources.Select(s => CSharpSyntaxTree.ParseText(s))],
			[.. ReferenceAssemblies.Net110, .. _extraReferences],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		_ = CSharpGeneratorDriver.Create([new GrpcServerRegistrationGenerator().AsSourceGenerator()])
			.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

		return (diagnostics, outputCompilation);
	}

	static string Generate(params string[] sources)
	{
		var (_, outputCompilation) = Run(sources);
		return outputCompilation.SyntaxTrees.Skip(sources.Length).Select(tree => tree.ToString()).Single();
	}

	static ImmutableArray<Diagnostic> GenerateDiagnostics(params string[] sources) =>
		Run(sources).Diagnostics;
}

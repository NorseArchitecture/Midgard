using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Norse.Infrastructure.Web.Client.Generator.Tests;

public sealed class GrpcClientRegistrationGeneratorTests
{
	const string Contract = """
		using System.ServiceModel;
		using System.Threading.Tasks;
		using Norse.Abstractions.Contracts;

		namespace Norse.AuthN.Services;

		[ServiceContract]
		public interface IAuthenticationService
		{
			Task<Outcome<LoginResult>> Login(LoginRequest request);
			Task<Outcome<Unit>> Register(RegisterRequest request);
		}

		public sealed record LoginRequest;
		public sealed record RegisterRequest;
		public sealed record LoginResult;
		""";

	[Fact]
	void Emits_CreateGrpcService_over_an_intercepted_invoker()
	{
		var generated = Generate(Contract);
		generated.ShouldContain("global::Grpc.Core.Interceptors.CallInvokerExtensions.Intercept(");
		generated.ShouldContain("new global::Norse.Infrastructure.Web.Client.Grpc.OutcomeClientInterceptor()");
		generated.ShouldContain("global::ProtoBuf.Grpc.Client.GrpcClientFactory.CreateGrpcService<global::Norse.AuthN.Services.IAuthenticationService>(invoker)");
	}

	[Fact]
	void Emits_a_CallInvoker_overload_as_the_primary_registration_surface()
	{
		var generated = Generate(Contract);
		generated.ShouldContain("global::Grpc.Core.CallInvoker callInvoker");
	}

	[Fact]
	void The_GrpcChannel_overload_delegates_to_the_CallInvoker_overload()
	{
		var generated = Generate(Contract);
		generated.ShouldContain("global::Grpc.Net.Client.GrpcChannel channel");
		generated.ShouldContain("AddNorseGrpcClients(services, channel.CreateCallInvoker())");
	}

	[Fact]
	void Emits_the_consuming_assemblys_root_namespace()
	{
		var generated = Generate(Contract);
		generated.ShouldContain("namespace Norse.Hosting.Web.Client;");
	}

	[Fact]
	void Emits_one_guarded_SetSurrogate_per_distinct_payload_including_Unit()
	{
		var generated = Generate(Contract);
		generated.ShouldContain("SetSurrogate(typeof(global::Norse.AuthN.Services.LoginResult))");
		generated.ShouldContain("SetSurrogate(typeof(global::Norse.Abstractions.Contracts.Unit))");

		var isDefinedCount = CountOccurrences(generated, "model.IsDefined(");
		var setSurrogateCount = CountOccurrences(generated, ".SetSurrogate(");
		isDefinedCount.ShouldBe(setSurrogateCount);
		isDefinedCount.ShouldBe(2); // LoginResult, Unit
	}

	[Fact]
	void RegisterNorseOutcomeSurrogates_is_called_first_inside_AddNorseGrpcClients()
	{
		var generated = Generate(Contract);
		var methodIndex = generated.IndexOf("AddNorseGrpcClients", StringComparison.Ordinal);
		var callIndex = generated.IndexOf("RegisterNorseOutcomeSurrogates();", methodIndex, StringComparison.Ordinal);
		var interceptIndex = generated.IndexOf("Intercept(", StringComparison.Ordinal);
		callIndex.ShouldBeGreaterThan(-1);
		callIndex.ShouldBeLessThan(interceptIndex);
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
	void Emits_nothing_when_no_contract_is_discovered()
	{
		const string NoContract = """
			namespace Norse.Empty.Web.Client;

			public sealed class NotAContract;
			""";
		var (_, outputCompilation) = Run(NoContract);
		outputCompilation.SyntaxTrees.Count().ShouldBe(1); // no generated tree added
	}

	[Fact]
	void NORSE021_fires_on_a_payload_short_name_collision_across_namespaces()
	{
		const string FirstWidgetContract = """
			using System.ServiceModel;
			using System.Threading.Tasks;
			using Norse.Abstractions.Contracts;

			namespace Norse.AuthN.Services;

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

			namespace Norse.Other.Web.Client;

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
	void RegisterNorseOutcomeSurrogates_uses_a_blocking_guard_not_a_flag_first_race()
	{
		var generated = Generate(Contract);
		generated.ShouldNotContain("Interlocked.Exchange");
		generated.ShouldContain("global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication");

		// The actual registration work must live INSIDE the lazy-guarded factory method, not
		// accidentally sit outside it where a concurrent caller could reach it unguarded.
		var coreMethodIndex = generated.IndexOf("RegisterNorseOutcomeSurrogatesCore()", StringComparison.Ordinal);
		var registerCallIndex = generated.IndexOf(
			"global::Norse.Infrastructure.Web.Grpc.IdentifierSerializers.Register(model);",
			StringComparison.Ordinal);
		coreMethodIndex.ShouldBeGreaterThan(-1);
		registerCallIndex.ShouldBeGreaterThan(coreMethodIndex);
	}

	[Fact]
	void Emitted_source_compiles_cleanly_against_real_protobuf_net_grpc_and_client_references()
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

	static readonly MetadataReference[] _sharedFramework =
	[
		.. Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll")
			.Select(f => MetadataReference.CreateFromFile(f)),
	];

	static readonly MetadataReference[] _extraReferences =
	[
		MetadataReference.CreateFromFile(typeof(System.ServiceModel.ServiceContractAttribute).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Norse.Abstractions.Contracts.Outcome<>).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(ProtoBuf.Meta.RuntimeTypeModel).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(ProtoBuf.Meta.TypeModel).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(ProtoBuf.Grpc.Client.GrpcClientFactory).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(global::Grpc.Core.Interceptors.ChannelExtensions).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(global::Grpc.Net.Client.GrpcChannel).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Norse.Infrastructure.Web.Client.Grpc.OutcomeClientInterceptor).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(Norse.Infrastructure.Web.Grpc.IdentifierSerializers).Assembly.Location),
		.. _sharedFramework,
	];

	static (ImmutableArray<Diagnostic> Diagnostics, Compilation OutputCompilation) Run(params string[] sources)
	{
		var compilation = CSharpCompilation.Create(
			"Norse.Hosting.Web.Client",
			[.. sources.Select(s => CSharpSyntaxTree.ParseText(s))],
			[.. ReferenceAssemblies.Net110, .. _extraReferences],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		_ = CSharpGeneratorDriver.Create([new GrpcClientRegistrationGenerator().AsSourceGenerator()])
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

using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Server.Xml.Generator.Tests;

/// <summary>
/// Compiles a fixture contract set through the real generator, loads the emitted assembly, and calls
/// the generated <c>NorseXmlShapeRegistration.Build()</c> — asserting the returned
/// <see cref="XmlShapeRegistry"/> genuinely resolves every fixture shape by its real CLR
/// <see cref="Type"/>, not merely that the emitted source text mentions the right names.
/// </summary>
public sealed class RegistrationEmissionTests
{
	const string TwoContractFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.Registration;

		[DataContract]
		public sealed record PingRequest
		{
			[DataMember]
			public Result<string> Value { get; init; }
		}

		public sealed record PingResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class PingController : GrpcControllerBase
		{
			public Task<ActionResult<PingResponse>> Do([FromBody] PingRequest request) =>
				Task.FromResult(new ActionResult<PingResponse>(new PingResponse()));
		}
		""";

	const string SharedTypeFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.RegistrationShared;

		public sealed record SharedAddress
		{
			[DataMember]
			public Result<string> Line1 { get; init; }
		}

		[DataContract]
		public sealed record RequestA
		{
			[DataMember]
			public SharedAddress Home { get; init; } = null!;
		}

		[DataContract]
		public sealed record RequestB
		{
			[DataMember]
			public SharedAddress Office { get; init; } = null!;
		}

		public sealed record SharedResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class ControllerA : GrpcControllerBase
		{
			public Task<ActionResult<SharedResponse>> Do([FromBody] RequestA request) =>
				Task.FromResult(new ActionResult<SharedResponse>(new SharedResponse()));
		}

		public sealed class ControllerB : GrpcControllerBase
		{
			public Task<ActionResult<SharedResponse>> Do([FromBody] RequestB request) =>
				Task.FromResult(new ActionResult<SharedResponse>(new SharedResponse()));
		}
		""";

	[Fact]
	void Build_registers_every_fixture_shape_resolvable_by_its_real_contract_type()
	{
		var (registry, resolveType) = BuildRegistration(TwoContractFixture);

		registry.TryGet(resolveType("Norse.Fixtures.Registration.PingRequest"), out var requestShape).ShouldBeTrue();
		requestShape!.ContractType.ShouldBe(resolveType("Norse.Fixtures.Registration.PingRequest"));

		registry.TryGet(resolveType("Norse.Fixtures.Registration.PingResponse"), out var responseShape).ShouldBeTrue();
		responseShape!.ContractType.ShouldBe(resolveType("Norse.Fixtures.Registration.PingResponse"));
	}

	[Fact]
	void Build_never_double_registers_a_type_shared_across_two_controllers()
	{
		// SharedAddress is reachable from both ControllerA and ControllerB — XmlShapeGenerator's own
		// closure-walk dedup (Task 6) already guarantees exactly one SharedAddressXmlShape class; this
		// proves Build() only ever calls registry.Add(...) once for it too (XmlShapeRegistry.Add throws
		// ArgumentException on a duplicate ContractType — Build() succeeding at all is the assertion).
		var (registry, resolveType) = BuildRegistration(SharedTypeFixture);

		registry.TryGet(resolveType("Norse.Fixtures.RegistrationShared.SharedAddress"), out var sharedShape).ShouldBeTrue();
		sharedShape!.ContractType.ShouldBe(resolveType("Norse.Fixtures.RegistrationShared.SharedAddress"));
		registry.TryGet(resolveType("Norse.Fixtures.RegistrationShared.RequestA"), out _).ShouldBeTrue();
		registry.TryGet(resolveType("Norse.Fixtures.RegistrationShared.RequestB"), out _).ShouldBeTrue();
	}

	const string EnumFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.RegistrationEnum;

		public enum Status
		{
			Active = 1,
			Inactive = 2
		}

		// Never reachable from any [DataMember] closure — no facade controller's request/response graph
		// exposes it, so it must never appear in NorseEnumNameRegistration.Build()'s registry.
		public enum Unexposed
		{
			OnlyValue = 1
		}

		[DataContract]
		public sealed record PingRequest
		{
			[DataMember]
			public Result<Status> State { get; init; }
		}

		public sealed record PingResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class PingController : GrpcControllerBase
		{
			public Task<ActionResult<PingResponse>> Do([FromBody] PingRequest request) =>
				Task.FromResult(new ActionResult<PingResponse>(new PingResponse()));
		}
		""";

	[Fact]
	void EnumRegistration_Build_registers_a_table_for_every_reachable_enum_and_none_for_an_unexposed_one()
	{
		var (diagnostics, outputCompilation) = GeneratorTestHarness.Run(EnumFixture);
		diagnostics.ShouldBeEmpty();

		var assembly = Emit(outputCompilation);
		var rootNamespace = outputCompilation.AssemblyName!;
		var registrationType = assembly.GetType($"{rootNamespace}.NorseXmlShapes.NorseEnumNameRegistration")
			?? throw new InvalidOperationException("NorseEnumNameRegistration was not generated.");
		var buildMethod = registrationType.GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!;

		var registry = (EnumNameRegistry)buildMethod.Invoke(null, null)!;

		var statusType = assembly.GetType("Norse.Fixtures.RegistrationEnum.Status")
			?? throw new InvalidOperationException("Status was not found in the compiled fixture assembly.");
		var unexposedType = assembly.GetType("Norse.Fixtures.RegistrationEnum.Unexposed")
			?? throw new InvalidOperationException("Unexposed was not found in the compiled fixture assembly.");

		registry.TryGet(statusType, out var statusTable).ShouldBeTrue();
		statusTable!.TypeName.ShouldBe("Status");
		statusTable.Count.ShouldBe(2);

		registry.TryGet(unexposedType, out _).ShouldBeFalse();
	}

	const string SharedEnumFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.RegistrationSharedEnum;

		public enum Status
		{
			Active = 1,
			Inactive = 2
		}

		[DataContract]
		public sealed record RequestA
		{
			[DataMember]
			public Result<Status> State { get; init; }
		}

		[DataContract]
		public sealed record RequestB
		{
			[DataMember]
			public Result<Status> State { get; init; }
		}

		public sealed record SharedResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class ControllerA : GrpcControllerBase
		{
			public Task<ActionResult<SharedResponse>> Do([FromBody] RequestA request) =>
				Task.FromResult(new ActionResult<SharedResponse>(new SharedResponse()));
		}

		public sealed class ControllerB : GrpcControllerBase
		{
			public Task<ActionResult<SharedResponse>> Do([FromBody] RequestB request) =>
				Task.FromResult(new ActionResult<SharedResponse>(new SharedResponse()));
		}
		""";

	[Fact]
	void EnumRegistration_Build_never_double_registers_an_enum_type_shared_across_two_controllers()
	{
		// Status is reachable from both RequestA (via ControllerA) and RequestB (via ControllerB) —
		// mirrors Build_never_double_registers_a_type_shared_across_two_controllers above, but for
		// NorseEnumNameRegistration: RegistrationEmitter.EmitEnumRegistration dedups by ScalarTypeName
		// before emitting fields, so exactly one Status table field/registry.Add(...) call results
		// (EnumNameRegistry.Add throws InvalidOperationException on a duplicate EnumType — Build()
		// succeeding at all is the assertion), and both shapes' Result<Status> members resolve to it.
		var (diagnostics, outputCompilation) = GeneratorTestHarness.Run(SharedEnumFixture);
		diagnostics.ShouldBeEmpty();

		var assembly = Emit(outputCompilation);
		var rootNamespace = outputCompilation.AssemblyName!;
		var registrationType = assembly.GetType($"{rootNamespace}.NorseXmlShapes.NorseEnumNameRegistration")
			?? throw new InvalidOperationException("NorseEnumNameRegistration was not generated.");
		var buildMethod = registrationType.GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!;

		var registry = (EnumNameRegistry)buildMethod.Invoke(null, null)!;

		var statusType = assembly.GetType("Norse.Fixtures.RegistrationSharedEnum.Status")
			?? throw new InvalidOperationException("Status was not found in the compiled fixture assembly.");

		registry.TryGet(statusType, out var statusTable).ShouldBeTrue();
		statusTable!.TypeName.ShouldBe("Status");
		statusTable.Count.ShouldBe(2);
	}

	[Fact]
	void EnumRegistration_Build_returns_a_working_empty_registry_when_the_compilation_exposes_no_enums()
	{
		const string NoEnumFixture = "namespace Norse.Fixtures.RegistrationEnumEmpty;\n\npublic sealed record Plain;";

		var (diagnostics, outputCompilation) = GeneratorTestHarness.Run(NoEnumFixture);
		diagnostics.ShouldBeEmpty();

		var assembly = Emit(outputCompilation);
		var registrationType = assembly.GetType($"{outputCompilation.AssemblyName}.NorseXmlShapes.NorseEnumNameRegistration")
			?? throw new InvalidOperationException("NorseEnumNameRegistration was not generated for an enum-free compilation.");
		var buildMethod = registrationType.GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!;

		var registry = (EnumNameRegistry)buildMethod.Invoke(null, null)!;

		registry.TryGet(typeof(DayOfWeek), out _).ShouldBeFalse();
	}

	[Fact]
	void Build_returns_a_working_empty_registry_when_the_compilation_has_no_facade_controllers()
	{
		const string NoControllerFixture = "namespace Norse.Fixtures.RegistrationEmpty;\n\npublic sealed record Plain;";

		var (diagnostics, outputCompilation) = GeneratorTestHarness.Run(NoControllerFixture);
		diagnostics.ShouldBeEmpty();

		var assembly = Emit(outputCompilation);
		var registrationType = assembly.GetType($"{outputCompilation.AssemblyName}.NorseXmlShapes.NorseXmlShapeRegistration")
			?? throw new InvalidOperationException("NorseXmlShapeRegistration was not generated for a controller-free compilation.");
		var buildMethod = registrationType.GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!;

		var registry = (XmlShapeRegistry)buildMethod.Invoke(null, null)!;

		registry.TryGet(typeof(object), out _).ShouldBeFalse();
	}

	static (XmlShapeRegistry Registry, Func<string, Type> ResolveType) BuildRegistration(string source)
	{
		var (diagnostics, outputCompilation) = GeneratorTestHarness.Run(source);
		diagnostics.ShouldBeEmpty();

		var assembly = Emit(outputCompilation);
		var rootNamespace = outputCompilation.AssemblyName!;
		var registrationType = assembly.GetType($"{rootNamespace}.NorseXmlShapes.NorseXmlShapeRegistration")
			?? throw new InvalidOperationException("NorseXmlShapeRegistration was not generated.");
		var buildMethod = registrationType.GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!;

		var registry = (XmlShapeRegistry)buildMethod.Invoke(null, null)!;
		Type ResolveType(string fullyQualifiedName) =>
			assembly.GetType(fullyQualifiedName) ?? throw new InvalidOperationException($"Type '{fullyQualifiedName}' was not found in the compiled fixture assembly.");

		return (registry, ResolveType);
	}

	static Assembly Emit(Compilation compilation)
	{
		using MemoryStream stream = new();
		var emitResult = compilation.Emit(stream);
		emitResult.Success.ShouldBeTrue(string.Join("\n", emitResult.Diagnostics));

		stream.Position = 0;
		return Assembly.Load(stream.ToArray());
	}
}

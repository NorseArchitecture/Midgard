using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Norse.Infrastructure.Web.Server.Generator.Xml;
using Norse.Infrastructure.Web.Server.Xml;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Generator.Tests.Xml;

/// <summary>
///     Shared compilation-harness helpers for <see cref="XmlShapeGenerator" /> tests — mirrors the sibling
///     <c>Infrastructure.Web.Server.Generator.Tests</c> harness idiom (compile fixture source through the
///     real generator via <see cref="CSharpGeneratorDriver" />, inspect diagnostics/output).
///     <c>GrpcControllerBase</c> does not exist on the platform until Task 10 (a different repo, Asgard) —
///     <see cref="StubGrpcControllerBase" /> supplies the exact metadata name
///     (<c>Norse.Abstractions.Web.Server.Facade.GrpcControllerBase</c>) the generator keys on, deriving
///     from the real ASP.NET Core <c>ControllerBase</c> so fixture controllers behave exactly like the
///     eventual real base class.
/// </summary>
static class GeneratorTestHarness
{
	public const string StubGrpcControllerBase = """
		namespace Norse.Abstractions.Web.Server.Facade;

		public abstract class GrpcControllerBase : Microsoft.AspNetCore.Mvc.ControllerBase
		{
		}
		""";

	public static readonly MetadataReference[] ExtraReferences =
	[
		MetadataReference.CreateFromFile(typeof(Result<>).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(IXmlShape).Assembly.Location),
		.. ReferenceAssemblies.Bcl,
		.. ReferenceAssemblies.AspNetCore
	];

	/// <summary>
	///     Preview lang version — required from Task 6 onward, not by Task 5's own fixtures (diagnostics-
	///     only; never called a <c>Result&lt;T&gt;</c> member). <c>Result&lt;T&gt;</c>/<c>Success&lt;T&gt;</c>
	///     carry the <c>[Union]</c> C# 15 preview attribute, and generator-emitted writer code (Task 6) calls
	///     <c>Result&lt;T&gt;.TryGetValue(...)</c> directly — without this, the emitted source (parsed with
	///     whatever parse options this fixture compilation carries) fails CS8652 ("the feature 'unions' is
	///     currently in Preview") the moment it's added to the compilation, cascading into unrelated CS1061s.
	/// </summary>
	public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

	/// <summary>
	///     The one compilation-options instance every harness compilation shares.
	///     <c>WithGeneralDiagnosticOption(ReportDiagnostic.Error)</c> plus <c>WithWarningLevel(9999)</c>
	///     mirror <c>TreatWarningsAsErrors</c>/<c>WarningLevel</c> from the platform's real root
	///     <c>Directory.Build.props</c> — every real consuming project (a downstream <c>.csproj</c> like
	///     Yggdrasil's) compiles generator output warnings-as-errors, so this harness must too, or a
	///     generator that emits genuinely unreachable code (CS0162 among it) can pass here and still break
	///     a real build. Without this, <c>Emit(...).Success</c> stays <see langword="true" /> even with
	///     warning-severity diagnostics sitting unexamined in the emit result — exactly how the CS0162
	///     regression this harness now guards against shipped past this project's own test suite once.
	///     Declared before <see cref="StubFacadeReference" /> deliberately: static initializers run in
	///     textual order, and that field's <see cref="EmitToMetadataReference" /> call reads this one.
	/// </summary>
	static readonly CSharpCompilationOptions _compilationOptions =
		new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
			.WithGeneralDiagnosticOption(ReportDiagnostic.Error)
			.WithWarningLevel(9999);

	/// <summary>
	///     <see cref="StubGrpcControllerBase" /> compiled into its own metadata assembly, carrying the
	///     real package's assembly name — the referenced-assembly discovery tests' base class. A realm
	///     fixture assembly and the host compilation referencing it must both resolve
	///     <c>GrpcControllerBase</c> to this one reference: compiling the stub into host source alongside
	///     a realm assembly built against its own copy would split the symbol identity and defeat the
	///     generator's <see cref="SymbolEqualityComparer" /> derivation check — correctly, but uselessly
	///     for a fixture.
	/// </summary>
	public static readonly MetadataReference StubFacadeReference =
		EmitToMetadataReference("Norse.Abstractions.Web.Server", ExtraReferences, StubGrpcControllerBase);

	/// <summary>
	///     Builds the fixture compilation, stub base class included, unrun — warnings-as-errors via the
	///     shared <see cref="_compilationOptions" />.
	/// </summary>
	public static Compilation CreateCompilation(params string[] sources) =>
		CSharpCompilation.Create(
			"Norse.Hosting.Web.Server",
			[
				.. new[] { StubGrpcControllerBase }.Concat(sources)
					.Select(s => CSharpSyntaxTree.ParseText(s, ParseOptions))
			],
			ExtraReferences,
			_compilationOptions);

	/// <summary>Runs the real generator once against fixture source, the stub base class included.</summary>
	public static (ImmutableArray<Diagnostic> Diagnostics, Compilation OutputCompilation) Run(params string[] sources)
	{
		_ = CSharpGeneratorDriver.Create([new XmlShapeGenerator().AsSourceGenerator()], parseOptions: ParseOptions)
			.RunGeneratorsAndUpdateCompilation(CreateCompilation(sources), out var outputCompilation,
				out var diagnostics);
		return (diagnostics, outputCompilation);
	}

	public static ImmutableArray<Diagnostic> GenerateDiagnostics(params string[] sources) =>
		Run(sources).Diagnostics;

	/// <summary>
	///     Compiles fixture source into an in-memory assembly image and returns it as a
	///     <see cref="MetadataReference" /> — the referenced-realm-assembly build path: a contract +
	///     controller pair compiled here reaches the generator only through the host compilation's
	///     reference closure, never as syntax. Same warnings-as-errors bar as
	///     <see cref="CreateCompilation" />; a fixture that doesn't compile clean throws here rather than
	///     surfacing as a confusing downstream discovery miss.
	/// </summary>
	public static MetadataReference EmitToMetadataReference(string assemblyName, MetadataReference[] references,
		params string[] sources)
	{
		var compilation = CSharpCompilation.Create(
			assemblyName,
			[.. sources.Select(s => CSharpSyntaxTree.ParseText(s, ParseOptions))],
			references,
			_compilationOptions);

		using MemoryStream stream = new();
		var emitResult = compilation.Emit(stream);
		if (!emitResult.Success)
			throw new InvalidOperationException(
				$"Fixture assembly '{assemblyName}' failed to compile:\n{string.Join("\n", emitResult.Diagnostics)}");

		return MetadataReference.CreateFromImage(stream.ToArray());
	}

	/// <summary>
	///     Runs the real generator against a host compilation whose <c>GrpcControllerBase</c> arrives by
	///     metadata (<see cref="StubFacadeReference" />, always included) rather than in source, plus any
	///     referenced fixture assemblies — the harness path for referenced-assembly discovery tests.
	///     <paramref name="sources" /> may be empty: a host with no source controllers at all is exactly
	///     the case the reference-closure widening exists for.
	/// </summary>
	public static (ImmutableArray<Diagnostic> Diagnostics, Compilation OutputCompilation) RunWithReferences(
		MetadataReference[] additionalReferences, params string[] sources)
	{
		var compilation = CSharpCompilation.Create(
			"Norse.Hosting.Web.Server",
			[.. sources.Select(s => CSharpSyntaxTree.ParseText(s, ParseOptions))],
			[.. ExtraReferences, StubFacadeReference, .. additionalReferences],
			_compilationOptions);

		_ = CSharpGeneratorDriver.Create([new XmlShapeGenerator().AsSourceGenerator()], parseOptions: ParseOptions)
			.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
		return (diagnostics, outputCompilation);
	}
}

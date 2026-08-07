using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Norse.Abstractions.Emit;
using Norse.Infrastructure.Web.Grpc.Generator.Shared;

namespace Norse.Infrastructure.Web.Client.Generator;

/// <summary>
/// Discovers a compilation's FluentValidation validators and Blazor-routable assemblies at compile
/// time and emits <c>AddNorseClientComponents()</c> — idempotent validator registration via
/// <c>TryAddEnumerable</c>, plus a <c>RoutesAdditionalAssemblies</c> singleton when Yggdrasil's
/// routing composition seam (<c>Norse.Hosting.Web.Components.RoutesAdditionalAssemblies</c>) is
/// visible to the compilation. Emits nothing when neither a validator nor the routing seam is
/// discovered.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ClientComponentRegistrationGenerator : IIncrementalGenerator
{
	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var models = context.CompilationProvider
			.Combine(context.AnalyzerConfigOptionsProvider)
			// The compilation alone cannot answer whether this project's own .razor files declare routes
			// -- the Razor SDK's generator shares this generation pass, so its [Route]-attributed output
			// is invisible here. The raw .razor AdditionalFiles are the only channel that can.
			.Combine(ComponentDiscovery.RazorRouteDeclarationProvider(context))
			.Select(Discover);
		context.RegisterSourceOutput(models, static (productionContext, result) =>
		{
			if (result.Discovery.Validators.IsEmpty && !result.Discovery.RoutesAdditionalAssembliesTypeExists)
				return;
			productionContext.AddSource("NorseClientComponentRegistration.g.cs",
				SourceText.From(ClientComponentRegistrationEmitter.Emit(result.RootNamespace, result.Discovery), Utf8NoBom.Encoding));
		});
	}

	static DiscoveryResult Discover(((Compilation Compilation, AnalyzerConfigOptionsProvider Options) Semantic, bool OwnAssemblyDeclaresRazorRoutes) input, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var (compilation, options) = input.Semantic;
		var discovery = ComponentDiscovery.Discover(compilation, input.OwnAssemblyDeclaresRazorRoutes);
		var rootNamespace = RootNamespaceResolution.Resolve(compilation, options);
		return new DiscoveryResult(rootNamespace, discovery);
	}

	sealed record DiscoveryResult(string RootNamespace, ComponentDiscoveryResult Discovery);
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Norse.Abstractions.Emit;
using Norse.Infrastructure.Web.Grpc.Generator.Shared;

namespace Norse.Infrastructure.Web.Server.Generator;

/// <summary>
/// Discovers a compilation's FluentValidation validators and Blazor-routable assemblies at compile
/// time and emits <c>AddNorseClientComponents()</c> — the server-host counterpart of Task 4's
/// client-side generator, same name/shape (idempotent validator registration via
/// <c>TryAddEnumerable</c>, plus a <c>RoutesAdditionalAssemblies</c> singleton for the Router) so a
/// host references one package or the other, never both — plus
/// <c>AddNorseComponentAssemblies()</c>, an <c>AddAdditionalAssemblies</c> extension on
/// <c>RazorComponentsEndpointConventionBuilder</c> feeding Razor endpoint discovery, the render-mode
/// half of composition the client side has no counterpart for. Both are emitted only when Yggdrasil's
/// routing composition seam (<c>Norse.Hosting.Web.Components.RoutesAdditionalAssemblies</c>) is
/// visible to the compilation, mirroring Task 4's "non-Yggdrasil consumers get validators-only
/// output" rule. Emits nothing when neither a validator nor the routing seam is discovered.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ServerComponentRegistrationGenerator : IIncrementalGenerator
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
			productionContext.AddSource("NorseServerComponentRegistration.g.cs",
				SourceText.From(ServerComponentRegistrationEmitter.Emit(result.RootNamespace, result.Discovery), Utf8NoBom.Encoding));
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

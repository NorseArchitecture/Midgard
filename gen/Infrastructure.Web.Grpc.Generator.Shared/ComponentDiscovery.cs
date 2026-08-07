using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Norse.Infrastructure.Web.Grpc.Generator.Shared;

// Linked into both Infrastructure.Web.Server.Generator and Infrastructure.Web.Client.Generator via
// <Compile Include> -- same shared-source-per-consumer shape as ContractDiscovery.cs, for the same
// reason (Roslyn generators can't reference other analyzer-only assemblies). Transport-agnostic
// discovery of FluentValidation validators and Blazor-routable assemblies for the generator heads
// (Tasks 4/5) to emit host registration/composition code from. Validator discovery and route
// discovery are independent concerns -- a compilation without FluentValidation referenced still
// yields route results, and a compilation without Norse.Hosting.Web.Components.Routes referenced
// still yields validator results, so non-Yggdrasil consumers get validators-only output.
static class ComponentDiscovery
{
	const string ValidatorInterfaceMetadataName = "FluentValidation.IValidator`1";
	const string RouteAttributeMetadataName = "Microsoft.AspNetCore.Components.RouteAttribute";
	const string RoutesMetadataName = "Norse.Hosting.Web.Components.Routes";
	const string RoutesAdditionalAssembliesMetadataName = "Norse.Hosting.Web.Components.RoutesAdditionalAssemblies";
	const string RazorComponentExtension = ".razor";
	const string PageDirective = "@page";

	/// <summary>
	/// Discovers every FluentValidation validator and Blazor-routable assembly visible to
	/// <paramref name="compilation"/> -- its own assembly plus every referenced assembly, mirroring
	/// <c>ContractDiscovery</c>'s walk (<c>compilation.Assembly</c> plus
	/// <c>compilation.SourceModule.ReferencedAssemblySymbols</c>, each swept via a recursive walk of
	/// its global namespace). References <c>ContractDiscovery</c> by name only (no <c>cref</c>) --
	/// not every consumer linking this file also links ContractDiscovery.cs.
	/// <para>
	/// <paramref name="ownAssemblyDeclaresRazorRoutes"/> carries the half of route discovery the
	/// semantic walk structurally cannot reach, sourced from <see cref="RazorRouteDeclarationProvider"/>
	/// -- see <see cref="ComponentDiscoveryResult.RequiresOwnAssemblyRouterEntry"/> for why it exists
	/// and <see cref="DeclaresRazorRoute(SourceText)"/> for how it's determined. Required rather than
	/// defaulted: a consumer that forgets to wire the provider would otherwise silently reintroduce
	/// the exact gap it closes.
	/// </para>
	/// </summary>
	public static ComponentDiscoveryResult Discover(Compilation compilation, bool ownAssemblyDeclaresRazorRoutes)
	{
		var format = SymbolDisplayFormat.FullyQualifiedFormat;
		IAssemblySymbol[] assemblies = [compilation.Assembly, .. compilation.SourceModule.ReferencedAssemblySymbols];

		var validators = DiscoverValidators(compilation, assemblies, format);
		var (routableMarkers, routesHolderMarker, routesHolderIsOwnAssembly, ownAssemblyRoutableMarker) = DiscoverRoutes(compilation, assemblies, format);
		var routesAdditionalAssembliesTypeExists = compilation.GetTypeByMetadataName(RoutesAdditionalAssembliesMetadataName) is not null;

		return new ComponentDiscoveryResult(
			validators,
			routableMarkers,
			routesHolderMarker,
			routesHolderIsOwnAssembly,
			routesAdditionalAssembliesTypeExists,
			ownAssemblyRoutableMarker,
			// Same exclusion the per-assembly walk in DiscoverRoutes already applies to the routes-holder
			// assembly, applied to the Razor-sourced half for the same reason: when the compilation's own
			// assembly IS the Routes holder, the Router's AppAssembly already covers it, and naming it a
			// second time via AdditionalAssemblies makes Blazor throw on duplicate route discovery.
			ownAssemblyDeclaresRazorRoutes && !routesHolderIsOwnAssembly);
	}

	/// <summary>
	/// The compilation's own <c>.razor</c> files, reduced to a single "does this project declare at
	/// least one routable page" flag for <see cref="Discover"/>.
	/// <para>
	/// This is the only channel that answers the question. A <c>.razor</c> page becomes a
	/// <c>[Route]</c>-attributed type solely through the Razor SDK's own incremental generator, which
	/// is registered on the same compilation as this one and therefore runs in the same generation
	/// pass -- and Roslyn hands every generator in a pass the original, pre-generation compilation, so
	/// no generator ever observes another's output. The semantic walk in <see cref="DiscoverRoutes"/>
	/// consequently sees a referenced assembly's pages (already real metadata, generated during
	/// <em>that</em> assembly's own build) but never the compiling project's own. The raw file text is
	/// what remains, and it is genuinely available: <c>Microsoft.NET.Sdk.Razor.SourceGenerators.targets</c>
	/// adds every <c>RazorComponentWithTargetPath</c> item to <c>@(AdditionalFiles)</c>, which is
	/// exactly how the Razor generator itself receives them.
	/// </para>
	/// <para>
	/// <c>.cshtml</c> is deliberately not scanned: <c>@page</c> there declares a Razor Pages endpoint,
	/// a different routing mechanism entirely, and contributes nothing to a Blazor <c>Router</c>.
	/// </para>
	/// </summary>
	public static IncrementalValueProvider<bool> RazorRouteDeclarationProvider(IncrementalGeneratorInitializationContext context) =>
		context.AdditionalTextsProvider
			.Where(static text => text.Path.EndsWith(RazorComponentExtension, StringComparison.OrdinalIgnoreCase))
			.Select(static (text, cancellationToken) => text.GetText(cancellationToken) is { } source && DeclaresRazorRoute(source))
			.Collect()
			.Select(static (declarations, _) => declarations.Contains(true));

	/// <summary>
	/// Whether <paramref name="source"/> -- the raw text of a <c>.razor</c> file -- carries at least one
	/// <c>@page</c> route directive.
	/// <para>
	/// A directive is recognized only as the first non-whitespace token on its line, followed by
	/// horizontal whitespace and an opening quote whose closing quote lands on the same line: every
	/// <c>@page</c> is <c>@page "/some/template"</c>, and requiring the quoted template is what keeps
	/// the word <c>@page</c> in prose from counting. Razor comments (<c>@* ... *@</c>) and HTML
	/// comments (<c>&lt;!-- ... --&gt;</c>) are skipped, including across lines; an unterminated opener
	/// is treated as ordinary text rather than swallowing the rest of the file, so a stray <c>&lt;!--</c>
	/// can never hide a real directive below it. <c>@@page</c> is Razor's escape for a literal <c>@</c>
	/// and never matches, nor does an identifier continuation such as <c>@pageSize</c>.
	/// </para>
	/// <para>
	/// This does not tokenize C#, so a <c>@page "..."</c> sequence sitting at the start of a line
	/// inside a multi-line string literal in an <c>@code</c> block reads as a directive. That
	/// asymmetry is deliberate and safe in this direction: a false positive only adds an assembly with
	/// no routes to the Router's scan list, which discovers nothing and changes no behavior, whereas a
	/// false negative leaves a real page unreachable. Correctness is never traded for it -- only a
	/// no-op.
	/// </para>
	/// </summary>
	public static bool DeclaresRazorRoute(SourceText source)
	{
		var text = source.ToString();
		var index = 0;
		var atLineStart = true;

		while (index < text.Length)
		{
			var current = text[index];

			if (current == '\n')
			{
				atLineStart = true;
				index++;
				continue;
			}

			if (atLineStart && current == '@' && IsPageDirective(text, index))
				return true;

			// Only leading whitespace keeps a line at its start; everything else, including a comment or an
			// escaped '@', puts the scan mid-line -- and comments are consumed whole below, so nothing
			// inside one is ever read as a directive.
			if (!char.IsWhiteSpace(current))
				atLineStart = false;
			index += SkipLength(text, index);
		}

		return false;
	}

	/// <summary>How many characters the token at <paramref name="index"/> occupies: a whole Razor or HTML comment, both characters of an escaped <c>@@</c>, or a single character otherwise.</summary>
	static int SkipLength(string text, int index)
	{
		if (Matches(text, index, "@*"))
			return CommentLength(text, index, 2, "*@");

		if (Matches(text, index, "<!--"))
			return CommentLength(text, index, 4, "-->");

		// "@@" is Razor's escape for a literal '@' -- "@@page" renders as text, never a directive.
		return Matches(text, index, "@@") ? 2 : 1;
	}

	/// <summary>The span of a comment opened at <paramref name="opener"/>, or a single character when it is never closed -- an unterminated opener is ordinary text, not a swallow-everything-below.</summary>
	static int CommentLength(string text, int opener, int openerLength, string terminator)
	{
		var close = text.IndexOf(terminator, opener + openerLength, StringComparison.Ordinal);
		return close < 0 ? 1 : close + terminator.Length - opener;
	}

	/// <summary>Whether the <c>@</c> at <paramref name="at"/> opens a <c>@page</c> directive carrying a single-line quoted route template.</summary>
	static bool IsPageDirective(string text, int at)
	{
		if (!Matches(text, at, PageDirective))
			return false;

		var index = at + PageDirective.Length;
		// Word boundary: "@pageSize" is a C# expression, and "@page" alone declares no route.
		if (!IsHorizontalWhitespace(text, index))
			return false;

		while (IsHorizontalWhitespace(text, index))
			index++;

		if (index >= text.Length || text[index] != '"')
			return false;

		var close = text.IndexOf('"', index + 1);
		if (close < 0)
			return false;

		var newline = text.IndexOf('\n', index + 1);
		return newline < 0 || close < newline;
	}

	static bool IsHorizontalWhitespace(string text, int at) => at < text.Length && (text[at] == ' ' || text[at] == '\t');

	static bool Matches(string text, int at, string value) =>
		at + value.Length <= text.Length && string.CompareOrdinal(text, at, value, 0, value.Length) == 0;

	/// <summary>
	/// A non-abstract named type implementing <c>FluentValidation.IValidator&lt;T&gt;</c>, matched by
	/// symbol on the interface's original definition. Empty (not an error) when FluentValidation isn't
	/// referenced. Two further guards keep a discovered validator usable by the emitted registration
	/// code rather than merely discoverable: <c>Compilation.IsSymbolAccessibleWithin</c> excludes a
	/// validator the discovering compilation can't legally reference via <c>typeof(...)</c>
	/// (own-assembly internal types pass this check for free -- same assembly means accessible-to-self
	/// -- so this is only ever a restriction on referenced-assembly validators), and <see
	/// cref="HasAccessiblePublicInstanceConstructor"/> excludes a validator
	/// Microsoft.Extensions.DependencyInjection could reference but never actually construct (its
	/// reflection-based activation only ever sees public constructors -- an explicitly-declared
	/// internal, protected, or no-modifier (private, for a class member) constructor fails this even
	/// for an own-assembly validator, though a fully implicit, no-constructor-declared class does not:
	/// the compiler emits that constructor as IL-public regardless of the containing type's own
	/// accessibility).
	/// </summary>
	static ImmutableArray<ValidatorModel> DiscoverValidators(Compilation compilation, IAssemblySymbol[] assemblies, SymbolDisplayFormat format)
	{
		var validatorInterface = compilation.GetTypeByMetadataName(ValidatorInterfaceMetadataName);
		if (validatorInterface is null)
			return [];

		return
		[
			.. assemblies
				.SelectMany(a => AllTypes(a.GlobalNamespace))
				// IsGenericType excludes open generic validator definitions (e.g. FluentValidation's own
				// InlineValidator<T>, always present once FluentValidation itself is a referenced
				// assembly) -- Tasks 4/5 need a concrete, closed request type to key a
				// typeof(IValidator<TRequest>) registration on; an open T can't back one.
				.Where(t => t is { TypeKind: TypeKind.Class, IsAbstract: false, IsGenericType: false })
				.Where(t => compilation.IsSymbolAccessibleWithin(t, compilation.Assembly))
				.Where(HasAccessiblePublicInstanceConstructor)
				// A validator implementing IValidator<T> more than once (legal, if unusual, in
				// FluentValidation) gets one ValidatorModel per implemented interface -- otherwise every
				// T past the first silently never gets a registration.
				.SelectMany(t => ValidatedRequestTypes(t, validatorInterface)
					.Select(request => new ValidatorModel(t.ToDisplayString(format), request.ToDisplayString(format))))
				.OrderBy(v => v.ValidatorTypeName, StringComparer.Ordinal)
				.ThenBy(v => v.RequestTypeName, StringComparer.Ordinal)
		];
	}

	/// <summary>
	/// DI resolves a validator via reflection-based activation, which only ever considers public
	/// instance constructors (<c>Type.GetConstructors()</c>'s default, no-<c>BindingFlags</c> overload)
	/// -- an explicitly-declared internal, protected, or no-modifier (private, for a class member)
	/// constructor is invisible to it even when the validator type itself is perfectly accessible (e.g.
	/// a public validator class with only a private constructor). A class with NO constructor declared
	/// at all is a different case, deliberately not excluded here: the C# compiler emits that implicit
	/// constructor as IL-public regardless of the containing type's own accessibility (verified against
	/// real <c>Type.GetConstructors()</c>/<c>Activator.CreateInstance</c> reflection behavior, not just
	/// <see cref="Accessibility"/> naming), so <see cref="INamedTypeSymbol.InstanceConstructors"/>
	/// already reports it as <see cref="Accessibility.Public"/> and this filter needs no special case
	/// for it.
	/// </summary>
	static bool HasAccessiblePublicInstanceConstructor(INamedTypeSymbol type) =>
		type.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public);

	static IEnumerable<ITypeSymbol> ValidatedRequestTypes(INamedTypeSymbol type, INamedTypeSymbol validatorInterface) =>
		type.AllInterfaces
			.Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, validatorInterface))
			.Select(i => i.TypeArguments[0]);

	/// <summary>
	/// A routable assembly carries at least one <c>[Route]</c>-attributed type; its marker is the
	/// first such type, ordinal. The assembly declaring <c>Norse.Hosting.Web.Components.Routes</c> is
	/// reported separately as <see cref="ComponentDiscoveryResult.RoutesHolderMarker"/> (the Routes
	/// type itself -- always unambiguous, unlike a per-assembly first-of-many pick) and excluded from
	/// <see cref="ComponentDiscoveryResult.RoutableAssemblyMarkers"/> entirely: the Router's
	/// <c>AppAssembly</c> already covers it, and Blazor throws on duplicate route discovery if it also
	/// shows up in <c>AdditionalAssemblies</c>. Also reports, separately again, whichever of those
	/// markers (if any) belongs to <paramref name="compilation"/>'s own assembly -- Task 5's Razor
	/// endpoint discovery excludes it (<c>MapRazorComponents&lt;App&gt;</c>'s implicit root already
	/// covers it) even though Task 4/5's Router registration does not (the Router has no equivalent
	/// implicit-root exception), so the two consumers need this split, not just the raw marker list.
	/// Route markers get the same <c>Compilation.IsSymbolAccessibleWithin</c> guard <see
	/// cref="DiscoverValidators"/> applies -- an inaccessible routed type in a referenced assembly
	/// can't back a <c>typeof(...)</c> in the emitted registration either. Also reports whether the
	/// routes-holder assembly <em>is</em>
	/// <paramref name="compilation"/>'s own assembly (<see
	/// cref="ComponentDiscoveryResult.RoutesHolderIsOwnAssembly"/>) -- the routes-holder assembly is
	/// always excluded from the per-assembly walk above (it's covered by <c>RoutesHolderMarker</c>
	/// itself, not the generic first-of-many pick), so when Routes lives in the compilation's own
	/// assembly, <c>OwnAssemblyRoutableMarker</c> comes back null with nothing left to match -- Task 5's
	/// endpoint-list composition needs the separate boolean to still exclude that in-compilation holder.
	/// </summary>
	static (ImmutableArray<string> RoutableMarkers, string? RoutesHolderMarker, bool RoutesHolderIsOwnAssembly, string? OwnAssemblyRoutableMarker) DiscoverRoutes(Compilation compilation, IAssemblySymbol[] assemblies, SymbolDisplayFormat format)
	{
		var routesType = compilation.GetTypeByMetadataName(RoutesMetadataName);
		var routesHolderAssembly = routesType?.ContainingAssembly;
		var routesHolderMarker = routesType?.ToDisplayString(format);
		var routesHolderIsOwnAssembly = routesHolderAssembly is not null && SymbolEqualityComparer.Default.Equals(routesHolderAssembly, compilation.Assembly);

		var routeAttribute = compilation.GetTypeByMetadataName(RouteAttributeMetadataName);
		if (routeAttribute is null)
			return ([], routesHolderMarker, routesHolderIsOwnAssembly, null);

		var perAssemblyMarkers =
			assemblies
				.Where(a => !SymbolEqualityComparer.Default.Equals(a, routesHolderAssembly))
				.Select(a => (Assembly: a, Marker: AllTypes(a.GlobalNamespace)
					.Where(t => t.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, routeAttribute)))
					.Where(t => compilation.IsSymbolAccessibleWithin(t, compilation.Assembly))
					.Select(t => t.ToDisplayString(format))
					.OrderBy(name => name, StringComparer.Ordinal)
					.FirstOrDefault()))
				.Where(x => x.Marker is not null)
				.ToImmutableArray();

		ImmutableArray<string> routableMarkers =
		[
			.. perAssemblyMarkers
				.Select(x => x.Marker!)
				.OrderBy(marker => marker, StringComparer.Ordinal)
		];

		var ownAssemblyRoutableMarker = perAssemblyMarkers
			.Where(x => SymbolEqualityComparer.Default.Equals(x.Assembly, compilation.Assembly))
			.Select(x => x.Marker)
			.FirstOrDefault();

		return (routableMarkers, routesHolderMarker, routesHolderIsOwnAssembly, ownAssemblyRoutableMarker);
	}

	/// <summary>Recursive walk of every named type reachable from <paramref name="root"/>, including nested namespaces and each type's own nested types -- same shape as <c>ContractDiscovery.AllTypes</c> plus nested-type recursion, kept local rather than shared so this file has no compile-time dependency on ContractDiscovery.cs being linked into the same consumer.</summary>
	static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol root)
	{
		foreach (var type in root.GetTypeMembers())
			foreach (var nested in AllTypes(type))
				yield return nested;

		foreach (var child in root.GetNamespaceMembers())
			foreach (var type in AllTypes(child))
				yield return type;
	}

	/// <summary>Yields <paramref name="type"/> itself followed by every type nested inside it, at any depth -- a validator or routed component declared as a nested class (scoped inside a partial class, a common test-fixture-grouping pattern) is otherwise silently unreachable from the namespace-only walk above.</summary>
	static IEnumerable<INamedTypeSymbol> AllTypes(INamedTypeSymbol type)
	{
		yield return type;

		foreach (var nested in type.GetTypeMembers())
			foreach (var descendant in AllTypes(nested))
				yield return descendant;
	}
}

/// <summary>Discovered validators, routable-assembly markers, and the routes-holder assembly's own marker (plus whether that holder assembly is the discovering compilation's own), plus whether a routing composition seam (<c>RoutesAdditionalAssemblies</c>) is present for Tasks 4/5 to emit against. <c>OwnAssemblyDeclaresRazorRoutes</c> closes out the record: whether the compilation's own <c>.razor</c> sources declare at least one <c>@page</c> route and the compilation is not itself the routes holder — the half of own-assembly route discovery <c>OwnAssemblyRoutableMarker</c> structurally cannot cover, since a Razor page only becomes a <c>[Route]</c>-attributed type through a generator sharing this one's pass.</summary>
sealed record ComponentDiscoveryResult(
	ImmutableArray<ValidatorModel> Validators,
	ImmutableArray<string> RoutableAssemblyMarkers,
	string? RoutesHolderMarker,
	bool RoutesHolderIsOwnAssembly,
	bool RoutesAdditionalAssembliesTypeExists,
	string? OwnAssemblyRoutableMarker,
	bool OwnAssemblyDeclaresRazorRoutes)
{
	/// <summary>
	/// Whether the Router registration must name the compilation's own assembly through the generated
	/// registration class rather than a discovered page type. True only when the own assembly declares
	/// Razor routes that nothing already in <see cref="RoutableAssemblyMarkers"/> represents: a
	/// C#-declared <c>[Route]</c> type in the same assembly makes <see cref="OwnAssemblyRoutableMarker"/>
	/// non-null and already puts that assembly in the list, and naming it twice makes Blazor throw on
	/// duplicate route discovery. The generated class is the marker precisely because no Razor page type
	/// is nameable at generation time — but any type in the assembly identifies it equally well, since
	/// the emitted registration only ever reads <c>typeof(...).Assembly</c>.
	/// <para>
	/// Deliberately absent from the endpoint half of composition: <c>MapRazorComponents&lt;App&gt;</c>'s
	/// implicit root already covers the host's own assembly there.
	/// </para>
	/// </summary>
	public bool RequiresOwnAssemblyRouterEntry => OwnAssemblyDeclaresRazorRoutes && OwnAssemblyRoutableMarker is null;
}

/// <summary>A discovered FluentValidation validator -- both names global::-qualified.</summary>
sealed record ValidatorModel(string ValidatorTypeName, string RequestTypeName);

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Server.Generator.Policies;

/// <summary>One declared policy: its name, the type that declares it, and the method that configures it.</summary>
readonly record struct PolicyDeclaration(string Name, string DeclaringType, string MethodName);

/// <summary>
///     One decorated method that is not a usable declaration, with the reason and where to report it.
///     <paramref name="Location" /> is the attribute's own syntax location when the declaration is source in
///     this compilation, and <see cref="Microsoft.CodeAnalysis.Location.None" /> when it arrived as
///     metadata — a referenced assembly has no syntax to point at. Not every malformed metadata
///     declaration reaches this type; see <see cref="PolicyDeclarationDiscovery" />'s remarks.
/// </summary>
readonly record struct InvalidDeclaration(string QualifiedMethod, string Reason, Location Location);

/// <summary>Both halves of discovery: what may be emitted, and what must be reported instead.</summary>
readonly record struct PolicyDiscoveryResult(
	ImmutableArray<PolicyDeclaration> Valid,
	ImmutableArray<InvalidDeclaration> Invalid);

/// <summary>
///     Finds every <c>[NorsePolicy]</c>-decorated method in the compilation and in the assemblies the
///     compiler resolved a reference to. Reads <b>attributes from metadata</b>, never method bodies: a
///     realm's declarations arrive as a published package, and a body does not cross that boundary.
/// </summary>
/// <remarks>
///     Scope is deliberately <c>SourceModule.ReferencedAssemblySymbols</c> and no further. The emitter names
///     each declaring type directly, so discovering a symbol this compilation cannot resolve a reference to
///     would emit code that does not compile. See the task's discovery-contract note.
///     <para>
///     The metadata backstop is not uniform: a real build's default <c>MetadataImportOptions.Public</c>
///     makes a private or internal <c>[NorsePolicy]</c> method on a referenced assembly invisible to
///     <see cref="Collect" />'s <c>GetMembers()</c> walk entirely, so NORSE015 can never fire for that
///     rejection class in production -- only this project's test harness, which overrides to <c>.All</c>,
///     exercises it.
///     </para>
/// </remarks>
static class PolicyDeclarationDiscovery
{
	const string AttributeMetadataName = "Norse.Abstractions.Web.Server.Authorization.NorsePolicyAttribute";

	internal static PolicyDiscoveryResult Discover(Compilation compilation)
	{
		var attribute = compilation.GetTypeByMetadataName(AttributeMetadataName);
		if (attribute is null)
			return new PolicyDiscoveryResult([], []);

		var found = ImmutableArray.CreateBuilder<PolicyDeclaration>();
		var invalid = ImmutableArray.CreateBuilder<InvalidDeclaration>();

		foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols.Append(compilation.Assembly))
			Walk(assembly.GlobalNamespace, attribute, compilation, found, invalid);

		// Reference order varies by machine; unsorted output would make the generated file differ between
		// agents and break the deterministic-build convention. Invalid entries are sorted too, so diagnostic
		// order is stable across builds.
		return new PolicyDiscoveryResult(
			[.. found.OrderBy(d => d.Name, StringComparer.Ordinal)],
			[.. invalid.OrderBy(d => d.QualifiedMethod, StringComparer.Ordinal)]);
	}

	static void Walk(INamespaceSymbol ns, INamedTypeSymbol attribute, Compilation compilation,
		ImmutableArray<PolicyDeclaration>.Builder found, ImmutableArray<InvalidDeclaration>.Builder invalid)
	{
		foreach (var member in ns.GetMembers())
		{
			switch (member)
			{
				case INamespaceSymbol nested:
					Walk(nested, attribute, compilation, found, invalid);
					break;
				case INamedTypeSymbol type:
					Collect(type, attribute, compilation, found, invalid);
					break;
			}
		}
	}

	static void Collect(INamedTypeSymbol type, INamedTypeSymbol attribute, Compilation compilation,
		ImmutableArray<PolicyDeclaration>.Builder found, ImmutableArray<InvalidDeclaration>.Builder invalid)
	{
		foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
		{
			// The attribute is inspected FIRST, before any filtering. Filtering first would make an
			// attributed private or instance method vanish silently -- a declared policy that never
			// registers and only fails when a request asks for it, which is precisely the failure mode this
			// mechanism exists to eliminate. Anything decorated is either a valid declaration or a build
			// error; there is no third outcome.
			var declaration = method.GetAttributes().FirstOrDefault(a =>
				SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));
			if (declaration is null)
				continue;

			if (Validate(method, type, compilation, declaration) is { } reason)
			{
				// Source declarations belong to Asgard's bundled analyzer, which reports them with a real
				// location in the project that authored them. Reporting here too would double-strike the
				// same mistake in the same build. The halves are disjoint by provenance.
				if (declaration.ApplicationSyntaxReference is not null)
					continue;

				// Metadata only, so there is never syntax to point at. The qualified method name carries the
				// identification instead -- a diagnostic with no location must still say exactly what is
				// wrong and where it lives.
				invalid.Add(new InvalidDeclaration(
					$"{type.ToDisplayString()}.{method.Name}", reason, Location.None));
				continue;
			}

			var name = (string)declaration.ConstructorArguments[0].Value!;
			found.Add(new PolicyDeclaration(name, type.ToDisplayString(), method.Name));
		}

		foreach (var nested in type.GetTypeMembers())
			Collect(nested, attribute, compilation, found, invalid);
	}

	/// <summary>Returns null when the declaration is well-formed, or the human-readable reason it is not.</summary>
	static string? Validate(IMethodSymbol method, INamedTypeSymbol type, Compilation compilation,
		AttributeData declaration)
	{
		// Deliberately not list-pattern syntax (`is [{ ... }]`): netstandard2.0's reference assemblies
		// don't define System.Index, which Roslyn's list-pattern binder requires even for a fixed-length,
		// non-slice pattern -- CS0518/CS0656 in this generator's own compilation. Length + indexer checks
		// below are semantically identical, just spelled without the feature this TFM can't support.
		if (declaration.ConstructorArguments.Length != 1 ||
			declaration.ConstructorArguments[0].Value is not string name ||
			string.IsNullOrWhiteSpace(name))
			return "the policy name must be a non-empty constant string";
		if (!method.IsStatic)
			return "the method must be static";
		if (method.DeclaredAccessibility != Accessibility.Public)
			return "the method must be public -- generated registration lives in another assembly";
		if (method.IsGenericMethod || type.IsGenericType)
			return "neither the method nor its containing type may be generic";
		if (!compilation.IsSymbolAccessibleWithin(method, compilation.Assembly))
			return "the method must be accessible from the consuming compilation";
		if (!method.ReturnsVoid)
			return "the method must return void";

		var builder = compilation.GetTypeByMetadataName(
			"Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder");
		// Deliberately not list-pattern syntax here either, for the same CS0518/CS0656 reason noted
		// above -- Length + indexer checks, semantically identical to `is [{ Type: var parameter }]`.
		return method.Parameters.Length == 1
			&& builder is not null
			&& SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, builder) ?
			null :
			"the method must take exactly one AuthorizationPolicyBuilder parameter";
	}
}

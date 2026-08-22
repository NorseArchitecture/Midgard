using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Server.Generator;

#pragma warning disable RS2008 // No analyzer-release ledger, matching the platform's other generators.

/// <summary>
///     NORSE014/NORSE015 — Midgard's half of the <c>[NorsePolicy]</c> discovery contract, covering
///     declarations reachable from the compilation's resolved reference set, source and metadata alike.
///     NORSE015 shares its id and validation rules with Asgard's <c>NorsePolicyDeclarationAnalyzer</c>
///     (Task 2), which strikes the same malformed shape for source in the project that authors it, where
///     the diagnostic has a real location to report; this half catches what arrives as metadata instead.
///     The two halves are disjoint by provenance (<see cref="Policies.PolicyDeclarationDiscovery" />), so
///     a declaration is never reported twice.
/// </summary>
static class Diagnostics
{
	public static readonly DiagnosticDescriptor DuplicatePolicyName = new(
		"NORSE014",
		"Duplicate authorization policy name",
		"Policy '{0}' is declared more than once: {1}",
		"Norse.Mediator",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description:
		"Two declarations of the same policy name would resolve last-write-wins at runtime, making the "
		+ "effective policy depend on reference order. Sibling of NORSE010's duplicate-handler strike: the "
		+ "ambiguity is refused at build time rather than resolved arbitrarily. Reads [NorsePolicy] from "
		+ "metadata, so it sees declarations arriving as packages -- which is how every realm reaches the "
		+ "composition root.");

	public static readonly DiagnosticDescriptor InvalidPolicyDeclaration = new(
		"NORSE015",
		"Invalid [NorsePolicy] declaration",
		"'{0}' is decorated with [NorsePolicy] but {1}",
		"Norse.Mediator",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description:
		"A decorated method is either a valid declaration or a build error -- never silently skipped. "
		+ "Filtering for public/static before reading the attribute would make an attributed private or "
		+ "instance method vanish, producing a policy that is declared in source, absent from registration, "
		+ "and discovered only when a request asks for it. The generator reports every rejection instead.");
}

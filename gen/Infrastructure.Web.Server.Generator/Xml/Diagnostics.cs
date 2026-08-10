using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Server.Generator.Xml;

#pragma warning disable RS2008 // No analyzer-release ledger, matching the platform's other generators.

/// <summary>
///     Futhark's build-time shape-law catalog (design spec §14) — every diagnostic is an error, fires in
///     the host compilation where the facade controller exposes the offending contract (exposure
///     scoping, spec §15), and is reported through <see cref="DiagnosticInfo" /> rather than a live
///     <see cref="Diagnostic" /> until <c>RegisterSourceOutput</c>.
/// </summary>
/// <remarks>
///     IDs shifted up two from the plan's original NORSE020-026: a repo-wide sweep at implementation
///     time found NORSE020/NORSE021 already live in this same realm's sibling generator
///     (<c>GrpcServerRegistrationGenerator</c> — missing gRPC implementation / payload short-name
///     collision). NORSE022-028 sit clean between that pair and Urðarbrunnr's NORSE030-034 block.
///     NORSE035-037 extend this same block (the codex-review-fixes wave, 2026-08-09): NORSE035
///     (<see cref="DuplicateShapeShortName" />) covers a short-name collision across distinct contract
///     types reachable from the closure — trivially reachable once reference-closure discovery merges
///     independent realms; NORSE036 (<see cref="ContractConstructionInaccessible" />) covers a
///     contract's construction surface — the parameterless constructor and every wire member's
///     <c>set</c>/<c>init</c> accessor — going unreachable from the host: the generated reader compiles
///     <c>new {Contract} { Member = ... }</c> in the HOST assembly, so an internal or private constructor
///     or accessor that trivially passes same-assembly compilation still fails CS0272/CS0122 the moment
///     reference-closure discovery pulls the contract in from another assembly. NORSE036 also strikes the
///     construction surface's third failure mode, one the "inaccessible" framing above doesn't cover: a
///     contract — most commonly a positional record, whose only generated constructor takes its primary
///     constructor's parameters — that declares no parameterless constructor AT ALL, accessible or not.
///     Same law, same diagnostic, distinct message wording ("has no parameterless constructor at all"
///     rather than "is not accessible from the host"). NORSE037 (<see cref="NestedFacadeController" />,
///     ruled by Buvy 2026-08-09) covers the controller symbol itself, struck before any closure walk
///     begins: facade controllers are namespace-level types, so a <c>GrpcControllerBase</c> descendant
///     nested inside another type is a build error, struck identically whether discovered from host
///     source or a referenced assembly's reference closure — loud diagnostic, never silent exclusion.
/// </remarks>
static class Diagnostics
{
	public static readonly DiagnosticDescriptor RawScalarInRequestClosure = new(
		"NORSE022", "Raw scalar in request closure",
		"Member '{0}' on '{1}' is a raw scalar in the request closure — request scalars wrap in Result<T> or Result<T>?",
		"Norse.Xml",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	public static readonly DiagnosticDescriptor ResultInResponseClosure = new(
		"NORSE023", "Result<T> reachable in response closure",
		"Member '{0}' on '{1}' wraps Result<T> in the response closure — response scalars are never wrapped",
		"Norse.Xml",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	public static readonly DiagnosticDescriptor SharedAcrossDirections = new(
		"NORSE024", "Type shared across the request/response boundary",
		"'{0}' is reachable from both the request closure and the response closure — you shared a type across the boundary",
		"Norse.Xml",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	public static readonly DiagnosticDescriptor InvalidContractShape = new(
		"NORSE025", "Contract type is not sealed, object-based, and non-generic",
		"'{0}' must be sealed, derive from object only, and be non-generic to be reachable from a Futhark contract",
		"Norse.Xml",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	public static readonly DiagnosticDescriptor MemberUniquenessViolation = new(
		"NORSE026", "Member uniqueness violation",
		"{0}", "Norse.Xml",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	public static readonly DiagnosticDescriptor TaxonomyViolation = new(
		"NORSE027", "Scalar taxonomy violation",
		"{0}", "Norse.Xml",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	public static readonly DiagnosticDescriptor BodyTypeNotDataContract = new(
		"NORSE028", "Facade action body-bound type is not a [DataContract]",
		"Action parameter '{0}' of type '{1}' is body-bound but '{1}' carries no [DataContract]", "Norse.Xml",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	// NORSE029 (FlagsEnumInClosure) lived here until the 2026-08-09 amendment deleted it outright —
	// flags ride the closure bare and the channels translate. The ID is retired, never reused.

	public static readonly DiagnosticDescriptor DuplicateShapeShortName = new(
		"NORSE035", "Duplicate shape short name across the closure",
		"Short name '{0}' collides across distinct contract types reachable from this closure: {1} — XML shape class and hint names derive from the short name, so distinct contract types crossing the same host must not share one",
		"Norse.Xml",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	public static readonly DiagnosticDescriptor ContractConstructionInaccessible = new(
		"NORSE036", "Contract construction surface inaccessible from the host",
		"{0}", "Norse.Xml",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	public static readonly DiagnosticDescriptor NestedFacadeController = new(
		"NORSE037", "Facade controller nested inside another type",
		"'{0}' is nested inside '{1}' — facade controllers are namespace-level types",
		"Norse.Xml",
		DiagnosticSeverity.Error, isEnabledByDefault: true);
}

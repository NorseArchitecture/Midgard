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
}

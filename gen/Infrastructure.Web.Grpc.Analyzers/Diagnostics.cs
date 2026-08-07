using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Grpc.Analyzers;

#pragma warning disable RS2008 // No analyzer-release ledger, matching the platform's other generators/analyzers.

/// <summary>
/// NORSE080 — claimed 2026-08-06. A new block: NORSE070-079 is fully claimed for realm-dependency law
/// specifically (see Svartálfheim's Architecture.Analyzers), a different concern than this one.
/// NotConfigurable: the rule is not a severity preference. Spec:
/// ../../../Glitnir/docs/Midgard/specs/2026-08-06-wire-model-registration-guard-design.md.
/// </summary>
static class Diagnostics
{
	const string Category = "Norse.Infrastructure.Web.Grpc";

	public static readonly DiagnosticDescriptor WireModelMutatedOutsideGuard = new(
		"NORSE080", "RuntimeTypeModel mutated outside the registration guard",
		"'{0}' mutates a shared RuntimeTypeModel directly — registration must go through WireModelRegistrationGuard.EnsureRegistered, the only call site proven safe under concurrent first touch", Category,
		DiagnosticSeverity.Error, isEnabledByDefault: true, customTags: WellKnownDiagnosticTags.NotConfigurable);
}

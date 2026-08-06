using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Norse.Infrastructure.Web.Grpc.Analyzers;

/// <summary>
/// NORSE080: bans any direct invocation of <c>RuntimeTypeModel.Add</c>/<c>.Add&lt;T&gt;</c> unless it's
/// lexically part of an invocation of <c>WireModelRegistrationGuard.EnsureRegistered</c> — the check-then-act
/// and flag-first shapes an unguarded <c>Add</c> invites are exactly the defect class filed 2026-08-03
/// (<c>../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md</c>) and found live five
/// times, including inside a hand-rolled test fixture, before this rule existed. Not realm-scoped, unlike
/// Svartálfheim's WireFormatAnalyzer: every consumer, test projects included, must go through the guard,
/// since the defect was found live in test code squarely inside the wire-format-blessed zone.
///
/// Narrowed to <c>Add</c> only, 2026-08-06 during Task 9's closing sweep (see the spec's "Rule, narrowed
/// 2026-08-06" note, <c>../../../Glitnir/docs/Midgard/specs/2026-08-06-wire-model-registration-guard-design.md</c>):
/// the race requires an unguarded *write*, and a bare <c>IsDefined</c> read never mutates the model, so it
/// can never itself cause it — banning <c>Add</c> alone already makes the check-then-act pattern
/// impossible. Banning <c>IsDefined</c> too convicted a legitimate read-only assertion in Yggdrasil's
/// <c>CompositionTests.cs</c> with no correctness benefit.
///
/// The exemption is an ancestor-invocation walk, not a containing-type check (corrected 2026-08-06 during
/// Task 6 review — see the spec's "Exemption, corrected" note,
/// <c>../../../Glitnir/docs/Midgard/specs/2026-08-06-wire-model-registration-guard-design.md</c>): every
/// legitimate call site invokes <c>Add</c> from inside the <c>register</c> callback it passes to
/// <c>EnsureRegistered</c>, not from inside the guard's own type, so a containing-type check convicts every
/// sanctioned call site. <c>WireModelRegistrationGuard.EnsureRegistered</c> itself never calls <c>Add</c>
/// directly — only the caller-supplied delegate does — so nothing inside the guard's own body needs a
/// separate carve-out; a raw call added there outside the callback mechanism correctly still strikes.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WireModelGuardAnalyzer : DiagnosticAnalyzer
{
	const string RuntimeTypeModelMetadataName = "ProtoBuf.Meta.RuntimeTypeModel";
	const string GuardTypeMetadataName = "Norse.Infrastructure.Web.Grpc.WireModelRegistrationGuard";
	const string EnsureRegisteredMethodName = "EnsureRegistered";

	static readonly ImmutableArray<DiagnosticDescriptor> _supportedDiagnostics =
		[Diagnostics.WireModelMutatedOutsideGuard];

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		_supportedDiagnostics;

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
		context.EnableConcurrentExecution();
		context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
	}

	static void AnalyzeInvocation(OperationAnalysisContext context)
	{
		var invocation = (IInvocationOperation)context.Operation;
		var method = invocation.TargetMethod;
		if (method.Name != "Add")
			return;
		// The receiver's static type at the call site, not method.ContainingType: both resolve identically
		// for Add (all of RuntimeTypeModel's Add overloads are declared directly on RuntimeTypeModel itself,
		// confirmed during Task 6's review), so this is a style choice, not a correctness requirement — kept
		// as the receiver-type check for symmetry with WireFormatAnalyzer's banned-symbol technique and
		// because it reads directly as "is this call site typed against RuntimeTypeModel."
		var receiverType = invocation.Instance?.Type;
		if (receiverType?.ToDisplayString() != RuntimeTypeModelMetadataName)
			return;
		if (IsLexicallyInsideEnsureRegisteredCall(invocation.Parent))
			return;
		context.ReportDiagnostic(Diagnostic.Create(
			Diagnostics.WireModelMutatedOutsideGuard, invocation.Syntax.GetLocation(),
			$"{receiverType.ToDisplayString()}.{method.Name}"));
	}

	/// <summary>
	/// Walks the operation's ancestor chain (through the lambda it's nested inside, and the argument
	/// operation carrying that lambda) looking for an enclosing invocation of
	/// <c>WireModelRegistrationGuard.EnsureRegistered</c> — "is this call lexically part of an
	/// <c>EnsureRegistered(...)</c> invocation," not "is this call inside a specific type."
	/// </summary>
	static bool IsLexicallyInsideEnsureRegisteredCall(IOperation? ancestor)
	{
		for (var current = ancestor; current is not null; current = current.Parent)
			if (current is IInvocationOperation candidate &&
				candidate.TargetMethod.Name == EnsureRegisteredMethodName &&
				IsWireModelRegistrationGuardMember(candidate.TargetMethod))
				return true;
		return false;
	}

	/// <summary>
	/// True when <paramref name="method"/> is (or is an extension-block member of)
	/// <see cref="GuardTypeMetadataName"/>. A C# 14 <c>extension(RuntimeTypeModel model) { ... }</c> block
	/// compiles its members onto a compiler-synthesized wrapper type (<see cref="TypeKind.Extension"/>)
	/// nested inside the declaring type — verified empirically against this project's actual
	/// WireModelRegistrationGuard.cs: the instance-call form (<c>model.EnsureRegistered(...)</c>) resolves
	/// <c>TargetMethod.ContainingType</c> to that wrapper (whose own <c>ContainingType</c> is
	/// WireModelRegistrationGuard), while the fully-qualified static-invocation form
	/// (<c>WireModelRegistrationGuard.EnsureRegistered(model, ...)</c>) resolves ContainingType straight to
	/// WireModelRegistrationGuard with no wrapper in between — both call shapes appear in the platform's
	/// retrofitted call sites and both must exempt.
	/// </summary>
	static bool IsWireModelRegistrationGuardMember(IMethodSymbol method)
	{
		var containingType = method.ContainingType;
		if (containingType?.TypeKind == TypeKind.Extension)
			containingType = containingType.ContainingType;
		return containingType?.ToDisplayString() == GuardTypeMetadataName;
	}
}

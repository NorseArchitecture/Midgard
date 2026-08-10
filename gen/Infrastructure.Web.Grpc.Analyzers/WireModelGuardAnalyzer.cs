using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Norse.Infrastructure.Web.Grpc.Analyzers;

/// <summary>
///     NORSE080: bans any direct invocation of <c>RuntimeTypeModel.Add</c>/<c>.Add&lt;T&gt;</c> unless it's
///     lexically part of an invocation of <c>WireModelRegistrationGuard.EnsureRegistered</c> — the check-then-act
///     and flag-first shapes an unguarded <c>Add</c> invites are exactly the defect class filed 2026-08-03
///     (<c>../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md</c>) and found live five
///     times, including inside a hand-rolled test fixture, before this rule existed. Not realm-scoped, unlike
///     Svartálfheim's WireFormatAnalyzer: every consumer, test projects included, must go through the guard,
///     since the defect was found live in test code squarely inside the wire-format-blessed zone.
///     Narrowed to <c>Add</c> only, 2026-08-06 during Task 9's closing sweep (see the spec's "Rule, narrowed
///     2026-08-06" note, <c>../../../Glitnir/docs/Midgard/specs/2026-08-06-wire-model-registration-guard-design.md</c>):
///     the race requires an unguarded *write*, and a bare <c>IsDefined</c> read never mutates the model, so it
///     can never itself cause it — banning <c>Add</c> alone already makes the check-then-act pattern
///     impossible. Banning <c>IsDefined</c> too convicted a legitimate read-only assertion in Yggdrasil's
///     <c>CompositionTests.cs</c> with no correctness benefit.
///     The exemption is an ancestor-invocation walk, not a containing-type check (corrected 2026-08-06 during
///     Task 6 review — see the spec's "Exemption, corrected" note,
///     <c>../../../Glitnir/docs/Midgard/specs/2026-08-06-wire-model-registration-guard-design.md</c>): every
///     legitimate call site invokes <c>Add</c> from inside the <c>register</c> callback it passes to
///     <c>EnsureRegistered</c>, not from inside the guard's own type, so a containing-type check convicts every
///     sanctioned call site. <c>WireModelRegistrationGuard.EnsureRegistered</c> itself never calls <c>Add</c>
///     directly — only the caller-supplied delegate does — so nothing inside the guard's own body needs a
///     separate carve-out; a raw call added there outside the callback mechanism correctly still strikes.
///     Tightened twice on 2026-08-06 folding in PR #61 review: (1) the walk exempts only operations beneath
///     the delegate-typed <c>register</c> argument — an <c>Add</c> evaluated while building the <c>key</c>
///     argument (or the receiver expression) runs before the guard takes hold and stays convicted; (2) the
///     guard's model must match the <c>Add</c> receiver, since <c>EnsureRegistered</c> synchronizes only the
///     model it's invoked on — matching is by referenced symbol with a one-level local-initializer chase
///     (both emitters generate <c>var model = RuntimeTypeModel.Default;</c> inside a guard invoked on
///     <c>RuntimeTypeModel.Default</c>), strict on a provable mismatch and deliberately lenient when either
///     side is not a simple reference: this is a lexical tripwire, not a dataflow proof, and a receiver it
///     cannot resolve (a method-call result, a reassigned local) exempts rather than convicting shapes it
///     cannot reason about.
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
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze |
			GeneratedCodeAnalysisFlags.ReportDiagnostics);
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
		if (IsInsideMatchingEnsureRegisteredCallback(invocation))
			return;
		context.ReportDiagnostic(Diagnostic.Create(
			Diagnostics.WireModelMutatedOutsideGuard, invocation.Syntax.GetLocation(),
			$"{receiverType.ToDisplayString()}.{method.Name}"));
	}

	/// <summary>
	///     Walks the operation's ancestor chain (through the lambda it's nested inside, and the argument
	///     operation carrying that lambda) looking for an enclosing invocation of
	///     <c>WireModelRegistrationGuard.EnsureRegistered</c> that actually protects this <c>Add</c>: the
	///     walk must ascend into the invocation through its delegate-typed <c>register</c> argument (an
	///     <c>Add</c> in the <c>key</c> argument or receiver expression evaluates before the guard runs), and
	///     the guard's model must match the <c>Add</c> receiver per <see cref="GuardModelMatchesReceiver" />.
	///     A non-matching enclosing guard doesn't end the walk — a nested guard on one model inside an outer
	///     guard's callback on another still exempts an <c>Add</c> the outer guard protects.
	/// </summary>
	static bool IsInsideMatchingEnsureRegisteredCallback(IInvocationOperation addInvocation)
	{
		var previous = (IOperation)addInvocation;
		for (var current = addInvocation.Parent; current is not null; previous = current, current = current.Parent)
			if (current is IInvocationOperation candidate &&
				candidate.TargetMethod.Name == EnsureRegisteredMethodName &&
				IsWireModelRegistrationGuardMember(candidate.TargetMethod) &&
				previous is IArgumentOperation { Parameter.Type.TypeKind: TypeKind.Delegate } &&
				GuardModelMatchesReceiver(candidate, addInvocation))
				return true;
		return false;
	}

	/// <summary>
	///     True unless the guard's model and the <c>Add</c> receiver provably reference different things.
	///     Each side resolves to the symbol it references (local, parameter, field, or property — through
	///     implicit conversions), with a one-level chase from a local to the symbol its initializer
	///     references, because both generator emitters produce <c>var model = RuntimeTypeModel.Default;</c>
	///     inside a guard invoked on <c>RuntimeTypeModel.Default</c>. Either side unresolvable (not a simple
	///     reference, or a local with a complex initializer) exempts — lexical tripwire, not dataflow proof.
	/// </summary>
	static bool GuardModelMatchesReceiver(IInvocationOperation guardInvocation, IInvocationOperation addInvocation)
	{
		var guardModel = ResolveReferencedSymbol(GuardModelOperation(guardInvocation));
		var addReceiver = ResolveReferencedSymbol(addInvocation.Instance);
		if (guardModel is null || addReceiver is null)
			return true;
		return SymbolEqualityComparer.Default.Equals(guardModel, addReceiver);
	}

	/// <summary>
	///     The operation carrying the guard's <see cref="RuntimeTypeModelMetadataName" /> — the instance
	///     receiver when Roslyn models the extension-block invocation with one, otherwise the argument whose
	///     parameter is typed <c>RuntimeTypeModel</c> (both the fully-qualified static form and the lowered
	///     extension form carry the model as the leading parameter, named <c>model</c> in both shapes).
	/// </summary>
	static IOperation? GuardModelOperation(IInvocationOperation guardInvocation)
	{
		if (guardInvocation.Instance is not null)
			return guardInvocation.Instance;
		foreach (var argument in guardInvocation.Arguments)
			if (argument.Parameter?.Type.ToDisplayString() == RuntimeTypeModelMetadataName)
				return argument.Value;
		return null;
	}

	/// <summary>
	///     The symbol a simple reference operation refers to, unwrapping implicit conversions, substituting a
	///     local for the symbol its declaration initializer references (one level, no transitive chase), and
	///     returning <see langword="null" /> for anything more complex — which
	///     <see cref="GuardModelMatchesReceiver" /> treats as unprovable, never as a mismatch.
	/// </summary>
	static ISymbol? ResolveReferencedSymbol(IOperation? operation)
	{
		while (operation is IConversionOperation conversion)
			operation = conversion.Operand;
		return operation switch
		{
			ILocalReferenceOperation local => ResolveLocalThroughInitializer(local),
			IParameterReferenceOperation parameter => parameter.Parameter,
			IFieldReferenceOperation field => field.Field,
			IPropertyReferenceOperation property => property.Property,
			_ => null
		};
	}

	/// <summary>
	///     A local whose declaration initializer is itself a simple reference resolves to that referenced
	///     symbol (the emitters' <c>var model = RuntimeTypeModel.Default;</c> shape); any other local — no
	///     findable declarator, or a complex initializer such as <c>RuntimeTypeModel.Create()</c> — resolves
	///     to the local itself, so two independently-constructed locals still provably mismatch. A local
	///     reassigned after declaration can fool the chase; accepted porosity for a lexical tripwire.
	/// </summary>
	static ISymbol ResolveLocalThroughInitializer(ILocalReferenceOperation local)
	{
		var root = (IOperation)local;
		while (root.Parent is not null)
			root = root.Parent;
		foreach (var descendant in root.DescendantsAndSelf())
			if (descendant is IVariableDeclaratorOperation declarator &&
				SymbolEqualityComparer.Default.Equals(declarator.Symbol, local.Local))
			{
				var initializer = declarator.GetVariableInitializer()?.Value;
				while (initializer is IConversionOperation conversion)
					initializer = conversion.Operand;
				return initializer switch
				{
					IParameterReferenceOperation parameter => parameter.Parameter,
					IFieldReferenceOperation field => field.Field,
					IPropertyReferenceOperation property => property.Property,
					_ => local.Local
				};
			}

		return local.Local;
	}

	/// <summary>
	///     True when <paramref name="method" /> is (or is an extension-block member of)
	///     <see cref="GuardTypeMetadataName" />. A C# 14 <c>extension(RuntimeTypeModel model) { ... }</c> block
	///     compiles its members onto a compiler-synthesized wrapper type (<see cref="TypeKind.Extension" />)
	///     nested inside the declaring type — verified empirically against this project's actual
	///     WireModelRegistrationGuard.cs: the instance-call form (<c>model.EnsureRegistered(...)</c>) resolves
	///     <c>TargetMethod.ContainingType</c> to that wrapper (whose own <c>ContainingType</c> is
	///     WireModelRegistrationGuard), while the fully-qualified static-invocation form
	///     (<c>WireModelRegistrationGuard.EnsureRegistered(model, ...)</c>) resolves ContainingType straight to
	///     WireModelRegistrationGuard with no wrapper in between — both call shapes appear in the platform's
	///     retrofitted call sites and both must exempt.
	/// </summary>
	static bool IsWireModelRegistrationGuardMember(IMethodSymbol method)
	{
		var containingType = method.ContainingType;
		if (containingType?.TypeKind == TypeKind.Extension)
			containingType = containingType.ContainingType;
		return containingType?.ToDisplayString() == GuardTypeMetadataName;
	}
}

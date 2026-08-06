using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Norse.Infrastructure.Web.Grpc.Analyzers;

/// <summary>
/// NORSE080: bans any direct invocation of <c>RuntimeTypeModel.Add</c>/<c>.Add&lt;T&gt;</c>/<c>.IsDefined</c>
/// outside <c>Norse.Infrastructure.Web.Grpc.WireModelRegistrationGuard</c> itself — the check-then-act
/// and flag-first shapes those methods invite are exactly the defect class filed
/// 2026-08-03 (<c>../../../Glitnir/docs/Midgard/2026-08-03-surrogate-guard-race-filing.md</c>) and found
/// live five times, including inside a hand-rolled test fixture, before this rule existed. Not
/// realm-scoped, unlike Svartálfheim's WireFormatAnalyzer: every consumer, test projects included, must
/// go through the guard, since the defect was found live in test code squarely inside the
/// wire-format-blessed zone.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WireModelGuardAnalyzer : DiagnosticAnalyzer
{
	const string RuntimeTypeModelMetadataName = "ProtoBuf.Meta.RuntimeTypeModel";
	const string GuardTypeMetadataName = "Norse.Infrastructure.Web.Grpc.WireModelRegistrationGuard";
	static readonly ImmutableHashSet<string> _bannedMembers = ["Add", "IsDefined"];

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
		if (!_bannedMembers.Contains(method.Name))
			return;
		// The receiver's static type at the call site, not method.ContainingType: protobuf-net declares
		// IsDefined on the abstract TypeModel base class (Add lives directly on RuntimeTypeModel), so a
		// ContainingType check misses every "model.IsDefined(...)" call where model is typed
		// RuntimeTypeModel — the exact shape this rule exists to catch.
		var receiverType = invocation.Instance?.Type;
		if (receiverType?.ToDisplayString() != RuntimeTypeModelMetadataName)
			return;
		if (context.ContainingSymbol.ContainingType?.ToDisplayString() == GuardTypeMetadataName)
			return;
		context.ReportDiagnostic(Diagnostic.Create(
			Diagnostics.WireModelMutatedOutsideGuard, invocation.Syntax.GetLocation(),
			$"{receiverType.ToDisplayString()}.{method.Name}"));
	}
}

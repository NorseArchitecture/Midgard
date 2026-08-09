using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Server.Generator.Xml;

/// <summary>
///     Walks one confirmed <c>GrpcControllerBase</c> descendant's action methods (spec §4.1): body-bound
///     parameter types seed the request closure, <c>ActionResult&lt;T&gt;</c>/<c>Task&lt;ActionResult&lt;T&gt;&gt;</c>/
///     <c>ValueTask&lt;ActionResult&lt;T&gt;&gt;</c> payload types seed the response closure. Every complex
///     type reachable from either seed set gets a <see cref="ShapeModel" />; every shape-law violation
///     along the way (NORSE022-028) becomes a <see cref="DiagnosticInfo" />. Pure symbol-to-value-model
///     projection — nothing this type touches survives into the returned <see cref="ControllerShapeResult" />.
/// </summary>
static class ClosureWalker
{
	static readonly SymbolDisplayFormat _displayFormat = SymbolDisplayFormat.FullyQualifiedFormat;

	public static ControllerShapeResult Analyze(INamedTypeSymbol controller, Compilation compilation)
	{
		var ctx = new TaxonomyContext(
			compilation.GetTypeByMetadataName("Norse.Primitives.Result`1"),
			compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1"),
			compilation.GetTypeByMetadataName("System.Collections.Generic.IDictionary`2"),
			compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyDictionary`2"),
			compilation.GetTypeByMetadataName("System.FlagsAttribute"),
			compilation.GetTypeByMetadataName("System.Runtime.Serialization.DataMemberAttribute"));

		var dataContractAttribute =
			compilation.GetTypeByMetadataName("System.Runtime.Serialization.DataContractAttribute");
		var fromBodyAttribute = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromBodyAttribute");
		var actionResultType = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ActionResult`1");
		var taskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
		var valueTaskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
		INamedTypeSymbol?[] explicitBindingAttributes =
		[
			compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromRouteAttribute"),
			compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromQueryAttribute"),
			compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromHeaderAttribute"),
			compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromServicesAttribute")
		];

		List<DiagnosticInfo> diagnostics = [];
		List<(IParameterSymbol Parameter, INamedTypeSymbol Type)> requestRoots = [];
		List<INamedTypeSymbol> responseRoots = [];

		foreach (var method in controller.GetMembers().OfType<IMethodSymbol>())
		{
			if (method is not
				{ MethodKind: MethodKind.Ordinary, IsStatic: false, DeclaredAccessibility: Accessibility.Public })
				continue;

			foreach (var parameter in method.Parameters)
			{
				if (parameter.Type is not INamedTypeSymbol parameterType)
					continue;

				var hasExplicitOtherSource = explicitBindingAttributes.Any(a => HasAttribute(parameter, a));
				if (HasAttribute(parameter, fromBodyAttribute))
					requestRoots.Add((parameter, parameterType));
				else if (!hasExplicitOtherSource && method.Parameters.Length == 1 && !IsSupportedScalar(parameterType))
					requestRoots.Add((parameter, parameterType));
			}

			if (TryGetActionResultPayload(method.ReturnType, actionResultType, taskType, valueTaskType) is
				INamedTypeSymbol payload)
				responseRoots.Add(payload);
		}

		if (requestRoots.Count == 0 && responseRoots.Count == 0)
			return new ControllerShapeResult(EquatableArray<ShapeModel>.Empty, EquatableArray<DiagnosticInfo>.Empty);

		foreach (var (parameter, type) in requestRoots)
			if (dataContractAttribute is not null && !HasAttribute(type, dataContractAttribute))
				diagnostics.Add(DiagnosticInfo.Create(Diagnostics.BodyTypeNotDataContract, parameter, parameter.Name,
					type.ToDisplayString(_displayFormat)));

		var requestReachable = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
		var responseReachable = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
		foreach (var (_, type) in requestRoots)
			Walk(type, requestReachable, ctx);
		foreach (var type in responseRoots)
			Walk(type, responseReachable, ctx);

		var crossDirection = new HashSet<INamedTypeSymbol>(requestReachable, SymbolEqualityComparer.Default);
		crossDirection.IntersectWith(responseReachable);

		foreach (var shared in crossDirection.OrderBy(t => t.ToDisplayString(_displayFormat), StringComparer.Ordinal))
			diagnostics.Add(DiagnosticInfo.Create(Diagnostics.SharedAcrossDirections, shared,
				shared.ToDisplayString(_displayFormat)));

		var allReachable = new HashSet<INamedTypeSymbol>(requestReachable, SymbolEqualityComparer.Default);
		allReachable.UnionWith(responseReachable);

		var shapes = BuildShapes(allReachable, requestReachable, crossDirection, ctx, diagnostics);

		return new ControllerShapeResult(EquatableArray<ShapeModel>.Create(shapes),
			EquatableArray<DiagnosticInfo>.Create(diagnostics));
	}

	static List<ShapeModel> BuildShapes(
		HashSet<INamedTypeSymbol> allReachable,
		HashSet<INamedTypeSymbol> requestReachable,
		HashSet<INamedTypeSymbol> crossDirection,
		TaxonomyContext ctx,
		List<DiagnosticInfo> diagnostics)
	{
		List<ShapeModel> shapes = [];

		foreach (var type in allReachable.OrderBy(t => t.ToDisplayString(_displayFormat), StringComparer.Ordinal))
		{
			if (!type.IsSealed || type.IsGenericType ||
				(type.BaseType is not null && type.BaseType.SpecialType != SpecialType.System_Object))
				diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidContractShape, type,
					type.ToDisplayString(_displayFormat)));

			var isCross = crossDirection.Contains(type);
			var isRequestSide = !isCross && requestReachable.Contains(type);

			List<(IPropertySymbol Property, MemberModel Model)> built = [];
			foreach (var property in GetInstanceProperties(type, ctx))
				built.Add((property, ClassifyMember(property, type, isCross, isRequestSide, ctx, diagnostics)));

			ReportUniquenessViolations(type, built, diagnostics);

			shapes.Add(new ShapeModel(
				type.ToDisplayString(_displayFormat),
				NameCasing.ApplyAll(type.Name),
				EquatableArray<MemberModel>.Create(built.Select(b => b.Model))));
		}

		return shapes;
	}

	static MemberModel ClassifyMember(IPropertySymbol property, INamedTypeSymbol owner, bool isCross,
		bool isRequestSide, TaxonomyContext ctx, List<DiagnosticInfo> diagnostics)
	{
		var classification = Classify(property.Type, ctx);

		if (classification.Problem != TaxonomyProblem.None)
		{
			diagnostics.Add(DiagnosticInfo.Create(Diagnostics.TaxonomyViolation, property,
				TaxonomyMessage(classification.Problem, property, owner)));
			return new MemberModel(property.Name, classification.Kind, NameCasing.ApplyAll(property.Name),
				classification.IsResultWrapped, classification.IsNullable, null, null, false, null,
				EquatableArray<EnumValueModel>.Empty);
		}

		if (classification.Kind == MemberKind.Scalar && !isCross)
		{
			if (isRequestSide && !classification.IsResultWrapped)
				diagnostics.Add(DiagnosticInfo.Create(Diagnostics.RawScalarInRequestClosure, property, property.Name,
					owner.ToDisplayString(_displayFormat)));
			else if (!isRequestSide && classification.IsResultWrapped)
				diagnostics.Add(DiagnosticInfo.Create(Diagnostics.ResultInResponseClosure, property, property.Name,
					owner.ToDisplayString(_displayFormat)));
		}

		// Flags are legal in either closure, carried bare on the contract (design spec
		// 2026-08-02-futhark-enum-wire-law-design.md, Amendment 2026-08-09) — recorded as a member
		// trait the emitters translate into the repeated governed-name element shape, never a strike.
		// The enum table builds identically for flags and plain enums: one table, one algorithm (§2.3).
		var isEnum = classification.ScalarType is { TypeKind: TypeKind.Enum };
		var isFlags = isEnum && ctx.FlagsAttribute is not null &&
			((INamedTypeSymbol)classification.ScalarType!).GetAttributes().Any(a =>
				SymbolEqualityComparer.Default.Equals(a.AttributeClass, ctx.FlagsAttribute));
		var enumValues = isEnum ?
			BuildEnumTable((INamedTypeSymbol)classification.ScalarType!) :
			EquatableArray<EnumValueModel>.Empty;
		// FullyQualifiedFormat renders special types as their bare keywords ("int", "uint", ...) — the
		// exact strings WriterEmitter's zero-extension dispatch matches on.
		var enumUnderlyingTypeName = isEnum ?
			((INamedTypeSymbol)classification.ScalarType!).EnumUnderlyingType!.ToDisplayString(_displayFormat) :
			null;

		return new MemberModel(
			property.Name,
			classification.Kind,
			NameCasing.ApplyAll(property.Name),
			classification.IsResultWrapped,
			classification.IsNullable,
			classification.ScalarType?.ToDisplayString(_displayFormat),
			classification.ComplexType?.ToDisplayString(_displayFormat),
			isFlags,
			enumUnderlyingTypeName,
			enumValues);
	}

	static void ReportUniquenessViolations(INamedTypeSymbol owner,
		List<(IPropertySymbol Property, MemberModel Model)> built, List<DiagnosticInfo> diagnostics)
	{
		foreach (var group in built.Where(b => b.Model.Kind != MemberKind.Scalar && b.Model.ComplexTypeName is not null)
			.GroupBy(b => b.Model.ComplexTypeName, StringComparer.Ordinal))
			if (group.Count() > 1)
				foreach (var (duplicateProperty, duplicateModel) in group.Skip(1))
					diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MemberUniquenessViolation, duplicateProperty,
						$"'{owner.ToDisplayString(_displayFormat)}' carries more than one member of complex type '{duplicateModel.ComplexTypeName}' ('{duplicateProperty.Name}' collides with an earlier member) — one member per complex type per contract, any arity"));

		// One diagnostic per offending member, not one per colliding style: two names built from the
		// same word list (e.g. "UserId"/"UserID") collide in every one of the five styles at once —
		// reporting per-style would fire the same law five times over for a single naming mistake.
		for (var i = 1; i < built.Count; i++)
		{
			var (currentProperty, currentModel) = built[i];
			for (var earlier = 0; earlier < i; earlier++)
			{
				var (earlierProperty, earlierModel) = built[earlier];
				var collidingStyle = FirstCollidingStyle(currentModel.WireNames, earlierModel.WireNames);
				if (collidingStyle is null)
					continue;

				diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MemberUniquenessViolation, currentProperty,
					$"'{owner.ToDisplayString(_displayFormat)}' has a wire-name collision between '{earlierProperty.Name}' and '{currentProperty.Name}' once case-transformed to {collidingStyle} ('{currentModel.WireNames[(int)collidingStyle.Value]}') — two members produce the same wire name"));
				break;
			}
		}
	}

	static XmlCaseStyle? FirstCollidingStyle(EquatableArray<string> left, EquatableArray<string> right)
	{
		for (var style = 0; style < 5; style++)
			if (StringComparer.Ordinal.Equals(left[style], right[style]))
				return (XmlCaseStyle)style;

		return null;
	}

	static void Walk(INamedTypeSymbol root, HashSet<INamedTypeSymbol> reachable, TaxonomyContext ctx)
	{
		Queue<INamedTypeSymbol> queue = [];
		if (reachable.Add(root))
			queue.Enqueue(root);

		while (queue.Count > 0)
		{
			var current = queue.Dequeue();
			foreach (var property in GetInstanceProperties(current, ctx))
			{
				var next = Classify(property.Type, ctx).ComplexType;
				if (next is not null && reachable.Add(next))
					queue.Enqueue(next);
			}
		}
	}

	// [DataMember] is an opt-in membership law (design spec §4b, plan Task 7): a property that never
	// carries the attribute does not exist to Futhark at all — no closure entry, no shape, no
	// diagnostic — mirroring the same law Midgard's JSON leg enforces via OptInContractModifier.
	static IEnumerable<IPropertySymbol> GetInstanceProperties(INamedTypeSymbol type, TaxonomyContext ctx) =>
		type.GetMembers().OfType<IPropertySymbol>().Where(p =>
			p is { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public } &&
			HasAttribute(p, ctx.DataMemberAttribute));

	static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attribute) =>
		attribute is not null && symbol.GetAttributes()
			.Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));

	static INamedTypeSymbol? TryGetActionResultPayload(ITypeSymbol returnType, INamedTypeSymbol? actionResultType,
		INamedTypeSymbol? taskType, INamedTypeSymbol? valueTaskType)
	{
		if (returnType is not INamedTypeSymbol { IsGenericType: true } named)
			return null;

		if (actionResultType is not null &&
			SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, actionResultType))
			return named.TypeArguments[0] as INamedTypeSymbol;

		var isAsyncWrapper =
			(taskType is not null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, taskType)) ||
			(valueTaskType is not null &&
				SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, valueTaskType));
		if (!isAsyncWrapper)
			return null;

		if (named.TypeArguments[0] is not INamedTypeSymbol { IsGenericType: true } inner || actionResultType is null ||
			!SymbolEqualityComparer.Default.Equals(inner.OriginalDefinition, actionResultType))
			return null;

		return inner.TypeArguments[0] as INamedTypeSymbol;
	}

	static MemberClassification Classify(ITypeSymbol propertyType, TaxonomyContext ctx)
	{
		var (underlying, isNullable, isResultWrapped) = Unwrap(propertyType, ctx.ResultType);

		if (underlying.SpecialType == SpecialType.System_String)
			return new MemberClassification(MemberKind.Scalar, isResultWrapped, isNullable, underlying, null,
				TaxonomyProblem.None);

		if (IsDictionary(underlying, ctx))
			return new MemberClassification(MemberKind.Collection, isResultWrapped, isNullable, null, null,
				TaxonomyProblem.Dictionary);

		if (TryGetEnumerableItemType(underlying, ctx.EnumerableOpen, out var itemType))
		{
			if (itemType.SpecialType != SpecialType.System_String && (IsDictionary(itemType, ctx) ||
				TryGetEnumerableItemType(itemType, ctx.EnumerableOpen, out _)))
				return new MemberClassification(MemberKind.Collection, isResultWrapped, isNullable, null, null,
					TaxonomyProblem.NestedCollection);

			if (IsSupportedScalar(itemType))
				return new MemberClassification(MemberKind.Collection, isResultWrapped, isNullable, null, null,
					TaxonomyProblem.ScalarCollection);

			if (itemType is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } complexItem)
				return new MemberClassification(MemberKind.Collection, isResultWrapped, isNullable, null, complexItem,
					TaxonomyProblem.None);

			return new MemberClassification(MemberKind.Collection, isResultWrapped, isNullable, null, null,
				TaxonomyProblem.NestedCollection);
		}

		if (IsSupportedScalar(underlying))
			return new MemberClassification(MemberKind.Scalar, isResultWrapped, isNullable, underlying, null,
				TaxonomyProblem.None);

		if (underlying is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } complex)
			return new MemberClassification(MemberKind.Complex, isResultWrapped, isNullable, null, complex,
				TaxonomyProblem.None);

		return new MemberClassification(MemberKind.Scalar, isResultWrapped, isNullable, underlying, null,
			TaxonomyProblem.UnsupportedScalar);
	}

	static (ITypeSymbol Underlying, bool IsNullable, bool IsResultWrapped) Unwrap(ITypeSymbol type,
		INamedTypeSymbol? resultType)
	{
		if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
		{
			var inner = nullable.TypeArguments[0];
			if (resultType is not null && inner is INamedTypeSymbol { IsGenericType: true } innerNamed &&
				SymbolEqualityComparer.Default.Equals(innerNamed.OriginalDefinition, resultType))
				return (innerNamed.TypeArguments[0], true, true);

			return (inner, true, false);
		}

		if (resultType is not null && type is INamedTypeSymbol { IsGenericType: true } named &&
			SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, resultType))
			return (named.TypeArguments[0], false, true);

		return (type, type is { IsReferenceType: true, NullableAnnotation: NullableAnnotation.Annotated }, false);
	}

	static bool IsDictionary(ITypeSymbol type, TaxonomyContext ctx)
	{
		bool Matches(INamedTypeSymbol candidate) =>
			(ctx.DictionaryOpen is not null &&
				SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, ctx.DictionaryOpen)) ||
			(ctx.ReadOnlyDictionaryOpen is not null &&
				SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, ctx.ReadOnlyDictionaryOpen));

		if (type is INamedTypeSymbol { IsGenericType: true } self && Matches(self))
			return true;

		return type.AllInterfaces.Any(i => i.IsGenericType && Matches(i));
	}

	static bool TryGetEnumerableItemType(ITypeSymbol type, INamedTypeSymbol? enumerableOpen, out ITypeSymbol itemType)
	{
		if (enumerableOpen is not null)
		{
			if (type is INamedTypeSymbol { IsGenericType: true } self &&
				SymbolEqualityComparer.Default.Equals(self.OriginalDefinition, enumerableOpen))
			{
				itemType = self.TypeArguments[0];
				return true;
			}

			foreach (var i in type.AllInterfaces)
			{
				if (i.IsGenericType && SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, enumerableOpen))
				{
					itemType = i.TypeArguments[0];
					return true;
				}
			}
		}

		itemType = null!;
		return false;
	}

	static bool IsSupportedScalar(ITypeSymbol type)
	{
		if (type.TypeKind == TypeKind.Enum)
			return true;

		return type.SpecialType is
				SpecialType.System_Boolean or
				SpecialType.System_SByte or SpecialType.System_Byte or
				SpecialType.System_Int16 or SpecialType.System_UInt16 or
				SpecialType.System_Int32 or SpecialType.System_UInt32 or
				SpecialType.System_Int64 or SpecialType.System_UInt64 or
				SpecialType.System_Decimal or
				SpecialType.System_Single or SpecialType.System_Double or
				SpecialType.System_Char or
				SpecialType.System_String
			|| IsKnownScalarStruct(type);
	}

	static bool IsKnownScalarStruct(ITypeSymbol type) =>
		type is INamedTypeSymbol { ContainingNamespace.Name: "System" } named &&
		named.Name is "Guid" or "DateTime" or "DateTimeOffset" or "DateOnly" or "TimeOnly" or "TimeSpan";

	static EquatableArray<EnumValueModel> BuildEnumTable(INamedTypeSymbol enumType) =>
		EquatableArray<EnumValueModel>.Create(
			enumType.GetMembers().OfType<IFieldSymbol>()
				.Where(f => f is { IsConst: true, HasConstantValue: true })
				.Select(f => new EnumValueModel(f.Name, NameCasing.ApplyAll(f.Name), ToBits(f.ConstantValue!))));

	/// <summary>
	///     Zero-extends a boxed enum-member constant into the shared 64-bit table representation — the
	///     build-time twin of the runtime law (<c>EnumLexical.ToBits</c>): 1/2/4-byte underlying types
	///     zero-extend through the unsigned same-width type, 8-byte types carry their bits identically
	///     (bit 63 genuinely is the sign bit there). A bare <c>Convert.ToInt64</c> would sign-extend
	///     instead, landing an int-backed <c>1 &lt;&lt; 31</c> member at -2147483648L — which fails every
	///     downstream single-bit test (generation-time mask and emitted table alike) and misclassifies
	///     the member composite.
	/// </summary>
	static long ToBits(object constantValue) => constantValue switch
	{
		sbyte value => unchecked((byte)value),
		byte value => value,
		short value => unchecked((ushort)value),
		ushort value => value,
		int value => unchecked((uint)value),
		uint value => value,
		long value => value,
		ulong value => unchecked((long)value),
		_ => Convert.ToInt64(constantValue, CultureInfo.InvariantCulture)
	};

	static string TaxonomyMessage(TaxonomyProblem problem, IPropertySymbol property, INamedTypeSymbol owner) =>
		problem switch
		{
			TaxonomyProblem.UnsupportedScalar =>
				$"Member '{property.Name}' on '{owner.ToDisplayString(_displayFormat)}' has type '{property.Type.ToDisplayString(_displayFormat)}', which is outside Futhark's closed scalar taxonomy",
			TaxonomyProblem.Dictionary =>
				$"Member '{property.Name}' on '{owner.ToDisplayString(_displayFormat)}' is a dictionary — dictionaries have no Futhark shape",
			TaxonomyProblem.ScalarCollection =>
				$"Member '{property.Name}' on '{owner.ToDisplayString(_displayFormat)}' is a collection of scalars — collection items must be complex types",
			TaxonomyProblem.NestedCollection =>
				$"Member '{property.Name}' on '{owner.ToDisplayString(_displayFormat)}' is a collection of collections (or a collection of dictionaries) — nested collections have no Futhark shape",
			_ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "Unrecognized TaxonomyProblem.")
		};

	readonly record struct TaxonomyContext(
		INamedTypeSymbol? ResultType,
		INamedTypeSymbol? EnumerableOpen,
		INamedTypeSymbol? DictionaryOpen,
		INamedTypeSymbol? ReadOnlyDictionaryOpen,
		INamedTypeSymbol? FlagsAttribute,
		INamedTypeSymbol? DataMemberAttribute);

	readonly record struct MemberClassification(
		MemberKind Kind,
		bool IsResultWrapped,
		bool IsNullable,
		ITypeSymbol? ScalarType,
		INamedTypeSymbol? ComplexType,
		TaxonomyProblem Problem);

	enum TaxonomyProblem
	{
		None,
		UnsupportedScalar,
		Dictionary,
		ScalarCollection,
		NestedCollection
	}
}

namespace Norse.Infrastructure.Web.Server.Xml.Generator;

/// <summary>
///     Emits the presence-aware, accumulating XML reader (design spec §8) for one contract shape — the
///     <c>Read</c> half of the class <see cref="WriterEmitter" />'s <see cref="WriterEmitter.Emit" /> method
///     composes. Shares <see cref="WriterEmitter" />'s per-member wire-name arrays and enum tables (never a
///     second table): the reader's name→value lookups scan the exact same <c>_{SafeName}Names</c> arrays
///     the writer declares its value→name lookups against.
/// </summary>
/// <remarks>
///     <b>Recursive, caller-pushes-the-path design.</b> <c>Read</c> never pushes its own path segment onto
///     <c>XmlReadContext</c> — the caller does, exactly once, before invoking it: the root caller
///     (a later task's formatter) pushes the root element; a parent shape reading a singleton nested
///     member pushes an element segment; a parent shape reading a collection item pushes an indexed item
///     segment. This is why the same recursive <c>Read</c> body works unmodified at every nesting depth —
///     the root-element-name-mismatch check at its top is provably a no-op for every nested invocation
///     (the parent already matched the child's local name against this exact type's own root name before
///     ever recursing), so it only ever fires for a genuine top-level mismatch, with no second code path
///     needed to distinguish "root" from "fragment" — the reader's mirror of the writer's own "one
///     recursive projection reused as both 'the root' and 'a fragment'" idiom (<see cref="WriterEmitter" />'s
///     class remarks).
/// </remarks>
static class ReaderEmitter
{
	const string XmlNs = "global::Norse.Infrastructure.Web.Server.Xml";
	const string PrimitivesNs = "global::Norse.Primitives";
	const string Invariant = "global::System.Globalization.CultureInfo.InvariantCulture";
	const string Ordinal = "global::System.StringComparison.Ordinal";

	/// <summary>
	///     Reader-only static fields: per-complex/collection-member expected element-name tables, and the two known-name
	///     tables <c>NameSuggestion</c> scans for unknown attribute/element suggestions.
	/// </summary>
	public static string FieldDeclarations(ShapeModel shape)
	{
		List<string> lines = [];
		foreach (var member in shape.Members.Where(m => m.Kind != MemberKind.Scalar))
			lines.Add(
				$"\tstatic readonly string[] _{member.ClrName}ElementNames = {WriterEmitter.NamesLiteral(NameCasing.ApplyAll(WriterEmitter.ShortName(member.ComplexTypeName!)))};");

		var attributeNameSets = shape.Members.Where(m => m.Kind == MemberKind.Scalar).Select(m => m.WireNames);
		var elementNameSets = shape.Members.Where(m => m.Kind != MemberKind.Scalar)
			.Select(m => NameCasing.ApplyAll(WriterEmitter.ShortName(m.ComplexTypeName!)));
		lines.Add($"\tstatic readonly string[][] _knownAttributeNames = {KnownNamesLiteral(attributeNameSets)};");
		lines.Add($"\tstatic readonly string[][] _knownElementNames = {KnownNamesLiteral(elementNameSets)};");

		return string.Join("\n", lines);
	}

	/// <summary>
	///     Transposes a per-member list of five-casing name arrays into five style-indexed rows —
	///     <c>_knownAttributeNames[(int)style]</c> is every scalar member's wire name in that one style.
	/// </summary>
	static string KnownNamesLiteral(IEnumerable<EquatableArray<string>> perMemberNames)
	{
		var materialized = perMemberNames.ToList();
		List<string> rows = [];
		for (var style = 0; style < 5; style++)
		{
			List<string> quoted = [];
			foreach (var names in materialized)
				quoted.Add(WriterEmitter.Quote(names[style]));
			rows.Add($"[{string.Join(", ", quoted)}]");
		}

		return $"[{string.Join(", ", rows)}]";
	}

	/// <summary>The full <c>Read</c> method — signature, braces, and body — spliced verbatim into the enclosing shape class.</summary>
	public static string ReadMethod(string rootNamespace, ShapeModel shape)
	{
		var scalarMembers = shape.Members.Where(m => m.Kind == MemberKind.Scalar).ToList();
		var complexMembers = shape.Members.Where(m => m.Kind == MemberKind.Complex).ToList();
		var collectionMembers = shape.Members.Where(m => m.Kind == MemberKind.Collection).ToList();

		List<string> body = [];
		body.Add(
			"\t\t// Locals — one content/value slot per member, resolved after the attribute and child walks below.");
		foreach (var member in scalarMembers)
			body.Add($"\t\tstring? {ContentVar(member)} = null;");
		foreach (var member in complexMembers)
		{
			body.Add($"\t\t{member.ComplexTypeName}? {ValueVar(member)} = null;");
			body.Add($"\t\tvar {SeenVar(member)} = false;");
		}

		foreach (var member in collectionMembers)
		{
			body.Add($"\t\tglobal::System.Collections.Generic.List<{member.ComplexTypeName}> {ItemsVar(member)} = [];");
			body.Add($"\t\tvar {CountVar(member)} = 0;");
		}

		body.Add(string.Empty);
		body.Add($"\t\tif (!string.Equals(reader.LocalName, _rootNames[(int)style], {Ordinal}))");
		body.Add(
			"\t\t\tcontext.AddFailure(context.CurrentPath, $\"unexpected root element — expected '{_rootNames[(int)style]}'\");");

		body.Add(string.Empty);
		body.Add("\t\tvar __isEmptyElement = reader.IsEmptyElement;");
		body.AddRange(AttributeLoop(scalarMembers));

		body.Add(string.Empty);
		body.Add("\t\treader.ReadStartElement();");
		body.Add("\t\tif (!__isEmptyElement)");
		body.Add("\t\t{");
		body.AddRange(ChildLoop(rootNamespace, complexMembers, collectionMembers));
		body.Add("\t\t\tif (!reader.EOF)");
		body.Add("\t\t\t\treader.ReadEndElement();");
		body.Add("\t\t}");

		body.Add(string.Empty);
		body.Add("\t\t// Presence-aware funnel (spec §8.2) — one resolution per scalar member.");
		foreach (var member in scalarMembers)
			body.AddRange(ScalarResolution(member, rootNamespace));

		body.Add(string.Empty);
		body.Add("\t\t// Required singleton complex/collection-backed members missing entirely.");
		foreach (var member in complexMembers.Where(m => !m.IsNullable))
			body.AddRange(RequiredElementMissingCheck(member));

		body.Add(string.Empty);
		body.Add($"\t\treturn new {shape.TypeName}");
		body.Add("\t\t{");
		List<string> initializers = [];
		foreach (var member in scalarMembers)
			initializers.Add($"\t\t\t{member.ClrName} = {ScalarFinalExpression(member)},");
		foreach (var member in complexMembers)
			initializers.Add(
				$"\t\t\t{member.ClrName} = {(member.IsNullable ? ValueVar(member) : $"{ValueVar(member)}!")},");
		foreach (var member in collectionMembers)
			initializers.Add($"\t\t\t{member.ClrName} = {ItemsVar(member)},");
		body.AddRange(initializers);
		body.Add("\t\t};");

		var signature =
			$"\tpublic {shape.TypeName}? Read(global::System.Xml.XmlReader reader, {XmlNs}.XmlCaseStyle style, {XmlNs}.XmlReadContext context)";
		return signature + "\n\t{\n" + string.Join("\n", body) + "\n\t}";
	}

	static List<string> AttributeLoop(List<MemberModel> scalarMembers)
	{
		List<string> lines = [];
		lines.Add(string.Empty);
		lines.Add("\t\tif (reader.MoveToFirstAttribute())");
		lines.Add("\t\t{");
		lines.Add("\t\t\tdo");
		lines.Add("\t\t\t{");
		for (var i = 0; i < scalarMembers.Count; i++)
		{
			var member = scalarMembers[i];
			var keyword = i == 0 ?
				"if" :
				"else if";
			lines.Add(
				$"\t\t\t\t{keyword} (string.Equals(reader.LocalName, _{member.ClrName}AttrNames[(int)style], {Ordinal}))");
			lines.Add($"\t\t\t\t\t{ContentVar(member)} = reader.Value;");
		}

		lines.Add(scalarMembers.Count == 0 ?
			"\t\t\t\t{" :
			"\t\t\t\telse\n\t\t\t\t{");
		lines.Add(
			$"\t\t\t\t\tvar __suggestion = {XmlNs}.NameSuggestion.Nearest(reader.LocalName, _knownAttributeNames[(int)style]);");
		lines.Add(
			"\t\t\t\t\tcontext.AddFailure(context.PathTo(reader.LocalName), __suggestion is null ? \"unknown attribute\" : $\"unknown attribute — did you mean '{__suggestion}'?\");");
		lines.Add("\t\t\t\t}");
		lines.Add("\t\t\t} while (reader.MoveToNextAttribute());");
		lines.Add("\t\t\treader.MoveToElement();");
		lines.Add("\t\t}");
		return lines;
	}

	static List<string> ChildLoop(string rootNamespace, List<MemberModel> complexMembers,
		List<MemberModel> collectionMembers)
	{
		List<string> lines = [];
		lines.Add("\t\t\twhile (!reader.EOF && reader.NodeType != global::System.Xml.XmlNodeType.EndElement)");
		lines.Add("\t\t\t{");
		lines.Add("\t\t\t\tif (reader.NodeType == global::System.Xml.XmlNodeType.Element)");
		lines.Add("\t\t\t\t{");
		lines.Add("\t\t\t\t\tvar __childName = reader.LocalName;");

		var first = true;
		foreach (var member in complexMembers)
		{
			var itemShape =
				$"global::{rootNamespace}.NorseXmlShapes.{WriterEmitter.ShortName(member.ComplexTypeName!)}XmlShape";
			var keyword = first ?
				"if" :
				"else if";
			first = false;
			lines.Add(
				$"\t\t\t\t\t{keyword} (string.Equals(__childName, _{member.ClrName}ElementNames[(int)style], {Ordinal}))");
			lines.Add("\t\t\t\t\t{");
			lines.Add($"\t\t\t\t\t\tif ({SeenVar(member)})");
			lines.Add("\t\t\t\t\t\t{");
			lines.Add("\t\t\t\t\t\t\tcontext.PushElement(__childName);");
			lines.Add("\t\t\t\t\t\t\tcontext.AddFailure(context.CurrentPath, \"duplicate element\");");
			lines.Add("\t\t\t\t\t\t\tcontext.Pop();");
			lines.Add("\t\t\t\t\t\t\treader.Skip();");
			lines.Add("\t\t\t\t\t\t}");
			lines.Add("\t\t\t\t\t\telse");
			lines.Add("\t\t\t\t\t\t{");
			lines.Add($"\t\t\t\t\t\t\t{SeenVar(member)} = true;");
			lines.Add("\t\t\t\t\t\t\tcontext.PushElement(__childName);");
			lines.Add($"\t\t\t\t\t\t\t{ValueVar(member)} = {itemShape}.Instance.Read(reader, style, context);");
			lines.Add("\t\t\t\t\t\t\tcontext.Pop();");
			lines.Add("\t\t\t\t\t\t}");
			lines.Add("\t\t\t\t\t}");
		}

		foreach (var member in collectionMembers)
		{
			var itemShape =
				$"global::{rootNamespace}.NorseXmlShapes.{WriterEmitter.ShortName(member.ComplexTypeName!)}XmlShape";
			var keyword = first ?
				"if" :
				"else if";
			first = false;
			lines.Add(
				$"\t\t\t\t\t{keyword} (string.Equals(__childName, _{member.ClrName}ElementNames[(int)style], {Ordinal}))");
			lines.Add("\t\t\t\t\t{");
			lines.Add($"\t\t\t\t\t\t{CountVar(member)}++;");
			lines.Add($"\t\t\t\t\t\tcontext.PushItem(__childName, {CountVar(member)});");
			lines.Add($"\t\t\t\t\t\tvar __item = {itemShape}.Instance.Read(reader, style, context);");
			lines.Add("\t\t\t\t\t\tcontext.Pop();");
			lines.Add($"\t\t\t\t\t\t{ItemsVar(member)}.Add(__item!);");
			lines.Add("\t\t\t\t\t}");
		}

		lines.Add(first ?
			"\t\t\t\t{" :
			"\t\t\t\telse\n\t\t\t\t{");
		lines.Add(
			$"\t\t\t\t\tvar __suggestion = {XmlNs}.NameSuggestion.Nearest(__childName, _knownElementNames[(int)style]);");
		lines.Add("\t\t\t\t\tcontext.PushElement(__childName);");
		lines.Add(
			"\t\t\t\t\tcontext.AddFailure(context.CurrentPath, __suggestion is null ? \"unknown element\" : $\"unknown element — did you mean '{__suggestion}'?\");");
		lines.Add("\t\t\t\t\tcontext.Pop();");
		lines.Add("\t\t\t\t\treader.Skip();");
		lines.Add("\t\t\t\t}");

		lines.Add("\t\t\t\t}");
		lines.Add(
			"\t\t\t\telse if (reader.NodeType is global::System.Xml.XmlNodeType.Text or global::System.Xml.XmlNodeType.CDATA or global::System.Xml.XmlNodeType.SignificantWhitespace)");
		lines.Add("\t\t\t\t{");
		lines.Add("\t\t\t\t\tcontext.AddFailure(context.CurrentPath, \"text content is not permitted\");");
		lines.Add("\t\t\t\t\treader.Read();");
		lines.Add("\t\t\t\t}");
		lines.Add("\t\t\t\telse");
		lines.Add("\t\t\t\t{");
		lines.Add("\t\t\t\t\treader.Read();");
		lines.Add("\t\t\t\t}");
		lines.Add("\t\t\t}");
		return lines;
	}

	static List<string> RequiredElementMissingCheck(MemberModel member)
	{
		List<string> lines = [];
		lines.Add($"\t\tif (!{SeenVar(member)})");
		lines.Add("\t\t{");
		lines.Add($"\t\t\tcontext.PushElement(_{member.ClrName}ElementNames[(int)style]);");
		lines.Add("\t\t\tcontext.AddFailure(context.CurrentPath, \"required value missing\");");
		lines.Add("\t\t\tcontext.Pop();");
		lines.Add("\t\t}");
		return lines;
	}

	/// <summary>
	///     The presence-aware funnel (spec §8.2) for one scalar member — builds the local <c>Result&lt;T&gt;</c>
	///     (or <c>Result&lt;T&gt;?</c> / raw unwrapped value, depending on <see cref="MemberModel.IsResultWrapped" />)
	///     that <see cref="ScalarFinalExpression" /> later reads back for the object initializer. As of Task 8,
	///     an enum-typed member's present-content parse (<see cref="PresentParseExpression" />) calls the shared
	///     runtime <c>EnumLexical.Parse</c> — which reports no failure of its own — so every branch below
	///     always runs the same <c>TryGetValue(out Failure)</c>/<c>context.AddScalarFailure</c> check
	///     regardless of scalar kind; enum and plain-scalar members share one accumulation path, never two.
	/// </summary>
	static List<string> ScalarResolution(MemberModel member, string rootNamespace)
	{
		var content = ContentVar(member);
		var attrNames = $"_{member.ClrName}AttrNames[(int)style]";
		var isString = member.ScalarTypeName == "string";

		if (member.IsResultWrapped && member.IsNullable)
		{
			// Result<T>? — optional. Absent (content is null) stays null, no parse call, no failure.
			// Present (even "") always calls the funnel.
			List<string> lines = [];
			lines.Add($"\t\t{PrimitivesNs}.Result<{member.ScalarTypeName}>? {ResultVar(member)} = null;");
			lines.Add($"\t\tif ({content} is not null)");
			lines.Add("\t\t{");
			if (isString)
			{
				lines.Add(
					$"\t\t\t{ResultVar(member)} = new {PrimitivesNs}.Result<string>(new {PrimitivesNs}.Success<string>({content}));");
			}
			else
			{
				lines.Add($"\t\t\tvar __inner = {PresentParseExpression(member, content, rootNamespace)};");
				lines.Add($"\t\t\tif (__inner.TryGetValue(out {PrimitivesNs}.Failure {FailureVar(member)}))");
				lines.Add($"\t\t\t\tcontext.AddScalarFailure({attrNames}, {FailureVar(member)});");
				lines.Add($"\t\t\t{ResultVar(member)} = __inner;");
			}

			lines.Add("\t\t}");
			return lines;
		}

		if (member.IsResultWrapped && !member.IsNullable)
		{
			// Result<T> — required. Absent and present-empty both funnel through "empty content" for
			// every type except string, which is only ever exercised on the present branch here.
			List<string> lines = [];
			if (isString)
			{
				lines.Add($"\t\t{PrimitivesNs}.Result<string> {ResultVar(member)};");
				lines.Add($"\t\tif ({content} is null)");
				lines.Add("\t\t{");
				lines.Add(
					$"\t\t\t{ResultVar(member)} = {PrimitivesNs}.Parser.ParseRequired<string>(string.Empty, {Invariant});");
				lines.Add(
					$"\t\t\tif ({ResultVar(member)}.TryGetValue(out {PrimitivesNs}.Failure {FailureVar(member)}))");
				lines.Add($"\t\t\t\tcontext.AddScalarFailure({attrNames}, {FailureVar(member)});");
				lines.Add("\t\t}");
				lines.Add("\t\telse");
				lines.Add("\t\t{");
				lines.Add(
					$"\t\t\t{ResultVar(member)} = new {PrimitivesNs}.Result<string>(new {PrimitivesNs}.Success<string>({content}));");
				lines.Add("\t\t}");
			}
			else if (member.EnumValues.Count > 0)
			{
				// Enum, required. EnumLexical.Parse never distinguishes absence from present-empty
				// (spec 5's own decision: "" is content, never absence) — so, unlike the plain-scalar
				// branch below (whose Parser.ParseRequired already special-cases "" as Empty), absence
				// is handled here at the emission layer, mirroring the isString branch above: an
				// entirely absent required enum member yields the presence-law "required value missing"
				// failure directly, never routed through the parse funnel and never Malformed.
				var tableRef = WriterEmitter.EnumTableReference(rootNamespace, member.ScalarTypeName!);
				lines.Add($"\t\t{PrimitivesNs}.Result<{member.ScalarTypeName}> {ResultVar(member)};");
				lines.Add($"\t\tif ({content} is null)");
				lines.Add("\t\t{");
				lines.Add(
					$"\t\t\t{ResultVar(member)} = new {PrimitivesNs}.Failure({PrimitivesNs}.ParseFailure.Empty, \"\", {tableRef}.TypeName);");
				lines.Add(
					$"\t\t\tif ({ResultVar(member)}.TryGetValue(out {PrimitivesNs}.Failure {FailureVar(member)}))");
				lines.Add($"\t\t\t\tcontext.AddScalarFailure({attrNames}, {FailureVar(member)});");
				lines.Add("\t\t}");
				lines.Add("\t\telse");
				lines.Add("\t\t{");
				lines.Add($"\t\t\t{ResultVar(member)} = {PresentParseExpression(member, content, rootNamespace)};");
				lines.Add(
					$"\t\t\tif ({ResultVar(member)}.TryGetValue(out {PrimitivesNs}.Failure {FailureVar(member)}))");
				lines.Add($"\t\t\t\tcontext.AddScalarFailure({attrNames}, {FailureVar(member)});");
				lines.Add("\t\t}");
			}
			else
			{
				lines.Add(
					$"\t\tvar {ResultVar(member)} = {PresentParseExpression(member, $"{content} ?? string.Empty", rootNamespace)};");
				lines.Add($"\t\tif ({ResultVar(member)}.TryGetValue(out {PrimitivesNs}.Failure {FailureVar(member)}))");
				lines.Add($"\t\t\tcontext.AddScalarFailure({attrNames}, {FailureVar(member)});");
			}

			return lines;
		}

		if (!member.IsResultWrapped && member.IsNullable)
		{
			// Raw optional (response). Absent stays null with no failure; present-malformed accumulates
			// a failure and falls back to null (there is no union to carry a failed-but-present state).
			if (isString)
				return []; // ScalarFinalExpression reads XContent directly for this case — nothing to resolve.

			List<string> lines = [];
			lines.Add($"\t\t{member.ScalarTypeName}? {ValueVar(member)} = null;");
			lines.Add($"\t\tif ({content} is not null)");
			lines.Add("\t\t{");
			lines.Add($"\t\t\tvar __inner = {PresentParseExpression(member, content, rootNamespace)};");
			lines.Add($"\t\t\tif (__inner.TryGetValue(out {PrimitivesNs}.Failure {FailureVar(member)}))");
			lines.Add($"\t\t\t\tcontext.AddScalarFailure({attrNames}, {FailureVar(member)});");
			lines.Add(
				$"\t\t\telse if (__inner.TryGetValue(out {PrimitivesNs}.Success<{member.ScalarTypeName}> {SuccessVar(member)}))");
			lines.Add($"\t\t\t\t{ValueVar(member)} = {SuccessVar(member)}.Value;");
			lines.Add("\t\t}");
			return lines;
		}

		// Raw required (response).
		if (isString)
		{
			List<string> lines = [];
			lines.Add($"\t\tstring {ValueVar(member)};");
			lines.Add($"\t\tif ({content} is null)");
			lines.Add("\t\t{");
			lines.Add(
				$"\t\t\tvar __required = {PrimitivesNs}.Parser.ParseRequired<string>(string.Empty, {Invariant});");
			lines.Add($"\t\t\tif (__required.TryGetValue(out {PrimitivesNs}.Failure {FailureVar(member)}))");
			lines.Add($"\t\t\t\tcontext.AddScalarFailure({attrNames}, {FailureVar(member)});");
			lines.Add($"\t\t\t{ValueVar(member)} = string.Empty;");
			lines.Add("\t\t}");
			lines.Add("\t\telse");
			lines.Add("\t\t{");
			lines.Add($"\t\t\t{ValueVar(member)} = {content};");
			lines.Add("\t\t}");
			return lines;
		}
		else
		{
			List<string> lines = [];
			lines.Add(
				$"\t\tvar __{member.ClrName}Result = {PresentParseExpression(member, $"{content} ?? string.Empty", rootNamespace)};");
			lines.Add(
				$"\t\tif (__{member.ClrName}Result.TryGetValue(out {PrimitivesNs}.Failure {FailureVar(member)}))");
			lines.Add($"\t\t\tcontext.AddScalarFailure({attrNames}, {FailureVar(member)});");
			lines.Add(
				$"\t\tvar {ValueVar(member)} = __{member.ClrName}Result.TryGetValue(out {PrimitivesNs}.Success<{member.ScalarTypeName}> {SuccessVar(member)}) ? {SuccessVar(member)}.Value : default!;");
			return lines;
		}
	}

	/// <summary>
	///     The C# expression evaluating to <c>Result&lt;T&gt;</c> for present (non-null) content — routed
	///     through <c>Parser.ParseRequired</c> for plain scalars, or (Task 8) the shared runtime
	///     <c>EnumLexical.Parse</c> against the generated <c>NorseEnumNameRegistration</c> table for
	///     enum-typed members, generic-inferred is not possible here (no parameter of type <c>TEnum</c>, unlike
	///     <see cref="WriterEmitter.EnumFormatCall" />'s <c>value</c> parameter), so the type argument is always
	///     explicit. Never called for <c>string</c> members, which bypass this funnel entirely (Task 3 precedent).
	/// </summary>
	static string PresentParseExpression(MemberModel member, string contentExpression, string rootNamespace)
	{
		if (member.EnumValues.Count == 0)
			return $"{PrimitivesNs}.Parser.ParseRequired<{member.ScalarTypeName}>({contentExpression}, {Invariant})";

		return
			$"{XmlNs}.EnumLexical.Parse<{member.ScalarTypeName}>({WriterEmitter.EnumTableReference(rootNamespace, member.ScalarTypeName!)}, {contentExpression}, (int)style)";
	}

	static string ScalarFinalExpression(MemberModel member)
	{
		if (member.IsResultWrapped)
			return ResultVar(member);

		if (member.IsNullable && member.ScalarTypeName == "string")
			return ContentVar(member);

		return ValueVar(member);
	}

	static string ContentVar(MemberModel member) => $"{member.ClrName}Content";
	static string ResultVar(MemberModel member) => $"{member.ClrName}Result";
	static string ValueVar(MemberModel member) => $"{member.ClrName}Value";
	static string SuccessVar(MemberModel member) => $"{member.ClrName}Success";
	static string FailureVar(MemberModel member) => $"{member.ClrName}Failure";
	static string SeenVar(MemberModel member) => $"{member.ClrName}Seen";
	static string ItemsVar(MemberModel member) => $"{member.ClrName}Items";
	static string CountVar(MemberModel member) => $"{member.ClrName}Count";
}

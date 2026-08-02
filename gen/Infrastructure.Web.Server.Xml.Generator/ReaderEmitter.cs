using System.Text;

namespace Norse.Infrastructure.Web.Server.Xml.Generator;

/// <summary>
/// Emits the presence-aware, accumulating XML reader (design spec §8) for one contract shape — the
/// <c>Read</c> half of the class <see cref="WriterEmitter"/>'s <see cref="WriterEmitter.Emit"/> method
/// composes. Shares <see cref="WriterEmitter"/>'s per-member wire-name arrays and enum tables (never a
/// second table): the reader's name→value lookups scan the exact same <c>_{SafeName}Names</c> arrays
/// the writer declares its value→name lookups against.
/// </summary>
/// <remarks>
/// <b>Recursive, caller-pushes-the-path design.</b> <c>Read</c> never pushes its own path segment onto
/// <c>XmlReadContext</c> — the caller does, exactly once, before invoking it: the root caller
/// (a later task's formatter) pushes the root element; a parent shape reading a singleton nested
/// member pushes an element segment; a parent shape reading a collection item pushes an indexed item
/// segment. This is why the same recursive <c>Read</c> body works unmodified at every nesting depth —
/// the root-element-name-mismatch check at its top is provably a no-op for every nested invocation
/// (the parent already matched the child's local name against this exact type's own root name before
/// ever recursing), so it only ever fires for a genuine top-level mismatch, with no second code path
/// needed to distinguish "root" from "fragment" — the reader's mirror of the writer's own "one
/// recursive projection reused as both 'the root' and 'a fragment'" idiom (<see cref="WriterEmitter"/>'s
/// class remarks).
/// </remarks>
static class ReaderEmitter
{
	const string XmlNs = "global::Norse.Infrastructure.Web.Server.Xml";
	const string PrimitivesNs = "global::Norse.Primitives";
	const string Invariant = "global::System.Globalization.CultureInfo.InvariantCulture";
	const string Ordinal = "global::System.StringComparison.Ordinal";

	/// <summary>Reader-only static fields: per-complex/collection-member expected element-name tables, and the two known-name tables <c>NameSuggestion</c> scans for unknown attribute/element suggestions.</summary>
	public static string FieldDeclarations(ShapeModel shape)
	{
		List<string> lines = [];
		foreach (var member in shape.Members.Where(m => m.Kind != MemberKind.Scalar))
			lines.Add($"\tstatic readonly string[] _{member.ClrName}ElementNames = {WriterEmitter.NamesLiteral(NameCasing.ApplyAll(WriterEmitter.ShortName(member.ComplexTypeName!)))};");

		var attributeNameSets = shape.Members.Where(m => m.Kind == MemberKind.Scalar).Select(m => m.WireNames);
		var elementNameSets = shape.Members.Where(m => m.Kind != MemberKind.Scalar).Select(m => NameCasing.ApplyAll(WriterEmitter.ShortName(m.ComplexTypeName!)));
		lines.Add($"\tstatic readonly string[][] _knownAttributeNames = {KnownNamesLiteral(attributeNameSets)};");
		lines.Add($"\tstatic readonly string[][] _knownElementNames = {KnownNamesLiteral(elementNameSets)};");

		return string.Join("\n", lines);
	}

	/// <summary>Transposes a per-member list of five-casing name arrays into five style-indexed rows — <c>_knownAttributeNames[(int)style]</c> is every scalar member's wire name in that one style.</summary>
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
	public static string ReadMethod(string rootNamespace, ShapeModel shape, Dictionary<string, WriterEmitter.EnumTable> enumTables)
	{
		var scalarMembers = shape.Members.Where(m => m.Kind == MemberKind.Scalar).ToList();
		var complexMembers = shape.Members.Where(m => m.Kind == MemberKind.Complex).ToList();
		var collectionMembers = shape.Members.Where(m => m.Kind == MemberKind.Collection).ToList();

		List<string> body = [];
		body.Add("\t\t// Locals — one content/value slot per member, resolved after the attribute and child walks below.");
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
		body.Add("\t\t\tcontext.AddFailure(context.CurrentPath, $\"unexpected root element — expected '{_rootNames[(int)style]}'\");");

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
			body.AddRange(ScalarResolution(member, enumTables));

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
			initializers.Add($"\t\t\t{member.ClrName} = {(member.IsNullable ? ValueVar(member) : $"{ValueVar(member)}!")},");
		foreach (var member in collectionMembers)
			initializers.Add($"\t\t\t{member.ClrName} = {ItemsVar(member)},");
		body.AddRange(initializers);
		body.Add("\t\t};");

		var signature = $"\tpublic {shape.TypeName}? Read(global::System.Xml.XmlReader reader, {XmlNs}.XmlCaseStyle style, {XmlNs}.XmlReadContext context)";
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
			var keyword = i == 0 ? "if" : "else if";
			lines.Add($"\t\t\t\t{keyword} (string.Equals(reader.LocalName, _{member.ClrName}AttrNames[(int)style], {Ordinal}))");
			lines.Add($"\t\t\t\t\t{ContentVar(member)} = reader.Value;");
		}

		lines.Add(scalarMembers.Count == 0 ? "\t\t\t\t{" : "\t\t\t\telse\n\t\t\t\t{");
		lines.Add($"\t\t\t\t\tvar __suggestion = {XmlNs}.NameSuggestion.Nearest(reader.LocalName, _knownAttributeNames[(int)style]);");
		lines.Add("\t\t\t\t\tcontext.AddFailure(context.PathTo(reader.LocalName), __suggestion is null ? \"unknown attribute\" : $\"unknown attribute — did you mean '{__suggestion}'?\");");
		lines.Add("\t\t\t\t}");
		lines.Add("\t\t\t} while (reader.MoveToNextAttribute());");
		lines.Add("\t\t\treader.MoveToElement();");
		lines.Add("\t\t}");
		return lines;
	}

	static List<string> ChildLoop(string rootNamespace, List<MemberModel> complexMembers, List<MemberModel> collectionMembers)
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
			var itemShape = $"global::{rootNamespace}.NorseXmlShapes.{WriterEmitter.ShortName(member.ComplexTypeName!)}XmlShape";
			var keyword = first ? "if" : "else if";
			first = false;
			lines.Add($"\t\t\t\t\t{keyword} (string.Equals(__childName, _{member.ClrName}ElementNames[(int)style], {Ordinal}))");
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
			var itemShape = $"global::{rootNamespace}.NorseXmlShapes.{WriterEmitter.ShortName(member.ComplexTypeName!)}XmlShape";
			var keyword = first ? "if" : "else if";
			first = false;
			lines.Add($"\t\t\t\t\t{keyword} (string.Equals(__childName, _{member.ClrName}ElementNames[(int)style], {Ordinal}))");
			lines.Add("\t\t\t\t\t{");
			lines.Add($"\t\t\t\t\t\t{CountVar(member)}++;");
			lines.Add($"\t\t\t\t\t\tcontext.PushItem(__childName, {CountVar(member)});");
			lines.Add($"\t\t\t\t\t\tvar __item = {itemShape}.Instance.Read(reader, style, context);");
			lines.Add("\t\t\t\t\t\tcontext.Pop();");
			lines.Add($"\t\t\t\t\t\t{ItemsVar(member)}.Add(__item!);");
			lines.Add("\t\t\t\t\t}");
		}

		lines.Add(first ? "\t\t\t\t{" : "\t\t\t\telse\n\t\t\t\t{");
		lines.Add($"\t\t\t\t\tvar __suggestion = {XmlNs}.NameSuggestion.Nearest(__childName, _knownElementNames[(int)style]);");
		lines.Add("\t\t\t\t\tcontext.PushElement(__childName);");
		lines.Add("\t\t\t\t\tcontext.AddFailure(context.CurrentPath, __suggestion is null ? \"unknown element\" : $\"unknown element — did you mean '{__suggestion}'?\");");
		lines.Add("\t\t\t\t\tcontext.Pop();");
		lines.Add("\t\t\t\t\treader.Skip();");
		lines.Add("\t\t\t\t}");

		lines.Add("\t\t\t\t}");
		lines.Add("\t\t\t\telse if (reader.NodeType is global::System.Xml.XmlNodeType.Text or global::System.Xml.XmlNodeType.CDATA or global::System.Xml.XmlNodeType.SignificantWhitespace)");
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
	/// The presence-aware funnel (spec §8.2) for one scalar member — builds the local <c>Result&lt;T&gt;</c>
	/// (or <c>Result&lt;T&gt;?</c> / raw unwrapped value, depending on <see cref="MemberModel.IsResultWrapped"/>)
	/// that <see cref="ScalarFinalExpression"/> later reads back for the object initializer.
	/// </summary>
	static List<string> ScalarResolution(MemberModel member, Dictionary<string, WriterEmitter.EnumTable> enumTables)
	{
		var content = ContentVar(member);
		var attrNames = $"_{member.ClrName}AttrNames[(int)style]";
		var isString = member.ScalarTypeName == "string";
		// Enum lookup helpers (ExactMatchParseMethod/FlagsParseMethod below) call context.AddScalarFailure
		// (or, for a duplicate flags token, context.AddFailure directly) themselves — they need context to
		// render the malformed-vs-empty-vs-duplicate-token distinction correctly, so the call sites below
		// must not report a second time over the same Result<T> failure.
		var selfReports = member.EnumValues.Count > 0;

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
				lines.Add($"\t\t\t{ResultVar(member)} = new {PrimitivesNs}.Result<string>(new {PrimitivesNs}.Success<string>({content}));");
			}
			else
			{
				lines.Add($"\t\t\tvar __inner = {PresentParseExpression(member, content, enumTables, attrNames)};");
				if (!selfReports)
				{
					lines.Add($"\t\t\tif (__inner.TryGetValue(out {PrimitivesNs}.Failure {FailureVar(member)}))");
					lines.Add($"\t\t\t\tcontext.AddScalarFailure({attrNames}, {FailureVar(member)});");
				}

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
				lines.Add($"\t\t\t{ResultVar(member)} = {PrimitivesNs}.Parser.ParseRequired<string>(string.Empty, {Invariant});");
				lines.Add($"\t\t\tif ({ResultVar(member)}.TryGetValue(out {PrimitivesNs}.Failure {FailureVar(member)}))");
				lines.Add($"\t\t\t\tcontext.AddScalarFailure({attrNames}, {FailureVar(member)});");
				lines.Add("\t\t}");
				lines.Add("\t\telse");
				lines.Add("\t\t{");
				lines.Add($"\t\t\t{ResultVar(member)} = new {PrimitivesNs}.Result<string>(new {PrimitivesNs}.Success<string>({content}));");
				lines.Add("\t\t}");
			}
			else
			{
				lines.Add($"\t\tvar {ResultVar(member)} = {PresentParseExpression(member, $"{content} ?? string.Empty", enumTables, attrNames)};");
				if (!selfReports)
				{
					lines.Add($"\t\tif ({ResultVar(member)}.TryGetValue(out {PrimitivesNs}.Failure {FailureVar(member)}))");
					lines.Add($"\t\t\tcontext.AddScalarFailure({attrNames}, {FailureVar(member)});");
				}
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
			lines.Add($"\t\t\tvar __inner = {PresentParseExpression(member, content, enumTables, attrNames)};");
			if (selfReports)
			{
				lines.Add($"\t\t\tif (__inner.TryGetValue(out {PrimitivesNs}.Success<{member.ScalarTypeName}> {SuccessVar(member)}))");
				lines.Add($"\t\t\t\t{ValueVar(member)} = {SuccessVar(member)}.Value;");
			}
			else
			{
				lines.Add($"\t\t\tif (__inner.TryGetValue(out {PrimitivesNs}.Failure {FailureVar(member)}))");
				lines.Add($"\t\t\t\tcontext.AddScalarFailure({attrNames}, {FailureVar(member)});");
				lines.Add($"\t\t\telse if (__inner.TryGetValue(out {PrimitivesNs}.Success<{member.ScalarTypeName}> {SuccessVar(member)}))");
				lines.Add($"\t\t\t\t{ValueVar(member)} = {SuccessVar(member)}.Value;");
			}

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
			lines.Add($"\t\t\tvar __required = {PrimitivesNs}.Parser.ParseRequired<string>(string.Empty, {Invariant});");
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
			lines.Add($"\t\tvar __{member.ClrName}Result = {PresentParseExpression(member, $"{content} ?? string.Empty", enumTables, attrNames)};");
			if (!selfReports)
			{
				lines.Add($"\t\tif (__{member.ClrName}Result.TryGetValue(out {PrimitivesNs}.Failure {FailureVar(member)}))");
				lines.Add($"\t\t\tcontext.AddScalarFailure({attrNames}, {FailureVar(member)});");
			}

			lines.Add($"\t\tvar {ValueVar(member)} = __{member.ClrName}Result.TryGetValue(out {PrimitivesNs}.Success<{member.ScalarTypeName}> {SuccessVar(member)}) ? {SuccessVar(member)}.Value : default!;");
			return lines;
		}
	}

	/// <summary>The C# expression evaluating to <c>Result&lt;T&gt;</c> for present (non-null) content — routed through <c>Parser.ParseRequired</c> for plain scalars, or the shape's own enum lookup helpers for enum-typed members. Never called for <c>string</c> members, which bypass this funnel entirely (Task 3 precedent).</summary>
	static string PresentParseExpression(MemberModel member, string contentExpression, Dictionary<string, WriterEmitter.EnumTable> enumTables, string attrNamesExpression)
	{
		if (member.EnumValues.Count == 0)
			return $"{PrimitivesNs}.Parser.ParseRequired<{member.ScalarTypeName}>({contentExpression}, {Invariant})";

		var table = WriterEmitter.GetOrAddEnumTable(member, enumTables);
		var method = member.IsFlagsEnum ? $"{table.SafeName}ParseFlags" : $"{table.SafeName}ParseResult";
		return $"{method}({contentExpression}, (int)style, context, {attrNamesExpression})";
	}

	static string ScalarFinalExpression(MemberModel member)
	{
		if (member.IsResultWrapped)
			return ResultVar(member);

		if (member.IsNullable && member.ScalarTypeName == "string")
			return ContentVar(member);

		return ValueVar(member);
	}

	/// <summary>Reader-only enum helper methods — name→value lookups, the mirror of <see cref="WriterEmitter"/>'s value→name tables, sharing the exact same <c>_{SafeName}Names</c> field.</summary>
	public static string EnumHelperMethods(Dictionary<string, WriterEmitter.EnumTable> enumTables)
	{
		List<string> methods = [];
		foreach (var table in enumTables.Values)
			methods.Add(table.IsFlags ? FlagsParseMethod(table) : ExactMatchParseMethod(table));

		return methods.Count == 0 ? string.Empty : "\n" + string.Join("\n\n", methods);
	}

	static string ExactMatchParseMethod(WriterEmitter.EnumTable table)
	{
		StringBuilder sb = new();
		sb.Append($"\tstatic {PrimitivesNs}.Result<{table.EnumTypeName}> {table.SafeName}ParseResult(string content, int styleIndex, {XmlNs}.XmlReadContext context, string attributeName)\n\t{{\n");
		sb.Append("\t\tif (content.Length == 0)\n");
		sb.Append("\t\t{\n");
		sb.Append($"\t\t\tvar __empty = new {PrimitivesNs}.Failure({PrimitivesNs}.ParseFailure.Empty, string.Empty, \"{WriterEmitter.ShortName(table.EnumTypeName)}\");\n");
		sb.Append("\t\t\tcontext.AddScalarFailure(attributeName, __empty);\n");
		sb.Append($"\t\t\treturn new {PrimitivesNs}.Result<{table.EnumTypeName}>(__empty);\n");
		sb.Append("\t\t}\n\n");
		for (var i = 0; i < table.Values.Count; i++)
			sb.Append($"\t\tif (string.Equals(content, _{table.SafeName}Names[{i}][styleIndex], {Ordinal}))\n\t\t\treturn new {PrimitivesNs}.Result<{table.EnumTypeName}>(new {PrimitivesNs}.Success<{table.EnumTypeName}>({table.EnumTypeName}.{table.Values[i].ClrName}));\n\n");
		sb.Append($"\t\tvar __malformed = new {PrimitivesNs}.Failure({PrimitivesNs}.ParseFailure.Malformed, content, \"{WriterEmitter.ShortName(table.EnumTypeName)}\");\n");
		sb.Append("\t\tcontext.AddScalarFailure(attributeName, __malformed);\n");
		sb.Append($"\t\treturn new {PrimitivesNs}.Result<{table.EnumTypeName}>(__malformed);\n\t}}");
		return sb.ToString();
	}

	static string FlagsParseMethod(WriterEmitter.EnumTable table)
	{
		StringBuilder sb = new();
		sb.Append($"\tstatic {PrimitivesNs}.Result<{table.EnumTypeName}> {table.SafeName}ParseFlags(string content, int styleIndex, {XmlNs}.XmlReadContext context, string attributeName)\n\t{{\n");
		sb.Append("\t\tif (content.Length == 0)\n");
		sb.Append("\t\t{\n");
		sb.Append($"\t\t\tvar __empty = new {PrimitivesNs}.Failure({PrimitivesNs}.ParseFailure.Empty, string.Empty, \"{WriterEmitter.ShortName(table.EnumTypeName)}\");\n");
		sb.Append("\t\t\tcontext.AddScalarFailure(attributeName, __empty);\n");
		sb.Append($"\t\t\treturn new {PrimitivesNs}.Result<{table.EnumTypeName}>(__empty);\n");
		sb.Append("\t\t}\n\n");
		sb.Append("\t\tvar __tokens = content.Split(' ', global::System.StringSplitOptions.RemoveEmptyEntries);\n");
		sb.Append("\t\tlong __bits = 0;\n");
		sb.Append("\t\tvar __used = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal);\n");
		sb.Append("\t\tforeach (var __token in __tokens)\n");
		sb.Append("\t\t{\n");
		sb.Append("\t\t\tlong? __matched = null;\n");
		for (var i = 0; i < table.Values.Count; i++)
			sb.Append($"\t\t\tif (string.Equals(__token, _{table.SafeName}Names[{i}][styleIndex], {Ordinal})) __matched = {table.Values[i].Value}L;\n");
		sb.Append("\t\t\tif (__matched is null)\n");
		sb.Append("\t\t\t{\n");
		sb.Append($"\t\t\t\tvar __malformed = new {PrimitivesNs}.Failure({PrimitivesNs}.ParseFailure.Malformed, content, \"{WriterEmitter.ShortName(table.EnumTypeName)}\");\n");
		sb.Append("\t\t\t\tcontext.AddScalarFailure(attributeName, __malformed);\n");
		sb.Append($"\t\t\t\treturn new {PrimitivesNs}.Result<{table.EnumTypeName}>(__malformed);\n");
		sb.Append("\t\t\t}\n\n");
		sb.Append("\t\t\tif (!__used.Add(__token))\n");
		sb.Append("\t\t\t{\n");
		sb.Append("\t\t\t\tcontext.AddFailure(context.PathTo(attributeName), $\"duplicate flags token '{__token}'\");\n");
		sb.Append($"\t\t\t\treturn new {PrimitivesNs}.Result<{table.EnumTypeName}>(new {PrimitivesNs}.Failure({PrimitivesNs}.ParseFailure.Malformed, content, \"{WriterEmitter.ShortName(table.EnumTypeName)}\"));\n");
		sb.Append("\t\t\t}\n\n");
		sb.Append("\t\t\t__bits |= __matched.Value;\n");
		sb.Append("\t\t}\n\n");
		sb.Append($"\t\treturn new {PrimitivesNs}.Result<{table.EnumTypeName}>(new {PrimitivesNs}.Success<{table.EnumTypeName}>(({table.EnumTypeName})__bits));\n\t}}");
		return sb.ToString();
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

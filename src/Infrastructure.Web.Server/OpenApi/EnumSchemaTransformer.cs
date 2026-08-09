using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.OpenApi;

/// <summary>
///     The plain-member half of the platform's enum wire law's OpenAPI enforcement (spec §6.5, §7's
///     twentieth taxonomy row): every schema node the native AspNetCore.OpenApi pipeline generates for an
///     enum CLR type — never gated to <c>[DataContract]</c> types, since an enum is an enum regardless of
///     what encloses it — becomes a case-styled <c>string</c>/<c>enum:</c> list sourced from the generated
///     <see cref="EnumNameRegistry" />, never the framework's own numeric default; a <c>[Flags]</c> CLR type
///     forks onto <c>type: array</c> with that same list moved down to <c>items</c> instead (see
///     <see cref="ApplyGovernedFlagsList" />) — the picklist survives the shape change, never a second table.
///     Twinned with
///     <see cref="ResultSchemaTransformer" />, which routes the <c>Result&lt;TEnum&gt;</c>-wrapped half of
///     the same law through <see cref="ApplyGovernedList" /> below — the one shared mechanism both
///     transformers project a table's names through, so the two can never independently drift on how a
///     governed list renders. A schema whose CLR type is an enum with no table registered is the same
///     impossible-by-construction gap <see cref="Norse.Infrastructure.Web.Server.Json.EnumLexicalJsonConverterFactory" />
///     already refuses:
///     an enum reached the document with no text wire law, so this throws rather than falling back to a
///     numeric or reflected name.
/// </summary>
/// <param name="registry">
///     The generated per-enum name-table registry — the host passes the same instance
///     <c>AddNorseJson</c> registers.
/// </param>
/// <param name="options">
///     Carries the platform's single <see cref="NorseXmlOptions.CaseStyle" /> — the JSON, XML, and
///     OpenAPI channels never diverge on which case style is live.
/// </param>
public sealed class EnumSchemaTransformer(EnumNameRegistry registry, NorseXmlOptions options)
	: IOpenApiSchemaTransformer
{
	readonly NorseXmlOptions _options = options ?? throw new ArgumentNullException(nameof(options));
	readonly EnumNameRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

	/// <inheritdoc />
	/// <exception cref="NotSupportedException">
	///     The schema's CLR type is an enum with no table registered in
	///     <see cref="EnumNameRegistry" />.
	/// </exception>
	public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context,
		CancellationToken cancellationToken)
	{
		var type = context.JsonTypeInfo.Type;
		if (!type.IsEnum)
			return Task.CompletedTask;

		if (!_registry.TryGet(type, out var table))
			throw new NotSupportedException(
				$"no generated name table for enum '{type.Name}' — an enum outside every facade closure has no text wire law");

		if (type.IsDefined(typeof(FlagsAttribute), inherit: false))
			ApplyGovernedFlagsList(schema, table, (int)_options.CaseStyle);
		else
			ApplyGovernedList(schema, table, (int)_options.CaseStyle);

		// A raw (non-Result) enum member is always response-side by the shape law (NORSE022/23 ban
		// raw scalars from request closures), and this governed component is never referenced from a
		// request direction — Result<TEnum> request members get their own inline schema from
		// ResultSchemaTransformer instead, stamped WriteOnly there, never through this path. Stamped
		// here rather than inside ApplyGovernedList so that shared helper stays direction-agnostic —
		// ResultSchemaTransformer's Result<TEnum> branch calls it too and must not inherit ReadOnly.
		schema.ReadOnly = true;
		return Task.CompletedTask;
	}

	/// <summary>
	///     Stamps <paramref name="schema" /> as the governed <c>string</c>/<c>enum:</c> projection of
	///     <paramref name="table" /> at <paramref name="styleIndex" />, in table (declaration) order —
	///     replacing whatever numeric type/format/enum the framework's own default schema generation left
	///     in place. The one mechanism both this transformer and <see cref="ResultSchemaTransformer" />
	///     project an <see cref="EnumNameTable" /> through.
	/// </summary>
	internal static void ApplyGovernedList(OpenApiSchema schema, EnumNameTable table, int styleIndex)
	{
		schema.Type = JsonSchemaType.String;
		schema.Format = null;
		schema.Enum =
			[.. Enumerable.Range(0, table.Count).Select(memberIndex => (JsonNode)table.Name(memberIndex, styleIndex))];
	}

	/// <summary>
	///     Stamps <paramref name="schema" /> as <c>type: array</c> whose <c>items</c> is the identical
	///     governed <c>string</c>/<c>enum:</c> projection <see cref="ApplyGovernedList" /> would have built
	///     for a plain member of the same <paramref name="table" /> — a <c>[Flags]</c> member's picklist
	///     survives the shape change instead of forking onto a separate table. Detected via the CLR type's
	///     own <see cref="FlagsAttribute" />, mirroring the binary channel's identical
	///     <c>typeof(TEnum).IsDefined(typeof(FlagsAttribute), inherit: false)</c> check
	///     (<c>Norse.Infrastructure.Web.Grpc.ResultEnumSerializer&lt;TEnum&gt;</c>).
	/// </summary>
	internal static void ApplyGovernedFlagsList(OpenApiSchema schema, EnumNameTable table, int styleIndex)
	{
		schema.Type = JsonSchemaType.Array;
		schema.Format = null;
		schema.Enum = null;
		OpenApiSchema items = new();
		ApplyGovernedList(items, table, styleIndex);
		schema.Items = items;
	}
}

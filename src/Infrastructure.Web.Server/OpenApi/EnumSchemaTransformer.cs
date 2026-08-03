using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.OpenApi;

/// <summary>
/// The plain-member half of the platform's enum wire law's OpenAPI enforcement (spec §6.5, §7's
/// twentieth taxonomy row): every schema node the native AspNetCore.OpenApi pipeline generates for an
/// enum CLR type — never gated to <c>[DataContract]</c> types, since an enum is an enum regardless of
/// what encloses it — becomes a case-styled <c>string</c>/<c>enum:</c> list sourced from the generated
/// <see cref="EnumNameRegistry"/>, never the framework's own numeric default. Twinned with
/// <see cref="ResultSchemaTransformer"/>, which routes the <c>Result&lt;TEnum&gt;</c>-wrapped half of
/// the same law through <see cref="ApplyGovernedList"/> below — the one shared mechanism both
/// transformers project a table's names through, so the two can never independently drift on how a
/// governed list renders. A schema whose CLR type is an enum with no table registered is the same
/// impossible-by-construction gap <see cref="Norse.Infrastructure.Web.Server.Json.EnumLexicalJsonConverterFactory"/> already refuses:
/// an enum reached the document with no text wire law, so this throws rather than falling back to a
/// numeric or reflected name.
/// </summary>
/// <param name="registry">The generated per-enum name-table registry — the host passes the same instance <c>AddNorseJson</c> registers.</param>
/// <param name="options">Carries the platform's single <see cref="NorseXmlOptions.CaseStyle"/> — the JSON, XML, and OpenAPI channels never diverge on which case style is live.</param>
public sealed class EnumSchemaTransformer(EnumNameRegistry registry, NorseXmlOptions options) : IOpenApiSchemaTransformer
{
	readonly EnumNameRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
	readonly NorseXmlOptions _options = options ?? throw new ArgumentNullException(nameof(options));

	/// <inheritdoc/>
	/// <exception cref="NotSupportedException">The schema's CLR type is an enum with no table registered in <see cref="EnumNameRegistry"/>.</exception>
	public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
	{
		var type = context.JsonTypeInfo.Type;
		if (!type.IsEnum)
			return Task.CompletedTask;

		if (!_registry.TryGet(type, out var table))
			throw new NotSupportedException($"no generated name table for enum '{type.Name}' — an enum outside every facade closure has no text wire law");

		ApplyGovernedList(schema, table, (int)_options.CaseStyle);
		return Task.CompletedTask;
	}

	/// <summary>
	/// Stamps <paramref name="schema"/> as the governed <c>string</c>/<c>enum:</c> projection of
	/// <paramref name="table"/> at <paramref name="styleIndex"/>, in table (declaration) order —
	/// replacing whatever numeric type/format/enum the framework's own default schema generation left
	/// in place. The one mechanism both this transformer and <see cref="ResultSchemaTransformer"/>
	/// project an <see cref="EnumNameTable"/> through.
	/// </summary>
	internal static void ApplyGovernedList(OpenApiSchema schema, EnumNameTable table, int styleIndex)
	{
		schema.Type = JsonSchemaType.String;
		schema.Format = null;
		schema.Enum = [.. Enumerable.Range(0, table.Count).Select(memberIndex => (JsonNode)table.Name(memberIndex, styleIndex))];
	}
}

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.OpenApi;

/// <summary>
///     The request-side half of the symmetry law's OpenAPI enforcement (spec §10.1, §12): every
///     <c>Result&lt;T&gt;</c>/<c>Result&lt;T&gt;?</c> member of a Futhark <c>[DataContract]</c> becomes the
///     underlying scalar's schema (<c>Result&lt;DateOnly&gt;</c> → <c>string</c>/<c>date</c>) via
///     <see cref="ScalarTaxonomy" />'s closed static table for the BCL row and the generated
///     <see cref="EnumNameRegistry" /> for the enum row — never the union's
///     own reflected shape (<c>anyOf: [Success, Failure]</c>), which is what AspNetCore.OpenApi's default
///     schema generation produces for it left alone (verified empirically against the real generated
///     document, not assumed). A nullable <c>Result&lt;T&gt;?</c> member leaves the schema's
///     <c>required</c> list; a non-nullable <c>Result&lt;T&gt;</c> member stays in it. By the shape law
///     (NORSE022/NORSE023), a raw (non-Result) scalar member can only ever be response-side, so this
///     transformer also table-drives every raw <em>BCL</em> closed-taxonomy scalar member it finds,
///     marking it <c>readOnly</c> — request members get <c>writeOnly</c>, response members get
///     <c>readOnly</c>, both from the same one table (spec §12). Scoped to <c>[DataContract]</c> types only
///     (<see cref="ScalarTaxonomy.IsFutharkContract" />) — see that method's remarks for why touching a
///     schema outside that gate is not just out of scope but empirically unsafe. A raw <em>enum</em> member
///     is deliberately left untouched here — the framework's own <c>$ref</c> to the enum type's shared
///     component schema survives, and <see cref="EnumSchemaTransformer" /> governs that referenced component
///     directly, so it is never inlined twice. Only the <c>Result&lt;TEnum&gt;</c> branch resolves an enum's
///     governed name list here, from the generated <see cref="EnumNameRegistry" /> via
///     <see cref="EnumSchemaTransformer.ApplyGovernedList" /> — the same shared helper
///     <see cref="EnumSchemaTransformer" /> uses for a plain enum schema node, so the two transformers never
///     independently drift on how a table's names render.
/// </summary>
public sealed class ResultSchemaTransformer(EnumNameRegistry registry, NorseXmlOptions options)
	: IOpenApiSchemaTransformer
{
	readonly NorseXmlOptions _options = options ?? throw new ArgumentNullException(nameof(options));
	readonly EnumNameRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

	/// <inheritdoc />
	public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context,
		CancellationToken cancellationToken)
	{
		var type = context.JsonTypeInfo.Type;
		if (context.JsonTypeInfo.Properties.Count == 0 || !ScalarTaxonomy.IsFutharkContract(type))
			return Task.CompletedTask;

		foreach (var property in context.JsonTypeInfo.Properties)
		{
			if (ScalarTaxonomy.TryUnwrapResult(property.PropertyType, out var elementType, out var isNullable))
			{
				if (!TryBuildScalarSchema(elementType, out var unwrapped))
					continue; // outside the closed taxonomy entirely (neither the BCL table nor an enum).

				unwrapped.WriteOnly = true;
				schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
				schema.Properties[property.Name] = unwrapped;

				schema.Required ??= new HashSet<string>();
				if (isNullable)
					schema.Required.Remove(property.Name);
				else
					schema.Required.Add(property.Name);

				continue;
			}

			if (ScalarTaxonomy.TryBuildSchema(property.PropertyType, out var raw))
			{
				raw.ReadOnly = true;
				schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
				schema.Properties[property.Name] = raw;
			}
		}

		return Task.CompletedTask;
	}

	/// <summary>
	///     Builds the unwrapped <c>Result&lt;T&gt;</c> element's schema — the fixed BCL table first, the
	///     generic enum branch second, the two rows of §7's taxonomy the <c>Result&lt;TEnum&gt;</c> branch
	///     needs. (Only that branch calls this — the raw, non-Result branch stays BCL-only via
	///     <see cref="ScalarTaxonomy.TryBuildSchema" /> directly; a raw enum member is
	///     <see cref="EnumSchemaTransformer" />'s territory instead.) The enum branch resolves through the
	///     generated <see cref="EnumNameRegistry" />, never runtime-algorithmic casing — a registry miss is
	///     the impossible-by-construction tripwire (an enum reached the document with no text wire law).
	/// </summary>
	/// <exception cref="NotSupportedException">
	///     <paramref name="clrType" /> is an enum with no table registered in
	///     <see cref="EnumNameRegistry" />.
	/// </exception>
	bool TryBuildScalarSchema(Type clrType, out OpenApiSchema schema)
	{
		if (ScalarTaxonomy.TryBuildSchema(clrType, out schema))
			return true;

		if (!clrType.IsEnum)
		{
			schema = null!;
			return false;
		}

		if (!_registry.TryGet(clrType, out var table))
			throw new NotSupportedException(
				$"no generated name table for enum '{clrType.Name}' — an enum outside every facade closure has no text wire law");

		schema = new OpenApiSchema();
		EnumSchemaTransformer.ApplyGovernedList(schema, table, (int)_options.CaseStyle);
		return true;
	}
}

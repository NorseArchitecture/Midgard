using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Norse.Infrastructure.Web.Server.OpenApi;

/// <summary>
/// The request-side half of the symmetry law's OpenAPI enforcement (spec §10.1, §12): every
/// <c>Result&lt;T&gt;</c>/<c>Result&lt;T&gt;?</c> member of a Futhark <c>[DataContract]</c> becomes the
/// underlying scalar's schema (<c>Result&lt;DateOnly&gt;</c> → <c>string</c>/<c>date</c>) via
/// <see cref="ScalarTaxonomy"/>'s closed static table — never the union's own reflected shape
/// (<c>anyOf: [Success, Failure]</c>), which is what AspNetCore.OpenApi's default schema generation
/// produces for it left alone (verified empirically against the real generated document, not assumed).
/// A nullable <c>Result&lt;T&gt;?</c> member leaves the schema's <c>required</c> list; a non-nullable
/// <c>Result&lt;T&gt;</c> member stays in it. By the shape law (NORSE022/NORSE023), a raw (non-Result)
/// scalar member can only ever be response-side, so this transformer also table-drives every raw
/// closed-taxonomy scalar member it finds, marking it <c>readOnly</c> — request members get
/// <c>writeOnly</c>, response members get <c>readOnly</c>, both from the same one table (spec §12).
/// Scoped to <c>[DataContract]</c> types only (<see cref="ScalarTaxonomy.IsFutharkContract"/>) — see
/// that method's remarks for why touching a schema outside that gate is not just out of scope but
/// empirically unsafe. Enum-wrapped <c>Result&lt;TEnum&gt;</c> members are a known, flagged gap (see
/// <see cref="ScalarTaxonomy"/>'s remarks) — left untouched, never guessed at.
/// </summary>
public sealed class ResultSchemaTransformer : IOpenApiSchemaTransformer
{
	/// <inheritdoc/>
	public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
	{
		var type = context.JsonTypeInfo.Type;
		if (context.JsonTypeInfo.Properties.Count == 0 || !ScalarTaxonomy.IsFutharkContract(type))
			return Task.CompletedTask;

		foreach (var property in context.JsonTypeInfo.Properties)
		{
			if (ScalarTaxonomy.TryUnwrapResult(property.PropertyType, out var elementType, out var isNullable))
			{
				if (!ScalarTaxonomy.TryBuildSchema(elementType, out var unwrapped))
					continue; // outside the closed taxonomy (e.g. an enum) — flagged gap, see ScalarTaxonomy.

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
}

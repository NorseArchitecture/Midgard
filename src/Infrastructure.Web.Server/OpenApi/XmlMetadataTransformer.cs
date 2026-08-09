using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.OpenApi;

/// <summary>
///     Stamps OpenAPI's <c>xml</c> object mechanically, per Futhark's fixed wire grammar (spec §6, §12) —
///     every scalar member (Result-wrapped or raw) becomes an XML attribute; every <c>[DataContract]</c>
///     type's own schema carries the element name its instances render under, case-styled through the
///     host's configured <see cref="XmlCaseStyle" /> via <see cref="RuntimeNameCasing" /> — the same casing
///     the generated shapes actually emit on the wire, never a second independently-drifting rule.
/// </summary>
/// <remarks>
///     <b>
///         No literal <c>attribute: true</c>/<c>wrapped: false</c> boolean pair is emitted, and that is
///         correct, not an omission.
///     </b>
///     The resolved <c>Microsoft.OpenApi</c> package version backing this
///     OpenAPI generation pipeline (3.6.0, OpenAPI 3.2's revised XML vocabulary) superseded that classic
///     boolean pair with a single <see cref="OpenApiXmlNodeType" /> enum (<c>Element</c>/<c>Attribute</c>/
///     <c>Text</c>/<c>Cdata</c>/<c>None</c>) — <c>NodeType = Attribute</c> below is the honest equivalent
///     of "attribute: true". Precisely: <c>OpenApiXml</c> still declares <c>Attribute</c>/<c>Wrapped</c>
///     properties internally, but both are non-public and carry
///     <c>[Obsolete("Use NodeType property instead. This property will be removed in a future version.")]</c>
///     — confirmed by reflecting the real assembly, not merely "absent". There is no accessible member to
///     set for the "wrapped: false" half at all, obsolete or otherwise. Verified empirically: a collection
///     member's array schema, left with no <c>xml</c> stamp at all, already serializes items as repeated
///     sibling <c>$ref</c> elements with no wrapper — the vocabulary's own default already matches
///     Futhark's "N child elements, no wrapper" law (spec §6.3), so nothing needs stamping there. Flagged
///     here, not silently routed around, because the spec text ("wrapped: false always") was written
///     against the classic boolean vocabulary this resolved package version has moved past.
/// </remarks>
public sealed class XmlMetadataTransformer(NorseXmlOptions options) : IOpenApiSchemaTransformer
{
	readonly NorseXmlOptions _options = options ?? throw new ArgumentNullException(nameof(options));

	/// <inheritdoc />
	public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context,
		CancellationToken cancellationToken)
	{
		var type = context.JsonTypeInfo.Type;
		if (!ScalarTaxonomy.IsFutharkContract(type))
			return Task.CompletedTask;

		schema.Xml = new OpenApiXml { Name = RuntimeNameCasing.Apply(_options.CaseStyle, type.Name) };

		if (schema.Properties is null)
			return Task.CompletedTask;

		foreach (var property in context.JsonTypeInfo.Properties)
		{
			if (!schema.Properties.TryGetValue(property.Name, out var propertySchema) ||
				propertySchema is not OpenApiSchema concrete)
				continue;

			var isScalar = ScalarTaxonomy.TryUnwrapResult(property.PropertyType, out var elementType, out _) ?
				ScalarTaxonomy.IsClosedScalar(elementType) :
				ScalarTaxonomy.IsClosedScalar(property.PropertyType);

			if (!isScalar)
				continue; // complex members and collections need no attribute stamp — the vocabulary's element default already matches Futhark's law.

			concrete.Xml = new OpenApiXml
			{
				NodeType = OpenApiXmlNodeType.Attribute,
				Name = RuntimeNameCasing.Apply(_options.CaseStyle, ClrName(property))
			};
		}

		return Task.CompletedTask;
	}

	/// <summary>
	///     The original CLR member name, not <see cref="System.Text.Json.Serialization.Metadata.JsonPropertyInfo.Name" />
	///     — that field is already JSON-naming-policy-transformed (camelCase by ASP.NET Core's default),
	///     and re-running <see cref="RuntimeNameCasing" />'s Pascal/camel word-splitter over an
	///     already-transformed name risks misreading an acronym boundary a naming policy's own decapitalization
	///     pass may have reshaped (e.g. a leading acronym run). <c>AttributeProvider</c> is the
	///     <see cref="MemberInfo" />/<see cref="ParameterInfo" /> the property was resolved from; falling back
	///     to the JSON name only if that reflection path is ever unavailable keeps this from throwing on an
	///     edge case neither this task nor its tests have exercised.
	/// </summary>
	static string ClrName(JsonPropertyInfo property) =>
		property.AttributeProvider switch
		{
			MemberInfo member => member.Name,
			_ => property.Name
		};
}

using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Norse.Infrastructure.Web.Server.Xml;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.OpenApi;

/// <summary>
/// The closed §7 scalar taxonomy, projected into OpenAPI schema type/format pairs — the runtime
/// counterpart to <c>XmlLexical</c>'s own type coverage (Task 2) and the Xml.Generator's
/// <c>ClosureWalker.IsSupportedScalar</c> (Task 5), independently reflection-driven since this
/// runtime assembly cannot see the generator's compile-time-only symbol classification. A static
/// dictionary keyed by CLR <see cref="Type"/>, not static abstract interface members — BCL types
/// (<see cref="Guid"/>, <see cref="DateOnly"/>, …) cannot implement a Norse interface, so a per-type
/// table is the honest mechanism (ratified at plan review, 2026-08-02).
/// </summary>
/// <remarks>
/// Enums are §7's twentieth row and are fully in scope — the BCL table above cannot key an
/// open-ended CLR type by construction, so enums are handled by a separate, generic branch
/// (<see cref="TryBuildEnumSchema"/>) mirroring how the Xml.Generator's own
/// <c>ClosureWalker.Classify</c> (Task 5) resolves enums: a <c>type.IsEnum</c> check plus a reflected
/// member table, never an enumeration of concrete enum types. (An earlier revision of this file
/// wrongly treated enums as out of scope — corrected in the same fix round that added
/// <see cref="TryBuildEnumSchema"/>.)
/// </remarks>
static class ScalarTaxonomy
{
	static readonly Dictionary<Type, (JsonSchemaType Type, string? Format)> _map = new()
	{
		[typeof(bool)] = (JsonSchemaType.Boolean, null),
		[typeof(sbyte)] = (JsonSchemaType.Integer, "int8"),
		[typeof(byte)] = (JsonSchemaType.Integer, "uint8"),
		[typeof(short)] = (JsonSchemaType.Integer, "int16"),
		[typeof(ushort)] = (JsonSchemaType.Integer, "uint16"),
		[typeof(int)] = (JsonSchemaType.Integer, "int32"),
		[typeof(uint)] = (JsonSchemaType.Integer, "uint32"),
		[typeof(long)] = (JsonSchemaType.Integer, "int64"),
		[typeof(ulong)] = (JsonSchemaType.Integer, "uint64"),
		[typeof(decimal)] = (JsonSchemaType.Number, "double"),
		[typeof(float)] = (JsonSchemaType.Number, "float"),
		[typeof(double)] = (JsonSchemaType.Number, "double"),
		[typeof(char)] = (JsonSchemaType.String, "char"),
		[typeof(string)] = (JsonSchemaType.String, null),
		[typeof(Guid)] = (JsonSchemaType.String, "uuid"),
		[typeof(DateTime)] = (JsonSchemaType.String, "date-time"),
		[typeof(DateTimeOffset)] = (JsonSchemaType.String, "date-time"),
		[typeof(DateOnly)] = (JsonSchemaType.String, "date"),
		[typeof(TimeOnly)] = (JsonSchemaType.String, "time"),
		[typeof(TimeSpan)] = (JsonSchemaType.String, "duration"),
	};

	/// <summary>
	/// Whether <paramref name="type"/> carries <c>[DataContract]</c> — the scope gate both
	/// <see cref="ResultSchemaTransformer"/> and <see cref="XmlMetadataTransformer"/> check before
	/// touching a schema, so they only ever act on Futhark's own contract surface (the same NORSE028
	/// marker the generator's shape law already requires of every facade body-bound/response type),
	/// never an unrelated schema elsewhere in the app's OpenAPI document. Empirically load-bearing,
	/// not merely tidy: mutating a schema instance AspNetCore.OpenApi builds for a type outside this
	/// gate (e.g. a bare <c>Result&lt;T&gt;</c>'s own reflected schema) was observed, during this
	/// task's own verification, to leak stray identity back into the final document via the
	/// framework's own schema reference resolution — gating first avoids the whole class of bug.
	/// </summary>
	public static bool IsFutharkContract(Type type) =>
		type.IsDefined(typeof(DataContractAttribute), inherit: false);

	/// <summary>Whether <paramref name="type"/> is a row in the closed scalar table — the fixed BCL set or an enum.</summary>
	public static bool IsClosedScalar(Type type) =>
		_map.ContainsKey(type) || type.IsEnum;

	/// <summary>
	/// Unwraps <c>Result&lt;T&gt;</c>/<c>Result&lt;T&gt;?</c>, returning the wrapped scalar type and
	/// whether the member was the nullable form. <see langword="false"/> for anything else, including
	/// a raw (non-Result) scalar — that case is <see cref="IsClosedScalar"/>'s job instead.
	/// </summary>
	public static bool TryUnwrapResult(Type propertyType, out Type elementType, out bool isNullable)
	{
		var underlying = Nullable.GetUnderlyingType(propertyType);
		var candidate = underlying ?? propertyType;

		if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(Result<>))
		{
			elementType = candidate.GetGenericArguments()[0];
			isNullable = underlying is not null;
			return true;
		}

		elementType = null!;
		isNullable = false;
		return false;
	}

	/// <summary>
	/// Builds a fresh, clean schema for a fixed-BCL-row scalar type — a new instance every call,
	/// deliberately: two properties sharing one mutable <see cref="OpenApiSchema"/> instance would
	/// alias each other's later <c>Xml</c>/<c>ReadOnly</c>/<c>WriteOnly</c> stamps.
	/// <see langword="false"/> outside the closed BCL set — including an enum, which is
	/// <see cref="TryBuildEnumSchema"/>'s row, not this one.
	/// </summary>
	public static bool TryBuildSchema(Type clrType, out OpenApiSchema schema)
	{
		if (!_map.TryGetValue(clrType, out var entry))
		{
			schema = null!;
			return false;
		}

		schema = new OpenApiSchema { Type = entry.Type, Format = entry.Format };
		return true;
	}

	/// <summary>
	/// Builds §7's twentieth taxonomy row — <see langword="false"/> for a non-enum type. Per spec §6.5
	/// ("Enums: names, never numerics"), the schema is <c>string</c> with an <c>enum</c> keyword listing
	/// every defined member's name, case-styled through <paramref name="style"/> exactly like
	/// <see cref="XmlMetadataTransformer"/>'s attribute names — the same wire form the generated shapes
	/// emit (spec §7's enums row: "case-styled member name(s)"). <c>[Flags]</c> enums still resolve to a
	/// <c>string</c> schema listing the individual defined member names; the space-separated multi-token
	/// combinations spec §6.5 allows for a flags value have no clean finite <c>enum</c> keyword
	/// representation in JSON Schema, so this deliberately lists the token vocabulary rather than every
	/// combination — a documented simplification, not a silent gap.
	/// </summary>
	public static bool TryBuildEnumSchema(Type clrType, XmlCaseStyle style, out OpenApiSchema schema)
	{
		if (!clrType.IsEnum)
		{
			schema = null!;
			return false;
		}

		schema = new OpenApiSchema
		{
			Type = JsonSchemaType.String,
			Enum = [.. Enum.GetNames(clrType).Select(name => (JsonNode)RuntimeNameCasing.Apply(style, name))]
		};
		return true;
	}
}

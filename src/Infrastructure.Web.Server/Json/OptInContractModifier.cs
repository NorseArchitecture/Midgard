using System.Runtime.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Norse.Infrastructure.Web.Server.Json;

/// <summary>
///     STJ contract-customization modifier enforcing the platform's <c>[DataContract]</c> opt-in law
///     (spec §4b): three serializers, one membership definition — a property STJ sees natively but WCF's
///     <see cref="DataMemberAttribute" /> vocabulary never opted in does not exist to STJ in either
///     direction, matching how the generated XML shape already treats membership. Scoped to
///     <see cref="JsonTypeInfoKind.Object" /> types carrying <see cref="DataContractAttribute" />; every
///     other shape — collections, dictionaries, and plain (non-<c>[DataContract]</c>) objects — keeps
///     STJ's default membership rules untouched.
/// </summary>
public static class OptInContractModifier
{
	/// <summary>
	///     Removes every property of <paramref name="typeInfo" /> whose <see cref="JsonPropertyInfo.AttributeProvider" />
	///     lacks <see cref="DataMemberAttribute" />, when <paramref name="typeInfo" />'s CLR type carries
	///     <see cref="DataContractAttribute" />. Registered via <c>WithAddedModifier</c> on the resolver
	///     chain in <c>AddNorseJson</c>.
	/// </summary>
	public static void Apply(JsonTypeInfo typeInfo)
	{
		if (typeInfo.Kind != JsonTypeInfoKind.Object)
			return;
		if (!typeInfo.Type.IsDefined(typeof(DataContractAttribute), inherit: false))
			return;

		for (var index = typeInfo.Properties.Count - 1; index >= 0; index--)
		{
			if (typeInfo.Properties[index].AttributeProvider?.IsDefined(typeof(DataMemberAttribute), inherit: false) !=
				true)
				typeInfo.Properties.RemoveAt(index);
		}
	}
}

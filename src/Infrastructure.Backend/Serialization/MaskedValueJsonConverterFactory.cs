using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Norse.Primitives.Pii;

namespace Norse.Infrastructure.Backend.Serialization;

/// <summary>
/// Defense-in-depth for serialization paths no analyzer can see (spec §1.5 layer 2, relocated here
/// from the forge by NORSE070 — encodings live inside the wire border): any <see cref="IMaskedValue"/>
/// value struct writes its masked rendering and refuses to read. Reading is refused because masked
/// forms can be syntactically valid inputs (<c>j***@d***.com</c> parses as an email address) — a
/// lossy round-trip that succeeds would fabricate a well-formed value that silently is not the
/// person's data. Wire contracts are unaffected: transports carry plain strings filled explicitly
/// at the disclosure edge. Lives here, in <c>Infrastructure.Backend</c>, rather than beside the MVC
/// JSON converter family in <c>Infrastructure.Web.Server/Json</c>: the serialization seam
/// (<see cref="SystemTextJsonSerializer"/>) needs it too, and Web.Server → Backend is the only
/// direction that doesn't invert the realm's dependency graph — deliberately <see langword="public"/>
/// so the MVC pipeline's <c>AddNorseJson</c> can reach it from the sibling assembly.
/// </summary>
public sealed class MaskedValueJsonConverterFactory : JsonConverterFactory
{
	/// <inheritdoc/>
	public override bool CanConvert(Type typeToConvert) =>
		typeToConvert.IsValueType && typeof(IMaskedValue).IsAssignableFrom(typeToConvert);

	/// <inheritdoc/>
	[UnconditionalSuppressMessage("Trimming", "IL2071", Justification = "The IMaskedValue-implementing PII scalar set (EmailAddress, PhoneNumber, PersonalName, BirthDate, and future additions per the Pii increment) is doctrinally finite and every member is a plain value struct with an implicit public parameterless constructor — MaskedValueJsonConverter<T>'s struct constraint guarantees this for every concrete T the trimmer could ever substitute here.")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "Same posture as ResultJsonConverterFactory.CreateConverter: converter-resolution reflection over a doctrinally finite, statically-known PII scalar type set — AOT source-generation for the resolver chain is a future increment.")]
	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
		(JsonConverter)Activator.CreateInstance(typeof(MaskedValueJsonConverter<>).MakeGenericType(typeToConvert))!;

	sealed class MaskedValueJsonConverter<T> : JsonConverter<T> where T : struct, IMaskedValue
	{
		public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			throw new NotSupportedException($"{typeToConvert.Name} is masked-write-only JSON; PII never rehydrates from JSON — parse the wire string at the boundary instead.");

		public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
			writer.WriteStringValue(value.Masked);
	}
}

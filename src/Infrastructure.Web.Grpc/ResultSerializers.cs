using System.Diagnostics.CodeAnalysis;
using Norse.Primitives;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// Registers protobuf-net surrogate serializers for <see cref="Result{T}"/> over the platform's
/// closed scalar taxonomy (the <see cref="ISpanParsable{TSelf}"/> types <c>Norse.Primitives.Parser</c>
/// routes) onto a <see cref="RuntimeTypeModel"/>, mirroring <see cref="IdentifierSerializers"/>'s
/// registration mechanism. Wire form: each row's own native protobuf-net encoding wherever one
/// exists — the same binary form a plain, unwrapped field of that type would use — except
/// <see cref="DateTimeOffset"/>, which protobuf-net cannot represent natively at all and so falls
/// back to a plain <c>string</c> carrying its §7 lexical form, funneled through
/// <see cref="Parser.ParseRequired{T}"/>; see <see cref="ResultSerializer{T}"/>'s remarks for the full
/// per-type accounting. <c>Write</c> unwraps a success <see cref="Result{T}"/> to the row's own wire
/// form — the union never rides the wire — and throws <see cref="InvalidOperationException"/> only for
/// the two illegal states, failure and default. An absent field on read leaves the member at
/// <c>default(Result{T})</c> — protobuf-net's native behavior for any field whose tag never appears on
/// the wire, since deserialize only ever invokes a field's serializer for tags it actually finds —
/// which <c>Infrastructure.Web.Server</c>'s <c>ResultRules</c> validation catches downstream (spec
/// §9.3). <see cref="ResultEnumSerializer{TEnum}"/> is the one row still fully deserialization-only:
/// its <c>Write</c> throws unconditionally for every state, success included, until it gains its own
/// success branch. <c>Result{T}?</c> (<see cref="Nullable{T}"/> of <see cref="Result{T}"/>) needs no
/// separate registration: protobuf-net's built-in <see cref="Nullable{T}"/> handling already skips the
/// write when null and leaves null on an absent read; a present <see cref="Result{T}"/> wrapped in the
/// nullable still reaches the same serializer's <c>Write</c>, unwrap-or-throw law and all.
/// </summary>
/// <remarks>
/// Open-generic surrogate registration was verified (spike, protobuf-net 3.2.56) and does not work:
/// <c>RuntimeTypeModel.Add(typeof(Result&lt;&gt;), false)</c> accepts the call, and even accepts an
/// open-generic <c>SerializerType</c> assignment, without throwing — but the registration is never
/// consulted for a closed instantiation. Serializing/deserializing <c>Result&lt;int&gt;</c> through
/// that path falls straight back to protobuf-net's default reflection-based contract inference (or
/// fails outright with "Type is not expected, and no contract can be inferred" when there is nothing
/// for it to infer from). This is the pre-approved fallback the spec names (§9.3, ~13 registrations):
/// one explicit closed-generic registration per scalar type in the platform's taxonomy —
/// <see cref="ResultSerializer{T}"/> itself stays a single generic type; only the registration calls
/// below are enumerated per type.
/// </remarks>
public static class ResultSerializers
{
	// A failed or default Result<T> is illegal to write; a success unwraps to the plain scalar
	// instead. One law, one message: the gRPC serializers throw this exact wording, and the JSON
	// converters and generated XML writer align on the same literal within this change series.
	internal const string IllegalWriteMessage = "a failed or default Result<T> is illegal to write";

	/// <summary>
	/// Registers <see cref="Result{T}"/> surrogates for every scalar type in the platform's closed
	/// taxonomy on <paramref name="model"/>, the general wire law for bare
	/// <see cref="DateTimeOffset"/> fields (<see cref="DateTimeOffsetSerializer"/> — response-side
	/// scalars never wrap, spec §5.4, so the bare type needs its own registration), and the
	/// discovery hook that gives <c>Result&lt;TEnum&gt;</c> members a wire law
	/// (<see cref="ResultEnumSerializer{TEnum}"/>): enums are user-declared, so the set is open and
	/// cannot be enumerated here — instead every contract type entering the model after this call is
	/// swept for <c>Result&lt;TEnum&gt;</c>/<c>Result&lt;TEnum&gt;?</c> members, each registered on
	/// first sight, the same must-run-before-contract-types contract
	/// <see cref="IdentifierSerializers.Register"/> documents. Idempotent per model. A registration
	/// failure is cached and rethrown to every subsequent caller for this model, never silently
	/// treated as success.
	/// </summary>
	public static void Register(RuntimeTypeModel model) =>
		model.EnsureRegistered(typeof(ResultSerializers), () =>
		{
			model.AfterApplyDefaultBehaviour += (_, e) => RegisterEnumResults(model, e);
			model.Add(typeof(DateTimeOffset), applyDefaultBehaviour: false).SerializerType = typeof(DateTimeOffsetSerializer);

			RegisterScalar<bool>(model);
			RegisterScalar<byte>(model);
			RegisterScalar<sbyte>(model);
			RegisterScalar<short>(model);
			RegisterScalar<ushort>(model);
			RegisterScalar<int>(model);
			RegisterScalar<uint>(model);
			RegisterScalar<long>(model);
			RegisterScalar<ulong>(model);
			RegisterScalar<float>(model);
			RegisterScalar<double>(model);
			RegisterScalar<decimal>(model);
			RegisterScalar<char>(model);
			RegisterScalar<string>(model);
			RegisterScalar<Guid>(model);
			RegisterScalar<DateOnly>(model);
			RegisterScalar<DateTime>(model);
			RegisterScalar<DateTimeOffset>(model);
			RegisterScalar<TimeOnly>(model);
			RegisterScalar<TimeSpan>(model);
		});

	static void RegisterScalar<T>(RuntimeTypeModel model) where T : notnull, ISpanParsable<T> =>
		model.Add(typeof(Result<T>), applyDefaultBehaviour: false).SerializerType = typeof(ResultSerializer<T>);

	[UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "ResultEnumSerializer<TEnum> is fully generic over enum types with no member dependencies beyond the enum itself; contract types that reach this sweep are already rooted by the model registration that triggered it.")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "Same posture as ResultJsonConverterFactory: the enum set is contract-declared and discovery-driven; AOT source-generation for it is a future increment.")]
	static void RegisterEnumResults(RuntimeTypeModel model, TypeAddedEventArgs e)
	{
		foreach (var field in e.MetaType.GetFields())
		{
			var memberType = Nullable.GetUnderlyingType(field.MemberType) ?? field.MemberType;
			if (!memberType.IsGenericType || memberType.GetGenericTypeDefinition() != typeof(Result<>))
				continue;
			var valueType = memberType.GetGenericArguments()[0];
			if (!valueType.IsEnum || model.IsDefined(memberType))
				continue;
			model.Add(memberType, applyDefaultBehaviour: false).SerializerType =
				typeof(ResultEnumSerializer<>).MakeGenericType(valueType);
		}
	}
}

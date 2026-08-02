using System.Runtime.CompilerServices;
using Norse.Primitives;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// Registers protobuf-net surrogate serializers for <see cref="Result{T}"/> over the platform's
/// closed scalar taxonomy (the <see cref="ISpanParsable{TSelf}"/> types <c>Norse.Primitives.Parser</c>
/// routes) onto a <see cref="RuntimeTypeModel"/>, mirroring <see cref="IdentifierSerializers"/>'s
/// registration mechanism. Wire form: a plain protobuf <c>string</c> field, presence-tracked, for
/// every row in the taxonomy — never that row's own native binary encoding, so a
/// malformed value is structurally representable on the wire (the reason for the string form in the
/// first place) rather than decoding to some valid-but-wrong value. <see cref="Result{T}"/> is a
/// deserialization-only type: <c>Write</c> always throws <see cref="InvalidOperationException"/>,
/// success included — see <see cref="ResultSerializer{T}"/>'s remarks for the full accounting. An
/// absent field on read leaves the member at <c>default(Result{T})</c> — protobuf-net's native
/// behavior for any field whose tag never appears on the wire, since deserialize only ever invokes a
/// field's serializer for tags it actually finds — which <c>Infrastructure.Web.Server</c>'s
/// <c>ResultRules</c> validation catches downstream (spec §9.3). <c>Result{T}?</c>
/// (<see cref="Nullable{T}"/> of <see cref="Result{T}"/>) needs no separate registration:
/// protobuf-net's built-in <see cref="Nullable{T}"/> handling already skips the write when null and
/// leaves null on an absent read; a present <see cref="Result{T}"/> wrapped in the nullable still
/// reaches the same serializer's <c>Write</c> and still throws, whatever state it carries.
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
	static readonly ConditionalWeakTable<RuntimeTypeModel, RuntimeTypeModel> _registered = [];

	/// <summary>
	/// Registers <see cref="Result{T}"/> surrogates for every scalar type in the platform's closed
	/// taxonomy on <paramref name="model"/>. Idempotent per model.
	/// </summary>
	public static void Register(RuntimeTypeModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		if (!_registered.TryAdd(model, model))
			return;

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
	}

	static void RegisterScalar<T>(RuntimeTypeModel model) where T : notnull, ISpanParsable<T> =>
		model.Add(typeof(Result<T>), applyDefaultBehaviour: false).SerializerType = typeof(ResultSerializer<T>);
}

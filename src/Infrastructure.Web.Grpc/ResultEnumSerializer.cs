using System.Globalization;
using System.Runtime.CompilerServices;
using Norse.Primitives;
using ProtoBuf;
using ProtoBuf.Serializers;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// The enum row of the §7 scalar taxonomy on the gRPC leg — the one row
/// <see cref="ResultSerializer{T}"/> structurally cannot carry (no enum satisfies
/// <see cref="ISpanParsable{TSelf}"/>). The wire form is protobuf-net's own native enum encoding: the
/// underlying integral as a varint — names never touch the binary channel, so the text channels' case
/// styling and generated name tables are irrelevant here. Undefined values are the binary channel's
/// counterpart to the text channels' undefined-enum-name accumulable (spec §6.5/§8.1): a varint
/// carrying no defined member — or, for a <see cref="FlagsAttribute"/> enum, leftover bits outside the
/// defined set — is representable on the wire, so it funnels to the platform's one typed
/// <see cref="Failure"/> exactly as a malformed <see cref="DateTimeOffset"/> wire string does, never a
/// throw on <see cref="Read"/>. Absent-field semantics are protobuf-net's own, identical to every other row:
/// <c>default(Result&lt;TEnum&gt;)</c> for required, <see langword="null"/> for optional, caught
/// downstream by <c>ResultRules</c> validation (spec §9.3). <see cref="Write(ref ProtoWriter.State, Result{TEnum})"/>
/// mirrors <see cref="ResultSerializer{T}.Write"/>: a defined success unwraps to the same varint a
/// plain <typeparamref name="TEnum"/> field would write — the union never rides the wire — while a
/// failure or default <see cref="Result{TEnum}"/> is illegal to write for the same reason every other
/// row's is, and a success carrying an undefined value (or, for a <see cref="FlagsAttribute"/> enum,
/// leftover bits outside the defined set) is illegal to write for a second, enum-specific reason: it
/// would put a value on the wire this same serializer's own <see cref="Read"/> cannot accept back.
/// </summary>
/// <typeparam name="TEnum">The contract's enum type — user-declared, so the set is open; registration is discovery-driven (see <see cref="ResultSerializers"/>).</typeparam>
sealed class ResultEnumSerializer<TEnum> : ISerializer<Result<TEnum>>, ISerializer<Result<TEnum>?> where TEnum : unmanaged, Enum
{
	// One-time per closed generic at type initialization — the "one-time startup wiring" allowance;
	// never per-read reflection.
	static readonly bool _isFlags = typeof(TEnum).IsDefined(typeof(FlagsAttribute), inherit: false);
	static readonly long _definedBits = ComputeDefinedBits();

	public SerializerFeatures Features =>
		SerializerFeatures.CategoryScalar | SerializerFeatures.WireTypeVarint;

	public Result<TEnum> Read(ref ProtoReader.State state, Result<TEnum> value)
	{
		var raw = state.ReadInt64();
		var candidate = FromBits(raw);
		if (IsDefined(candidate))
			return new Success<TEnum>(candidate);
		return new Failure(ParseFailure.Malformed, raw.ToString(CultureInfo.InvariantCulture), typeof(TEnum).Name);
	}

	/// <summary>
	/// Unwraps a success to the enum's own native varint — the same binary a plain, unwrapped
	/// <typeparamref name="TEnum"/> field would use — mirroring <see cref="ResultSerializer{T}.Write"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// <paramref name="value"/> is a failure or default (illegal for every row of the taxonomy), or a
	/// success carrying an undefined value — leftover bits outside the defined set for a
	/// <see cref="FlagsAttribute"/> enum (illegal because <see cref="Read"/> could never accept it back).
	/// </exception>
	public void Write(ref ProtoWriter.State state, Result<TEnum> value)
	{
		if (!value.TryGetValue(out Success<TEnum> success))
			throw new InvalidOperationException(ResultSerializers.IllegalWriteMessage);
		if (!IsDefined(success.Value))
			throw new InvalidOperationException($"'{success.Value}' is an undefined value of '{typeof(TEnum)}' and is illegal to write.");
		state.WriteInt64(ToBits(success.Value));
	}

	Result<TEnum>? ISerializer<Result<TEnum>?>.Read(ref ProtoReader.State state, Result<TEnum>? value) =>
		Read(ref state, value.GetValueOrDefault());

	/// <exception cref="InvalidOperationException">
	/// <paramref name="value"/> is present but a failure or default, or a success carrying an undefined
	/// value — see <see cref="Write(ref ProtoWriter.State, Result{TEnum})"/>.
	/// </exception>
	void ISerializer<Result<TEnum>?>.Write(ref ProtoWriter.State state, Result<TEnum>? value) =>
		Write(ref state, value.GetValueOrDefault());

	static bool IsDefined(TEnum value) =>
		_isFlags ? (ToBits(value) & ~_definedBits) == 0 : Enum.IsDefined(value);

	static long ComputeDefinedBits()
	{
		long bits = 0;
		foreach (var defined in Enum.GetValues<TEnum>())
			bits |= ToBits(defined);
		return bits;
	}

	// Unsafe.As identity reinterprets between the enum and its underlying integral — sound per branch
	// because Unsafe.SizeOf<TEnum> is a JIT constant, the same closed-dispatch pattern
	// ResultSerializer<T> uses. Unsigned widening keeps flag masks bit-faithful regardless of the
	// underlying type's signedness. Deliberately twinned with EnumLexical.ToBits/FromBits (Infrastructure.Web.Server, Xml/) — separate assemblies, no sharing; keep edits in lockstep.
	static long ToBits(TEnum value) => Unsafe.SizeOf<TEnum>() switch
	{
		1 => Unsafe.As<TEnum, byte>(ref value),
		2 => Unsafe.As<TEnum, ushort>(ref value),
		4 => Unsafe.As<TEnum, uint>(ref value),
		_ => Unsafe.As<TEnum, long>(ref value),
	};

	static TEnum FromBits(long raw)
	{
		if (Unsafe.SizeOf<TEnum>() == 1)
		{ var v = (byte)raw; return Unsafe.As<byte, TEnum>(ref v); }
		if (Unsafe.SizeOf<TEnum>() == 2)
		{ var v = (ushort)raw; return Unsafe.As<ushort, TEnum>(ref v); }
		if (Unsafe.SizeOf<TEnum>() == 4)
		{ var v = (uint)raw; return Unsafe.As<uint, TEnum>(ref v); }
		return Unsafe.As<long, TEnum>(ref raw);
	}
}

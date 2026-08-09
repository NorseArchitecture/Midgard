using System.Globalization;
using System.Runtime.CompilerServices;
using Norse.Primitives;
using ProtoBuf;
using ProtoBuf.Serializers;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
///     Reads a scalar <see cref="Result{T}" /> off the wire as <typeparamref name="T" />'s own native
///     protobuf-net encoding wherever one exists — the same binary form a plain <typeparamref name="T" />
///     field would use, no parsing, the binary is already typed. <see cref="Guid" /> is the platform's own
///     raw-bytes convention (<see cref="GuidWire" />), unrelated to this type. <see cref="DateTimeOffset" />
///     is the one genuine exception: protobuf-net has zero native support for it at any compatibility
///     level, so it rides a plain wire <c>string</c> carrying the exact §7 lexical form ("O" round-trip)
///     the JSON and XML legs already write — the same form <see cref="DateTimeOffsetSerializer" /> (the
///     bare type's registered wire law) emits, one wire form by construction —
///     funneled through <see cref="Parser.ParseRequired{T}" /> — the platform's one parsing door —
///     so a malformed value on this one type produces the platform's typed <see cref="Failure" /> rather
///     than either a thrown exception or an unrepresentable byte pattern. This type both reads and writes:
///     <see cref="Write" /> unwraps a success to <typeparamref name="T" />'s own wire form — the same binary
///     a plain, unwrapped field would use, mirroring <see cref="ReadScalar" /> branch-for-branch — because
///     the union itself never rides the wire; only a failure or default <see cref="Result{T}" /> is illegal
///     to write, since neither carries a value with legitimate business going out over a channel that
///     exists to validate untrusted input coming in. <see cref="Result{T}" />'s own
///     dispatch over the closed <see cref="ISpanParsable{TSelf}" /> taxonomy is <c>typeof</c>-branched and
///     JIT-eliminated per closed generic instantiation, the same pattern <c>Norse.Primitives.Parser</c>
///     itself uses — <see cref="Unsafe.As{TFrom,TTo}" /> is a sound identity reinterpret in each branch
///     because <typeparamref name="T" /> is statically the branch's concrete type there, never a real
///     layout coercion.
/// </summary>
/// <typeparam name="T">The validated value's type — one row of the platform's closed scalar taxonomy.</typeparam>
sealed class ResultSerializer<T> : ISerializer<Result<T>>, ISerializer<Result<T>?> where T : notnull, ISpanParsable<T>
{
	public SerializerFeatures Features =>
		SerializerFeatures.CategoryScalar | WireFeature();

	public Result<T> Read(ref ProtoReader.State state, Result<T> value)
	{
		if (typeof(T) == typeof(DateTimeOffset))
		{
			var text = state.ReadString() ?? string.Empty;
			var routed = Parser.ParseRequired<DateTimeOffset>(text, CultureInfo.InvariantCulture);
			return Unsafe.As<Result<DateTimeOffset>, Result<T>>(ref routed);
		}

		return new Success<T>(ReadScalar(ref state));
	}

	/// <summary>
	///     Unwraps a success to the scalar's own wire form — the same binary a plain, unwrapped
	///     <typeparamref name="T" /> field would use — the union never rides the wire.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	///     <paramref name="value" /> is a failure or default; both are illegal to
	///     write.
	/// </exception>
	public void Write(ref ProtoWriter.State state, Result<T> value)
	{
		if (!value.TryGetValue(out Success<T> success))
			throw new InvalidOperationException(ResultSerializers.IllegalWriteMessage);
		if (typeof(T) == typeof(DateTimeOffset))
		{
			var raw = success.Value;
			var dto = Unsafe.As<T, DateTimeOffset>(ref raw);
			state.WriteString(dto.ToString("O", CultureInfo.InvariantCulture));
			return;
		}

		WriteScalar(ref state, success.Value);
	}

	Result<T>? ISerializer<Result<T>?>.Read(ref ProtoReader.State state, Result<T>? value) =>
		Read(ref state, value.GetValueOrDefault());

	/// <exception cref="InvalidOperationException">
	///     <paramref name="value" /> is present but a failure or default; both are
	///     illegal to write.
	/// </exception>
	void ISerializer<Result<T>?>.Write(ref ProtoWriter.State state, Result<T>? value) =>
		Write(ref state, value.GetValueOrDefault());

	static SerializerFeatures WireFeature()
	{
		if (typeof(T) == typeof(float))
			return SerializerFeatures.WireTypeFixed32;
		if (typeof(T) == typeof(double))
			return SerializerFeatures.WireTypeFixed64;
		if (typeof(T) == typeof(string) || typeof(T) == typeof(decimal) || typeof(T) == typeof(Guid) ||
			typeof(T) == typeof(DateTime) || typeof(T) == typeof(DateTimeOffset) || typeof(T) == typeof(TimeSpan))
			return SerializerFeatures.WireTypeString;
		return SerializerFeatures.WireTypeVarint;
	}

	/// <summary>
	///     Native per-type read dispatch. Never called for <see cref="DateTimeOffset" /> — <see cref="Read" /> routes
	///     that one through the string+parser fallback before ever reaching here.
	/// </summary>
	static T ReadScalar(ref ProtoReader.State state)
	{
		if (typeof(T) == typeof(bool))
		{
			var v = state.ReadBoolean();
			return Unsafe.As<bool, T>(ref v);
		}

		if (typeof(T) == typeof(byte))
		{
			var v = state.ReadByte();
			return Unsafe.As<byte, T>(ref v);
		}

		if (typeof(T) == typeof(sbyte))
		{
			var v = state.ReadSByte();
			return Unsafe.As<sbyte, T>(ref v);
		}

		if (typeof(T) == typeof(short))
		{
			var v = state.ReadInt16();
			return Unsafe.As<short, T>(ref v);
		}

		if (typeof(T) == typeof(ushort))
		{
			var v = state.ReadUInt16();
			return Unsafe.As<ushort, T>(ref v);
		}

		if (typeof(T) == typeof(int))
		{
			var v = state.ReadInt32();
			return Unsafe.As<int, T>(ref v);
		}

		if (typeof(T) == typeof(uint))
		{
			var v = state.ReadUInt32();
			return Unsafe.As<uint, T>(ref v);
		}

		if (typeof(T) == typeof(long))
		{
			var v = state.ReadInt64();
			return Unsafe.As<long, T>(ref v);
		}

		if (typeof(T) == typeof(ulong))
		{
			var v = state.ReadUInt64();
			return Unsafe.As<ulong, T>(ref v);
		}

		if (typeof(T) == typeof(float))
		{
			var v = state.ReadSingle();
			return Unsafe.As<float, T>(ref v);
		}

		if (typeof(T) == typeof(double))
		{
			var v = state.ReadDouble();
			return Unsafe.As<double, T>(ref v);
		}

		if (typeof(T) == typeof(decimal))
		{
			var v = BclHelpers.ReadDecimalString(ref state);
			return Unsafe.As<decimal, T>(ref v);
		}

		if (typeof(T) == typeof(char))
		{
			var v = (char)state.ReadUInt16();
			return Unsafe.As<char, T>(ref v);
		}

		if (typeof(T) == typeof(string))
		{
			var v = state.ReadString() ?? string.Empty;
			return Unsafe.As<string, T>(ref v);
		}

		if (typeof(T) == typeof(Guid))
		{
			var v = GuidWire.Read(ref state);
			return Unsafe.As<Guid, T>(ref v);
		}

		if (typeof(T) == typeof(DateOnly))
		{
			var v = BclHelpers.ReadDateOnly(ref state);
			return Unsafe.As<DateOnly, T>(ref v);
		}

		if (typeof(T) == typeof(TimeOnly))
		{
			var v = BclHelpers.ReadTimeOnly(ref state);
			return Unsafe.As<TimeOnly, T>(ref v);
		}

		if (typeof(T) == typeof(DateTime))
		{
			var v = BclHelpers.ReadTimestamp(ref state);
			return Unsafe.As<DateTime, T>(ref v);
		}

		if (typeof(T) == typeof(TimeSpan))
		{
			var v = BclHelpers.ReadDuration(ref state);
			return Unsafe.As<TimeSpan, T>(ref v);
		}

		throw new NotSupportedException($"No Result<{typeof(T).Name}> wire mapping registered.");
	}

	/// <summary>
	///     Native per-type write dispatch, branch-for-branch the write counterpart of <see cref="ReadScalar" />. Never
	///     called for <see cref="DateTimeOffset" /> — <see cref="Write" /> routes that one through its own "O" string branch
	///     before ever reaching here.
	/// </summary>
	static void WriteScalar(ref ProtoWriter.State state, T value)
	{
		if (typeof(T) == typeof(bool))
		{
			state.WriteBoolean(Unsafe.As<T, bool>(ref value));
			return;
		}

		if (typeof(T) == typeof(byte))
		{
			state.WriteInt32(Unsafe.As<T, byte>(ref value));
			return;
		}

		if (typeof(T) == typeof(sbyte))
		{
			state.WriteInt32(Unsafe.As<T, sbyte>(ref value));
			return;
		}

		if (typeof(T) == typeof(short))
		{
			state.WriteInt32(Unsafe.As<T, short>(ref value));
			return;
		}

		if (typeof(T) == typeof(ushort))
		{
			state.WriteInt32(Unsafe.As<T, ushort>(ref value));
			return;
		}

		if (typeof(T) == typeof(int))
		{
			state.WriteInt32(Unsafe.As<T, int>(ref value));
			return;
		}

		if (typeof(T) == typeof(uint))
		{
			state.WriteUInt32(Unsafe.As<T, uint>(ref value));
			return;
		}

		if (typeof(T) == typeof(long))
		{
			state.WriteInt64(Unsafe.As<T, long>(ref value));
			return;
		}

		if (typeof(T) == typeof(ulong))
		{
			state.WriteUInt64(Unsafe.As<T, ulong>(ref value));
			return;
		}

		if (typeof(T) == typeof(float))
		{
			state.WriteSingle(Unsafe.As<T, float>(ref value));
			return;
		}

		if (typeof(T) == typeof(double))
		{
			state.WriteDouble(Unsafe.As<T, double>(ref value));
			return;
		}

		if (typeof(T) == typeof(decimal))
		{
			BclHelpers.WriteDecimalString(ref state, Unsafe.As<T, decimal>(ref value));
			return;
		}

		if (typeof(T) == typeof(char))
		{
			state.WriteUInt16(Unsafe.As<T, char>(ref value));
			return;
		}

		if (typeof(T) == typeof(string))
		{
			state.WriteString(Unsafe.As<T, string>(ref value));
			return;
		}

		if (typeof(T) == typeof(Guid))
		{
			GuidWire.Write(ref state, Unsafe.As<T, Guid>(ref value));
			return;
		}

		if (typeof(T) == typeof(DateOnly))
		{
			BclHelpers.WriteDateOnly(ref state, Unsafe.As<T, DateOnly>(ref value));
			return;
		}

		if (typeof(T) == typeof(TimeOnly))
		{
			BclHelpers.WriteTimeOnly(ref state, Unsafe.As<T, TimeOnly>(ref value));
			return;
		}

		if (typeof(T) == typeof(DateTime))
		{
			BclHelpers.WriteTimestamp(ref state, Unsafe.As<T, DateTime>(ref value));
			return;
		}

		if (typeof(T) == typeof(TimeSpan))
		{
			BclHelpers.WriteDuration(ref state, Unsafe.As<T, TimeSpan>(ref value));
			return;
		}

		throw new NotSupportedException($"No Result<{typeof(T).Name}> wire mapping registered.");
	}
}

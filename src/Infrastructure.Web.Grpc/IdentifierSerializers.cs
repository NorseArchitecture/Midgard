using System.Runtime.CompilerServices;
using Norse.Primitives.Identifiers;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// Applies the Norse wire law to a protobuf-net <see cref="RuntimeTypeModel"/>: every member of every
/// contract type at <see cref="CompatibilityLevel.Level300"/>, and every identifier on the wire as a
/// bare <c>bytes</c> field carrying 16 bytes in RFC 9562 order — never the legacy <c>bcl.Guid</c>
/// encoding, never the 36-character string.
/// </summary>
/// <remarks>
/// The level is applied per <see cref="ValueMember"/> rather than via
/// <see cref="RuntimeTypeModel.DefaultCompatibilityLevel"/> because protobuf-net categorically refuses
/// that setter on <see cref="RuntimeTypeModel.Default"/> — and the default model is exactly where the
/// generated client/server wiring registers. Member-level configuration wins over every ambient level,
/// so the two paths are wire-identical.
/// </remarks>
public static class IdentifierSerializers
{
	static readonly ConditionalWeakTable<RuntimeTypeModel, RuntimeTypeModel> _registered = [];

	/// <summary>
	/// Registers the wire law on <paramref name="model"/>. Idempotent per model. Must run before
	/// contract types enter the model — the sweep only sees types added after registration.
	/// </summary>
	public static void Register(RuntimeTypeModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		if (!_registered.TryAdd(model, model))
			return;

		model.AfterApplyDefaultBehaviour += ApplyWireLaw;
		model.Add(typeof(SequentialGuid), applyDefaultBehaviour: false).SerializerType =
			typeof(SequentialGuidSerializer);
		model.Add(typeof(DeterministicGuid), applyDefaultBehaviour: false).SerializerType =
			typeof(DeterministicGuidSerializer);
	}

	static void ApplyWireLaw(object? sender, TypeAddedEventArgs e)
	{
		foreach (var field in e.MetaType.GetFields())
		{
			field.CompatibilityLevel = CompatibilityLevel.Level300;
			if (field.MemberType == typeof(Guid) || field.MemberType == typeof(Guid?))
				field.DataFormat = DataFormat.FixedSize;
		}
	}
}

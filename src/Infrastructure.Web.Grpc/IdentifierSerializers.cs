using System.Runtime.CompilerServices;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc;

/// <summary>
/// Applies the Norse wire law to a protobuf-net <see cref="RuntimeTypeModel"/>:
/// <see cref="CompatibilityLevel.Level300"/> as the model default, and every identifier on the wire as a
/// bare <c>bytes</c> field carrying 16 bytes in RFC 9562 order — never the legacy <c>bcl.Guid</c>
/// encoding, never the 36-character string.
/// </summary>
public static class IdentifierSerializers
{
#pragma warning disable IDE0028
	static readonly ConditionalWeakTable<RuntimeTypeModel, RuntimeTypeModel> _registered = new();
#pragma warning restore IDE0028

	/// <summary>
	/// Registers the wire law on <paramref name="model"/>. Idempotent per model. Must run before any
	/// contract type enters the model — <see cref="RuntimeTypeModel.DefaultCompatibilityLevel"/> cannot
	/// change once types have been added, and protobuf-net fails loudly if it is attempted.
	/// </summary>
	public static void Register(RuntimeTypeModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		if (!_registered.TryAdd(model, model))
			return;

		model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
		model.AfterApplyDefaultBehaviour += SweepGuidMembers;
	}

	static void SweepGuidMembers(object? sender, TypeAddedEventArgs e)
	{
		foreach (var field in e.MetaType.GetFields())
		{
			if (field.MemberType == typeof(Guid) || field.MemberType == typeof(Guid?))
				field.DataFormat = DataFormat.FixedSize;
		}
	}
}

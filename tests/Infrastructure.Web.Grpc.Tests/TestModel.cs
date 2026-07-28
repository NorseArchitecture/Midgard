using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc.Tests;

static class TestModel
{
	internal static RuntimeTypeModel Create()
	{
		var model = RuntimeTypeModel.Create();
		IdentifierSerializers.Register(model);
		return model;
	}

	internal static byte[] Serialize<T>(TypeModel model, T value)
	{
		using MemoryStream stream = new();
		model.Serialize(stream, value!);
		return stream.ToArray();
	}

	internal static T Deserialize<T>(TypeModel model, byte[] payload) =>
		(T)model.Deserialize(new MemoryStream(payload), null, typeof(T))!;
}

using ProtoBuf.Grpc.Server;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
/// Generic gRPC hosting wiring, called once by the composition root (Yggdrasil), never by a
/// realm-specific service registration — no service, including Heimdall's, knows this call happens.
/// </summary>
public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>Wires protobuf-net.Grpc's code-first hosting with the platform's <see cref="UnhandledExceptionInterceptor"/>.</summary>
		public IServiceCollection AddNorseCodeFirstGrpc()
		{
			services.AddCodeFirstGrpc(options => options.Interceptors.Add<UnhandledExceptionInterceptor>());
			return services;
		}
	}
}

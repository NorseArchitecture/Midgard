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
		/// <summary>
		/// Wires protobuf-net.Grpc code-first hosting with the platform interceptor stack (spec §2.1):
		/// UnhandledExceptionInterceptor outermost (the net), PrincipalSeedingInterceptor (channel adapter),
		/// OutcomeServerInterceptor innermost (the DU's idiom translator — Failed → throw + ErrorInfo).
		/// </summary>
		public IServiceCollection AddNorseCodeFirstGrpc()
		{
			services.AddCodeFirstGrpc(options =>
			{
				options.Interceptors.Add<UnhandledExceptionInterceptor>();
				options.Interceptors.Add<PrincipalSeedingInterceptor>();
				options.Interceptors.Add<OutcomeServerInterceptor>();
			});
			return services;
		}
	}
}

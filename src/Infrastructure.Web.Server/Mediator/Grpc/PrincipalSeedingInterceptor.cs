using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
///     The gRPC channel adapter's half of the principal contract (spec §2.4): stamps the request
///     principal into the scoped <see cref="PrincipalAccessor" /> at entry, before any pipeline code can
///     ask for it. Grpc.AspNetCore activates interceptors from the request's DI scope, so the accessor
///     this constructor receives is the same instance the behaviors resolve.
/// </summary>
sealed class PrincipalSeedingInterceptor(PrincipalAccessor accessor) : Interceptor
{
	public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
		TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
	{
		accessor.Seed(context.GetHttpContext().User);
		return continuation(request, context);
	}
}

using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>Zero domain knowledge — registered once per gRPC-hosting realm, reused verbatim by every future gRPC-hosted mediator handler.</summary>
public sealed class OutcomeServerInterceptor : Interceptor
{
	/// <summary>
	/// Runs the unary call as normal; catches <see cref="OutcomeFailedException"/> and rethrows it as the
	/// <see cref="RpcException"/> <see cref="ProblemExtensions.ToRpcException"/> produces.
	/// </summary>
	[SuppressMessage("Trimming", "IL2026", Justification = "Problem.ToRpcException requires JSON serialization of the errors dictionary.")]
	[SuppressMessage("AOT", "IL3050", Justification = "Problem.ToRpcException requires JSON serialization of the errors dictionary.")]
	public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
		TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
	{
		try
		{
			return await continuation(request, context).ConfigureAwait(false);
		}
		catch (OutcomeFailedException ex)
		{
			throw ex.Problem.ToRpcException();
		}
	}
}

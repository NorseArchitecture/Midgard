#pragma warning disable IDE0005 // Using directive is unnecessary
using Grpc.Core;
using Grpc.Core.Interceptors;
using System.Runtime.CompilerServices;
#pragma warning restore IDE0005
using Norse.Abstractions.Contracts;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
/// The wire-boundary throw point for a business failure (spec §9, 2026-07-24 amendment). Every
/// Asgard-contracted service method returns <c>Outcome&lt;T&gt;</c> directly — nothing in-process
/// throws to communicate a business failure. This interceptor runs the call, inspects the returned
/// response for the <c>Failed</c> case via <see cref="IUnion"/>'s type-erased
/// <c>Value</c> escape hatch (no reflection, no knowledge of the payload type <c>T</c>), and throws
/// <see cref="ProblemExtensions.ToRpcException"/> only there. A response that isn't an
/// <see cref="IUnion"/> at all, or is the <c>Success&lt;T&gt;</c> case, passes through unchanged and
/// serializes as the bare payload — the composition root's <c>Outcome&lt;T&gt;</c> surrogate
/// registration (spec §9(c)) makes this byte-identical to the pre-envelope wire shape, so the
/// success path never carries partner-visible union structure, only the failure path adds
/// <c>google.rpc.Status</c>/<c>ErrorInfo</c> on top of what would otherwise ship anyway.
/// </summary>
sealed class OutcomeServerInterceptor : Interceptor
{
	public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
		TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
	{
		var response = await continuation(request, context).ConfigureAwait(false);
		if (response is IUnion { Value: Failed failed })
			throw failed.Problem.ToRpcException();
		return response;
	}
}

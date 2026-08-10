using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Norse.Infrastructure.Web.Client.Grpc;

/// <summary>
///     The sole inbound decoder in the land (spec §2.1): Norse clients receive failure on exactly one
///     wire — gRPC — and this interceptor translates it back into the DU. A faulted call whose response
///     type is a closed <c>Outcome&lt;T&gt;</c> has its <see cref="RpcException" /> decoded
///     (<c>ErrorInfo.Reason</c>-authoritative) and re-enveloped as <c>Failed(Problem)</c>; everything
///     else — non-Outcome responses, non-RpcException faults — passes through untouched.
/// </summary>
public sealed class OutcomeClientInterceptor : Interceptor
{
	/// <summary>
	///     Wraps a unary call's response task when <typeparamref name="TResponse" /> is a closed
	///     <c>Outcome&lt;T&gt;</c>, decoding a faulted <see cref="RpcException" /> back into
	///     <c>Failed(Problem)</c>. Non-Outcome responses pass through the original call untouched.
	/// </summary>
	[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
		Justification =
			"The returned AsyncUnaryCall's Dispose delegate is call.Dispose — ownership transfers to the caller, exactly as it would for the unwrapped call the analyzer doesn't flag.")]
	public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
		TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
		AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
	{
		var call = continuation(request, context);
		return OutcomeFactory<TResponse>.CanCreate ?
			new AsyncUnaryCall<TResponse>(Decode(call.ResponseAsync), call.ResponseHeadersAsync, call.GetStatus,
				call.GetTrailers, call.Dispose) :
			call;
	}

	/// <summary>
	///     Blocking counterpart to <see cref="AsyncUnaryCall{TRequest,TResponse}" />: when
	///     <typeparamref name="TResponse" /> is a closed <c>Outcome&lt;T&gt;</c>, a thrown
	///     <see cref="RpcException" /> decodes back into <c>Failed(Problem)</c> instead of propagating.
	/// </summary>
	public override TResponse BlockingUnaryCall<TRequest, TResponse>(
		TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
		BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
	{
		if (!OutcomeFactory<TResponse>.CanCreate)
			return continuation(request, context);

		try
		{
			return continuation(request, context);
		}
		catch (RpcException exception)
		{
			return OutcomeFactory<TResponse>.CreateErr(exception.DecodeProblem());
		}
	}

	static async Task<TResponse> Decode<TResponse>(Task<TResponse> response)
	{
		try
		{
			return await response.ConfigureAwait(false);
		}
		catch (RpcException exception)
		{
			return OutcomeFactory<TResponse>.CreateErr(exception.DecodeProblem());
		}
	}
}

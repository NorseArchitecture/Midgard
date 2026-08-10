using Grpc.Core;
using Grpc.Core.Interceptors;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Client.Grpc;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Infrastructure.Web.Client.Tests.Grpc;

public sealed class OutcomeClientInterceptorTests
{
	static ClientInterceptorContext<TRequest, TResponse> CreateContext<TRequest, TResponse>()
		where TRequest : class
		where TResponse : class
	{
		var requestMarshaller = Marshallers.Create<TRequest>(_ => [], _ => default!);
		var responseMarshaller = Marshallers.Create<TResponse>(_ => [], _ => default!);
		Method<TRequest, TResponse> method = new(MethodType.Unary, "Test", "Method", requestMarshaller,
			responseMarshaller);
		return new ClientInterceptorContext<TRequest, TResponse>(method, "localhost", new CallOptions());
	}

	static AsyncUnaryCall<TResponse> CreateCall<TResponse>(Task<TResponse> responseAsync) =>
		new(responseAsync, Task.FromResult<Metadata>([]), () => new Status(StatusCode.OK, string.Empty), () => [],
			() => { });

	[Fact]
	async Task Decodes_a_thrown_RpcException_into_a_Failed_outcome()
	{
		OutcomeClientInterceptor interceptor = new();
		var rpcException = new Problem { Category = ErrorCategory.LockedOut }.ToRpcException();
		var context = CreateContext<string, Outcome<BoolResponse>>();

		var call = interceptor.AsyncUnaryCall(
			"request",
			context,
			(_, _) => CreateCall(Task.FromException<Outcome<BoolResponse>>(rpcException)));

		var outcome = await call.ResponseAsync;

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.LockedOut);
	}

	[Fact]
	async Task Passes_success_responses_through_untouched()
	{
		OutcomeClientInterceptor interceptor = new();
		var ok = Outcome<BoolResponse>.Ok(new BoolResponse { Value = true });
		var context = CreateContext<string, Outcome<BoolResponse>>();

		var call = interceptor.AsyncUnaryCall(
			"request",
			context,
			(_, _) => CreateCall(Task.FromResult(ok)));

		var outcome = await call.ResponseAsync;

		outcome.ShouldBeSameAs(ok);
	}

	[Fact]
	async Task Propagates_non_rpc_exceptions()
	{
		OutcomeClientInterceptor interceptor = new();
		InvalidOperationException exception = new("boom");
		var context = CreateContext<string, Outcome<BoolResponse>>();

		var call = interceptor.AsyncUnaryCall(
			"request",
			context,
			(_, _) => CreateCall(Task.FromException<Outcome<BoolResponse>>(exception)));

		var thrown = await Should.ThrowAsync<InvalidOperationException>(async () => await call.ResponseAsync);

		thrown.ShouldBeSameAs(exception);
	}

	[Fact]
	void Passes_through_non_outcome_response_types_unchanged()
	{
		OutcomeClientInterceptor interceptor = new();
		var context = CreateContext<string, string>();
		using var originalCall = CreateCall(Task.FromResult("hello"));

		var call = interceptor.AsyncUnaryCall(
			"request",
			context,
			(_, _) => originalCall);

		call.ShouldBeSameAs(originalCall);
	}
}

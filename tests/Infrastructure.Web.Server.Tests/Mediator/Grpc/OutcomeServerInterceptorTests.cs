using Grpc.Core;
using Grpc.Core.Testing;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class OutcomeServerInterceptorTests
{
	static ServerCallContext CreateContext() =>
		TestServerCallContext.Create(
			method: "/Test/Method",
			host: "localhost",
			deadline: DateTime.MaxValue,
			requestHeaders: [],
			cancellationToken: CancellationToken.None,
			peer: "127.0.0.1:5000",
			authContext: null,
			contextPropagationToken: null,
			writeHeadersFunc: null,
			writeOptionsGetter: null,
			writeOptionsSetter: null);

	[Fact]
	async Task Failed_BecomesRpcException_WithCategoryFidelity()
	{
		OutcomeServerInterceptor interceptor = new();
		var outcome = Outcome<BoolResponse>.Err(ErrorCategory.LockedOut);

		var exception = await Should.ThrowAsync<RpcException>(async () =>
			await interceptor.UnaryServerHandler<string, Outcome<BoolResponse>>(
				"request",
				CreateContext(),
				(_, _) => Task.FromResult(outcome)).ConfigureAwait(false));

		exception.StatusCode.ShouldBe(StatusCode.PermissionDenied);
		exception.Status.Detail.ShouldBe(nameof(ErrorCategory.LockedOut));
	}

	[Fact]
	async Task Success_PassesThroughUnchanged()
	{
		OutcomeServerInterceptor interceptor = new();
		var outcome = Outcome<BoolResponse>.Ok(new BoolResponse { Value = true });

		var response = await interceptor.UnaryServerHandler<string, Outcome<BoolResponse>>(
			"request",
			CreateContext(),
			(_, _) => Task.FromResult(outcome));

		response.ShouldBeSameAs(outcome);
	}

	[Fact]
	async Task NonOutcomeResponse_PassesThroughUnchanged()
	{
		OutcomeServerInterceptor interceptor = new();
		BoolResponse response = new() { Value = true };

		var result = await interceptor.UnaryServerHandler<string, BoolResponse>(
			"request",
			CreateContext(),
			(_, _) => Task.FromResult(response));

		result.ShouldBeSameAs(response);
	}
}

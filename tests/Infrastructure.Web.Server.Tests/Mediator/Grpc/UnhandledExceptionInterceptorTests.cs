#pragma warning disable IDE0005 // Using directive is unnecessary
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;
using Shouldly;
#pragma warning restore IDE0005

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class UnhandledExceptionInterceptorTests
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
	async Task UnhandledException_BecomesInternalRpcException_WithErrorInfoFault()
	{
		var interceptor = new UnhandledExceptionInterceptor(NullLogger<UnhandledExceptionInterceptor>.Instance);

		var exception = await Should.ThrowAsync<RpcException>(async () =>
			await interceptor.UnaryServerHandler<string, object>(
				"request",
				CreateContext(),
				(_, _) => throw new InvalidOperationException("unexpected")).ConfigureAwait(false));

		exception.StatusCode.ShouldBe(StatusCode.Internal);
	}

	[Fact]
	async Task AlreadyWellFormedRpcException_PassesThroughUnchanged()
	{
		var interceptor = new UnhandledExceptionInterceptor(NullLogger<UnhandledExceptionInterceptor>.Instance);
		var original = new RpcException(new Status(StatusCode.NotFound, "not found"));

		var exception = await Should.ThrowAsync<RpcException>(async () =>
			await interceptor.UnaryServerHandler<string, object>(
				"request",
				CreateContext(),
				(_, _) => throw original).ConfigureAwait(false));

		exception.ShouldBeSameAs(original);
	}
}

#pragma warning disable IDE0005 // Using directive is unnecessary
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.Logging;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;
using Shouldly;
#pragma warning restore IDE0005

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

sealed class TestLogger : ILogger<UnhandledExceptionInterceptor>
{
	public int ErrorLogCount { get; private set; }

	IDisposable? ILogger.BeginScope<TState>(TState state) => null;
	bool ILogger.IsEnabled(LogLevel logLevel) => true;

	void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		if (logLevel == LogLevel.Error)
			ErrorLogCount++;
	}
}

public sealed class UnhandledExceptionInterceptorTests
{
	static ServerCallContext CreateContext(CancellationToken cancellationToken = default) =>
		TestServerCallContext.Create(
			method: "/Test/Method",
			host: "localhost",
			deadline: DateTime.MaxValue,
			requestHeaders: [],
			cancellationToken: cancellationToken,
			peer: "127.0.0.1:5000",
			authContext: null,
			contextPropagationToken: null,
			writeHeadersFunc: null,
			writeOptionsGetter: null,
			writeOptionsSetter: null);

	[Fact]
	async Task UnhandledException_BecomesInternalRpcException_WithErrorInfoFault()
	{
		var logger = new TestLogger();
		var interceptor = new UnhandledExceptionInterceptor(logger);

		var exception = await Should.ThrowAsync<RpcException>(async () =>
			await interceptor.UnaryServerHandler<string, object>(
				"request",
				CreateContext(),
				(_, _) => throw new InvalidOperationException("unexpected")).ConfigureAwait(false));

		exception.StatusCode.ShouldBe(StatusCode.Internal);
		logger.ErrorLogCount.ShouldBe(1);
	}

	[Fact]
	async Task AlreadyWellFormedRpcException_PassesThroughUnchanged()
	{
		var logger = new TestLogger();
		var interceptor = new UnhandledExceptionInterceptor(logger);
		var original = new RpcException(new Status(StatusCode.NotFound, "not found"));

		var exception = await Should.ThrowAsync<RpcException>(async () =>
			await interceptor.UnaryServerHandler<string, object>(
				"request",
				CreateContext(),
				(_, _) => throw original).ConfigureAwait(false));

		exception.ShouldBeSameAs(original);
	}

	[Fact]
	async Task OperationCanceledOnCallerCancelledToken_PassesThroughUnchanged()
	{
		var logger = new TestLogger();
		var interceptor = new UnhandledExceptionInterceptor(logger);
		using var cts = new CancellationTokenSource();
#pragma warning disable CA1849
		cts.Cancel();
#pragma warning restore CA1849

		var exception = await Should.ThrowAsync<OperationCanceledException>(async () =>
			await interceptor.UnaryServerHandler<string, object>(
				"request",
				CreateContext(cts.Token),
				(_, _) => throw new OperationCanceledException(cts.Token)).ConfigureAwait(false));

		exception.CancellationToken.ShouldBe(cts.Token);
	}
}

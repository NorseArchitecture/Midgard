#pragma warning disable IDE0005 // Using directive is unnecessary
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
#pragma warning restore IDE0005
using Norse.Abstractions.Contracts;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
/// Generic, zero-domain-knowledge safety net registered once for every gRPC-hosted service (spec
/// §2.6). Expected business failures are already well-formed <see cref="RpcException"/>s by the time
/// they reach this interceptor — a service implementation throws <c>Problem.ToRpcException()</c>
/// directly (Task 12). This interceptor's only job is catching whatever a service implementation
/// let escape uncaught and converting it to <see cref="ErrorCategory.Fault"/>.
/// </summary>
sealed class UnhandledExceptionInterceptor(ILogger<UnhandledExceptionInterceptor> logger) : Interceptor
{
	public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
		TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
	{
		try
		{
			return await continuation(request, context).ConfigureAwait(false);
		}
		catch (RpcException)
		{
			throw;
		}
		catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			var correlationId = Guid.NewGuid();
#pragma warning disable CA1848 // Use LoggerMessage delegates
			logger.LogError(ex, "Unhandled exception in {Method}, correlation id {CorrelationId}", context.Method, correlationId);
#pragma warning restore CA1848
			throw new Problem { Category = ErrorCategory.Fault, CorrelationId = correlationId }.ToRpcException();
		}
	}
}

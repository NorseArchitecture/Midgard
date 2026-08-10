using Grpc.Core;
using Grpc.Core.Interceptors;
using Norse.Abstractions.Contracts;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
///     Generic, zero-domain-knowledge safety net registered once for every gRPC-hosted service (spec
///     §2.6), outermost in the interceptor stack wired by <c>AddNorseCodeFirstGrpc()</c>. Expected
///     business failures are already well-formed <see cref="RpcException" />s by the time they reach this
///     interceptor — a service implementation never throws to communicate one; <see cref="OutcomeServerInterceptor" />
///     is the sole business-failure throw point, translating a <c>Failed</c> <c>Outcome&lt;T&gt;</c> into
///     <c>Problem.ToRpcException()</c> further in. This interceptor's only job is catching whatever a
///     service implementation let escape uncaught and converting it to <see cref="ErrorCategory.Fault" />.
/// </summary>
sealed partial class UnhandledExceptionInterceptor(ILogger<UnhandledExceptionInterceptor> logger) : Interceptor
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
			LogUnhandledException(logger, ex, context.Method, correlationId);
			throw new Problem { Category = ErrorCategory.Fault, CorrelationId = correlationId }.ToRpcException();
		}
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception in {Method}, correlation id {CorrelationId}")]
	static partial void LogUnhandledException(ILogger logger, Exception ex, string method, Guid correlationId);
}

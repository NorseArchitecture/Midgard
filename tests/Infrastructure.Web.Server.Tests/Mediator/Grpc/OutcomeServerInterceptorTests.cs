using Grpc.Core;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;
#pragma warning disable IDE0005 // Using directive is unnecessary
using NSubstitute;
#pragma warning restore IDE0005
#pragma warning disable IDE0005 // Using directive is unnecessary
using Shouldly;
#pragma warning restore IDE0005

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class OutcomeServerInterceptorTests
{
	readonly OutcomeServerInterceptor _interceptor = new();
	readonly ServerCallContext _context = Substitute.For<ServerCallContext>();

	[Fact]
	async Task UnaryServerHandler_returns_the_continuation_result_on_success()
	{
		var result = await _interceptor.UnaryServerHandler("request", _context, (_, _) => Task.FromResult("response"));

		result.ShouldBe("response");
	}

	[Fact]
	async Task UnaryServerHandler_translates_a_thrown_OutcomeFailedException_into_an_RpcException()
	{
		static Task<string> Continuation(string request, ServerCallContext context) =>
			throw new OutcomeFailedException(new Problem { Category = ErrorCategory.LockedOut });

		var exception = await Should.ThrowAsync<RpcException>(() =>
			_interceptor.UnaryServerHandler("request", _context, Continuation));

		exception.StatusCode.ShouldBe(StatusCode.PermissionDenied);
	}
}

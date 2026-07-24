#pragma warning disable IDE0005 // Using directive is unnecessary
using Norse.Infrastructure.Web.Client.Grpc;
using Shouldly;
#pragma warning restore IDE0005

namespace Norse.Infrastructure.Web.Client.Tests.Grpc;

public sealed class RpcExceptionExtensionsTests
{
	[Fact]
	void DecodeProblem_ReadsReason_NotStatusCode_DisambiguatesSharedStatus()
	{
		// Server-side ToRpcException() and client-side DecodeProblem() are the two halves of one
		// round-trip; this test exercises the client half against a hand-built trailer shaped exactly
		// like ToRpcException() produces, so LockedOut/Forbidden — same status code — decode distinctly.
		var lockedOutException = Norse.Infrastructure.Web.Server.Mediator.Grpc.ProblemExtensions.ToRpcException(
			new Norse.Abstractions.Contracts.Problem { Category = Norse.Abstractions.Contracts.ErrorCategory.LockedOut });
		var forbiddenException = Norse.Infrastructure.Web.Server.Mediator.Grpc.ProblemExtensions.ToRpcException(
			new Norse.Abstractions.Contracts.Problem { Category = Norse.Abstractions.Contracts.ErrorCategory.Forbidden });

		lockedOutException.DecodeProblem().Category.ShouldBe(Norse.Abstractions.Contracts.ErrorCategory.LockedOut);
		forbiddenException.DecodeProblem().Category.ShouldBe(Norse.Abstractions.Contracts.ErrorCategory.Forbidden);
	}
}

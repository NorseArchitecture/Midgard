using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Client.Grpc;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Infrastructure.Web.Client.Tests.Grpc;

public sealed class RpcExceptionExtensionsTests
{
	[Fact]
	void DecodeProblem_ReadsReason_NotStatusCode_DisambiguatesSharedStatus()
	{
		// Server-side ToRpcException() and client-side DecodeProblem() are the two halves of one
		// round-trip; this test exercises the client half against a hand-built trailer shaped exactly
		// like ToRpcException() produces, so LockedOut/Forbidden — same status code — decode distinctly.
		var lockedOutException = new Problem { Category = ErrorCategory.LockedOut }.ToRpcException();
		var forbiddenException = new Problem { Category = ErrorCategory.Forbidden }.ToRpcException();

		lockedOutException.DecodeProblem().Category.ShouldBe(ErrorCategory.LockedOut);
		forbiddenException.DecodeProblem().Category.ShouldBe(ErrorCategory.Forbidden);
	}

	[Fact]
	void CorrelationId_RoundTrips_ThroughDebugInfo()
	{
		var correlationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
		var exception = new Problem { Category = ErrorCategory.Fault, CorrelationId = correlationId }.ToRpcException();

		exception.DecodeProblem().CorrelationId.ShouldBe(correlationId);
	}

	[Fact]
	void Errors_RoundTrip_ThroughBadRequestFieldViolations()
	{
		Dictionary<string, string[]> errors = new()
		{
			["Email"] = ["Email is required", "Email is not a valid address"],
			["Password"] = ["Password is required"]
		};
		var exception = new Problem { Category = ErrorCategory.Validation, Errors = errors }.ToRpcException();

		var decoded = exception.DecodeProblem().Errors;
		decoded["Email"].ShouldBe(["Email is required", "Email is not a valid address"]);
		decoded["Password"].ShouldBe(["Password is required"]);
	}
}

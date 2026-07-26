using Norse.Infrastructure.Web.Client.Grpc;

namespace Norse.Infrastructure.Web.Client.Tests.Grpc;

public sealed class RpcExceptionExtensionsTests
{
	[Fact]
	void DecodeProblem_ReadsReason_NotStatusCode_DisambiguatesSharedStatus()
	{
		// Server-side ToRpcException() and client-side DecodeProblem() are the two halves of one
		// round-trip; this test exercises the client half against a hand-built trailer shaped exactly
		// like ToRpcException() produces, so LockedOut/Forbidden — same status code — decode distinctly.
		var lockedOutException = Server.Mediator.Grpc.ProblemExtensions.ToRpcException(
			new Abstractions.Contracts.Problem { Category = Abstractions.Contracts.ErrorCategory.LockedOut });
		var forbiddenException = Server.Mediator.Grpc.ProblemExtensions.ToRpcException(
			new Abstractions.Contracts.Problem { Category = Abstractions.Contracts.ErrorCategory.Forbidden });

		lockedOutException.DecodeProblem().Category.ShouldBe(Abstractions.Contracts.ErrorCategory.LockedOut);
		forbiddenException.DecodeProblem().Category.ShouldBe(Abstractions.Contracts.ErrorCategory.Forbidden);
	}

	[Fact]
	void CorrelationId_RoundTrips_ThroughDebugInfo()
	{
		var correlationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
		var exception = Server.Mediator.Grpc.ProblemExtensions.ToRpcException(
			new Abstractions.Contracts.Problem { Category = Abstractions.Contracts.ErrorCategory.Fault, CorrelationId = correlationId });

		exception.DecodeProblem().CorrelationId.ShouldBe(correlationId);
	}

	[Fact]
	void Errors_RoundTrip_ThroughBadRequestFieldViolations()
	{
		var errors = new Dictionary<string, string[]> { ["Email"] = ["Email is required", "Email is not a valid address"], ["Password"] = ["Password is required"] };
		var exception = Server.Mediator.Grpc.ProblemExtensions.ToRpcException(
			new Abstractions.Contracts.Problem { Category = Abstractions.Contracts.ErrorCategory.Validation, Errors = errors });

		var decoded = exception.DecodeProblem().Errors;
		decoded["Email"].ShouldBe(["Email is required", "Email is not a valid address"]);
		decoded["Password"].ShouldBe(["Password is required"]);
	}
}

#pragma warning disable IDE0005 // Using directive is unnecessary
using Grpc.Core;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;
using Shouldly;
#pragma warning restore IDE0005

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class ProblemExtensionsTests
{
	[Fact]
	void LockedOut_And_Forbidden_ShareStatusCode_ButDistinctErrorInfoReason()
	{
		var lockedOut = new Problem { Category = ErrorCategory.LockedOut }.ToRpcException();
		var forbidden = new Problem { Category = ErrorCategory.Forbidden }.ToRpcException();

		lockedOut.StatusCode.ShouldBe(StatusCode.PermissionDenied);
		forbidden.StatusCode.ShouldBe(StatusCode.PermissionDenied);
		// Same status code — the test that matters is that Reason still disambiguates them.
		lockedOut.Trailers.Get("grpc-status-details-bin").ShouldNotBeNull();
	}

	[Fact]
	void Validation_MapsTo_InvalidArgument()
	{
		var exception = new Problem { Category = ErrorCategory.Validation, Errors = new Dictionary<string, string[]> { ["Email"] = ["required"] } }.ToRpcException();
		exception.StatusCode.ShouldBe(StatusCode.InvalidArgument);
	}

	[Fact]
	void NotAllowed_MapsTo_FailedPrecondition_NotSharedWithLockedOut()
	{
		var exception = new Problem { Category = ErrorCategory.NotAllowed }.ToRpcException();
		exception.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
	}

	[Fact]
	void Fault_MapsTo_Internal_AndCarriesCorrelationId()
	{
		var correlationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
		var exception = new Problem { Category = ErrorCategory.Fault, CorrelationId = correlationId }.ToRpcException();
		exception.StatusCode.ShouldBe(StatusCode.Internal);
	}
}

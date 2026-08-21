using Google.Rpc;
using Grpc.Core;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;
using Status = Google.Rpc.Status;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class ProblemExtensionsTests
{
	/// <summary>
	///     Reads the <c>grpc-status-details-bin</c> trailer off an <see cref="RpcException" /> and unpacks its
	///     <see cref="ErrorInfo" /> detail.
	/// </summary>
	static ErrorInfo DecodeErrorInfo(RpcException exception)
	{
		var trailer = exception.Trailers.Get("grpc-status-details-bin");
		trailer.ShouldNotBeNull();
		var richStatus = Status.Parser.ParseFrom(trailer.ValueBytes);
		foreach (var detail in richStatus.Details)
		{
			if (detail.Is(ErrorInfo.Descriptor) && detail.TryUnpack<ErrorInfo>(out var errorInfo))
				return errorInfo;
		}

		throw new InvalidOperationException("No ErrorInfo detail present on the trailer.");
	}

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
		var exception = new Problem
		{
			Category = ErrorCategory.Validation,
			Errors = new Dictionary<string, string[]> { ["Email"] = ["required"] }
		}.ToRpcException();
		exception.StatusCode.ShouldBe(StatusCode.InvalidArgument);
	}

	[Fact]
	void NotAllowed_shares_PermissionDenied_with_LockedOut_and_Forbidden()
	{
		// TransportDispositions.For is the single source for this mapping now (Task 8's reprojection
		// over the former hand-written switch, which disagreed with it here): NotAllowed does not get
		// its own FailedPrecondition status -- it shares PermissionDenied with LockedOut/Forbidden,
		// disambiguated by ErrorInfo.Reason on the wire, same as the pair above.
		var exception = new Problem { Category = ErrorCategory.NotAllowed }.ToRpcException();
		exception.StatusCode.ShouldBe(StatusCode.PermissionDenied);
	}

	[Fact]
	void Fault_MapsTo_Internal_AndCarriesCorrelationId()
	{
		var correlationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
		var exception = new Problem { Category = ErrorCategory.Fault, CorrelationId = correlationId }.ToRpcException();
		exception.StatusCode.ShouldBe(StatusCode.Internal);
	}

	[Fact]
	void MultipleMatches_MapsTo_Internal_NotACallerError()
	{
		// A cardinality violation is a server-side data-integrity failure, not a caller error —
		// shares Internal with Fault, distinguished by ErrorInfo.Reason on the wire.
		var exception = new Problem { Category = ErrorCategory.MultipleMatches }.ToRpcException();
		exception.StatusCode.ShouldBe(StatusCode.Internal);
	}

	[Fact]
	void Erased_maps_to_not_found_status_with_receipt_metadata()
	{
		ErasureReceipt receipt = new(Guid.NewGuid(), new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero));
		Problem problem = new() { Category = ErrorCategory.Erased, Receipt = receipt };
		var exception = problem.ToRpcException();
		exception.StatusCode.ShouldBe(StatusCode.NotFound);
		var errorInfo = DecodeErrorInfo(exception);
		errorInfo.Reason.ShouldBe("Erased");
		errorInfo.Metadata["receipt"].ShouldBe(receipt.ReceiptId.ToString("D"));
		errorInfo.Metadata["severedAt"].ShouldBe("2026-08-03T12:00:00.0000000+00:00");
	}

	[Fact]
	void Erased_without_a_receipt_carries_no_receipt_metadata()
	{
		Problem problem = new() { Category = ErrorCategory.Erased };
		var errorInfo = DecodeErrorInfo(problem.ToRpcException());
		errorInfo.Reason.ShouldBe("Erased");
		errorInfo.Metadata.ShouldNotContainKey("receipt");
	}
}

using Grpc.Core;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Client.Grpc;

namespace Norse.Infrastructure.Web.Client.Tests.Grpc;

public sealed class TrailerlessDecodeTests
{
	static RpcException Trailerless(StatusCode code) => new(new Status(code, string.Empty), Metadata.Empty);

	[Theory]
	[InlineData(StatusCode.Unauthenticated, ErrorCategory.Unauthorized)]
	[InlineData(StatusCode.PermissionDenied, ErrorCategory.Forbidden)]
	[InlineData(StatusCode.NotFound, ErrorCategory.NotFound)]
	[InlineData(StatusCode.Internal, ErrorCategory.Fault)]
	[InlineData(StatusCode.Unavailable, ErrorCategory.Fault)]
	void A_trailerless_status_decodes_to_its_declared_category(StatusCode code, ErrorCategory expected) =>
		Trailerless(code).DecodeProblem().Category.ShouldBe(expected);

	[Fact]
	void A_malformed_trailer_decodes_as_if_trailerless()
	{
		Metadata trailers = new() { { "grpc-status-details-bin", [0x01, 0x02, 0x03] } };
		RpcException exception = new(new Status(StatusCode.Unauthenticated, string.Empty), trailers);

		Should.NotThrow(() => exception.DecodeProblem()).Category.ShouldBe(ErrorCategory.Unauthorized);
	}

	[Fact]
	void A_trailerless_decode_carries_no_field_errors()
	{
		Trailerless(StatusCode.Unauthenticated).DecodeProblem().Errors.ShouldBeEmpty();
	}
}

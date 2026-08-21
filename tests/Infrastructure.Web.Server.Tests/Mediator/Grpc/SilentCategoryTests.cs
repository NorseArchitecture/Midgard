using Grpc.Core;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class SilentCategoryTests
{
	static Problem Detailed(ErrorCategory category) =>
		Problem.ModelError(category, "leaked detail");

	[Theory]
	[InlineData(ErrorCategory.Unauthorized)]
	[InlineData(ErrorCategory.InvalidCredentials)]
	void Silent_categories_carry_no_status_details_trailer(ErrorCategory category)
	{
		var exception = Detailed(category).ToRpcException();

		exception.StatusCode.ShouldBe(StatusCode.Unauthenticated);
		exception.Trailers.Get("grpc-status-details-bin").ShouldBeNull();
	}

	[Fact]
	void Two_silent_categories_are_indistinguishable_on_the_wire()
	{
		var unauthorized = Detailed(ErrorCategory.Unauthorized).ToRpcException();
		var invalid = Detailed(ErrorCategory.InvalidCredentials).ToRpcException();

		invalid.StatusCode.ShouldBe(unauthorized.StatusCode);
		invalid.Status.Detail.ShouldBe(unauthorized.Status.Detail);
		invalid.Trailers.Count.ShouldBe(unauthorized.Trailers.Count);
	}

	[Fact]
	void Every_category_maps_to_its_declared_grpc_status()
	{
		foreach (var category in Enum.GetValues<ErrorCategory>().Where(c => c != ErrorCategory.Unspecified))
		{
			var expected = (StatusCode)TransportDispositions.For(category).GrpcStatus;
			Detailed(category).ToRpcException().StatusCode
				.ShouldBe(expected, $"{category} should map to {expected}");
		}
	}
}

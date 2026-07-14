using System.Text.Json;
using Grpc.Core;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class ProblemExtensionsTests
{
	[Theory]
	[InlineData(ErrorCategory.Validation, StatusCode.InvalidArgument)]
	[InlineData(ErrorCategory.Conflict, StatusCode.AlreadyExists)]
	[InlineData(ErrorCategory.LockedOut, StatusCode.PermissionDenied)]
	[InlineData(ErrorCategory.NotAllowed, StatusCode.PermissionDenied)]
	[InlineData(ErrorCategory.NotFound, StatusCode.Unknown)]
	void ToRpcException_maps_the_category_to_the_expected_status_code(ErrorCategory category, StatusCode expected)
	{
		var problem = new Problem { Category = category };

		var exception = problem.ToRpcException();

		exception.StatusCode.ShouldBe(expected);
	}

	[Fact]
	void ToRpcException_carries_the_errors_dictionary_in_the_problem_bin_trailer()
	{
		var problem = new Problem { Category = ErrorCategory.Validation, Errors = new Dictionary<string, string[]> { ["Email"] = ["required"] } };

		var exception = problem.ToRpcException();
		var trailer = exception.Trailers.Get("problem-bin");
		var decoded = JsonSerializer.Deserialize<Dictionary<string, string[]>>(trailer!.ValueBytes);

		decoded!["Email"].ShouldBe(["required"]);
	}
}

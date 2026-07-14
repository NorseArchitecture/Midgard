using System.Text.Json;
using Grpc.Core;
using Norse.Infrastructure.Web.Client.Grpc;

namespace Norse.Infrastructure.Web.Client.Tests.Grpc;

public sealed class RpcExceptionExtensionsTests
{
	[Fact]
	void DecodeProblem_returns_the_errors_from_the_problem_bin_trailer()
	{
		var errors = new Dictionary<string, string[]> { ["Email"] = ["required"] };
		var trailers = new Metadata { { "problem-bin", JsonSerializer.SerializeToUtf8Bytes(errors) } };
		var exception = new RpcException(new Status(StatusCode.InvalidArgument, "Validation"), trailers);

		var decoded = exception.DecodeProblem();

		decoded["Email"].ShouldBe(["required"]);
	}

	[Fact]
	void DecodeProblem_returns_empty_when_no_trailer_present()
	{
		var exception = new RpcException(new Status(StatusCode.Unknown, ""));

		exception.DecodeProblem().ShouldBeEmpty();
	}
}

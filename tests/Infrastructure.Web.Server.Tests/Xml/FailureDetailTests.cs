using Norse.Infrastructure.Web.Server.Xml;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

public sealed class FailureDetailTests
{
	[Fact]
	void Render_out_of_range_names_the_value_and_the_type() =>
		FailureDetail.Render(new(ParseFailure.OutOfRange, "256", "Byte"))
			.ShouldBe("value '256' is out of range for Byte");

	[Fact]
	void Render_malformed_without_detail_is_unchanged() =>
		FailureDetail.Render(new(ParseFailure.Malformed, "x", "Int32"))
			.ShouldBe("cannot parse 'x' as Int32");

	[Fact]
	void Render_empty_is_unchanged() =>
		FailureDetail.Render(new(ParseFailure.Empty, string.Empty, "Int32"))
			.ShouldBe("required value missing");
}

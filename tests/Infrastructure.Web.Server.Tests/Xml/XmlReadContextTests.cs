using Norse.Infrastructure.Web.Server.Xml;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

public sealed class XmlReadContextTests
{
	[Fact]
	void PathTo_renders_root_collection_index_and_attribute()
	{
		var ctx = new XmlReadContext();
		ctx.PushElement("Policy");
		ctx.PushItem("Coverage", 2);
		ctx.PathTo("limit").ShouldBe("Policy/Coverage[2]/@limit");
		ctx.Pop();
		ctx.CurrentPath.ShouldBe("Policy");
	}

	[Fact]
	void AddScalarFailure_formats_malformed_with_input_and_type()
	{
		var ctx = new XmlReadContext();
		ctx.PushElement("Policy");
		ctx.AddScalarFailure("limit", new Failure(ParseFailure.Malformed, "x", "Decimal"));
		ctx.Failures.ShouldHaveSingleItem().ShouldBe(
			new XmlReadFailure("Policy/@limit", "cannot parse 'x' as Decimal"));
	}
}

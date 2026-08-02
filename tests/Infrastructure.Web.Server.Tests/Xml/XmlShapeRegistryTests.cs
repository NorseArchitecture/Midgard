using System.Xml;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

public sealed class XmlShapeRegistryTests
{
	[Fact]
	void Add_then_TryGet_returns_the_registered_shape()
	{
		var registry = new XmlShapeRegistry();
		var shape = new FakeXmlShape();

		registry.Add(shape);
		var found = registry.TryGet(typeof(FakeContract), out var result);

		found.ShouldBeTrue();
		result.ShouldBeSameAs(shape);
	}

	[Fact]
	void TryGet_returns_false_for_an_unregistered_type()
	{
		var registry = new XmlShapeRegistry();

		var found = registry.TryGet(typeof(FakeContract), out var result);

		found.ShouldBeFalse();
		result.ShouldBeNull();
	}

	[Fact]
	void Add_throws_on_duplicate_ContractType()
	{
		var registry = new XmlShapeRegistry();
		registry.Add(new FakeXmlShape());

		Should.Throw<ArgumentException>(() => registry.Add(new FakeXmlShape()));
	}

	sealed class FakeContract;

	sealed class FakeXmlShape : IXmlShape<FakeContract>
	{
		public Type ContractType => typeof(FakeContract);

		public string RootName(XmlCaseStyle style) => nameof(FakeContract);

		public void WriteObject(XmlWriter writer, object value, XmlCaseStyle style) =>
			Write(writer, (FakeContract)value, style);

		public object? ReadObject(XmlReader reader, XmlCaseStyle style, XmlReadContext context) =>
			Read(reader, style, context);

		public void Write(XmlWriter writer, FakeContract value, XmlCaseStyle style)
		{
		}

		public FakeContract? Read(XmlReader reader, XmlCaseStyle style, XmlReadContext context) => new();
	}
}

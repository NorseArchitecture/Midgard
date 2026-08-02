namespace Norse.Infrastructure.Web.Server.Xml.Generator.Tests;

public sealed class NameCasingTests
{
	[Fact]
	void Apply_projects_ReadWrite_through_camelCase()
	{
		NameCasing.Apply(XmlCaseStyle.CamelCase, "ReadWrite").ShouldBe("readWrite");
	}

	[Fact]
	void Apply_projects_ReadWrite_through_PascalCase()
	{
		NameCasing.Apply(XmlCaseStyle.PascalCase, "ReadWrite").ShouldBe("ReadWrite");
	}

	[Fact]
	void Apply_projects_ReadWrite_through_snake_case()
	{
		NameCasing.Apply(XmlCaseStyle.SnakeCase, "ReadWrite").ShouldBe("read_write");
	}

	[Fact]
	void Apply_projects_ReadWrite_through_UPPERCASE()
	{
		NameCasing.Apply(XmlCaseStyle.UpperCase, "ReadWrite").ShouldBe("READWRITE");
	}

	[Fact]
	void Apply_projects_ReadWrite_through_lowercase()
	{
		NameCasing.Apply(XmlCaseStyle.LowerCase, "ReadWrite").ShouldBe("readwrite");
	}

	[Fact]
	void ApplyAll_returns_all_five_casings_ordinal_indexed_to_XmlCaseStyle()
	{
		var all = NameCasing.ApplyAll("ReadWrite");
		all[(int)XmlCaseStyle.CamelCase].ShouldBe("readWrite");
		all[(int)XmlCaseStyle.PascalCase].ShouldBe("ReadWrite");
		all[(int)XmlCaseStyle.SnakeCase].ShouldBe("read_write");
		all[(int)XmlCaseStyle.UpperCase].ShouldBe("READWRITE");
		all[(int)XmlCaseStyle.LowerCase].ShouldBe("readwrite");
	}

	[Fact]
	void Apply_projects_a_single_word_identically_in_camel_and_snake()
	{
		NameCasing.Apply(XmlCaseStyle.CamelCase, "Limit").ShouldBe("limit");
		NameCasing.Apply(XmlCaseStyle.SnakeCase, "Limit").ShouldBe("limit");
		NameCasing.Apply(XmlCaseStyle.PascalCase, "Limit").ShouldBe("Limit");
	}
}

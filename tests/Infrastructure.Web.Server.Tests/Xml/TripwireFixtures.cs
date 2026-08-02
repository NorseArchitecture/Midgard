using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Norse.Infrastructure.Web.Server.Xml;

#pragma warning disable IDE0130 // Namespace does not match folder structure — both blocks below are
// deliberate: the Facade namespace is the exact fully-qualified name XmlShapeTripwireStartupFilter
// keys on reflectively (a stand-in for Task 10's not-yet-shipped GrpcControllerBase, Asgard, a
// different repo, mirroring GeneratorTestHarness's own StubGrpcControllerBase for the same not-yet-
// real type), and the fixtures namespace is scoped one level deeper than the folder to keep these
// tripwire-only fixture types out of the ambient Xml test namespace.

namespace Norse.Abstractions.Web.Server.Facade
{
	public abstract class GrpcControllerBase : ControllerBase
	{
	}
}

namespace Norse.Infrastructure.Web.Server.Tests.Xml.TripwireFixtures
{
	public sealed class TripwireRequest
	{
		public string Value { get; init; } = "";
	}

	public sealed class TripwireResponse
	{
		public string Status { get; init; } = "";
	}

	public sealed class TripwireController : Norse.Abstractions.Web.Server.Facade.GrpcControllerBase
	{
#pragma warning disable CA1822 // ASP.NET Core actions must be instance methods — a hard framework requirement, not a design choice this analyzer's "mark as static" advice fits.
		public ActionResult<TripwireResponse> Do([FromBody] TripwireRequest request) =>
			new TripwireResponse { Status = request.Value };
#pragma warning restore CA1822
	}

	public sealed class UnregisteredPayload
	{
		public string Value { get; init; } = "";
	}

	/// <summary>Derives from plain <see cref="ControllerBase"/>, never <c>GrpcControllerBase</c> — the tripwire must never scan this.</summary>
	public sealed class PlainMvcController : ControllerBase
	{
#pragma warning disable CA1822 // See TripwireController's identical suppression above.
		public ActionResult<UnregisteredPayload> Do([FromBody] UnregisteredPayload request) =>
			request;
#pragma warning restore CA1822
	}

	/// <summary>A hand-written stand-in shape — the tripwire only ever calls <see cref="XmlShapeRegistry.TryGet"/>, never <c>Write</c>/<c>Read</c>, so no real XML behavior is needed here.</summary>
	sealed class FakeXmlShape<T> : IXmlShape<T> where T : class
	{
		public Type ContractType =>
			typeof(T);

		public string RootName(XmlCaseStyle style) =>
			typeof(T).Name;

		public void WriteObject(XmlWriter writer, object value, XmlCaseStyle style)
		{
		}

		public object? ReadObject(XmlReader reader, XmlCaseStyle style, XmlReadContext context) =>
			null;

		public void Write(XmlWriter writer, T value, XmlCaseStyle style)
		{
		}

		public T? Read(XmlReader reader, XmlCaseStyle style, XmlReadContext context) =>
			null;
	}
}

#pragma warning restore IDE0130

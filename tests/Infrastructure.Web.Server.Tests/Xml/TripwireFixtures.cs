using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Norse.Abstractions.Web.Server.Facade;
using Norse.Infrastructure.Web.Server.Xml;

#pragma warning disable IDE0130 // Namespace does not match folder structure — deliberate: scoped one
// level deeper than the folder to keep these tripwire-only fixture types out of the ambient Xml test
// namespace. This file used to also declare a stand-in `Norse.Abstractions.Web.Server.Facade.GrpcControllerBase`
// stub here (Task 10 was not yet shipped when Task 9 landed) — Task 10 now ships the real type in
// Asgard, so the stub is gone; `GrpcControllerBase` below resolves to the real one via the ordinary
// project reference.

namespace Norse.Infrastructure.Web.Server.Tests.Xml.TripwireFixtures;

public sealed class TripwireRequest
{
	public string Value { get; init; } = "";
}

public sealed class TripwireResponse
{
	public string Status { get; init; } = "";
}

public sealed class TripwireController : GrpcControllerBase
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

/// <summary>The implicit-body-binding payload: no <c>[FromBody]</c>, but it's the action's only parameter and a non-scalar type — the same convention <c>ClosureWalker</c> treats as body-bound (spec §4.1) that <see cref="XmlShapeTripwireStartupFilter"/> must catch too.</summary>
public sealed class ImplicitBodyPayload
{
	public string Value { get; init; } = "";
}

public sealed class ImplicitBodyController : GrpcControllerBase
{
#pragma warning disable CA1822 // See TripwireController's identical suppression above.
	public ActionResult<TripwireResponse> Do(ImplicitBodyPayload payload) =>
		new TripwireResponse { Status = payload.Value };
#pragma warning restore CA1822
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

#pragma warning restore IDE0130

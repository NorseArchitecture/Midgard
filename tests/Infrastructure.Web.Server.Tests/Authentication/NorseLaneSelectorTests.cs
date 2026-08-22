using System.Reflection;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Norse.Abstractions.Components.Authorization;
using Norse.Abstractions.Web.Server.Facade;
using Norse.Infrastructure.Web.Server.Authentication;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

public sealed class NorseLaneSelectorTests
{
	[Fact]
	void A_facade_endpoint_selects_the_machine_lane() =>
		NorseLaneSelector.Select(EndpointFactory.Facade()).ShouldBe(NorseSchemes.Machine);

	[Fact]
	void A_facade_endpoint_selects_the_machine_lane_even_with_cookies_present() =>
		NorseLaneSelector.Select(EndpointFactory.Facade()).ShouldBe(NorseSchemes.Machine);

	[Fact]
	void A_grpc_endpoint_selects_the_identity_cookie_only_lane() =>
		NorseLaneSelector.Select(EndpointFactory.Grpc()).ShouldBe(NorseSchemes.IdentityCookieOnly);

	[Fact]
	void A_probe_endpoint_selects_the_probe_lane() =>
		NorseLaneSelector.Select(EndpointFactory.Probe()).ShouldBe(NorseSchemes.Probe);

	[Fact]
	void A_probe_endpoint_never_falls_through_to_the_browser_lane() =>
		NorseLaneSelector.Select(EndpointFactory.Probe()).ShouldNotBe(NorseSchemes.Browser);

	[Fact]
	void A_razor_endpoint_selects_the_browser_lane() =>
		NorseLaneSelector.Select(EndpointFactory.Razor()).ShouldBe(NorseSchemes.Browser);

	[Fact]
	void An_endpointless_request_selects_the_browser_lane() =>
		NorseLaneSelector.Select(endpoint: null).ShouldBe(NorseSchemes.Browser);
}

/// <summary>
///     Builds bare <see cref="Endpoint" /> instances carrying exactly the metadata each lane row of
///     <see cref="NorseLaneSelector.Select" /> matches on — no request delegate is ever invoked, so a
///     no-op one satisfies the constructor.
/// </summary>
static class EndpointFactory
{
	/// <summary>A facade action: metadata carries a <see cref="ControllerActionDescriptor" /> whose controller descends from <see cref="GrpcControllerBase" />.</summary>
	public static Endpoint Facade()
	{
		ControllerActionDescriptor descriptor = new() { ControllerTypeInfo = typeof(FacadeStubController).GetTypeInfo() };
		return new Endpoint(NoOp, new EndpointMetadataCollection(descriptor), displayName: "facade");
	}

	/// <summary>A code-first gRPC endpoint: metadata carries <see cref="GrpcMethodMetadata" />, Task 0's discovered marker.</summary>
	public static Endpoint Grpc()
	{
		Method<byte[], byte[]> method = new(MethodType.Unary, "norse.probe.v1.ProbeService", "Invoke",
			Marshallers.Create<byte[]>(static bytes => bytes, static bytes => bytes),
			Marshallers.Create<byte[]>(static bytes => bytes, static bytes => bytes));
		GrpcMethodMetadata metadata = new(typeof(object), method);
		return new Endpoint(NoOp, new EndpointMetadataCollection(metadata), displayName: "grpc");
	}

	/// <summary>An orchestrator-probe endpoint: metadata carries an <see cref="IAuthorizeData" /> whose <see cref="IAuthorizeData.Policy" /> is <see cref="NorsePolicies.Probe" />.</summary>
	public static Endpoint Probe()
	{
		AuthorizeAttribute authorize = new() { Policy = NorsePolicies.Probe };
		return new Endpoint(NoOp, new EndpointMetadataCollection(authorize), displayName: "probe");
	}

	/// <summary>A Razor-shaped GET: carries none of the facade/gRPC/probe markers.</summary>
	public static Endpoint Razor() =>
		new(NoOp, new EndpointMetadataCollection(), displayName: "razor");

	static Task NoOp(HttpContext context) => Task.CompletedTask;

	sealed class FacadeStubController : GrpcControllerBase
	{
	}
}

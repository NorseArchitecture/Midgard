using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norse.Abstractions.Components.Authorization;
using Norse.Abstractions.Web.Server.Facade;
using Norse.Infrastructure.Web.Server.Authentication;
using OpenIddict.Validation.AspNetCore;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Grpc.Server;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

/// <summary>
///     A <see cref="TestServer" /> fixture wiring <see cref="AuthenticationBuilderExtensions.AddNorseAuthentication" />
///     against three endpoint shapes — a code-first gRPC probe service (<see cref="ProbeGrpcService" />), a
///     <see cref="GrpcControllerBase" />-descendant facade controller (<see cref="LaneFacadeController" />),
///     and a Razor-shaped GET (<c>"/"</c>) — plus the two orchestrator-probe endpoints
///     (<c>"/alive"</c>/<c>"/health"</c>) that pin blocker 2's regression. The <see cref="IdentityConstants.ApplicationScheme" />
///     cookie handler is registered with its defaults (never exercised by any credentialless request below)
///     purely so <see cref="NorseSchemes.IdentityCookieOnly" />'s unconditional <c>ForwardAuthenticate</c>
///     resolves to a real handler instead of throwing a handler-lookup exception. <see cref="NorseSchemes.Machine" />'s
///     forward target, <see cref="OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme" />, gets the same
///     treatment via <see cref="MachineStandInHandler" /> — a real OpenIddict validation registration is out of
///     scope for this fixture, so this test-only stand-in reproduces the pre-Himinbjorg#49 rejection behavior
///     for authenticate/forbid, purely so the forward resolves instead of throwing; challenge branches on
///     whether authenticate already ran, modeling the real Authenticate-before-Challenge asymmetry this
///     fixture pins (see <see cref="MachineStandInHandler" />'s own doc comment). <see cref="NorsePolicies.Machine" /> is
///     registered alongside <see cref="NorsePolicies.Probe" /> for the same class of reason on the
///     authorization side: <see cref="GrpcControllerBase" />'s class-level <c>[Authorize(Policy = ...)]</c>
///     names it, and an unregistered named policy throws at request time rather than the clean 401 below —
///     a pre-existing gap in this fixture, independent of and predating the Machine-scheme bridge.
/// </summary>
sealed class LaneHost : IDisposable
{
	readonly WebApplication _app;
	readonly FacadeInvocationCounter _facadeInvocations;

	LaneHost(WebApplication app, FacadeInvocationCounter facadeInvocations, HttpClient client)
	{
		_app = app;
		_facadeInvocations = facadeInvocations;
		Client = client;
	}

	/// <summary>A plain <see cref="HttpClient" /> against the live host, defaulted to HTTP/2 so the code-first gRPC probe route negotiates cleanly.</summary>
	public HttpClient Client { get; }

	/// <summary>How many times <see cref="LaneFacadeController.Get" /> actually ran — asserts rejection happened before the action, not merely that the status code looks right.</summary>
	public int FacadeActionInvocations => _facadeInvocations.Count;

	public static async Task<LaneHost> StartAsync()
	{
		// TestServer's in-memory handler has no TLS to negotiate -- mirrors SwoopHostFixture/
		// WirePathAuthorizationTests' identical opt-in for a plain "http://" gRPC call.
		AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();

		FacadeInvocationCounter facadeInvocations = new();
		builder.Services.AddSingleton(facadeInvocations);

		builder.Services.AddNorseAuthentication()
			.AddCookie(IdentityConstants.ApplicationScheme)
			.AddScheme<AuthenticationSchemeOptions, MachineStandInHandler>(
				OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, null);
		builder.Services.AddAuthorizationBuilder()
			.AddPolicy(NorsePolicies.Probe, NorsePlatformPolicies.Probe)
			.AddPolicy(NorsePolicies.Machine, NorsePlatformPolicies.Machine);

		// The default ApplicationPartManager scans the entire entry assembly for controllers -- under
		// Microsoft.Testing.Platform that IS this test assembly, so plain AddControllers() would also pick
		// up Xml/TripwireFixtures.cs's deliberately unrouted test-local controllers and fail application
		// model creation before this fixture ever starts. Scoping to exactly the one facade controller this
		// fixture maps keeps that fixture's own tests independent of it. The direction runs both ways:
		// Xml/AddNorseXmlTests.cs's own hosts add the whole assembly as an ApplicationPart too
		// (AddApplicationPart(typeof(TripwireController).Assembly)), which would otherwise also discover
		// LaneFacadeController and fail on its unregistered-there ProbeIdResponse shape. Swapping in a
		// feature provider that recognizes only this one type -- instead of the default convention (public,
		// name/attribute-based) -- lets LaneFacadeController stay non-public (house style) and keeps it
		// invisible to every other host's default-provider, whole-assembly scan.
		builder.Services.AddControllers()
			.ConfigureApplicationPartManager(manager =>
			{
				manager.ApplicationParts.Clear();
				manager.ApplicationParts.Add(new SingleControllerApplicationPart(typeof(LaneFacadeController)));
				foreach (var stale in manager.FeatureProviders.OfType<ControllerFeatureProvider>().ToArray())
					manager.FeatureProviders.Remove(stale);
				manager.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(LaneFacadeController)));
			});
		builder.Services.AddCodeFirstGrpc();

		var app = builder.Build();
		app.UseRouting();
		app.UseAuthentication();
		app.UseAuthorization();

		app.MapControllers();
		app.MapGrpcService<ProbeGrpcService>();
		app.MapGet("/", () => Results.Ok());
		app.MapGet("/alive", () => Results.Ok()).RequireAuthorization(NorsePolicies.Probe);
		app.MapGet("/health", () => Results.Ok()).RequireAuthorization(NorsePolicies.Probe);

		await app.StartAsync().ConfigureAwait(false);

		var client = app.GetTestServer().CreateClient();
		client.DefaultRequestVersion = HttpVersion.Version20;
		client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

		return new LaneHost(app, facadeInvocations, client);
	}

	/// <summary>A well-formed, zero-length unary gRPC request body: a 5-byte frame (no compression, zero-length message) -- enough for routing/authentication to run without a real serialized message.</summary>
	public static HttpContent EmptyGrpcBody()
	{
		ByteArrayContent content = new([0, 0, 0, 0, 0]);
		content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
		return content;
	}

	public void Dispose()
	{
		Client.Dispose();
		_app.StopAsync().GetAwaiter().GetResult();
		_app.DisposeAsync().AsTask().GetAwaiter().GetResult();
	}
}

/// <summary>
///     A test-only stand-in for <see cref="OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme" /> --
///     <see cref="LaneHost" /> composes <see cref="AuthenticationBuilderExtensions.AddNorseAuthentication" /> for
///     real but never registers a real OpenIddict validation handler (out of scope for this fixture), so
///     <see cref="NorseSchemes.Machine" />'s forward needs *something* registered under that exact scheme name
///     or it throws a handler-lookup exception rather than the clean 401 a credentialless facade caller must
///     get. Reproduces the deleted <c>NorseMachineRejectionHandler</c>'s behavior for authenticate (nothing,
///     ever) and forbid (always 403, silently, never a cookie) exactly. Challenge is the one place this stand-in
///     deliberately diverges from that deleted handler and instead models real OpenIddict's own asymmetry
///     (decompiled <c>OpenIddict.Validation.AspNetCore</c> 7.6.0, Himinbjorg#49): Challenge only resolves to
///     the specific "missing_token" -&gt; 401 outcome when <em>this scheme's own</em>
///     <see cref="HandleAuthenticateAsync" /> already ran earlier in the same request and stashed that
///     transaction; called cold, with no prior Authenticate pass on this scheme, it falls back to the generic
///     "insufficient_access" default, which maps to 403. The <c>_authenticateRan</c> field is safe as
///     per-request state -- ASP.NET Core's <c>AuthenticationHandlerProvider</c> is a scoped service that
///     caches one handler instance per scheme for the lifetime of a single request, never reused across
///     requests.
/// </summary>
sealed class MachineStandInHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	bool _authenticateRan;

	protected override Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		_authenticateRan = true;
		return Task.FromResult(AuthenticateResult.NoResult());
	}

	protected override Task HandleChallengeAsync(AuthenticationProperties properties)
	{
		Response.StatusCode = _authenticateRan ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden;
		Response.ContentLength = 0;
		return Task.CompletedTask;
	}

	protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
	{
		Response.StatusCode = StatusCodes.Status403Forbidden;
		Response.ContentLength = 0;
		return Task.CompletedTask;
	}
}

/// <summary>An <see cref="ApplicationPart" /> exposing exactly one controller type -- see <see cref="LaneHost.StartAsync" />'s remarks on why default assembly-wide scanning is unsafe here.</summary>
sealed class SingleControllerApplicationPart(Type controllerType) : ApplicationPart, IApplicationPartTypeProvider
{
	public override string Name { get; } = controllerType.FullName ?? controllerType.Name;

	public IEnumerable<TypeInfo> Types { get; } = [controllerType.GetTypeInfo()];
}

/// <summary>
///     Recognizes exactly one controller type regardless of the default convention (public, name/attribute
///     based) -- paired with <see cref="SingleControllerApplicationPart" />, which already exposes only
///     that one type, so this provider's own type check is defense in depth rather than load-bearing.
///     Lets <see cref="LaneFacadeController" /> stay non-public (house style) while remaining invisible to
///     every other host in this assembly that still uses the default <see cref="ControllerFeatureProvider" />
///     over a whole-assembly <c>ApplicationPart</c> (see <see cref="LaneHost.StartAsync" />'s remarks).
/// </summary>
sealed class SingleControllerFeatureProvider(Type controllerType) : ControllerFeatureProvider
{
	protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == controllerType;
}

/// <summary>A DI singleton counter <see cref="LaneFacadeController" /> increments, so <see cref="LaneHost.FacadeActionInvocations" /> can assert directly on whether the action ran.</summary>
sealed class FacadeInvocationCounter
{
	int _count;

	public int Count => _count;

	public void Increment() => Interlocked.Increment(ref _count);
}

/// <summary>
///     The facade lane's probe: a hand-authored <see cref="GrpcControllerBase" /> descendant (Futhark spec
///     §4 shape), exactly what <see cref="NorseLaneSelector.Select" /> matches for the machine lane.
/// </summary>
[Route("api/probe")]
sealed class LaneFacadeController(FacadeInvocationCounter invocations) : GrpcControllerBase
{
	[HttpGet("{id:int}")]
	[Authorize]
	public ActionResult<ProbeIdResponse> Get(int id)
	{
		invocations.Increment();
		return Ok(new ProbeIdResponse { Id = id });
	}
}

/// <summary>
///     <see cref="LaneFacadeController" />'s response shape. A bare <c>int</c> return trips
///     <see cref="Norse.Infrastructure.Web.Server.Xml.XmlShapeTripwireStartupFilter" /> in any other test in this assembly whose own host
///     scans the default (unscoped) <c>ApplicationPartManager</c> and so also discovers this controller —
///     the tripwire's request-parameter check skips the closed scalar taxonomy, but its response-payload
///     check does not, matching this platform's own convention that a facade response is always a
///     contract-shaped type, never a bare scalar. A plain POCO — no <c>[DataContract]</c> needed, mirroring
///     <c>Xml/TripwireFixtures.cs</c>'s <c>TripwireResponse</c> — gets a shape generated for it like any
///     other host-compilation facade type.
/// </summary>
sealed class ProbeIdResponse
{
	public int Id { get; init; }
}

// ProtoBuf.Grpc.Configuration.Service, not System.ServiceModel.ServiceContract -- mirrors
// GrpcEndpointMetadataDiscoveryTests' identical choice: protobuf-net.Grpc's ServiceBinder treats the two
// identically, and this one needs no extra package reference.
[Service("probe.ProbeService")]
// PBN2008: the analyzer can't verify a unary CallContext-based method shape on this SDK/TFM (same false
// positive already suppressed on IProbeService in Infrastructure.Web.Client.Tests/Grpc/GrpcWebRoundTripTests.cs
// and GrpcEndpointMetadataDiscoveryTests) -- this endpoint round-trips at runtime, which is all this fixture cares about.
#pragma warning disable PBN2008
interface IProbeGrpcService
{
	Task<PingResponse> PingAsync(PingRequest request, CallContext context = default);
}
#pragma warning restore PBN2008

[DataContract]
sealed class PingRequest
{
	[DataMember(Order = 1)] public string Value { get; set; } = string.Empty;
}

[DataContract]
sealed class PingResponse
{
	[DataMember(Order = 1)] public string Value { get; set; } = string.Empty;
}

/// <summary>
///     The gRPC lane's probe: a code-first protobuf-net.Grpc service, exactly what
///     <see cref="NorseLaneSelector.Select" /> matches for the identity-cookie-only lane. Carries
///     <see cref="NorsePolicies.Machine" /> the same way a real production service does (mirroring
///     <see cref="GrpcControllerBase" />'s class-level attribute on the facade lane) -- without an
///     authorization requirement here, a credential-less call never reaches Challenge/Forbid at all,
///     which is why the original fixture's total absence of one let the Challenge/Forbid status-code
///     bug (Himinbjorg#49) go unnoticed.
/// </summary>
[Authorize(Policy = NorsePolicies.Machine)]
sealed class ProbeGrpcService : IProbeGrpcService
{
	public Task<PingResponse> PingAsync(PingRequest request, CallContext context = default) =>
		Task.FromResult(new PingResponse());
}

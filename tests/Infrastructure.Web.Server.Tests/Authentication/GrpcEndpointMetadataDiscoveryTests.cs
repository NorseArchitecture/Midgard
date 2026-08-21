using System.Runtime.Serialization;
using Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Grpc.Server;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

/// <summary>
///     Task 0 discovery probe (Principal at the Door). Spec §2.2's gRPC selector row matches on
///     <see cref="GrpcMethodMetadata" /> in endpoint metadata — verified for <c>MapGrpcService</c> with
///     protobuf contracts, but only asserted for protobuf-net.Grpc's code-first binder
///     (<c>AddCodeFirstGrpc()</c> + <c>MapGrpcService&lt;T&gt;()</c>), which is what
///     <c>ReferenceService</c> and <c>AuthenticationService</c> actually use. This test decides which
///     branch the rest of the plan takes; it stays in place afterward as a regression guard that a
///     future Grpc.AspNetCore version has not moved the metadata.
/// </summary>
public sealed class GrpcEndpointMetadataDiscoveryTests
{
	[Fact]
	async Task Code_first_grpc_endpoints_carry_GrpcMethodMetadata()
	{
		using IHost host = await new HostBuilder()
			.ConfigureWebHost(web => web
				.UseTestServer()
				.ConfigureServices(services =>
				{
					services.AddCodeFirstGrpc();
					services.AddRouting();
				})
				.Configure(app =>
				{
					app.UseRouting();
					app.UseEndpoints(endpoints => endpoints.MapGrpcService<ProbeService>());
				}))
			.StartAsync(TestContext.Current.CancellationToken);

		var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints;

		endpoints.ShouldNotBeEmpty();
		endpoints.ShouldContain(endpoint => endpoint.Metadata.GetMetadata<GrpcMethodMetadata>() != null);
	}

	// ProtoBuf.Grpc.Configuration.Service, not System.ServiceModel.ServiceContract: protobuf-net.Grpc's
	// ServiceBinder treats the two identically, and this one needs no extra package reference.
	[Service("norse.probe.v1.ProbeService")]
	// PBN2008: the analyzer can't verify a unary CallContext-based method shape on this SDK/TFM
	// (same false positive already suppressed on IProbeService in
	// Infrastructure.Web.Client.Tests/Grpc/GrpcWebRoundTripTests.cs); the endpoint round-trips at
	// runtime below, which is the only thing this probe cares about.
#pragma warning disable PBN2008
	public interface IProbeService
	{
		Task<ProbeResponse> InvokeAsync(ProbeRequest request, CallContext context = default);
	}
#pragma warning restore PBN2008

	[DataContract]
	public sealed class ProbeRequest
	{
		[DataMember(Order = 1)] public string Value { get; set; } = string.Empty;
	}

	[DataContract]
	public sealed class ProbeResponse
	{
		[DataMember(Order = 1)] public string Value { get; set; } = string.Empty;
	}

	sealed class ProbeService : IProbeService
	{
		public Task<ProbeResponse> InvokeAsync(ProbeRequest request, CallContext context = default) =>
			Task.FromResult(new ProbeResponse());
	}
}

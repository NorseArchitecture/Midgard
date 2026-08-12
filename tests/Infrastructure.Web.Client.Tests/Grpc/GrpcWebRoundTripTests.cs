using System.Runtime.Serialization;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Client.Grpc;
using Norse.Infrastructure.Web.Grpc;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;
using Norse.Primitives;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Grpc.Server;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Client.Tests.Grpc;

/// <summary>
///     The wire shapes every other test in this folder hand-assembles, produced by the real
///     <c>Grpc.AspNetCore.Server</c> + <c>Grpc.AspNetCore.Web</c> stack instead. That gap is what let a
///     <c>Failed(Problem)</c> decode as <see cref="ErrorCategory.Fault" /> on a live browser while every
///     hand-assembled unit test stayed green: the invoker's trailers-only branch was exercised only
///     against a response nobody had checked against a real server's bytes.
/// </summary>
[Collection(nameof(GrpcWebRoundTripTests))]
[CollectionDefinition(nameof(GrpcWebRoundTripTests), DisableParallelization = true)]
public sealed class GrpcWebRoundTripTests
{
	// The same shape the generated AddNorseGrpcClients/MapNorseGrpcServices wiring emits, guard and all.
	static void RegisterSurrogates() =>
		RuntimeTypeModel.Default.EnsureRegistered(typeof(GrpcWebRoundTripTests),
			static () =>
			{
				var model = RuntimeTypeModel.Default;
				IdentifierSerializers.Register(model);
				if (!model.IsDefined(typeof(Outcome<ProbeResponse>)))
					model.Add(typeof(Outcome<ProbeResponse>), applyDefaultBehaviour: false)
						.SetSurrogate(typeof(ProbeResponse));
			});

	static WebApplication BuildHost()
	{
		RegisterSurrogates();
		var builder = WebApplication.CreateSlimBuilder();
		// Kestrel/routing/gRPC request logging would otherwise bury the test runner's own output.
		builder.Logging.ClearProviders();
		builder.WebHost.UseTestServer();
		builder.Services.AddCodeFirstGrpc();
		var app = builder.Build();
		app.UseGrpcWeb();
		app.MapGrpcService<ProbeService>().EnableGrpcWeb();
		return app;
	}

	// The client half of the production chain: Yggdrasil's WASM host builds exactly this — a
	// GrpcWebCallInvoker over an HttpClient, wrapped by OutcomeClientInterceptor, handed to
	// protobuf-net.Grpc's proxy factory (see the generated AddNorseGrpcClients).
	static IProbeService CreateClient(HttpClient httpClient) =>
		new GrpcWebCallInvoker(httpClient).Intercept(new OutcomeClientInterceptor()).CreateGrpcService<IProbeService>();

	[Fact]
	async Task A_successful_outcome_decodes_over_the_real_grpc_web_stack()
	{
		await using var app = BuildHost();
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var httpClient = app.GetTestClient();

		var outcome = await CreateClient(httpClient).SucceedAsync(
			new ProbeRequest(),
			new CallContext(new CallOptions(cancellationToken: TestContext.Current.CancellationToken)));

		outcome.TryGetValue(out Success<ProbeResponse> success).ShouldBeTrue();
		success.Value.Value.ShouldBe("ok");
	}

	[Fact]
	async Task A_failed_outcome_decodes_over_the_real_grpc_web_stack()
	{
		await using var app = BuildHost();
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var httpClient = app.GetTestClient();

		var outcome = await CreateClient(httpClient).FailAsync(
			new ProbeRequest(),
			new CallContext(new CallOptions(cancellationToken: TestContext.Current.CancellationToken)));

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.InvalidCredentials);
	}

	// ProtoBuf.Grpc.Configuration.Service, not System.ServiceModel.ServiceContract: protobuf-net.Grpc's
	// ServiceBinder treats the two identically, and this one needs no extra package reference.
	[Service("norse.probe.v1.ProbeService")]
	// PBN2008: the analyzer can't see RegisterSurrogates()'s runtime RuntimeTypeModel.Add/SetSurrogate call
	// (the same pattern the generated AddNorseGrpcClients/MapNorseGrpcServices wiring relies on), so it
	// can't statically confirm Outcome<ProbeResponse> marshals — proving that it does is this file's entire
	// point, verified below by the round-trip itself, not by the analyzer.
#pragma warning disable PBN2008
	public interface IProbeService
	{
		Task<Outcome<ProbeResponse>> SucceedAsync(ProbeRequest request, CallContext context = default);

		Task<Outcome<ProbeResponse>> FailAsync(ProbeRequest request, CallContext context = default);
	}
#pragma warning restore PBN2008

	[DataContract]
	public sealed class ProbeRequest
	{
	}

	[DataContract]
	public sealed class ProbeResponse
	{
		[DataMember(Order = 1)] public string Value { get; set; } = string.Empty;
	}

	sealed class ProbeService : IProbeService
	{
		public Task<Outcome<ProbeResponse>> SucceedAsync(ProbeRequest request, CallContext context = default) =>
			Task.FromResult(Outcome<ProbeResponse>.Ok(new ProbeResponse { Value = "ok" }));

		// Mirrors OutcomeServerInterceptor exactly — that type is internal to
		// Infrastructure.Web.Server, whose IVT grant names Infrastructure.Web.Server.Tests, not this
		// assembly. The behavior under test is the wire encoding it produces, which is identical.
		public Task<Outcome<ProbeResponse>> FailAsync(ProbeRequest request, CallContext context = default)
		{
			var outcome = Outcome<ProbeResponse>.Err(ErrorCategory.InvalidCredentials);
			return outcome is Failed(var problem) ?
				throw problem.ToRpcException() :
				Task.FromResult(outcome);
		}
	}
}

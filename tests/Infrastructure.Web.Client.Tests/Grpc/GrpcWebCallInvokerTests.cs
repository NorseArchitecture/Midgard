using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Text;
using Grpc.Core;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Client.Grpc;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Infrastructure.Web.Client.Tests.Grpc;

public sealed class GrpcWebCallInvokerTests
{
	static readonly Marshaller<string> _utf8 = Marshallers.Create(Encoding.UTF8.GetBytes, Encoding.UTF8.GetString);

	static Method<string, string> Method() =>
		new(MethodType.Unary, "test.v1.TestService", "Echo", _utf8, _utf8);

	static GrpcWebCallInvoker CreateInvoker(RecordingHandler handler)
	{
#pragma warning disable CA2000 // The invoker owns the HttpClient for the test's lifetime; the handler is disposed by the test.
		return new(new HttpClient(handler) { BaseAddress = new Uri("https://unit.test") });
#pragma warning restore CA2000
	}

	static byte[] MessageFrame(byte[] payload)
	{
		var frame = new byte[5 + payload.Length];
		BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1), (uint)payload.Length);
		payload.CopyTo(frame, 5);
		return frame;
	}

	static byte[] TrailerFrame(string trailerBlock)
	{
		var payload = Encoding.ASCII.GetBytes(trailerBlock);
		var frame = new byte[5 + payload.Length];
		frame[0] = 0x80;
		BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1), (uint)payload.Length);
		payload.CopyTo(frame, 5);
		return frame;
	}

	static HttpResponseMessage GrpcWebResponse(params byte[][] frames)
	{
		HttpResponseMessage response = new(HttpStatusCode.OK)
		{
			Content = new ByteArrayContent([.. frames.SelectMany(f => f)]),
		};
		response.Content.Headers.ContentType = new("application/grpc-web+proto");
		return response;
	}

	[Fact]
	async Task Posts_a_length_prefixed_frame_to_the_service_method_path()
	{
		using var response = GrpcWebResponse(MessageFrame("pong"u8.ToArray()), TrailerFrame("grpc-status: 0\r\n"));
		using RecordingHandler handler = new(response);
		var invoker = CreateInvoker(handler);

		await invoker.AsyncUnaryCall(Method(), null, new CallOptions(), "ping").ResponseAsync;

		handler.Request.ShouldNotBeNull();
		handler.Request.Method.ShouldBe(HttpMethod.Post);
		handler.Request.RequestUri.ShouldBe(new Uri("https://unit.test/test.v1.TestService/Echo"));
		handler.Request.Content!.Headers.ContentType!.MediaType.ShouldBe("application/grpc-web+proto");
		var body = handler.RequestBody!;
		body[0].ShouldBe((byte)0); // uncompressed message frame
		BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(1)).ShouldBe(4u);
		Encoding.UTF8.GetString(body[5..]).ShouldBe("ping");
	}

	[Fact]
	async Task Deserializes_a_success_response_and_exposes_ok_status_and_trailers()
	{
		using var response = GrpcWebResponse(MessageFrame("pong"u8.ToArray()), TrailerFrame("grpc-status: 0\r\n"));
		using RecordingHandler handler = new(response);
		var invoker = CreateInvoker(handler);

		var call = invoker.AsyncUnaryCall(Method(), null, new CallOptions(), "ping");
		var result = await call.ResponseAsync;

		result.ShouldBe("pong");
		call.GetStatus().StatusCode.ShouldBe(StatusCode.OK);
	}

	[Fact]
	async Task A_failure_status_with_binary_trailers_round_trips_through_DecodeProblem()
	{
		// Encode with the server's own ToRpcException, render its trailers the way ASP.NET Core's
		// gRPC-Web bridge does (base64 for -bin), and prove the full decode chain reconstructs the
		// Problem — the exact path OutcomeClientInterceptor depends on.
		var serverException = new Problem
		{
			Category = ErrorCategory.Validation,
			Errors = new Dictionary<string, string[]> { ["code"] = ["banana"] },
		}.ToRpcException();
		StringBuilder trailerBlock = new();
		trailerBlock.Append(CultureInfo.InvariantCulture, $"grpc-status: {(int)serverException.StatusCode}\r\n");
		foreach (var entry in serverException.Trailers)
		{
			if (entry.IsBinary)
				trailerBlock.Append(CultureInfo.InvariantCulture, $"{entry.Key}: {Convert.ToBase64String(entry.ValueBytes)}\r\n");
			else
				trailerBlock.Append(CultureInfo.InvariantCulture, $"{entry.Key}: {entry.Value}\r\n");
		}

		using var response = GrpcWebResponse(TrailerFrame(trailerBlock.ToString()));
		using RecordingHandler handler = new(response);
		var invoker = CreateInvoker(handler);

		var thrown = await Should.ThrowAsync<RpcException>(
			invoker.AsyncUnaryCall(Method(), null, new CallOptions(), "ping").ResponseAsync);

		var problem = thrown.DecodeProblem();
		problem.Category.ShouldBe(ErrorCategory.Validation);
		problem.Errors["code"].ShouldBe(["banana"]);
	}

	[Fact]
	async Task A_trailers_only_response_reads_grpc_status_from_http_headers()
	{
		using HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
		response.Content.Headers.ContentType = new("application/grpc-web+proto");
		response.Headers.Add("grpc-status", $"{(int)StatusCode.Unauthenticated}");
		response.Headers.Add("grpc-message", "who%20are%20you");
		using RecordingHandler handler = new(response);
		var invoker = CreateInvoker(handler);

		var thrown = await Should.ThrowAsync<RpcException>(
			invoker.AsyncUnaryCall(Method(), null, new CallOptions(), "ping").ResponseAsync);

		thrown.StatusCode.ShouldBe(StatusCode.Unauthenticated);
		thrown.Status.Detail.ShouldBe("who are you");
	}

	[Fact]
	async Task A_non_success_http_status_throws_unavailable()
	{
		using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new ByteArrayContent([]) };
		using RecordingHandler handler = new(response);
		var invoker = CreateInvoker(handler);

		var thrown = await Should.ThrowAsync<RpcException>(
			invoker.AsyncUnaryCall(Method(), null, new CallOptions(), "ping").ResponseAsync);

		thrown.StatusCode.ShouldBe(StatusCode.Unavailable);
		thrown.Status.Detail.ShouldContain("503");
	}

	[Fact]
	async Task A_missing_grpc_status_anywhere_fails_loudly()
	{
		using var response = GrpcWebResponse(MessageFrame("pong"u8.ToArray()));
		using RecordingHandler handler = new(response);
		var invoker = CreateInvoker(handler);

		var thrown = await Should.ThrowAsync<RpcException>(
			invoker.AsyncUnaryCall(Method(), null, new CallOptions(), "ping").ResponseAsync);

		thrown.StatusCode.ShouldBe(StatusCode.Internal);
		thrown.Status.Detail.ShouldContain("grpc-status");
	}

	[Fact]
	async Task A_compressed_response_frame_fails_loudly()
	{
		var frame = MessageFrame("pong"u8.ToArray());
		frame[0] = 0x01; // compression flag — never negotiated, so never legal
		using var response = GrpcWebResponse(frame, TrailerFrame("grpc-status: 0\r\n"));
		using RecordingHandler handler = new(response);
		var invoker = CreateInvoker(handler);

		var thrown = await Should.ThrowAsync<RpcException>(
			invoker.AsyncUnaryCall(Method(), null, new CallOptions(), "ping").ResponseAsync);

		thrown.StatusCode.ShouldBe(StatusCode.Internal);
	}

	[Fact]
	async Task A_canceled_token_surfaces_as_a_cancelled_rpc()
	{
		using CancellationTokenSource cts = new();
		await cts.CancelAsync();
		using var response = GrpcWebResponse(MessageFrame("pong"u8.ToArray()), TrailerFrame("grpc-status: 0\r\n"));
		using RecordingHandler handler = new(response);
		var invoker = CreateInvoker(handler);

		var thrown = await Should.ThrowAsync<RpcException>(
			invoker.AsyncUnaryCall(Method(), null, new CallOptions(cancellationToken: cts.Token), "ping").ResponseAsync);

		thrown.StatusCode.ShouldBe(StatusCode.Cancelled);
	}

	[Fact]
	void Every_streaming_shape_fails_loudly()
	{
		using var response = GrpcWebResponse();
		using RecordingHandler handler = new(response);
		var invoker = CreateInvoker(handler);
		Method<string, string> method = new(MethodType.DuplexStreaming, "test.v1.TestService", "Stream", _utf8, _utf8);

		Should.Throw<NotSupportedException>(() => invoker.AsyncServerStreamingCall(Method(), null, new CallOptions(), "ping"));
		Should.Throw<NotSupportedException>(() => invoker.AsyncClientStreamingCall(new Method<string, string>(MethodType.ClientStreaming, "test.v1.TestService", "Stream", _utf8, _utf8), null, new CallOptions()));
		Should.Throw<NotSupportedException>(() => invoker.AsyncDuplexStreamingCall(method, null, new CallOptions()));
		Should.Throw<NotSupportedException>(() => invoker.BlockingUnaryCall(Method(), null, new CallOptions(), "ping"));
	}

	[Fact]
	void A_client_without_a_base_address_fails_loudly_at_construction()
	{
		using var response = GrpcWebResponse();
		using RecordingHandler handler = new(response);
		using HttpClient addressless = new(handler);
		Should.Throw<ArgumentException>(() => new GrpcWebCallInvoker(addressless));
	}

	sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
	{
		internal HttpRequestMessage? Request { get; private set; }

		internal byte[]? RequestBody { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Request = request;
			RequestBody = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
			return response;
		}
	}
}

using System.Buffers;
using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Text;
using Grpc.Core;

namespace Norse.Infrastructure.Web.Client.Grpc;

/// <summary>
/// A unary-only gRPC-Web <see cref="CallInvoker"/> over a plain <see cref="HttpClient"/> — no
/// <c>Grpc.Net.Client</c> channel machinery underneath. Exists because that machinery's
/// <c>BalancerHttpHandler</c>/<c>Subchannel</c> connect path performs a synchronous
/// <c>SemaphoreSlim.Wait(0)</c>, which the single-threaded .NET 11 preview WASM runtime rejects with
/// <see cref="PlatformNotSupportedException"/> inside a fire-and-forget task — every call parks
/// forever without dispatching (root-caused 2026-08-05). This invoker is the browser-side
/// workaround; it dies the day a <c>GrpcChannel</c> dispatches from WASM again (dotnet/runtime
/// moving the multithreading guard behind SemaphoreSlim's try-acquire fast path, or grpc-dotnet
/// dropping the sync wait), at which point the host swaps <c>GrpcChannel.CreateCallInvoker()</c>
/// back in and this type is deleted.
/// </summary>
/// <remarks>
/// Wire shape per the gRPC-Web protocol: request and response bodies are length-prefixed frames —
/// 1 flag byte (bit 0 compression, bit 7 trailers) + 4-byte big-endian length + payload. Trailers
/// arrive either as a 0x80-flagged body frame (HTTP/1.1-style <c>key: value</c> lines, binary
/// values base64) or, for trailers-only responses, as plain HTTP response headers. Unary is the
/// only shape the Norse platform's contracts use; every streaming shape fails loudly.
/// </remarks>
public sealed class GrpcWebCallInvoker : CallInvoker
{
	const string ContentType = "application/grpc-web+proto";

	static readonly MediaTypeHeaderValue _contentType = new(ContentType);

	readonly HttpClient _httpClient;

	/// <summary>
	/// Creates an invoker over <paramref name="httpClient"/>, whose <see cref="HttpClient.BaseAddress"/>
	/// must be set — service paths (<c>/package.Service/Method</c>) resolve against it.
	/// </summary>
	public GrpcWebCallInvoker(HttpClient httpClient)
	{
		ArgumentNullException.ThrowIfNull(httpClient);
		if (httpClient.BaseAddress is null)
			throw new ArgumentException("HttpClient.BaseAddress must be set — gRPC-Web service paths resolve against it.", nameof(httpClient));
		_httpClient = httpClient;
	}

	/// <inheritdoc />
	public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
		Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
	{
		UnaryCallState state = new();
		var response = InvokeAsync(method, options, request, state);
		return new(response, state.ResponseHeaders.Task, () => state.Status, () => state.Trailers, static () => { });
	}

	/// <inheritdoc />
	public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
		throw new NotSupportedException("Blocking unary calls cannot exist on the browser's single thread — await AsyncUnaryCall instead.");

	/// <inheritdoc />
	public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
		throw new NotSupportedException($"{nameof(GrpcWebCallInvoker)} is unary-only; server streaming for '{method.FullName}' is not implemented.");

	/// <inheritdoc />
	public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
		throw new NotSupportedException($"{nameof(GrpcWebCallInvoker)} is unary-only; client streaming for '{method.FullName}' is not implemented.");

	/// <inheritdoc />
	public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
		throw new NotSupportedException($"{nameof(GrpcWebCallInvoker)} is unary-only; duplex streaming for '{method.FullName}' is not implemented.");

	async Task<TResponse> InvokeAsync<TRequest, TResponse>(Method<TRequest, TResponse> method, CallOptions options, TRequest request, UnaryCallState state)
		where TRequest : class
		where TResponse : class
	{
		try
		{
			using ByteArrayContent content = new(Frame(Serialize(method.RequestMarshaller, request)));
			content.Headers.ContentType = _contentType;
			using HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(method.FullName, UriKind.Relative)) { Content = content };
			foreach (var entry in options.Headers ?? [])
			{
				httpRequest.Headers.TryAddWithoutValidation(
					entry.Key, entry.IsBinary ? Convert.ToBase64String(entry.ValueBytes) : entry.Value);
			}

			using var httpResponse = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, options.CancellationToken).ConfigureAwait(false);
			state.ResponseHeaders.TrySetResult(ReadMetadata(httpResponse.Headers));
			if (!httpResponse.IsSuccessStatusCode)
				throw new RpcException(new Status(StatusCode.Unavailable, $"gRPC-Web transport returned HTTP {(int)httpResponse.StatusCode}."));

			var body = await httpResponse.Content.ReadAsByteArrayAsync(options.CancellationToken).ConfigureAwait(false);
			var (payload, trailers) = ParseFrames(body);
			// A trailers-only response carries no trailer frame at all — grpc-status, grpc-message and
			// grpc-status-details-bin arrive as plain HTTP response headers, and the response body is
			// empty. That is exactly what Grpc.AspNetCore.Server emits whenever a call fails before
			// writing a message, which is every Failed(Problem) the platform produces. In that shape the
			// response headers ARE the trailers (Grpc.Net.Client resolves it identically); dropping them
			// leaves RpcException.Trailers empty, so RpcExceptionExtensions.DecodeProblem finds no
			// grpc-status-details-bin and degrades every business failure to ErrorCategory.Fault. Read a
			// second time rather than reusing the ResponseHeaders instance: Metadata is mutable and
			// nothing freezes it, so handing the same object to both would alias them.
			state.Trailers = trailers ?? ReadMetadata(httpResponse.Headers);

			var status = ResolveStatus(trailers, httpResponse.Headers);
			state.Status = status;
			if (status.StatusCode != StatusCode.OK)
				throw new RpcException(status, state.Trailers);
			if (payload is null)
				throw new RpcException(new Status(StatusCode.Internal, $"gRPC-Web response for '{method.FullName}' carried OK status but no message frame."));

			return Deserialize(method.ResponseMarshaller, payload);
		}
		catch (OperationCanceledException exception) when (options.CancellationToken.IsCancellationRequested)
		{
			var status = new Status(StatusCode.Cancelled, "Call canceled by the client.");
			state.Status = status;
			throw new RpcException(status, exception.Message);
		}
		catch (RpcException exception)
		{
			state.Status = exception.Status;
			state.ResponseHeaders.TrySetResult([]);
			throw;
		}
		catch (HttpRequestException exception)
		{
			var status = new Status(StatusCode.Unavailable, $"gRPC-Web transport failed: {exception.Message}", exception);
			state.Status = status;
			state.ResponseHeaders.TrySetResult([]);
			throw new RpcException(status);
		}
	}

	static byte[] Serialize<T>(Marshaller<T> marshaller, T value)
	{
		CallSerializationContext context = new();
		marshaller.ContextualSerializer(value, context);
		return context.Payload;
	}

	static T Deserialize<T>(Marshaller<T> marshaller, byte[] payload) =>
		marshaller.ContextualDeserializer(new CallDeserializationContext(payload));

	static byte[] Frame(byte[] payload)
	{
		var frame = new byte[5 + payload.Length];
		BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1), (uint)payload.Length);
		payload.CopyTo(frame, 5);
		return frame;
	}

	static (byte[]? Payload, Metadata? Trailers) ParseFrames(ReadOnlySpan<byte> body)
	{
		byte[]? payload = null;
		Metadata? trailers = null;
		while (body.Length >= 5)
		{
			var flags = body[0];
			var length = BinaryPrimitives.ReadUInt32BigEndian(body[1..]);
			if (body.Length < 5 + length)
				throw new RpcException(new Status(StatusCode.Internal, "Truncated gRPC-Web frame."));
			var framePayload = body.Slice(5, (int)length);

			if ((flags & 0x01) != 0)
				throw new RpcException(new Status(StatusCode.Internal, "Compressed gRPC-Web frames are not supported — compression is never negotiated."));
			if ((flags & 0x80) != 0)
				trailers = ParseTrailerBlock(Encoding.ASCII.GetString(framePayload));
			else
				payload = framePayload.ToArray();

			body = body[(5 + (int)length)..];
		}

		if (body.Length != 0)
			throw new RpcException(new Status(StatusCode.Internal, "Truncated gRPC-Web frame."));
		return (payload, trailers);
	}

	static Metadata ParseTrailerBlock(string block)
	{
		Metadata trailers = [];
		foreach (var line in block.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
		{
			var separator = line.IndexOf(':', StringComparison.Ordinal);
			if (separator <= 0)
				throw new RpcException(new Status(StatusCode.Internal, $"Malformed gRPC-Web trailer line: '{line}'."));
			var key = line[..separator].Trim();
			var value = line[(separator + 1)..].Trim();
			if (key.EndsWith(Metadata.BinaryHeaderSuffix, StringComparison.OrdinalIgnoreCase))
				trailers.Add(key, Convert.FromBase64String(value));
			else
				trailers.Add(key, value);
		}

		return trailers;
	}

	static Metadata ReadMetadata(HttpResponseHeaders headers)
	{
		Metadata metadata = [];
		foreach (var (key, values) in headers)
		{
			foreach (var value in values)
			{
				if (key.EndsWith(Metadata.BinaryHeaderSuffix, StringComparison.OrdinalIgnoreCase))
					metadata.Add(key, Convert.FromBase64String(value));
				else
					metadata.Add(key, value);
			}
		}

		return metadata;
	}

	static Status ResolveStatus(Metadata? trailers, HttpResponseHeaders headers)
	{
		var statusValue = trailers?.Get("grpc-status")?.Value
			?? (headers.TryGetValues("grpc-status", out var headerValues) ? headerValues.FirstOrDefault() : null);
		if (statusValue is null || !int.TryParse(statusValue, out var statusCode))
			throw new RpcException(new Status(StatusCode.Internal, "gRPC-Web response carried no grpc-status in trailers or headers."));

		var message = trailers?.Get("grpc-message")?.Value
			?? (headers.TryGetValues("grpc-message", out var messageValues) ? messageValues.FirstOrDefault() : null);
		return new((StatusCode)statusCode, message is null ? string.Empty : Uri.UnescapeDataString(message));
	}

	sealed class UnaryCallState
	{
		internal TaskCompletionSource<Metadata> ResponseHeaders { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		internal Status Status { get; set; } = new(StatusCode.Unknown, "Call has not completed.");

		internal Metadata Trailers { get; set; } = [];
	}

	sealed class CallSerializationContext : SerializationContext
	{
		readonly ArrayBufferWriter<byte> _buffer = new();

		bool _completed;

		internal byte[] Payload =>
			_completed ? _buffer.WrittenSpan.ToArray() : throw new InvalidOperationException("Serializer never called Complete().");

		public override void Complete(byte[] payload)
		{
			_buffer.Write(payload);
			_completed = true;
		}

		public override IBufferWriter<byte> GetBufferWriter() => _buffer;

		public override void SetPayloadLength(int payloadLength)
		{
		}

		public override void Complete() => _completed = true;
	}

	sealed class CallDeserializationContext(byte[] payload) : DeserializationContext
	{
		public override int PayloadLength => payload.Length;

		public override byte[] PayloadAsNewBuffer() => payload;

		public override ReadOnlySequence<byte> PayloadAsReadOnlySequence() => new(payload);
	}
}

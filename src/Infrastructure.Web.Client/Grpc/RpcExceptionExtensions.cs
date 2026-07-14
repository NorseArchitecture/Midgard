using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Grpc.Core;

namespace Norse.Infrastructure.Web.Client.Grpc;

/// <summary>Client-side companion to Infrastructure.Web.Server's OutcomeServerInterceptor — decodes an
/// RpcException's problem-bin trailer directly into a plain dictionary. Never references Asgard's
/// Problem/ErrorCategory (server-only) — this project compiles into a WASM client bundle.</summary>
public static class RpcExceptionExtensions
{
	/// <summary>
	/// Extracts and deserializes the errors from a gRPC RpcException's "problem-bin" trailer.
	/// </summary>
	/// <param name="exception">The RpcException to decode.</param>
	/// <returns>A dictionary mapping error field names to arrays of error messages, or an empty dictionary if no trailer is present.</returns>
	[SuppressMessage("Trimming", "IL2026", Justification = "JSON deserialization of error dictionary from gRPC trailer.")]
	[SuppressMessage("AOT", "IL3050", Justification = "JSON deserialization of error dictionary from gRPC trailer.")]
	public static IReadOnlyDictionary<string, string[]> DecodeProblem(this RpcException exception)
	{
		var trailer = exception.Trailers.Get("problem-bin");
		return trailer is null
			? []
			: JsonSerializer.Deserialize<Dictionary<string, string[]>>(trailer.ValueBytes) ?? [];
	}
}

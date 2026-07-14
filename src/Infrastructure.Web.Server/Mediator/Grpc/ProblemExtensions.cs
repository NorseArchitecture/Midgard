using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Grpc.Core;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
/// Extension methods for <see cref="Problem"/> that translate <see cref="ErrorCategory"/> to gRPC <see cref="StatusCode"/>
/// and serialize error details into an RpcException trailer.
/// </summary>
public static class ProblemExtensions
{
	/// <summary>
	/// Converts a <see cref="Problem"/> to an <see cref="RpcException"/>, mapping the error category to the appropriate
	/// <see cref="StatusCode"/> and serializing the errors dictionary into a "problem-bin" trailer.
	/// </summary>
	/// <param name="problem">The problem to convert.</param>
	/// <returns>An RpcException with the appropriate status code and a problem-bin trailer containing the serialized errors.</returns>
	[SuppressMessage("Trimming", "IL2026", Justification = "JSON serialization of error dictionary is required for gRPC trailers.")]
	[SuppressMessage("AOT", "IL3050", Justification = "JSON serialization of error dictionary is required for gRPC trailers.")]
	public static RpcException ToRpcException(this Problem problem)
	{
		var status = problem.Category switch
		{
			ErrorCategory.Validation => StatusCode.InvalidArgument,
			ErrorCategory.Conflict => StatusCode.AlreadyExists,
			ErrorCategory.LockedOut or ErrorCategory.NotAllowed => StatusCode.PermissionDenied,
			_ => StatusCode.Unknown,
		};
		var trailers = new Metadata { { "problem-bin", JsonSerializer.SerializeToUtf8Bytes(problem.Errors) } };
		return new RpcException(new Status(status, problem.Category.ToString()), trailers);
	}
}

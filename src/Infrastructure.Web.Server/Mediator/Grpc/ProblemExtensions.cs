using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Norse.Abstractions.Contracts;
using Status = Google.Rpc.Status;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
/// Converts a <see cref="Problem"/> to an <see cref="RpcException"/>. The gRPC status code is the
/// partner-legible idiom — standard tooling reads it correctly without knowing Norse exists — but it
/// is not injective (<see cref="ErrorCategory.LockedOut"/>/<see cref="ErrorCategory.Forbidden"/> share
/// PermissionDenied; <see cref="ErrorCategory.Unauthorized"/>/<see cref="ErrorCategory.InvalidCredentials"/>
/// share Unauthenticated). Every response also carries a <c>google.rpc.ErrorInfo</c> detail whose
/// <c>Reason</c> is the exact <see cref="ErrorCategory"/> member name — the only field
/// the client-side <c>RpcExceptionExtensions.DecodeProblem</c> method trusts (spec §2.1).
/// </summary>
public static class ProblemExtensions
{
	const string ErrorInfoDomain = "norse.io";

	extension(Problem problem)
	{
		/// <summary>Converts a <see cref="Problem"/> to an <see cref="RpcException"/> carrying a <c>grpc-status-details-bin</c> trailer.</summary>
		public RpcException ToRpcException()
		{
			var statusCode = problem.Category switch
			{
				ErrorCategory.Validation => StatusCode.InvalidArgument,
				ErrorCategory.NotFound => StatusCode.NotFound,
				ErrorCategory.Conflict => StatusCode.AlreadyExists,
				ErrorCategory.Unauthorized => StatusCode.Unauthenticated,
				ErrorCategory.Forbidden or ErrorCategory.LockedOut => StatusCode.PermissionDenied,
				ErrorCategory.NotAllowed => StatusCode.FailedPrecondition,
				ErrorCategory.InvalidCredentials => StatusCode.Unauthenticated,
				ErrorCategory.Fault => StatusCode.Internal,
				ErrorCategory.MultipleMatches => StatusCode.Internal,
				_ => StatusCode.Unknown
			};

			Status richStatus = new()
			{
				Code = (int)MapToGoogleRpcCode(statusCode),
				Message = problem.Category.ToString()
			};
			richStatus.Details.Add(Any.Pack(new ErrorInfo
			{
				Reason = problem.Category.ToString(),
				Domain = ErrorInfoDomain
			}));
			if (problem.Errors.Count > 0)
			{
				BadRequest badRequest = new();
				foreach (var (field, messages) in problem.Errors)
				{
					foreach (var message in messages)
						badRequest.FieldViolations.Add(new BadRequest.Types.FieldViolation { Field = field, Description = message });
				}
				richStatus.Details.Add(Any.Pack(badRequest));
			}
			if (problem.CorrelationId is { } correlationId)
			{
				richStatus.Details.Add(Any.Pack(new DebugInfo { Detail = correlationId.ToString() }));
			}

			Metadata trailers = new() { { "grpc-status-details-bin", richStatus.ToByteString().ToByteArray() } };
			return new(new(statusCode, problem.Category.ToString()), trailers);
		}
	}

	static Code MapToGoogleRpcCode(StatusCode statusCode) => statusCode switch
	{
		StatusCode.InvalidArgument => Code.InvalidArgument,
		StatusCode.NotFound => Code.NotFound,
		StatusCode.AlreadyExists => Code.AlreadyExists,
		StatusCode.Unauthenticated => Code.Unauthenticated,
		StatusCode.PermissionDenied => Code.PermissionDenied,
		StatusCode.FailedPrecondition => Code.FailedPrecondition,
		StatusCode.Internal => Code.Internal,
		_ => Code.Unknown
	};
}

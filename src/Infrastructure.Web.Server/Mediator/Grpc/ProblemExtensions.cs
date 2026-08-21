using System.Globalization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Norse.Abstractions.Contracts;
using Status = Google.Rpc.Status;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
///     Converts a <see cref="Problem" /> to an <see cref="RpcException" />. The gRPC status code is the
///     partner-legible idiom — standard tooling reads it correctly without knowing Norse exists — but it
///     is not injective across all members (<see cref="ErrorCategory.LockedOut" />/<see cref="ErrorCategory.Forbidden" />
///     share
///     PermissionDenied; <see cref="ErrorCategory.Unauthorized" />/<see cref="ErrorCategory.InvalidCredentials" />
///     share Unauthenticated; <see cref="ErrorCategory.Erased" /> shares NotFound). Every response also carries
///     a <c>google.rpc.ErrorInfo</c> detail whose <c>Reason</c> is the exact <see cref="ErrorCategory" /> member
///     name — the only field the client-side <c>RpcExceptionExtensions.DecodeProblem</c> method trusts (spec §2.1).
///     When <see cref="Problem.Receipt" /> is populated (the <see cref="ErrorCategory.Erased" /> crypto-shred
///     producer), the <c>ErrorInfo.Metadata</c> also carries <c>receipt</c> (Guid <c>"D"</c> format) and
///     <c>severedAt</c> (<c>"O"</c> format, invariant culture).
/// </summary>
public static class ProblemExtensions
{
	const string ErrorInfoDomain = "norse.io";

	extension(Problem problem)
	{
		/// <summary>
		///     Converts a <see cref="Problem" /> to an <see cref="RpcException" /> carrying a <c>grpc-status-details-bin</c>
		///     trailer.
		/// </summary>
		public RpcException ToRpcException()
		{
			var disposition = TransportDispositions.For(problem.Category);
			var statusCode = (StatusCode)disposition.GrpcStatus;

			// A silent category answers "who am I? -- unknown", and the platform never explains that answer.
			// No ErrorInfo, no metadata, no detail string: the response is the status and nothing else, which
			// is what makes two silent categories provably indistinguishable rather than merely similar.
			if (!disposition.BodyPermitted)
				return new RpcException(new global::Grpc.Core.Status(statusCode, string.Empty));

			Status richStatus = new()
			{
				Code = (int)MapToGoogleRpcCode(statusCode),
				Message = problem.Category.ToString()
			};
			ErrorInfo errorInfo = new() { Reason = problem.Category.ToString(), Domain = ErrorInfoDomain };
			if (problem.Receipt is { } receipt)
			{
				errorInfo.Metadata.Add("receipt", receipt.ReceiptId.ToString("D"));
				errorInfo.Metadata.Add("severedAt", receipt.SeveredAt.ToString("O", CultureInfo.InvariantCulture));
			}

			richStatus.Details.Add(Any.Pack(errorInfo));
			if (problem.Errors.Count > 0)
			{
				BadRequest badRequest = new();
				foreach (var (field, messages) in problem.Errors)
				{
					foreach (var message in messages)
						badRequest.FieldViolations.Add(
							new BadRequest.Types.FieldViolation { Field = field, Description = message });
				}

				richStatus.Details.Add(Any.Pack(badRequest));
			}

			if (problem.CorrelationId is { } correlationId)
			{
				richStatus.Details.Add(Any.Pack(new DebugInfo { Detail = correlationId.ToString() }));
			}

			Metadata trailers = new() { { "grpc-status-details-bin", richStatus.ToByteString().ToByteArray() } };
			return new RpcException(new global::Grpc.Core.Status(statusCode, problem.Category.ToString()), trailers);
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

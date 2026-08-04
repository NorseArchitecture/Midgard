using System.Globalization;
using Google.Rpc;
using Grpc.Core;
using Norse.Abstractions.Contracts;
using Status = Google.Rpc.Status;

namespace Norse.Infrastructure.Web.Client.Grpc;

/// <summary>
/// Client-side companion to Infrastructure.Web.Server's <c>ProblemExtensions.ToRpcException</c>.
/// Decodes the <c>grpc-status-details-bin</c> trailer's <c>google.rpc.ErrorInfo.Reason</c> field
/// authoritatively — never the gRPC status code, which is not injective across all
/// <see cref="ErrorCategory"/> members (spec §2.1). When <c>ErrorInfo.Metadata</c> carries both
/// <c>receipt</c> and <c>severedAt</c> entries, they are rehydrated into <see cref="Problem.Receipt"/>;
/// either entry absent leaves <see cref="Problem.Receipt"/> <see langword="null"/> (tombstone producer).
/// </summary>
public static class RpcExceptionExtensions
{
	extension(RpcException exception)
	{
		/// <summary>Decodes an <see cref="RpcException"/>'s <c>grpc-status-details-bin</c> trailer into a <see cref="Problem"/>.</summary>
		public Problem DecodeProblem()
		{
			var trailer = exception.Trailers.Get("grpc-status-details-bin");
			if (trailer is null)
				return new Problem { Category = ErrorCategory.Fault };

			var richStatus = Status.Parser.ParseFrom(trailer.ValueBytes);
			var category = ErrorCategory.Fault;
			Dictionary<string, string[]> errors = [];
			Guid? correlationId = null;
			ErasureReceipt? receipt = null;

			foreach (var detail in richStatus.Details)
			{
				if (detail.Is(ErrorInfo.Descriptor) && detail.TryUnpack<ErrorInfo>(out var errorInfo) && Enum.TryParse<ErrorCategory>(errorInfo.Reason, out var parsed))
				{
					category = parsed;
					if (errorInfo.Metadata.TryGetValue("receipt", out var receiptId) &&
						errorInfo.Metadata.TryGetValue("severedAt", out var severedAt) &&
						Guid.TryParse(receiptId, out var parsedReceiptId) &&
						DateTimeOffset.TryParse(severedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedSeveredAt))
					{
						receipt = new ErasureReceipt(parsedReceiptId, parsedSeveredAt);
					}
				}
				else if (detail.Is(BadRequest.Descriptor) && detail.TryUnpack<BadRequest>(out var badRequest))
				{
					errors = badRequest.FieldViolations
						.GroupBy(violation => violation.Field)
						.ToDictionary(group => group.Key, group => group.Select(violation => violation.Description).ToArray());
				}
				else if (detail.Is(DebugInfo.Descriptor) && detail.TryUnpack<DebugInfo>(out var debugInfo) && Guid.TryParse(debugInfo.Detail, out var parsedCorrelationId))
				{
					correlationId = parsedCorrelationId;
				}
			}

			return new() { Category = category, Errors = errors, CorrelationId = correlationId, Receipt = receipt };
		}
	}
}

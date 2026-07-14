using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
/// Thrown only by <see cref="OutcomeExtensions.ThrowIfFailed{T}"/>, caught only by <see cref="OutcomeServerInterceptor"/> —
/// scoped to this project so it's never visible to code that isn't already building a gRPC-hosted mediator handler.
/// </summary>
sealed class OutcomeFailedException(Problem problem) : Exception
{
	public Problem Problem { get; } = problem;
}

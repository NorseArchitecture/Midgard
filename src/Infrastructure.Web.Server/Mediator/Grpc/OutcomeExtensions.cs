using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
/// Extension methods for <see cref="Outcome{T}"/> and <see cref="Outcome"/> that throw an <see cref="OutcomeFailedException"/>
/// on failure, intended to be caught and converted to an RpcException by <see cref="OutcomeServerInterceptor"/>.
/// </summary>
public static class OutcomeExtensions
{
	/// <summary>
	/// Returns the success value, or throws <see cref="OutcomeFailedException"/> on failure.
	/// </summary>
	/// <param name="outcome">The outcome to evaluate.</param>
	/// <typeparam name="T">The type of the success value.</typeparam>
	/// <returns>The success value if <paramref name="outcome"/> succeeded.</returns>
	/// <exception cref="OutcomeFailedException">Thrown when <paramref name="outcome"/> failed.</exception>
	public static T ThrowIfFailed<T>(this Outcome<T> outcome) =>
		outcome.IsSuccess ? outcome.Value! : throw new OutcomeFailedException(outcome.Problem!);

	/// <summary>
	/// Throws <see cref="OutcomeFailedException"/> if the outcome failed; otherwise, returns normally.
	/// </summary>
	/// <param name="outcome">The outcome to evaluate.</param>
	/// <exception cref="OutcomeFailedException">Thrown when <paramref name="outcome"/> failed.</exception>
	public static void ThrowIfFailed(this Outcome outcome)
	{
		if (!outcome.IsSuccess)
			throw new OutcomeFailedException(outcome.Problem!);
	}
}

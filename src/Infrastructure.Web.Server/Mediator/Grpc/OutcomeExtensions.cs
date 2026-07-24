using Norse.Abstractions.Contracts;

namespace Norse.Infrastructure.Web.Server.Mediator.Grpc;

/// <summary>
/// Extension methods for <see cref="Outcome{T}"/> that throw an <see cref="OutcomeFailedException"/>
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
	public static T ThrowIfFailed<T>(this Outcome<T> outcome) where T : notnull
	{
		if (outcome.TryGetValue(out Norse.Primitives.Success<T> success))
			return success.Value;
		if (outcome.TryGetValue(out Failed failed))
			throw new OutcomeFailedException(failed.Problem);
		throw new InvalidOperationException("Outcome was neither success nor failure");
	}
}

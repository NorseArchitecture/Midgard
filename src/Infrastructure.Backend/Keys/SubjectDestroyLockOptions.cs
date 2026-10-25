namespace Norse.Infrastructure.Backend.Keys;

/// <summary>Bounded acquisition for <see cref="SubjectDestroyLock" />: attempts, and the backoff between them.</summary>
/// <param name="MaxAttempts">Attempts on the file lock before acquisition throws <see cref="TimeoutException" />.</param>
/// <param name="InitialDelay">The wait after the first refused attempt; it doubles after each one.</param>
/// <param name="MaxDelay">The cap on the doubling wait.</param>
sealed record SubjectDestroyLockOptions(int MaxAttempts, TimeSpan InitialDelay, TimeSpan MaxDelay)
{
	/// <summary>About ten seconds in all: 10 ms doubling to 250 ms, forty attempts.</summary>
	public static SubjectDestroyLockOptions Default { get; } =
		new(40, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(250));
}

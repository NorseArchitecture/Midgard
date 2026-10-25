namespace Norse.Infrastructure.Backend.Keys;

/// <summary>
///     The platform's checked, exclusive, non-blocking lock on one file (spec §7.5). Exclusion holds
///     between two handles in one process as well as between processes; the store's startup self-test
///     proves it before any destruction can run.
/// </summary>
interface IFileLockPrimitive
{
	/// <summary>
	///     The primitive this host's operating system supports: native <c>flock</c> on macOS and Linux,
	///     <see cref="FileStream.Lock" /> on Windows.
	/// </summary>
	/// <exception cref="PlatformNotSupportedException">Any other operating system.</exception>
	static IFileLockPrimitive ForCurrentPlatform() =>
		OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() ?
			new UnixFileLock() :
			OperatingSystem.IsWindows() ?
				new WindowsFileLock() :
				throw new PlatformNotSupportedException(
					"The development key store's destroy lock supports macOS, Linux, and Windows only.");

	/// <summary>Attempts the lock once, without waiting.</summary>
	/// <param name="path">The lock file; created if it does not exist.</param>
	/// <returns>
	///     The held lock, released on dispose; or <see langword="null" /> when the primitive reported, checked,
	///     that another handle holds it. Any other failure throws.
	/// </returns>
	IDisposable? TryAcquire(string path);
}

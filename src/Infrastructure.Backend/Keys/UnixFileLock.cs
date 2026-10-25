using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Norse.Infrastructure.Backend.Keys;

/// <summary>
///     macOS and Linux: <c>flock(LOCK_EX | LOCK_NB)</c> on a descriptor from libc <c>open</c>, never from a
///     <see cref="FileStream" /> — the runtime takes its own unchecked <c>flock</c> on every
///     <see cref="FileStream" /> open on Unix, so contention would surface at open and the checked call would
///     never be reached (V8, ruled 2026-10-24). <c>flock</c> locks belong to the open file description, so two
///     handles in one process exclude each other as well as two processes; <c>O_CLOEXEC</c> keeps a child
///     process from inheriting, and so prolonging, a held lock.
/// </summary>
sealed partial class UnixFileLock : IFileLockPrimitive
{
	const int OpenReadWrite = 2; // O_RDWR, both platforms
	const int LockExclusive = 2; // LOCK_EX
	const int LockNonBlocking = 4; // LOCK_NB
	const int LockUnlock = 8; // LOCK_UN

	static int OpenCloseOnExec => // O_CLOEXEC
		OperatingSystem.IsMacOS() ? 0x1000000 : 0x80000;

	static int WouldBlock => // EWOULDBLOCK
		OperatingSystem.IsMacOS() ? 35 : 11;

	/// <inheritdoc />
	public IDisposable? TryAcquire(string path)
	{
		EnsureExists(path);
		var handle = Open(path, OpenReadWrite | OpenCloseOnExec);
		if (handle.IsInvalid)
		{
			var openError = Marshal.GetLastPInvokeError();
			handle.Dispose();
			throw new IOException($"open('{path}') failed with errno {openError}.");
		}

		if (Flock(handle, LockExclusive | LockNonBlocking) == 0)
			return new HeldLock(handle);

		var lockError = Marshal.GetLastPInvokeError();
		handle.Dispose();
		return lockError == WouldBlock ?
			null :
			throw new IOException($"flock('{path}', LOCK_EX | LOCK_NB) failed with errno {lockError}.");
	}

	/// <summary>
	///     The native <c>open</c> can't create the file: it's variadic, and passing a <c>mode</c> through P/Invoke
	///     isn't safe under the Apple arm64 calling convention. A .NET open-or-create, disposed at once, creates
	///     it instead; an <see cref="IOException" /> there means the file exists and someone holds it, so the
	///     native open proceeds.
	/// </summary>
	static void EnsureExists(string path)
	{
		if (File.Exists(path))
			return;

		try
		{
			File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite).Dispose();
		}
		catch (IOException)
		{
			// Exists and held: the runtime's own lock refused the open. The native open below decides.
		}
	}

	// The two-argument form only: no O_CREAT, so no variadic mode argument. SafeDirectories omits
	// AssemblyDirectory, so libc always resolves through the system loader, never a planted copy.
	[DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
	[LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
	private static partial SafeFileHandle Open(string path, int flags);

	[DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
	[LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
	private static partial int Flock(SafeFileHandle fd, int operation);

	sealed class HeldLock(SafeFileHandle handle) : IDisposable
	{
		// Closing the only descriptor on the open file description releases the lock whatever LOCK_UN
		// reports, so its result carries no decision.
		public void Dispose()
		{
			_ = Flock(handle, LockUnlock);
			handle.Dispose();
		}
	}
}

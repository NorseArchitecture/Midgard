using System.Runtime.Versioning;

namespace Norse.Infrastructure.Backend.Keys;

/// <summary>
///     Windows: <see cref="FileStream.Lock" /> (<c>LockFileEx</c>) on byte 0, which throws
///     <see cref="IOException" /> while another handle holds the range.
/// </summary>
[SupportedOSPlatform("windows")]
sealed class WindowsFileLock : IFileLockPrimitive
{
	/// <inheritdoc />
	public IDisposable? TryAcquire(string path)
	{
		FileStream stream = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
		try
		{
			stream.Lock(0, 1);
			return new HeldLock(stream);
		}
		catch (IOException)
		{
			stream.Dispose();
			return null;
		}
	}

	sealed class HeldLock(FileStream stream) : IDisposable
	{
		public void Dispose()
		{
			stream.Unlock(0, 1);
			stream.Dispose();
		}
	}
}

using Norse.Infrastructure.Backend.Keys;

namespace Norse.Infrastructure.Backend.Tests.Keys;

/// <summary>A primitive that fails outright on every attempt — never a checked refusal, never a lock.</summary>
sealed class FailingFileLock : IFileLockPrimitive
{
	public IDisposable TryAcquire(string path) =>
		throw new IOException($"Injected primitive failure on '{path}'.");
}

/// <summary>A primitive that grants every attempt, so two handles never exclude each other.</summary>
sealed class NonExclusiveFileLock : IFileLockPrimitive
{
	public IDisposable TryAcquire(string path) =>
		new Released();

	sealed class Released : IDisposable
	{
		public void Dispose()
		{
		}
	}
}

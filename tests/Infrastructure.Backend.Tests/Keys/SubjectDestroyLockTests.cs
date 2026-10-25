using Norse.Infrastructure.Backend.Keys;

namespace Norse.Infrastructure.Backend.Tests.Keys;

public sealed class SubjectDestroyLockTests : IDisposable
{
	// Exhaustion in milliseconds, not the default's ten seconds.
	static SubjectDestroyLockOptions Fast { get; } = new(3, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(5));

	readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"norse-lock-{Guid.NewGuid():N}"))
		.FullName;

	public void Dispose() =>
		Directory.Delete(_root, recursive: true);

	[Fact]
	async Task Lock_held_by_another_handle_in_process_blocks_until_exhaustion()
	{
		var subject = Guid.NewGuid();
		SubjectDestroyLock destroyLock = new(IFileLockPrimitive.ForCurrentPlatform(), Fast);
		using (var other = IFileLockPrimitive.ForCurrentPlatform().TryAcquire(LockPath(subject)))
		{
			other.ShouldNotBeNull();
			await Should.ThrowAsync<TimeoutException>(async () =>
				await destroyLock.AcquireAsync(_root, subject, TestContext.Current.CancellationToken));
		}

		// Released elsewhere, the same lock is now taken: the refusal was the other handle, not a lock
		// that can never be held.
		await using var held = await destroyLock.AcquireAsync(_root, subject, TestContext.Current.CancellationToken);
	}

	[Fact]
	async Task Lock_held_by_another_process_blocks_until_exhaustion()
	{
		var subject = Guid.NewGuid();
		SubjectDestroyLock destroyLock = new(IFileLockPrimitive.ForCurrentPlatform(), Fast);
		using var holder = TestHostProcess.Start("--lock-holder", LockPath(subject));
		try
		{
			(await holder.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken)).ShouldBe("HELD");
			await Should.ThrowAsync<TimeoutException>(async () =>
				await destroyLock.AcquireAsync(_root, subject, TestContext.Current.CancellationToken));

			holder.StandardInput.Close();
			await holder.WaitForExitAsync(TestContext.Current.CancellationToken);
			holder.ExitCode.ShouldBe(0);
			await using var held =
				await destroyLock.AcquireAsync(_root, subject, TestContext.Current.CancellationToken);
		}
		finally
		{
			if (!holder.HasExited)
				holder.Kill();
		}
	}

	[Fact]
	async Task Primitive_failure_is_treated_as_not_held()
	{
		SubjectDestroyLock destroyLock = new(new FailingFileLock(), Fast);
		var exception = await Should.ThrowAsync<TimeoutException>(async () =>
			await destroyLock.AcquireAsync(_root, Guid.NewGuid(), TestContext.Current.CancellationToken));
		exception.InnerException.ShouldBeOfType<IOException>();
	}

	string LockPath(Guid subject) =>
		Path.Combine(_root, $"{subject:N}.destroy.lock");
}

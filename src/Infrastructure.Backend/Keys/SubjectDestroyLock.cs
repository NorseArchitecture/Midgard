namespace Norse.Infrastructure.Backend.Keys;

/// <summary>
///     The per-subject destroy lock (spec §7.5): <c>{subjectId:N}.destroy.lock</c> in the store root, taken
///     through the platform's checked primitive. A per-subject in-process <see cref="SemaphoreSlim" /> sits in
///     front so threads in one process queue instead of spinning on the descriptor — an ordering convenience,
///     never the exclusion guarantee. Any failure to obtain the file lock, refusal or fault alike, means
///     "not held"; acquisition is bounded with backoff and throws <see cref="TimeoutException" /> on exhaustion.
///     There is no unlocked fallback.
/// </summary>
sealed class SubjectDestroyLock(IFileLockPrimitive primitive, SubjectDestroyLockOptions options)
{
	const string SupportedConfigurations =
		"Supported: a local filesystem that honors flock on macOS and Linux (APFS, ext4, xfs) or byte-range locks " +
		"on Windows (NTFS). Network and synced filesystems (NFS, SMB, cloud-drive folders) and " +
		"DOTNET_SYSTEM_IO_DISABLEFILELOCKING are unsupported.";

	readonly Dictionary<string, Gate> _gates = [];
	readonly Lock _gatesLock = new();

	/// <summary>Takes the destroy lock for <paramref name="subjectId" /> in <paramref name="root" />.</summary>
	/// <returns>The held lock; disposing it releases the file lock, then the in-process gate.</returns>
	/// <exception cref="TimeoutException">The lock was not obtained within the bounded attempts.</exception>
	public async ValueTask<IAsyncDisposable> AcquireAsync(string root, Guid subjectId,
		CancellationToken cancellationToken = default)
	{
		var path = Path.GetFullPath(Path.Combine(root, $"{subjectId:N}.destroy.lock"));
		var gate = Rent(path);
		try
		{
			var queued = await gate.Semaphore.WaitAsync(options.MaxDelay * options.MaxAttempts, cancellationToken)
				.ConfigureAwait(false);
			if (!queued)
				throw new TimeoutException($"Timed out queuing in-process for the destroy lock '{path}'.");
		}
		catch
		{
			Return(path, gate);
			throw;
		}

		try
		{
			var held = await AcquireFileLockAsync(path, cancellationToken).ConfigureAwait(false);
			return new Releaser(this, path, gate, held);
		}
		catch
		{
			gate.Semaphore.Release();
			Return(path, gate);
			throw;
		}
	}

	/// <summary>
	///     The startup exclusion self-test: a probe file locked through one handle must refuse a second handle.
	/// </summary>
	/// <exception cref="InvalidOperationException">Exclusion could not be demonstrated in <paramref name="root" />.</exception>
	public void VerifyExclusion(string root)
	{
		// A fresh probe per test: two processes starting together on one root never contend for it.
		var probe = Path.Combine(root, $"{Guid.NewGuid():N}.exclusion.probe");
		try
		{
			using var first = primitive.TryAcquire(probe);
			using var second = first is null ? null : primitive.TryAcquire(probe);
			if (first is null || second is not null)
				throw new InvalidOperationException(ExclusionFailure(root));
		}
		catch (Exception exception) when (exception is not InvalidOperationException)
		{
			throw new InvalidOperationException(ExclusionFailure(root), exception);
		}
		finally
		{
			File.Delete(probe);
		}
	}

	static string ExclusionFailure(string root) =>
		$"The development key store cannot demonstrate exclusive file locking in '{root}', so it cannot " +
		$"guarantee one authoritative erasure receipt per subject. {SupportedConfigurations}";

	async ValueTask<IDisposable> AcquireFileLockAsync(string path, CancellationToken cancellationToken)
	{
		var delay = options.InitialDelay;
		for (var attempt = 1; ; attempt++)
		{
			Exception? failure = null;
			try
			{
				if (primitive.TryAcquire(path) is { } held)
					return held;
			}
			catch (Exception exception)
			{
				failure = exception; // a faulted primitive is a lock not held, never a lock assumed
			}

			if (attempt >= options.MaxAttempts)
				throw new TimeoutException(
					$"The destroy lock '{path}' was not obtained after {attempt} attempts. {SupportedConfigurations}",
					failure);

			await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
			delay = delay * 2 < options.MaxDelay ?
				delay * 2 :
				options.MaxDelay;
		}
	}

	Gate Rent(string path)
	{
		lock (_gatesLock)
		{
			if (!_gates.TryGetValue(path, out var gate))
			{
				gate = new();
				_gates.Add(path, gate);
			}

			gate.References++;
			return gate;
		}
	}

	void Return(string path, Gate gate)
	{
		lock (_gatesLock)
		{
			if (--gate.References > 0)
				return;

			_gates.Remove(path);
			gate.Dispose();
		}
	}

	/// <summary>One subject's in-process queue, reference-counted so idle subjects don't accumulate.</summary>
	sealed class Gate : IDisposable
	{
		public SemaphoreSlim Semaphore { get; } = new(1, 1);

		public int References { get; set; }

		public void Dispose() =>
			Semaphore.Dispose();
	}

	sealed class Releaser(SubjectDestroyLock owner, string path, Gate gate, IDisposable held) : IAsyncDisposable
	{
		int _released;

		public ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _released, 1) is 1)
				return ValueTask.CompletedTask;

			try
			{
				held.Dispose();
			}
			finally
			{
				gate.Semaphore.Release();
				owner.Return(path, gate);
			}

			return ValueTask.CompletedTask;
		}
	}
}

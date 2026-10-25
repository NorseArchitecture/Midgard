using System.Text;
using System.Text.Json;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Backend.Keys;

namespace Norse.Infrastructure.Backend.Tests.Keys;

/// <summary>
///     The linearizable destruction contract (spec §7.5): one authoritative receipt per subject, beginning at
///     durable publication of the pending file, returned to every racing caller and every retry.
/// </summary>
public sealed class DevelopmentSubjectKeyStoreDestroyTests : IDisposable
{
	// Exhaustion in milliseconds, not the default's ten seconds.
	static SubjectDestroyLockOptions Fast { get; } = new(3, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(5));

	readonly KeyStoreRoot _files = new();

	static CancellationToken Cancellation =>
		TestContext.Current.CancellationToken;

	string Root =>
		_files.Path;

	public void Dispose() =>
		_files.Dispose();

	[Fact]
	async Task Concurrent_destroys_in_process_return_the_same_receipt_as_stored()
	{
		var subject = await KeyedSubjectAsync();
		DevelopmentSubjectKeyStore shared = new(Root);
		TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
		var destroys = Enumerable.Range(0, 16)
			.Select(index => Task.Run(async () =>
				{
					// Half queue on one instance's in-process gate; half open their own, so only the file lock
					// stands between them.
					var store = index % 2 is 0 ? shared : new(Root);
					await start.Task;
					return await store.DestroyAsync(subject, Cancellation);
				},
				Cancellation))
			.ToArray();

		start.SetResult();
		var receipts = await Task.WhenAll(destroys);

		var stored = await File.ReadAllBytesAsync(ReceiptPath(subject), Cancellation);
		foreach (var receipt in receipts)
			Serialize(receipt).ShouldBe(stored);
		ShouldBeDestroyed(subject);
	}

	[Fact]
	async Task Concurrent_destroys_across_processes_return_the_same_receipt_as_stored()
	{
		var subject = await KeyedSubjectAsync();
		using var children = await ChildProcesses.StartReadyAsync(2, ["--destroy", Root, subject.ToString("N")],
			Cancellation);
		DevelopmentSubjectKeyStore store = new(Root);
		await children.SignalAsync(Cancellation);

		var local = Enumerable.Range(0, 4)
			.Select(_ => Task.Run(async () => Serialize(await store.DestroyAsync(subject, Cancellation)),
				Cancellation));
		var remote = children.ReadResultsAsync("RECEIPT", Cancellation);
		var receipts = (await Task.WhenAll(local)).Concat((await remote).Select(Encoding.UTF8.GetBytes));

		var stored = await File.ReadAllBytesAsync(ReceiptPath(subject), Cancellation);
		foreach (var receipt in receipts)
			receipt.ShouldBe(stored);
		ShouldBeDestroyed(subject);
	}

	[Fact]
	async Task Failure_before_publication_leaves_the_key_intact_and_no_receipt()
	{
		var subject = await KeyedSubjectAsync();
		var key = await File.ReadAllBytesAsync(KeyPath(subject), Cancellation);

		await ShouldFailAsync(FailingOnceAt(DestroyCheckpoint.Minted), subject);

		(await File.ReadAllBytesAsync(KeyPath(subject), Cancellation)).ShouldBe(key);
		File.Exists(PendingPath(subject)).ShouldBeFalse();
		File.Exists(ReceiptPath(subject)).ShouldBeFalse();
		Directory.EnumerateFiles(Root, $"{subject:N}.receipt*").ShouldBeEmpty();
	}

	[Fact]
	async Task Retry_after_failure_before_publication_completes_destruction()
	{
		// The lost candidate had no authority: the retry's receipt is deliberately not compared with it.
		var subject = await KeyedSubjectAsync();
		var store = FailingOnceAt(DestroyCheckpoint.Minted);
		await ShouldFailAsync(store, subject);

		var receipt = await store.DestroyAsync(subject, Cancellation);

		Serialize(receipt).ShouldBe(await File.ReadAllBytesAsync(ReceiptPath(subject), Cancellation));
		ShouldBeDestroyed(subject);
	}

	[Fact]
	async Task Retry_after_failure_after_pending_write_returns_the_published_receipt()
	{
		var subject = await KeyedSubjectAsync();
		var store = FailingOnceAt(DestroyCheckpoint.PendingPublished);
		await ShouldFailAsync(store, subject);
		File.Exists(KeyPath(subject)).ShouldBeTrue();
		var published = await File.ReadAllBytesAsync(PendingPath(subject), Cancellation);

		var receipt = await store.DestroyAsync(subject, Cancellation);

		Serialize(receipt).ShouldBe(published);
		(await File.ReadAllBytesAsync(ReceiptPath(subject), Cancellation)).ShouldBe(published);
		ShouldBeDestroyed(subject);
	}

	[Fact]
	async Task Retry_after_failure_after_key_delete_returns_the_published_receipt()
	{
		var subject = await KeyedSubjectAsync();
		var store = FailingOnceAt(DestroyCheckpoint.KeyDeleted);
		await ShouldFailAsync(store, subject);
		File.Exists(KeyPath(subject)).ShouldBeFalse();
		var published = await File.ReadAllBytesAsync(PendingPath(subject), Cancellation);

		var receipt = await store.DestroyAsync(subject, Cancellation);

		Serialize(receipt).ShouldBe(published);
		(await File.ReadAllBytesAsync(ReceiptPath(subject), Cancellation)).ShouldBe(published);
		ShouldBeDestroyed(subject);
	}

	[Fact]
	async Task Retry_after_publication_survives_a_new_store_instance()
	{
		var subject = await KeyedSubjectAsync();
		await ShouldFailAsync(FailingOnceAt(DestroyCheckpoint.PendingPublished), subject);
		var published = await File.ReadAllBytesAsync(PendingPath(subject), Cancellation);

		// Fresh instances over the same root stand in for a process restart: once to finish the pending
		// destruction, once more after promotion.
		DevelopmentSubjectKeyStore restarted = new(Root);
		Serialize(await restarted.DestroyAsync(subject, Cancellation)).ShouldBe(published);
		DevelopmentSubjectKeyStore restartedAgain = new(Root);
		Serialize(await restartedAgain.DestroyAsync(subject, Cancellation)).ShouldBe(published);
		ShouldBeDestroyed(subject);
	}

	[Fact]
	async Task Destroy_without_the_lock_leaves_the_key_and_writes_no_receipt()
	{
		var subject = await KeyedSubjectAsync();
		DevelopmentSubjectKeyStore store = new(Root, IFileLockPrimitive.ForCurrentPlatform(), Fast);
		using var other = IFileLockPrimitive.ForCurrentPlatform()
			.TryAcquire(Path.Combine(Root, $"{subject:N}.destroy.lock"));
		other.ShouldNotBeNull();

		await Should.ThrowAsync<TimeoutException>(async () => await store.DestroyAsync(subject, Cancellation));

		File.Exists(KeyPath(subject)).ShouldBeTrue();
		File.Exists(PendingPath(subject)).ShouldBeFalse();
		File.Exists(ReceiptPath(subject)).ShouldBeFalse();
	}

	async Task<Guid> KeyedSubjectAsync()
	{
		var subject = Guid.NewGuid();
		DevelopmentSubjectKeyStore store = new(Root);
		_ = await store.GetOrCreateAsync(subject, Cancellation);
		return subject;
	}

	/// <summary>A store whose destruction throws once, at <paramref name="checkpoint" />, then runs clean.</summary>
	DevelopmentSubjectKeyStore FailingOnceAt(DestroyCheckpoint checkpoint)
	{
		var armed = true;
		return new(Root)
		{
			OnDestroyCheckpoint = reached =>
			{
				if (!armed || reached != checkpoint)
					return;

				armed = false;
				throw new InvalidOperationException($"Injected failure at {checkpoint}.");
			}
		};
	}

	static async Task ShouldFailAsync(DevelopmentSubjectKeyStore store, Guid subject) =>
		(await Should.ThrowAsync<InvalidOperationException>(async () =>
			await store.DestroyAsync(subject, Cancellation))).Message.ShouldStartWith("Injected failure");

	void ShouldBeDestroyed(Guid subject)
	{
		File.Exists(ReceiptPath(subject)).ShouldBeTrue();
		File.Exists(PendingPath(subject)).ShouldBeFalse();
		File.Exists(KeyPath(subject)).ShouldBeFalse();
	}

	static byte[] Serialize(ErasureReceipt receipt) =>
		JsonSerializer.SerializeToUtf8Bytes(new ReceiptDocument(receipt.ReceiptId, receipt.SeveredAt),
			KeysJsonContext.Default.ReceiptDocument);

	string KeyPath(Guid subject) =>
		_files.KeyPath(subject);

	string PendingPath(Guid subject) =>
		_files.PendingPath(subject);

	string ReceiptPath(Guid subject) =>
		_files.ReceiptPath(subject);
}

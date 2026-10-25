using System.Text.Json;
using Norse.Abstractions.Backend.Keys;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Backend.Keys;

namespace Norse.Infrastructure.Backend.Tests.Keys;

/// <summary>
///     The lock-free readers racing the writers: a promotion between the reader's two receipt checks never
///     resurrects an erased subject, and concurrent first mints agree on one whole key.
/// </summary>
public sealed class DevelopmentSubjectKeyStoreReaderTests : IDisposable
{
	readonly KeyStoreRoot _files = new();

	static CancellationToken Cancellation =>
		TestContext.Current.CancellationToken;

	string Root =>
		_files.Path;

	public void Dispose() =>
		_files.Dispose();

	[Fact]
	async Task GetOrCreate_never_rekeys_when_promotion_lands_between_the_receipt_checks()
	{
		var subject = PendingDestruction(out var receipt);
		DevelopmentSubjectKeyStore store = new(Root) { OnReadCheckpoint = PromoteAt(subject) };

		var exception = await Should.ThrowAsync<KeyDestroyedException>(async () =>
			await store.GetOrCreateAsync(subject, Cancellation));

		exception.Receipt.ShouldBe(receipt);
		File.Exists(_files.KeyPath(subject)).ShouldBeFalse();
	}

	[Fact]
	async Task Get_answers_destroyed_when_promotion_lands_between_the_receipt_checks()
	{
		var subject = PendingDestruction(out var receipt);
		DevelopmentSubjectKeyStore store = new(Root) { OnReadCheckpoint = PromoteAt(subject) };

		var result = await store.GetAsync(subject, Cancellation);

		result.Match<ErasureReceipt?>(_ => null, destroyed => destroyed, () => null).ShouldBe(receipt);
	}

	[Fact]
	async Task Concurrent_first_GetOrCreate_in_process_returns_one_key()
	{
		var subject = Guid.NewGuid();
		DevelopmentSubjectKeyStore store = new(Root);
		TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
		var mints = Enumerable.Range(0, 16)
			.Select(_ => Task.Run(async () =>
				{
					await start.Task;
					return await store.GetOrCreateAsync(subject, Cancellation);
				},
				Cancellation))
			.ToArray();

		start.SetResult();
		var keys = await Task.WhenAll(mints);

		var stored = await File.ReadAllBytesAsync(_files.KeyPath(subject), Cancellation);
		stored.Length.ShouldBe(32);
		foreach (var key in keys)
			key.ShouldBe(stored);
	}

	[Fact]
	async Task Concurrent_first_GetOrCreate_across_processes_returns_one_key()
	{
		var subject = Guid.NewGuid();
		using var children = await ChildProcesses.StartReadyAsync(2,
			["--get-or-create", Root, subject.ToString("N")], Cancellation);
		DevelopmentSubjectKeyStore store = new(Root);
		await children.SignalAsync(Cancellation);

		var local = Enumerable.Range(0, 4)
			.Select(_ => Task.Run(async () => await store.GetOrCreateAsync(subject, Cancellation), Cancellation));
		var remote = children.ReadResultsAsync("KEY", Cancellation);
		var keys = (await Task.WhenAll(local)).Concat((await remote).Select(Convert.FromBase64String));

		var stored = await File.ReadAllBytesAsync(_files.KeyPath(subject), Cancellation);
		stored.Length.ShouldBe(32);
		foreach (var key in keys)
			key.ShouldBe(stored);
	}

	[Fact]
	async Task Key_file_is_never_observed_partially_written()
	{
		var subject = Guid.NewGuid();
		var keyPath = _files.KeyPath(subject);
		byte[]? observed = null;
		DevelopmentSubjectKeyStore store = new(Root)
		{
			OnReadCheckpoint = reached =>
			{
				if (reached is ReadCheckpoint.KeyWriting && File.Exists(keyPath))
					observed = File.ReadAllBytes(keyPath);
			}
		};

		var key = await store.GetOrCreateAsync(subject, Cancellation);

		// Mid-write, a reader finds either no key at all or the whole key; never a torn one.
		observed.ShouldBeNull();
		(await File.ReadAllBytesAsync(keyPath, Cancellation)).ShouldBe(key);
	}

	/// <summary>A subject mid-destruction: its key deleted, its receipt published but not yet promoted.</summary>
	Guid PendingDestruction(out ErasureReceipt receipt)
	{
		var subject = Guid.NewGuid();
		receipt = new(Guid.NewGuid(), DateTimeOffset.UtcNow);
		Directory.CreateDirectory(Root);
		File.WriteAllBytes(_files.PendingPath(subject),
			JsonSerializer.SerializeToUtf8Bytes(new ReceiptDocument(receipt.ReceiptId, receipt.SeveredAt),
				KeysJsonContext.Default.ReceiptDocument));
		return subject;
	}

	/// <summary>Stands in for a concurrent destroy's promotion, landing exactly between the receipt checks.</summary>
	Action<ReadCheckpoint> PromoteAt(Guid subject) =>
		reached =>
		{
			if (reached is ReadCheckpoint.BetweenReceiptChecks && File.Exists(_files.PendingPath(subject)))
				File.Move(_files.PendingPath(subject), _files.ReceiptPath(subject), overwrite: false);
		};
}

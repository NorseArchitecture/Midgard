using System.Text.Json;
using Norse.Abstractions.Backend.Keys;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Backend.Keys;

namespace Norse.Infrastructure.Backend.Tests.Keys;

public sealed class DevelopmentSubjectKeyStoreTests : IDisposable
{
	readonly string _root = Path.Combine(Path.GetTempPath(), $"norse-keys-{Guid.NewGuid():N}");
	// Deliberately mints a fresh instance per access: correct ONLY because the store is file-backed,
	// so state lives on disk, not in the instance. An in-memory refactor would silently break every
	// multi-access test here — this comment is the tripwire.
	DevelopmentSubjectKeyStore Store => new(_root);

	public void Dispose() =>
		Directory.Delete(_root, recursive: true);

	[Fact]
	async Task Get_or_create_mints_a_32_byte_key_and_get_returns_it()
	{
		var subject = Guid.NewGuid();
		var key = await Store.GetOrCreateAsync(subject, TestContext.Current.CancellationToken);
		key.Length.ShouldBe(32);
		var result = await Store.GetAsync(subject, TestContext.Current.CancellationToken);
		result.Match(k => k, _ => null!, () => null!).ShouldBe(key);
	}

	[Fact]
	async Task Get_returns_missing_for_an_unknown_subject()
	{
		var result = await Store.GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
		result.Match(_ => "available", _ => "destroyed", () => "missing").ShouldBe("missing");
	}

	[Fact]
	async Task Keys_survive_a_store_recreate()
	{
		var subject = Guid.NewGuid();
		var key = await Store.GetOrCreateAsync(subject, TestContext.Current.CancellationToken);
		DevelopmentSubjectKeyStore reopened = new(_root);
		var result = await reopened.GetAsync(subject, TestContext.Current.CancellationToken);
		result.Match(k => k, _ => null!, () => null!).ShouldBe(key);
	}

	[Fact]
	async Task Destroy_deletes_the_key_material_and_answers_destroyed_with_the_receipt()
	{
		var subject = Guid.NewGuid();
		await Store.GetOrCreateAsync(subject, TestContext.Current.CancellationToken);
		var receipt = await Store.DestroyAsync(subject, TestContext.Current.CancellationToken);
		File.Exists(Path.Combine(_root, $"{subject:N}.key")).ShouldBeFalse(); // unrecoverable from current state
		var result = await Store.GetAsync(subject, TestContext.Current.CancellationToken);
		result.Match(_ => Guid.Empty, r => r.ReceiptId, () => Guid.Empty).ShouldBe(receipt.ReceiptId);
	}

	[Fact]
	async Task Destroy_is_idempotent_and_returns_the_original_receipt()
	{
		var subject = Guid.NewGuid();
		await Store.GetOrCreateAsync(subject, TestContext.Current.CancellationToken);
		var first = await Store.DestroyAsync(subject, TestContext.Current.CancellationToken);
		var second = await Store.DestroyAsync(subject, TestContext.Current.CancellationToken);
		second.ShouldBe(first);
	}

	[Fact]
	async Task Destruction_survives_a_store_recreate_and_a_destroyed_subject_never_rekeys()
	{
		// Verify item 9 at dev-store scope: the receipt is durable, the key is gone, and
		// GetOrCreate refuses resurrection. The production provider owns the backup-window SLA.
		var subject = Guid.NewGuid();
		await Store.GetOrCreateAsync(subject, TestContext.Current.CancellationToken);
		var receipt = await Store.DestroyAsync(subject, TestContext.Current.CancellationToken);
		DevelopmentSubjectKeyStore reopened = new(_root);
		var exception = await Should.ThrowAsync<KeyDestroyedException>(
			async () => await reopened.GetOrCreateAsync(subject, TestContext.Current.CancellationToken));
		exception.Receipt.ShouldBe(receipt);
	}

	[Fact]
	async Task GetOrCreate_treats_a_pending_receipt_marker_as_destroyed_and_never_mints_a_key()
	{
		// Simulates the crash window inside DestroyAsync: the key has already been deleted and the
		// pending marker written, but the marker hasn't been promoted to the final receipt yet (the
		// marker file is created directly here, bypassing DestroyAsync, to land in exactly that
		// window). GetOrCreate must treat that the same as an already-destroyed subject — never
		// mint a fresh key underneath a subject that is mid-erasure.
		var subject = Guid.NewGuid();
		ErasureReceipt receipt = new(Guid.NewGuid(), DateTimeOffset.UtcNow);
		Directory.CreateDirectory(_root);
		await File.WriteAllBytesAsync(
			Path.Combine(_root, $"{subject:N}.receipt.pending"),
			JsonSerializer.SerializeToUtf8Bytes(new ReceiptDocument(receipt.ReceiptId, receipt.SeveredAt), KeysJsonContext.Default.ReceiptDocument),
			TestContext.Current.CancellationToken);

		var exception = await Should.ThrowAsync<KeyDestroyedException>(
			async () => await Store.GetOrCreateAsync(subject, TestContext.Current.CancellationToken));
		exception.Receipt.ShouldBe(receipt);
		File.Exists(Path.Combine(_root, $"{subject:N}.key")).ShouldBeFalse();
	}

	[Fact]
	async Task Lookup_ring_mints_a_current_key_and_answers_by_id()
	{
		var store = Store;
		_ = await store.GetOrCreateAsync(Guid.NewGuid(), TestContext.Current.CancellationToken); // touch → init
		store.CurrentKeyId.ShouldNotBeNullOrWhiteSpace();
		store.KeyIds.ShouldContain(store.CurrentKeyId);
		store.GetKey(store.CurrentKeyId).Length.ShouldBe(32);
	}

	[Fact]
	void Lookup_ring_throws_on_an_unknown_key_id() =>
		Should.Throw<KeyNotFoundException>(() => Store.GetKey("no-such-key"));
}

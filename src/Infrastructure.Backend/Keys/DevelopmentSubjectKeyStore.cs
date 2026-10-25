using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Norse.Abstractions.Backend.Keys;
using Norse.Abstractions.Contracts;

namespace Norse.Infrastructure.Backend.Keys;

/// <summary>
///     Dev-grade only: key material rests unwrapped on local disk. Never a production path — the
///     production seam is a vault-backed provider. File-backed under the store root so
///     local identities survive process restarts: one <c>{subjectId:N}.key</c> per subject (32 random
///     bytes), one <c>{subjectId:N}.receipt</c> once destroyed, and a single <c>lookup.json</c> backing
///     the blind-index keyring, auto-minted on first touch.
/// </summary>
public sealed class DevelopmentSubjectKeyStore : ISubjectKeyStore, ILookupKeyRing
{
	const string LookupRingFileName = "lookup.json";
	const string CurrentLookupKeyId = "k1";
	readonly Lock _lookupGate = new();

	readonly string _root;
	readonly SubjectDestroyLock _destroyLock;

	LookupDocument? _lookup;

	/// <summary>
	///     Opens (creating if needed) the store at <paramref name="rootPath" /> and proves, before returning, that
	///     the root's filesystem honors the per-subject destroy lock (spec §7.5).
	/// </summary>
	/// <param name="rootPath">The directory keys, receipts, and the lookup ring are read from and written to.</param>
	/// <exception cref="PlatformNotSupportedException">The operating system is not macOS, Linux, or Windows.</exception>
	/// <exception cref="InvalidOperationException">
	///     The root's filesystem can't demonstrate exclusion between two lock handles: a network or synced
	///     filesystem, or one that ignores <c>flock</c> or byte-range locks.
	/// </exception>
	public DevelopmentSubjectKeyStore(string rootPath) :
		this(rootPath, IFileLockPrimitive.ForCurrentPlatform(), SubjectDestroyLockOptions.Default)
	{
	}

	/// <summary>The test seam: substitutes the lock primitive and the acquisition bounds.</summary>
	internal DevelopmentSubjectKeyStore(string rootPath, IFileLockPrimitive lockPrimitive,
		SubjectDestroyLockOptions lockOptions)
	{
		_root = CreateRoot(rootPath);
		_destroyLock = new(lockPrimitive, lockOptions);
		_destroyLock.VerifyExclusion(_root);
	}

	/// <summary>
	///     The test seam's failure points inside <see cref="DestroyAsync" />: invoked at each
	///     <see cref="DestroyCheckpoint" />, so a test can throw there. A no-op outside tests.
	/// </summary>
	internal Action<DestroyCheckpoint> OnDestroyCheckpoint { get; init; } = static _ => { };

	/// <summary>
	///     The test seam's points inside the lock-free readers, <see cref="GetAsync" /> and
	///     <see cref="GetOrCreateAsync" />: invoked at each <see cref="ReadCheckpoint" />, so a test can change the
	///     files there. A no-op outside tests.
	/// </summary>
	internal Action<ReadCheckpoint> OnReadCheckpoint { get; init; } = static _ => { };

	/// <inheritdoc />
	public string CurrentKeyId
	{
		get
		{
			EnsureLookupRing();
			return _lookup!.Current;
		}
	}

	/// <inheritdoc />
	public IEnumerable<string> KeyIds
	{
		get
		{
			EnsureLookupRing();
			return _lookup!.Keys.Keys;
		}
	}

	/// <inheritdoc />
	public byte[] GetKey(string keyId)
	{
		EnsureLookupRing();
		return _lookup!.Keys.TryGetValue(keyId, out var base64) ?
			Convert.FromBase64String(base64) :
			throw new KeyNotFoundException($"No key ring entry for key id '{keyId}'.");
	}

	/// <inheritdoc />
	/// <remarks>
	///     Lock-free, so it must agree with <see cref="DestroyAsync" />'s three-file state machine (key / pending
	///     receipt / promoted receipt) while a destruction runs concurrently, same as
	///     <see cref="GetOrCreateAsync" />. The key comes first, so the window where the key and the pending
	///     receipt both exist still answers <c>Available</c>. The pending receipt comes before the promoted one:
	///     promotion is a single move, so the promoted receipt exists no later than the pending one disappears,
	///     and a reader that finds no pending receipt is guaranteed to find the promoted one. Checked the other
	///     way round, a promotion landing between the two checks would hide both and answer <c>Missing</c> for an
	///     erased subject. A file that vanishes between being found and being read counts as absent, and the
	///     next check answers.
	/// </remarks>
	public async ValueTask<SubjectKeyResult> GetAsync(Guid subjectId, CancellationToken cancellationToken = default)
	{
		if (await TryReadAllBytesAsync(KeyPath(subjectId), cancellationToken).ConfigureAwait(false) is { } key)
			return SubjectKeyResult.Available(key);

		return await TryReadReceiptAsync(subjectId, cancellationToken).ConfigureAwait(false) is { } receipt ?
			SubjectKeyResult.Destroyed(receipt) :
			SubjectKeyResult.Missing;
	}

	/// <inheritdoc />
	/// <remarks>
	///     Reads the same three files in the same order as <see cref="GetAsync" />, for the same reason: a
	///     promotion landing between the receipt checks must never make an erased subject look unknown, or this
	///     would mint a fresh key for it. Either receipt throws <see cref="KeyDestroyedException" /> with its
	///     content. An existing key is read without locking. A new subject's key is minted under the subject's
	///     destroy lock, which re-checks all three files first, so concurrent first calls, in this process or
	///     another sharing the root, agree on one stored key: the first to hold the lock mints it, and the rest
	///     find it. The lock is what decides the race, not the move: on Unix, .NET's no-overwrite
	///     <see cref="File.Move(string, string, bool)" /> is an existence check followed by a rename, which two
	///     racing callers can both pass. The key is published like the pending receipt (written through to a
	///     temporary file, then moved into place), so no reader ever sees a partly written key file.
	/// </remarks>
	/// <exception cref="TimeoutException">
	///     A new subject's lock was not obtained within the bounded attempts; no key was minted.
	/// </exception>
	public async ValueTask<byte[]> GetOrCreateAsync(Guid subjectId, CancellationToken cancellationToken = default)
	{
		var keyPath = KeyPath(subjectId);
		if (await TryReadAllBytesAsync(keyPath, cancellationToken).ConfigureAwait(false) is { } existing)
			return existing;

		if (await TryReadReceiptAsync(subjectId, cancellationToken).ConfigureAwait(false) is { } receipt)
			throw new KeyDestroyedException(receipt);

		var held = await _destroyLock.AcquireAsync(_root, subjectId, cancellationToken).ConfigureAwait(false);
		await using var heldDisposable = held.ConfigureAwait(false);

		// Under the lock, another first call may have minted it, or a destruction run since the reads above.
		if (await TryReadAllBytesAsync(keyPath, cancellationToken).ConfigureAwait(false) is { } minted)
			return minted;

		if (await TryReadReceiptAsync(subjectId, cancellationToken).ConfigureAwait(false) is { } destroyed)
			throw new KeyDestroyedException(destroyed);

		var key = RandomNumberGenerator.GetBytes(32);
		var temporaryPath = $"{keyPath}.tmp"; // fixed: only the lock's holder writes it
		File.Delete(temporaryPath); // a leftover from a failed attempt, never published
		await PublishAsync(keyPath, temporaryPath, key, () => OnReadCheckpoint(ReadCheckpoint.KeyWriting),
				cancellationToken)
			.ConfigureAwait(false);
		return key;
	}

	/// <summary>
	///     The subject's receipt, pending first, then promoted (see <see cref="GetAsync" /> for why that order);
	///     <see langword="null" /> when it has neither.
	/// </summary>
	async ValueTask<ErasureReceipt?> TryReadReceiptAsync(Guid subjectId, CancellationToken cancellationToken)
	{
		var pendingReceiptPath = PendingReceiptPath(subjectId);
		if (await TryReadAllBytesAsync(pendingReceiptPath, cancellationToken).ConfigureAwait(false) is { } pending)
			return ToReceipt(pending, pendingReceiptPath);

		OnReadCheckpoint(ReadCheckpoint.BetweenReceiptChecks);
		var receiptPath = ReceiptPath(subjectId);
		return await TryReadAllBytesAsync(receiptPath, cancellationToken).ConfigureAwait(false) is { } promoted ?
			ToReceipt(promoted, receiptPath) :
			null;
	}

	/// <summary>
	///     The file's contents, or <see langword="null" /> when it doesn't exist — including when a concurrent
	///     move or delete takes it between the existence check and the read.
	/// </summary>
	static async ValueTask<byte[]?> TryReadAllBytesAsync(string path, CancellationToken cancellationToken)
	{
		if (!File.Exists(path))
			return null;

		try
		{
			return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
		}
		catch (FileNotFoundException)
		{
			return null;
		}
	}

	/// <inheritdoc />
	/// <remarks>
	///     Linearizable per subject (spec §7.5): every step runs under the subject's destroy lock, taken through
	///     the platform's checked primitive, and destruction never proceeds without it — exhausting acquisition
	///     throws <see cref="TimeoutException" /> with nothing touched. Under the lock: a promoted receipt is
	///     returned as stored; a pending receipt finishes the key delete if one is still due, is promoted, and is
	///     returned; a key mints a candidate, publishes it as the pending receipt atomically (temporary file,
	///     flushed to disk, moved into place without overwrite), deletes the key, promotes, and returns.
	///     <para>
	///         The published pending receipt is the one authoritative receipt: every later call, racing or
	///         retrying, in this process or another sharing the root, after a restart or not, returns its
	///         stored bytes, never a fresh mint. A failure before publication leaves the key intact and no
	///         receipt, so a retry completes destruction under a fresh candidate, which becomes authoritative
	///         on its own publication. Promotion is an atomic no-overwrite move; a failure before it leaves the
	///         pending receipt for the next call to promote. <see cref="GetAsync" /> and
	///         <see cref="GetOrCreateAsync" /> read the same key / receipt / pending files, so they answer
	///         <c>Destroyed</c> off the pending receipt from the key delete onward.
	///     </para>
	/// </remarks>
	/// <exception cref="TimeoutException">The subject's destroy lock was not obtained; nothing was changed.</exception>
	public async ValueTask<ErasureReceipt> DestroyAsync(Guid subjectId, CancellationToken cancellationToken = default)
	{
		var held = await _destroyLock.AcquireAsync(_root, subjectId, cancellationToken).ConfigureAwait(false);
		await using var heldDisposable = held.ConfigureAwait(false);

		var receiptPath = ReceiptPath(subjectId);
		if (File.Exists(receiptPath))
			return await ReadReceiptAsync(receiptPath, cancellationToken).ConfigureAwait(false);

		var keyPath = KeyPath(subjectId);
		var pendingReceiptPath = PendingReceiptPath(subjectId);
		if (File.Exists(pendingReceiptPath))
		{
			var pendingReceipt = await ReadReceiptAsync(pendingReceiptPath, cancellationToken).ConfigureAwait(false);
			File.Delete(keyPath); // idempotent: already gone if the prior attempt got this far
			File.Move(pendingReceiptPath, receiptPath, overwrite: false);
			return pendingReceipt;
		}

		if (!File.Exists(keyPath))
			throw new KeyMissingException(subjectId);

		var published = JsonSerializer.SerializeToUtf8Bytes(
			new ReceiptDocument(Guid.NewGuid(), DateTimeOffset.UtcNow), KeysJsonContext.Default.ReceiptDocument);
		OnDestroyCheckpoint(DestroyCheckpoint.Minted);

		var temporaryPath = $"{pendingReceiptPath}.tmp"; // fixed: only the destroy lock's holder writes it
		File.Delete(temporaryPath); // a leftover from a failed attempt, never published
		await PublishAsync(pendingReceiptPath, temporaryPath, published, static () => { }, cancellationToken)
			.ConfigureAwait(false);
		OnDestroyCheckpoint(DestroyCheckpoint.PendingPublished);

		File.Delete(keyPath); // unrecoverable from current state
		OnDestroyCheckpoint(DestroyCheckpoint.KeyDeleted);

		File.Move(pendingReceiptPath, receiptPath, overwrite: false);
		return ToReceipt(published, pendingReceiptPath);
	}

	/// <summary>
	///     Writes <paramref name="contents" /> to <paramref name="path" /> atomically: written through to disk in
	///     <paramref name="temporaryPath" />, which must not exist, then moved into place without overwrite, so
	///     the path either doesn't exist or holds the whole contents. <paramref name="writing" /> runs once the
	///     temporary file is open, before its contents are written.
	/// </summary>
	/// <remarks>
	///     Callers hold the subject's lock: the no-overwrite move refuses an existing <paramref name="path" />, but
	///     on Unix it checks before it renames, so it is not an atomic guard on its own.
	/// </remarks>
	static async ValueTask PublishAsync(string path, string temporaryPath, byte[] contents, Action writing,
		CancellationToken cancellationToken)
	{
		// Write-through (O_SYNC on Unix, FILE_FLAG_WRITE_THROUGH on Windows): the write reaches the disk before
		// it returns, so the move below never publishes contents that a crash could still lose.
		FileStream stream = new(temporaryPath,
			new FileStreamOptions
			{
				Mode = FileMode.CreateNew,
				Access = FileAccess.Write,
				Share = FileShare.None,
				Options = FileOptions.WriteThrough | FileOptions.Asynchronous
			});
		await using (stream.ConfigureAwait(false))
		{
			writing();
			await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
			await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
		}

		File.Move(temporaryPath, path, overwrite: false);
	}

	string KeyPath(Guid subjectId) =>
		Path.Combine(_root, $"{subjectId:N}.key");

	string ReceiptPath(Guid subjectId) =>
		Path.Combine(_root, $"{subjectId:N}.receipt");

	string PendingReceiptPath(Guid subjectId) =>
		Path.Combine(_root, $"{subjectId:N}.receipt.pending");

	static async ValueTask<ErasureReceipt> ReadReceiptAsync(string receiptPath, CancellationToken cancellationToken) =>
		ToReceipt(await File.ReadAllBytesAsync(receiptPath, cancellationToken).ConfigureAwait(false), receiptPath);

	static ErasureReceipt ToReceipt(byte[] contents, string receiptPath)
	{
		var document = JsonSerializer.Deserialize(contents, KeysJsonContext.Default.ReceiptDocument) ??
			throw new InvalidOperationException($"Receipt file '{receiptPath}' deserialized to null.");
		return new(document.ReceiptId, document.SeveredAt);
	}

	void EnsureLookupRing()
	{
		if (_lookup is not null)
			return;

		lock (_lookupGate)
		{
			if (_lookup is not null)
				return;

			var lookupPath = LookupPath();
			if (File.Exists(lookupPath))
			{
				_lookup = JsonSerializer.Deserialize(File.ReadAllBytes(lookupPath),
						KeysJsonContext.Default.LookupDocument) ??
					throw new InvalidOperationException($"Lookup ring file '{lookupPath}' deserialized to null.");
				return;
			}

			Dictionary<string, string> keys = new()
			{
				[CurrentLookupKeyId] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
			};
			LookupDocument minted = new(CurrentLookupKeyId, keys);
			File.WriteAllBytes(lookupPath,
				JsonSerializer.SerializeToUtf8Bytes(minted, KeysJsonContext.Default.LookupDocument));
			_lookup = minted;
		}
	}

	string LookupPath() =>
		Path.Combine(_root, LookupRingFileName);

	static string CreateRoot(string rootPath)
	{
		Directory.CreateDirectory(rootPath);
		return rootPath;
	}
}

/// <summary>
///     The points inside <see cref="DevelopmentSubjectKeyStore.GetAsync" /> and
///     <see cref="DevelopmentSubjectKeyStore.GetOrCreateAsync" /> a test can act at.
/// </summary>
enum ReadCheckpoint
{
	/// <summary>Not a checkpoint.</summary>
	None = 0,

	/// <summary>Between the readers' pending-receipt and promoted-receipt checks, where a promotion can land.</summary>
	BetweenReceiptChecks = 1,

	/// <summary>A new key's file is open and its bytes are about to be written.</summary>
	KeyWriting = 2
}

/// <summary>The points inside <see cref="DevelopmentSubjectKeyStore.DestroyAsync" /> a test can fail at.</summary>
enum DestroyCheckpoint
{
	/// <summary>Not a checkpoint.</summary>
	None = 0,

	/// <summary>The candidate receipt is minted in memory; nothing is published yet.</summary>
	Minted = 1,

	/// <summary>The pending receipt is durably published; the key is still present.</summary>
	PendingPublished = 2,

	/// <summary>The key is deleted; the pending receipt is not yet promoted.</summary>
	KeyDeleted = 3
}

/// <summary>The on-disk shape of a <c>{subjectId:N}.receipt</c> file.</summary>
sealed record ReceiptDocument(Guid ReceiptId, DateTimeOffset SeveredAt);

/// <summary>The on-disk shape of <c>lookup.json</c>: the current key id and every key the ring can answer for.</summary>
sealed record LookupDocument(string Current, Dictionary<string, string> Keys);

/// <summary>
///     Source-generated JSON binding for the two file-backed document shapes this store owns — a bounded,
///     statically-known type set, so this is the AOT/trim-safe path rather than a suppression (contrast
///     <c>SystemTextJsonSerializer</c>'s <c>ISerializer</c> arm, which is genuinely generic over an
///     unbounded caller-supplied <c>T</c> and suppresses instead).
/// </summary>
[JsonSerializable(typeof(ReceiptDocument))]
[JsonSerializable(typeof(LookupDocument))]
sealed partial class KeysJsonContext : JsonSerializerContext;

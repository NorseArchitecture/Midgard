using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Norse.Abstractions.Backend.Keys;
using Norse.Abstractions.Contracts;

namespace Norse.Infrastructure.Backend.Keys;

/// <summary>
/// Dev-grade only: key material rests unwrapped on local disk. Never a production path — the
/// production seam is a vault-backed provider. File-backed under <paramref name="rootPath"/> so
/// local identities survive process restarts: one <c>{subjectId:N}.key</c> per subject (32 random
/// bytes), one <c>{subjectId:N}.receipt</c> once destroyed, and a single <c>lookup.json</c> backing
/// the blind-index keyring, auto-minted on first touch.
/// </summary>
/// <param name="rootPath">The directory keys, receipts, and the lookup ring are read from and written to.</param>
public sealed class DevelopmentSubjectKeyStore(string rootPath) : ISubjectKeyStore, ILookupKeyRing
{
	const string LookupRingFileName = "lookup.json";
	const string CurrentLookupKeyId = "k1";

	readonly string _root = CreateRoot(rootPath);
	readonly Lock _lookupGate = new();

	LookupDocument? _lookup;

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
	/// Must agree with <see cref="DestroyAsync"/>'s three-file state machine (key / receipt /
	/// pending-marker), same as <see cref="GetOrCreateAsync"/>: the key-exists check stays first (so
	/// the narrow key-exists-and-marker-exists sub-window still correctly answers <c>Available</c>),
	/// then the final receipt, then the pending marker. Without the marker check, the crash window
	/// between the key delete and the marker promotion — which can persist indefinitely on a
	/// transient I/O failure, not just briefly — would answer <c>Missing</c> while
	/// <see cref="GetOrCreateAsync"/> throws <see cref="KeyDestroyedException"/> for the same
	/// subject; checking all three files here keeps the two methods in agreement on every subject
	/// state, always.
	/// </remarks>
	public async ValueTask<SubjectKeyResult> GetAsync(Guid subjectId, CancellationToken cancellationToken = default)
	{
		var keyPath = KeyPath(subjectId);
		if (File.Exists(keyPath))
			return SubjectKeyResult.Available(await File.ReadAllBytesAsync(keyPath, cancellationToken).ConfigureAwait(false));

		var receiptPath = ReceiptPath(subjectId);
		if (File.Exists(receiptPath))
			return SubjectKeyResult.Destroyed(await ReadReceiptAsync(receiptPath, cancellationToken).ConfigureAwait(false));

		var pendingReceiptPath = PendingReceiptPath(subjectId);
		return File.Exists(pendingReceiptPath) ?
			SubjectKeyResult.Destroyed(await ReadReceiptAsync(pendingReceiptPath, cancellationToken).ConfigureAwait(false)) :
			SubjectKeyResult.Missing;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Must agree with <see cref="DestroyAsync"/>'s three-file state machine (key / receipt /
	/// pending-marker), not just the final key/receipt pair: during the crash window between the
	/// key delete and the marker promotion, only the pending marker proves the subject is mid-
	/// destroy. Treating that window as "unknown subject" would mint a fresh key for a subject
	/// already being erased — this checks the marker and throws <see cref="KeyDestroyedException"/>
	/// off its (not-yet-promoted) receipt content, same as the final-receipt case, rather than ever
	/// re-keying.
	/// </remarks>
	public async ValueTask<byte[]> GetOrCreateAsync(Guid subjectId, CancellationToken cancellationToken = default)
	{
		var keyPath = KeyPath(subjectId);
		if (File.Exists(keyPath))
			return await File.ReadAllBytesAsync(keyPath, cancellationToken).ConfigureAwait(false);

		var receiptPath = ReceiptPath(subjectId);
		if (File.Exists(receiptPath))
			throw new KeyDestroyedException(await ReadReceiptAsync(receiptPath, cancellationToken).ConfigureAwait(false));

		var pendingReceiptPath = PendingReceiptPath(subjectId);
		if (File.Exists(pendingReceiptPath))
			throw new KeyDestroyedException(await ReadReceiptAsync(pendingReceiptPath, cancellationToken).ConfigureAwait(false));

		var key = RandomNumberGenerator.GetBytes(32);
		await File.WriteAllBytesAsync(keyPath, key, cancellationToken).ConfigureAwait(false);
		return key;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Crash-safety ordering: a bare key/receipt file pair cannot tell "this subject never had a
	/// key" apart from "a prior destroy was interrupted mid-flight" — both leave neither file on
	/// disk. The pending-receipt marker resolves that: it is written (durably proving intent to
	/// destroy, receipt content and all) <em>before</em> the key is touched, so any crash point
	/// leaves resumable evidence. A crash before the marker write leaves the key untouched and no
	/// marker — indistinguishable from never having started, which is correct, nothing happened
	/// yet. A crash after the marker write but before the key delete resumes by finishing the
	/// delete and promoting the same marker (not minting a second receipt). A crash after the
	/// delete but before the promotion resumes by promoting the marker outright. In every case
	/// <see cref="GetAsync"/> — which checks the key file, the final receipt file, and the pending
	/// marker, same three files as <see cref="GetOrCreateAsync"/> — answers <c>Destroyed</c> off the
	/// marker's (not-yet-promoted) receipt for the entire window between the key disappearing and the
	/// marker being promoted; it can never show a still-available key alongside a receipt that has
	/// already been handed out, and a destroyed subject can never be silently re-keyed by a retried
	/// destroy.
	/// </remarks>
	public async ValueTask<ErasureReceipt> DestroyAsync(Guid subjectId, CancellationToken cancellationToken = default)
	{
		var receiptPath = ReceiptPath(subjectId);
		if (File.Exists(receiptPath))
			return await ReadReceiptAsync(receiptPath, cancellationToken).ConfigureAwait(false);

		var keyPath = KeyPath(subjectId);
		var pendingReceiptPath = PendingReceiptPath(subjectId);

		// A surviving marker proves a destroy was already under way for this subject — resume it
		// (finish deleting the key, then promote the same receipt) rather than re-deciding
		// KeyMissingException vs. proceed, which the key file's mere absence cannot answer.
		if (File.Exists(pendingReceiptPath))
		{
			var pendingReceipt = await ReadReceiptAsync(pendingReceiptPath, cancellationToken).ConfigureAwait(false);
			File.Delete(keyPath); // idempotent — already gone if the prior attempt got this far
			File.Move(pendingReceiptPath, receiptPath);
			return pendingReceipt;
		}

		if (!File.Exists(keyPath))
			throw new KeyMissingException(subjectId);

		ErasureReceipt receipt = new(Guid.NewGuid(), DateTimeOffset.UtcNow);
		ReceiptDocument document = new(receipt.ReceiptId, receipt.SeveredAt);
		await File.WriteAllBytesAsync(pendingReceiptPath, JsonSerializer.SerializeToUtf8Bytes(document, KeysJsonContext.Default.ReceiptDocument), cancellationToken).ConfigureAwait(false);
		File.Delete(keyPath); // unrecoverable from current state
		File.Move(pendingReceiptPath, receiptPath);
		return receipt;
	}

	string KeyPath(Guid subjectId) =>
		Path.Combine(_root, $"{subjectId:N}.key");

	string ReceiptPath(Guid subjectId) =>
		Path.Combine(_root, $"{subjectId:N}.receipt");

	string PendingReceiptPath(Guid subjectId) =>
		Path.Combine(_root, $"{subjectId:N}.receipt.pending");

	static async ValueTask<ErasureReceipt> ReadReceiptAsync(string receiptPath, CancellationToken cancellationToken)
	{
		var bytes = await File.ReadAllBytesAsync(receiptPath, cancellationToken).ConfigureAwait(false);
		var document = JsonSerializer.Deserialize(bytes, KeysJsonContext.Default.ReceiptDocument) ??
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
				_lookup = JsonSerializer.Deserialize(File.ReadAllBytes(lookupPath), KeysJsonContext.Default.LookupDocument) ??
					throw new InvalidOperationException($"Lookup ring file '{lookupPath}' deserialized to null.");
				return;
			}

			Dictionary<string, string> keys = new()
			{
				[CurrentLookupKeyId] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
			};
			LookupDocument minted = new(CurrentLookupKeyId, keys);
			File.WriteAllBytes(lookupPath, JsonSerializer.SerializeToUtf8Bytes(minted, KeysJsonContext.Default.LookupDocument));
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

/// <summary>The on-disk shape of a <c>{subjectId:N}.receipt</c> file.</summary>
sealed record ReceiptDocument(Guid ReceiptId, DateTimeOffset SeveredAt);

/// <summary>The on-disk shape of <c>lookup.json</c>: the current key id and every key the ring can answer for.</summary>
sealed record LookupDocument(string Current, Dictionary<string, string> Keys);

/// <summary>
/// Source-generated JSON binding for the two file-backed document shapes this store owns — a bounded,
/// statically-known type set, so this is the AOT/trim-safe path rather than a suppression (contrast
/// <c>SystemTextJsonSerializer</c>'s <c>ISerializer</c> arm, which is genuinely generic over an
/// unbounded caller-supplied <c>T</c> and suppresses instead).
/// </summary>
[JsonSerializable(typeof(ReceiptDocument))]
[JsonSerializable(typeof(LookupDocument))]
sealed partial class KeysJsonContext : JsonSerializerContext;

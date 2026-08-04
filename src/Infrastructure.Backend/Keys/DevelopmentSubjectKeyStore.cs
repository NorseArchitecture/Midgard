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
	public async ValueTask<SubjectKeyResult> GetAsync(Guid subjectId, CancellationToken cancellationToken = default)
	{
		var keyPath = KeyPath(subjectId);
		if (File.Exists(keyPath))
			return SubjectKeyResult.Available(await File.ReadAllBytesAsync(keyPath, cancellationToken).ConfigureAwait(false));

		var receiptPath = ReceiptPath(subjectId);
		return File.Exists(receiptPath) ?
			SubjectKeyResult.Destroyed(await ReadReceiptAsync(receiptPath, cancellationToken).ConfigureAwait(false)) :
			SubjectKeyResult.Missing;
	}

	/// <inheritdoc />
	public async ValueTask<byte[]> GetOrCreateAsync(Guid subjectId, CancellationToken cancellationToken = default)
	{
		var keyPath = KeyPath(subjectId);
		if (File.Exists(keyPath))
			return await File.ReadAllBytesAsync(keyPath, cancellationToken).ConfigureAwait(false);

		var receiptPath = ReceiptPath(subjectId);
		if (File.Exists(receiptPath))
			throw new KeyDestroyedException(await ReadReceiptAsync(receiptPath, cancellationToken).ConfigureAwait(false));

		var key = RandomNumberGenerator.GetBytes(32);
		await File.WriteAllBytesAsync(keyPath, key, cancellationToken).ConfigureAwait(false);
		return key;
	}

	/// <inheritdoc />
	public async ValueTask<ErasureReceipt> DestroyAsync(Guid subjectId, CancellationToken cancellationToken = default)
	{
		var receiptPath = ReceiptPath(subjectId);
		if (File.Exists(receiptPath))
			return await ReadReceiptAsync(receiptPath, cancellationToken).ConfigureAwait(false);

		var keyPath = KeyPath(subjectId);
		if (!File.Exists(keyPath))
			throw new KeyMissingException(subjectId);

		ErasureReceipt receipt = new(Guid.NewGuid(), DateTimeOffset.UtcNow);
		ReceiptDocument document = new(receipt.ReceiptId, receipt.SeveredAt);
		await File.WriteAllBytesAsync(receiptPath, JsonSerializer.SerializeToUtf8Bytes(document, KeysJsonContext.Default.ReceiptDocument), cancellationToken).ConfigureAwait(false);
		File.Delete(keyPath); // unrecoverable from current state
		return receipt;
	}

	string KeyPath(Guid subjectId) =>
		Path.Combine(_root, $"{subjectId:N}.key");

	string ReceiptPath(Guid subjectId) =>
		Path.Combine(_root, $"{subjectId:N}.receipt");

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

namespace Norse.Infrastructure.Backend.Tests;

/// <summary>A fresh temporary store root, and the paths of one subject's files in it; deleted on dispose.</summary>
sealed class KeyStoreRoot : IDisposable
{
	public string Path { get; } =
		System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"norse-keys-{Guid.NewGuid():N}");

	public void Dispose() =>
		Directory.Delete(Path, recursive: true);

	public string KeyPath(Guid subject) =>
		System.IO.Path.Combine(Path, $"{subject:N}.key");

	public string PendingPath(Guid subject) =>
		System.IO.Path.Combine(Path, $"{subject:N}.receipt.pending");

	public string ReceiptPath(Guid subject) =>
		System.IO.Path.Combine(Path, $"{subject:N}.receipt");
}

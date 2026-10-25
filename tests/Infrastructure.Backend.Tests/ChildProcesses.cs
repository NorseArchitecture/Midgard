using System.Diagnostics;

namespace Norse.Infrastructure.Backend.Tests;

/// <summary>
///     Children in one of <see cref="Program" />'s racing modes (<c>--destroy</c>, <c>--get-or-create</c>): each
///     reports <c>READY</c>, waits for the start signal, then reports one <c>{prefix} {payload}</c> result line.
///     Disposing kills any child still running.
/// </summary>
sealed class ChildProcesses(Process[] children) : IDisposable
{
	/// <summary>Starts <paramref name="count" /> children with <paramref name="arguments" />, each one ready.</summary>
	public static async Task<ChildProcesses> StartReadyAsync(int count, string[] arguments,
		CancellationToken cancellationToken = default)
	{
		Process[] started = [.. Enumerable.Range(0, count).Select(_ => TestHostProcess.Start(arguments))];
		ChildProcesses processes = new(started);
		try
		{
			foreach (var child in started)
				(await child.StandardOutput.ReadLineAsync(cancellationToken)).ShouldBe("READY");

			return processes;
		}
		catch
		{
			processes.Dispose();
			throw;
		}
	}

	/// <summary>Releases every child at once.</summary>
	public async Task SignalAsync(CancellationToken cancellationToken = default)
	{
		foreach (var child in children)
		{
			await child.StandardInput.WriteLineAsync("GO");
			await child.StandardInput.FlushAsync(cancellationToken);
		}
	}

	/// <summary>Each child's payload after <paramref name="prefix" />, once it has exited cleanly.</summary>
	public Task<string[]> ReadResultsAsync(string prefix, CancellationToken cancellationToken = default) =>
		Task.WhenAll(children.Select(async child =>
		{
			var line = await child.StandardOutput.ReadLineAsync(cancellationToken);
			line.ShouldNotBeNull();
			line.ShouldStartWith($"{prefix} ");
			await child.WaitForExitAsync(cancellationToken);
			child.ExitCode.ShouldBe(0);
			return line[(prefix.Length + 1)..];
		}));

	public void Dispose()
	{
		foreach (var child in children)
		{
			if (!child.HasExited)
				child.Kill();
			child.Dispose();
		}
	}
}

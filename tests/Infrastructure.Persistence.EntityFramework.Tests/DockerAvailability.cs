using System.Diagnostics;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

// No real "skip-when-no-docker" precedent exists anywhere on the platform to copy verbatim —
// Mimisbrunnr/tests/Reference.Data.Migrations.SqlServer.Tests (the fixture Task 6's dispatch
// pointed at) only carries a design-time-factory unit test with no container and no Docker gate
// at all; Task 6's own SqlServerContainerFixture is the platform's first real Testcontainers use
// against SQL Server. This is a from-scratch, minimal gate in the spirit of the one precedent that
// does exist (VoyageLiveSmokeTests.cs's `[Fact(SkipUnless = nameof(HasApiKey), Skip = "...")]`
// static-bool-gate shape) — a `docker info` probe stands in for the API-key check. Docker is
// confirmed running for this task's actual run, so this path is exercised only for portability,
// never as the live gate.
static class DockerAvailability
{
	public static bool IsAvailable { get; } = Probe();

	static bool Probe()
	{
		try
		{
			using Process process = new()
			{
				StartInfo = new ProcessStartInfo("docker", "info")
				{
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
				},
			};
			process.Start();
			return process.WaitForExit(TimeSpan.FromSeconds(5)) && process.ExitCode == 0;
		}
		catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			// docker CLI not on PATH at all — the same "not available" outcome as a daemon that
			// won't answer, not a reason to fail the whole run loudly: this is a portability probe,
			// not production code the platform's no-silent-fallback law binds.
			return false;
		}
	}
}

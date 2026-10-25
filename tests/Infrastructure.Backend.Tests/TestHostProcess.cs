using System.Diagnostics;

namespace Norse.Infrastructure.Backend.Tests;

/// <summary>Launches this test assembly as a child process in one of <see cref="Program" />'s child modes.</summary>
static class TestHostProcess
{
	/// <summary>Starts the child with stdin and stdout redirected; the caller owns and disposes it.</summary>
	public static Process Start(params string[] arguments)
	{
		ProcessStartInfo start = new(Environment.ProcessPath!)
		{
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			UseShellExecute = false
		};
		if (Path.GetFileNameWithoutExtension(start.FileName) is "dotnet")
		{
			start.ArgumentList.Add("exec");
			start.ArgumentList.Add(typeof(TestHostProcess).Assembly.Location);
		}

		foreach (var argument in arguments)
			start.ArgumentList.Add(argument);

		return Process.Start(start)!;
	}
}

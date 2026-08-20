// Every test in this assembly builds a TracerProvider that subscribes ASP.NET Core instrumentation,
// which listens to a process-wide DiagnosticListener. Two providers alive at once both receive every
// host's requests, so one test class's in-memory exporter sees another's traffic and any
// ShouldBeEmpty() assertion goes non-deterministic. Observed as an intermittent 11/13 vs 12/13 during
// Task 3. The subject here is process-global diagnostic state; it cannot be tested in parallel.

using Xunit.Sdk;
using Xunit.v3;

[assembly: Parallelization(Mode = ParallelMode.None)]

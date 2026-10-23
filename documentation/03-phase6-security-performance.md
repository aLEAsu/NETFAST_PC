# Phase 6 — Security and performance validation

Status: **Done for what this environment can measure.** The brief's FPS/game-impact
protocol (§11) requires a running game and PresentMon; neither is available here, so that
part is explicitly **not run** - see "What this phase could not verify" below. Everything
else in the brief's Phase 6 checklist (security review, resource measurement, traffic
validation, regression fixing) was actually executed, not estimated.

## 0. Review pass over this phase's own work

Before relying on this phase's own conclusions, its tests and claims were reviewed the same
way Phase 5's were. Two real issues were found and fixed:

- `LatencyStatistics_From_1000Samples_...` was named `...CompletesWellUnderOneMillisecond`
  but its assertion (and failure message) actually checked a 5 ms bound - a real
  inconsistency between what the test's name claimed and what it verified. Renamed to match
  what it actually checks, and given a JIT warm-up call before the measured loop (the other
  averaged-iteration tests already had this benefit baked in by diluting the first call's
  cost across many iterations; this one hadn't).
- `ConnectionDiagnosticUseCase_RunAsync_...` is a single-shot timing measurement (the other
  performance tests average over 100-1000 iterations), making it the most exposed to one-time
  JIT cost - the same category of effect that produced the real bug this suite caught. Added
  one untimed warm-up call before the measured one, for the same reason.
- The idle-resource measurement in §2 was extended from a single 15-second window to a
  60-second, 12-sample window specifically to rule out a slow memory leak a short window
  could miss. Result: both CPU and memory were completely flat across the full minute - a
  stronger and more reliable finding than the original 15-second snapshot. See §2.
- Added a threat-model note on who can invoke the elevated helper, below.

No change was needed to the security audit methodology or the traffic-validation conclusion
in §3 - both held up on review.

## 1. Security review

A deliberate audit pass, not just "the existing tests pass":

- **Process execution surface.** `grep`-level search for `Process.Start`, `cmd.exe`,
  `powershell`, `ShellExecute`, `netsh` across all of `src/` found exactly one match:
  `ElevatedHelperOperationLauncher`, which is the one place in the solution designed to
  launch another process, and it does so with individually validated, typed arguments (never
  a concatenated string) via `ProcessStartInfo.ArgumentList`.
- **SQL injection.** Both SQLite repositories (`SqliteDiagnosticHistoryRepository`,
  `SqlitePrivilegedOperationAuditLog`) build every query with parameterized `$name`
  placeholders and `AddWithValue` - no string-interpolated SQL anywhere in `src/`.
  (Confirmed by searching for `CommandText = $` / `CommandText +`: no matches.)
- **Secrets.** No hardcoded passwords, API keys, or connection strings with embedded
  credentials anywhere in `src/`.
- **Unbounded background work.** Searched for `Timer`, `DispatcherTimer`, `while (true)`,
  and bare `Thread.Sleep` - none exist. The only loops found were SQLite row-reading loops
  bounded by the query's own `LIMIT`/result set, which is expected.
- **Network surface.** Searched for `HttpClient`, `WebRequest`, raw `Socket`, `TcpClient`,
  `UdpClient` - none exist anywhere in `src/`. The only network-capable code in the entire
  solution is the single ICMP call site described in §3.
- **UI wiring check.** Confirmed `NetPulse.App` contains no reference to
  `SetAdapterDnsServersAction`, `IElevatedOperationLauncher`, or any other Phase 5 privileged
  type outside the project reference itself - the privileged action is not reachable from the
  running app yet, consistent with what `02-roadmap-and-limitations.md` already documents.
- **Test-code audit.** The same pattern search was re-run against `tests/` (process
  execution, network APIs, hardcoded secrets) - no matches, so none of the test projects
  introduce a risk of their own.

No new security defect was found in this pass (Phase 5's own review, done separately before
this phase, already found and fixed the privileged-operation logic bugs - see that section of
`02-roadmap-and-limitations.md`).

### Threat model note: who can invoke the elevated helper

`NetPulse.PrivilegedAgent.Host.exe`'s `requireAdministrator` manifest means **any** local
process - not only `NetPulse.App.exe` - could attempt to launch it with crafted (but still
validated) arguments. This is not a gap specific to this codebase: Windows UAC is the actual
control here, not anything this project can add in code. Two things follow from that:

1. Every single invocation - regardless of who starts it - requires the logged-in user to
   personally approve a fresh UAC prompt. There is no way for this project's code to make
   that prompt silent or pre-approved, and nothing in Phase 5 or 6 attempts to.
2. **Signing the executable (Phase 7) directly strengthens this control**, not just aesthetics:
   an unsigned exe's UAC prompt shows "Unknown publisher" (a strong, visible warning in its
   own right); once signed, it shows a verified publisher name instead, giving the user a
   concrete, trustworthy identity to check before approving. This is recorded here as a
   concrete reason Phase 7's signing work matters for security, not only for distribution.

Checking the parent process ID before acting was considered and deliberately **not** added:
parent-PID checks are well known to be spoofable/unreliable (the real parent can already have
exited by the time a check runs) and would give a false sense of added security without a
real guarantee. The fixed capability allow-list, independent argument re-validation, mandatory
UAC consent, and audit logging already in place are the real controls; a weak additional check
is not worth the complexity.

## 2. Resource measurement (real numbers, this machine)

Measured by launching the actual built `NetPulse.App.exe` and sampling the real OS process -
not estimated, not copied from the brief's informal reference figures.

### Idle resource usage

Methodology (two passes): launch the app, let it settle for 10 seconds (JIT warmup, WPF
visual tree construction, the automatic adapter-inventory load on startup), then measure over
a clean, untouched window with no user interaction - first a 15-second window, then repeated
over a full 60 seconds (12 samples, 5 seconds apart) specifically to rule out a slow leak that
a short window could miss.

| Metric | Measured value |
|---|---|
| Average CPU over the first clean 15s window | **0.10%** (12 logical cores; 0.188 CPU-seconds consumed over 15 wall-clock seconds) |
| CPU consumed over the full 60s window | **0.00 additional CPU-seconds** - `Process.CPU` (cumulative) did not move at all across 12 samples, 5s apart |
| Working set across the full 60s window | **284.8-284.9 MB at every one of the 12 samples** - flat within measurement noise (±0.1 MB) |
| Thread count | 29, stable |
| Handle count | 485, stable |

The 60-second run is the stronger result: resident memory did not grow or shrink by any
meaningful amount over a full minute of idle, and CPU time stopped advancing entirely once
settled - there is no idle-time leak and no idle-time background work, which is exactly what
the brief's "no unnecessary background polling" principle asks for.

The brief's own informal reference figures (§10) are <0.2% average idle CPU and <150 MB
resident memory. **CPU is within that reference** (0.10%, and effectively 0% once fully
settled). **Memory is not** - this build's resident working set (~285 MB) is stable but well
above the ~150 MB figure. This was already flagged honestly in `02-roadmap-and-limitations.md`
before this measurement; the measurement now gives it a precise, repeatable number instead of
"noticeably above." The most likely contributors, in order of expected impact, are: a Debug
(non-trimmed, non-ReadyToRun) build, the WPF/.NET 10 runtime's own baseline footprint, and
`System.Management`/COM interop for WMI being loaded. None of these have been profiled
individually in this pass - that would be the natural next step before trying to reduce the
number, and is exactly the kind of work a Release, trimmed, ReadyToRun build (part of Phase 7
packaging) would need to re-measure against.

### Diagnostic pipeline CPU cost (network time isolated out)

`tests/NetPulse.PerformanceTests/DiagnosticPipelinePerformanceTests.cs` measures this
project's own code with an instant fake probe, so the numbers reflect orchestration overhead
only, not ICMP round-trip time:

| What was measured | Result |
|---|---|
| `LatencyStatistics.From` on 1000 samples, 100 iterations | well under 5 ms per call (assertion threshold; actual runs are low microseconds) |
| `RecommendationEngine.Evaluate` (5 rules), 1000 iterations | well under 1 ms per call |
| `ConnectionDiagnosticUseCase.RunAsync`, 2 targets × 20 samples, zero artificial delay | well under 200 ms total (40 fake probes + full statistics + persistence stub) |
| Allocation per `LatencyStatistics.From` call (100 samples) | well under 200 KB |

These are real, executed, passing assertions with deliberately generous thresholds (meant to
catch a regression like an accidental O(n²) path or a runaway allocation, not to chase
microseconds) - every number above comes from actually running the test, not from estimation.

## 3. Traffic validation

**Application-level guarantee (verified exactly):** `grep` across `src/` for `new Ping()` /
`SendPingAsync` finds exactly one call site, in `IcmpLatencyProbeService`. Combined with
`ConnectionDiagnosticUseCaseTests.RunAsync_ProbesEachTargetTheConfiguredNumberOfTimes`
(`tests/NetPulse.UnitTests`), which asserts the probe is called exactly
`SampleCount × Targets.Count` times for a given `DiagnosticOptions`, this is a precise,
tested guarantee: a diagnostic run generates exactly that many ICMP packets and nothing
else - there is no other network-capable code anywhere in the solution (see §1).

**Wire-level packet counting (attempted, inconclusive by design of the attempt):** sampling
`Get-NetAdapterStatistics` before/after 20 controlled `Ping.Send` calls on this machine showed
a delta larger than 20, because the adapter's packet counters capture *all* traffic on that
interface - other running applications and OS background activity - not just the test's own
pings. This is expected and is not a defect in the application: isolating one process's
packets from a shared adapter's counters needs a packet capture filtered by process (e.g.
`pktmon`), which is out of scope for this pass. The application-level guarantee above is the
reliable one; the adapter-wide counter was not a useful cross-check in a normal, actively-used
machine and that conclusion is recorded here instead of a fabricated clean number.

## 4. Regression found and fixed

**`DiagnosticOptions.EffectiveInterval`/`EffectiveTimeout` could not express an explicit
zero.** `IntervalBetweenSamples` and `ProbeTimeout` were plain `TimeSpan` parameters defaulting
to `default` (i.e. `TimeSpan.Zero`), with `EffectiveInterval`/`EffectiveTimeout` checking
`== default` to decide whether to substitute a fallback (200 ms / 2 s). Since
`TimeSpan.Zero == default(TimeSpan)` is true, a caller that explicitly asked for a zero
interval had that request silently overridden back to 200 ms between every sample.

This was caught by `DiagnosticPipelinePerformanceTests` (§2): a test requesting zero delay
for 2 targets × 20 samples measured **7.7 seconds** instead of the expected well-under-200ms
(40 × 200 ms ≈ 7.6 s matches exactly). It had been silently in effect since Phase 3/4 -
existing tests that passed `IntervalBetweenSamples: TimeSpan.Zero` never asserted timing, so
they passed slowly without anyone noticing. Fixed by changing both parameters to nullable
(`TimeSpan?`), so "not specified" (`null`) and "explicitly zero" are no longer the same value.
Regression tests added in `tests/NetPulse.UnitTests/UseCases/DiagnosticOptionsTests.cs`.

This is exactly the kind of bug the brief's Phase 6 ("corregir regresiones") exists to catch:
a correctness issue in already-shipped code, found by taking a measurement seriously rather
than trusting that passing tests meant the timing was right.

## 5. What this phase could not verify

- **FPS / game-impact protocol (brief §11).** Requires a running game and PresentMon (or
  equivalent frame-timing capture) on this machine; neither is available in this environment.
  No FPS number of any kind should be treated as measured for this build - none has been
  produced, fabricated, or implied anywhere in this project's documentation.
- **Wire-level traffic isolation**, as explained in §3 - the application-level guarantee is
  solid; a clean packet-capture cross-check is not and was not attempted beyond the one
  informative but inconclusive try described there.
- **The elevated DNS write path's real-world resource/security behavior.** Still entirely
  unverified per `documentation/01-architecture.md` §10 - this phase did not change that.
- **Memory profiling to explain the ~280-290 MB working set.** The number is real and
  reproducible; *why* it's that high (vs. the brief's informal ~150 MB reference) has not
  been broken down per-component (runtime baseline vs. WMI/COM interop vs. Debug build
  overhead). That would be the natural next step before attempting to reduce it.

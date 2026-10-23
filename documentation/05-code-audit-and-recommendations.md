# Code audit and recommendations (post-Phase-7 completion pass)

Scope: a deliberate read-through of every project in `src/` and `tests/`, not just "the
tests pass." This complements (doesn't replace) the narrower audits already done in the
Phase 5 verification pass and Phase 6's security review - see
`02-roadmap-and-limitations.md` and `03-phase6-security-performance.md` for those. This pass
found and fixed two new defects, implemented the completions requested (TCP/DNS probes,
DNS-action UI wiring, Profiles), and produces the recommendations below for what's next.

## 1. Defects found and fixed in this pass

### 1.1 `DashboardViewModel.LoadAdaptersAsync` had no exception handling

**Where:** `src/NetPulse.App/ViewModels/DashboardViewModel.cs`, and the WPF
`MainWindow.xaml.cs` constructor that calls it.

**The bug:** `MainWindow`'s constructor wires `Loaded += async (_, _) =>
await viewModel.LoadAdaptersCommand.ExecuteAsync(null);`. An `async` lambda assigned to an
event handler is effectively `async void` - any exception thrown inside propagates to WPF's
unhandled-exception machinery, not back to a caller that could catch it. `LoadAdaptersAsync`
had no try/catch, unlike `RunDiagnosticAsync`, which did. A transient failure enumerating
adapters (a WMI hiccup, a driver timing out) would have crashed the entire app on startup,
silently, with no error shown to the user - worse than any diagnostic failure this project
otherwise tries hard to report clearly.

**The fix:** wrapped the body in try/catch, same pattern as `RunDiagnosticAsync`, reporting
the failure via `StatusText` instead of letting it propagate.

### 1.2 No global unhandled-exception backstop

**Where:** `src/NetPulse.App/App.xaml.cs`.

**The bug:** nothing handled `Application.DispatcherUnhandledException`. Even after fixing
1.1, any *other* future code path that forgets a try/catch would still crash the app
silently - there was no safety net for the class of bug 1.1 represents, only a fix for one
instance of it.

**The fix:** added a `DispatcherUnhandledException` handler that shows the exception message
in a `MessageBox` and marks it handled, so the app degrades to "something went wrong, here's
why" instead of disappearing. This is a backstop, not a substitute for catching expected
failures close to where they happen - `LoadAdaptersAsync`/`RunDiagnosticAsync` still have
their own try/catch for the failures they can anticipate.

### 1.3 (Found during Phase 6, listed here for completeness of the audit trail)

`DiagnosticOptions.EffectiveInterval`/`EffectiveTimeout` could not express an explicit zero -
see `03-phase6-security-performance.md` §4. Not re-litigated here; mentioned only so this
document is a complete audit trail alongside the Phase 5 and Phase 6 ones.

## 2. What this pass implemented (not defects - the requested completions)

| Area | What exists now | Where |
|---|---|---|
| TCP connectivity confirmation | Real TCP handshake probe, one per target per run | `ITcpConnectProbeService`, `TcpConnectProbeService` |
| DNS resolution timing | Real DNS lookup probe, skipped for IP-literal targets | `IDnsResolutionProbeService`, `DnsResolutionProbeService` |
| ICMP-filtered vs. actually-down | `ConnectionDiagnosticUseCase.Classify` now takes the TCP result into account | `src/NetPulse.Application/UseCases/ConnectionDiagnosticUseCase.cs` |
| DNS-slowness recommendation | New rule, the only one with an applyable action behind it | `SlowDnsResolutionRule` |
| Recommendation engine evidence model | Widened from `(stats, finding)` to a `NetworkEvidence` bundle so future evidence types don't need another signature change | `NetworkEvidence`, `IRecommendationRule` |
| DNS action reachable from the UI | Adapter selection, confirmation dialog, Check→Capture→Apply→Verify→Rollback wired to real buttons | `DashboardViewModel`, `MainWindow.xaml` |
| Profiles (brief Module H) | 6 profiles adjusting sample count/interval/timeout and recommendation ordering only - never applying anything | `DiagnosticProfile`, `DiagnosticProfileCatalog`, `DiagnosticProfileSettings` |

All of the above is covered by real, executed tests (see the updated counts in
`02-roadmap-and-limitations.md`), including new integration tests against a real throwaway
`TcpListener` and real DNS lookups (`localhost` / a reserved `.invalid` domain).

## 3. Structural observations (not bugs, but worth recording)

- **`RecommendationRowViewModel.ApplyableDnsRecommendationId` is a string-matching
  convention, not a type-safe link.** Domain cannot reference `PrivilegedAgent` (it would be
  circular), so the UI recognizes "this is the DNS recommendation" by comparing
  `descriptor.Id` to a constant. This works and is documented in both files, but it is the
  single point where adding a second applyable action needs *coordinated* changes across
  three layers (the rule's Id, the ViewModel's recognition logic, and the action
  construction). If a second applyable action is added, consider a small lookup table
  (`Dictionary<string, Func<...>>` mapping recommendation id → action factory) instead of
  more `if` branches in the ViewModel, to keep that coordination point singular.
- **`SetAdapterDnsServersAction` is constructed directly in the ViewModel, not resolved
  through DI**, because it needs per-use runtime values (which adapter, which servers) that
  don't exist until the user acts. This is correct for one action; it won't scale cleanly to
  several without a small factory abstraction (see above).
- **`MessageBox.Show` is called directly from `DashboardViewModel`**, a deliberate, documented
  MVVM compromise (see the doc comment on `ApplyDnsRecommendationAsync`). It makes that one
  method harder to unit-test (nothing today exercises the confirmation-dialog branch under
  test, since `MessageBox.Show` can't be faked without introducing an abstraction). If a
  second confirmable action is ever added, introduce an `IUserConfirmation` port at that
  point rather than before - not needed for one call site.
- **Profiles are honest about not unlocking new capabilities.** `DiagnosticProfileCatalog`'s
  doc comment explicitly says Gaming/VideoCalls profiles don't add a throughput or
  stability-over-time test that doesn't exist. Worth preserving this framing if profiles grow
  - it would be easy to let a future profile's description overclaim what it actually does.

## 4. Security re-check (narrow, since Phase 6 already did the broad pass)

Re-ran the same category of checks Phase 6 used, scoped to the new code:

- `TcpConnectProbeService`/`DnsResolutionProbeService`: no raw sockets beyond `TcpClient`/
  `Dns`, no payload sent beyond the TCP handshake itself, both respect cancellation and
  timeout, neither can be coerced into connecting anywhere the caller didn't explicitly ask
  for (target/port come from `DiagnosticOptions`, ultimately from the user's own target list
  and the fixed `TcpConfirmationPort` default).
- `ApplyDnsRecommendationAsync`: the DNS servers offered are a fixed, hardcoded pair
  (`1.1.1.1`, `8.8.8.8`) - **not** user-editable in this build, and not derived from anything
  the diagnostic measured. This is deliberately conservative: the UI cannot be made to
  propose an attacker-influenced DNS server through any diagnostic input, because the
  proposed value never varies. If a future version lets the user pick or type a DNS server,
  `DnsServerAddressValidator` (already in place) is the control that must keep validating it
  - re-verify it is still called on that new path when it's added.
- `DiagnosticProfileCatalog`: closed enum, closed switch, no external configuration source -
  same "fixed, auditable catalog" property `DefaultRecommendationRules` already has.

No new security defect found in this re-check.

## 5. Recommendations, prioritized

1. **Get a person to run the DNS action for real, once, deliberately**, before trusting it
   in any way beyond "the code is reviewed and the fakes-based tests pass." This is the
   single highest-value next step - everything else in Phase 5 is downstream of it being
   actually exercised. See `06-continuation-brief.md` for exactly what to check.
2. **Add the TCP-connect/DNS-resolution timing to the dashboard UI.** They're computed and
   available on every `DiagnosticRunResult` but `FindingRowViewModel` doesn't surface them
   yet - a real, user-visible gap between what the backend measures and what the screen
   shows. Cheap to close (extend `FindingRowViewModel`, add DataGrid columns), not done in
   this pass to keep this pass's own scope from expanding further.
3. **Design review before adding a second `IOptimizationAction`.** The string-convention
   link (§3) works for one action; decide the lookup-table approach *before* a second action
   makes the ViewModel's branching logic the place bugs hide.
4. **Don't let "Phase N done" headers go stale again.** Several counts/claims across
   `01-architecture.md` needed correcting in this pass purely because they were written at a
   point in time and never revisited as the codebase moved past them. This document and
   `02-roadmap-and-limitations.md`'s "post-Phase-7" section are the current source of truth;
   treat section-level prose in `01-architecture.md` as historical narrative, not a live
   status feed, unless it's re-verified.
5. See `06-continuation-brief.md` for the prioritized, detailed plan on the four modules
   still unstarted (bufferbloat, per-process bandwidth, pktmon/ETW, advanced NIC properties)
   and the other open items (WinUI 3, real certificate, per-machine install option).

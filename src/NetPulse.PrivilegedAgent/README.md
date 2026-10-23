# NetPulse.PrivilegedAgent

Holds the privileged optimization actions and orchestrates reaching the elevated boundary -
it never performs a write itself. See `documentation/01-architecture.md` §10 for the full
elevation architecture and `documentation/02-roadmap-and-limitations.md` for what is and
isn't verified yet.

## What's here

- `SetAdapterDnsServersAction` — the first real `IOptimizationAction`. Its `CheckAsync`/
  `CaptureAsync`/`VerifyAsync` only ever call read-only ports. Its `ApplyAsync`/`RollbackAsync`
  hand a validated `PrivilegedOperationRequest` to `IElevatedOperationLauncher` and log the
  result; the actual write happens in a separate process (`NetPulse.PrivilegedAgent.Host`),
  never in this project.

## Rules for adding a second action

1. The capability must be added to `NetPulse.Domain.PrivilegedOperations.PrivilegedCapability`
   first - there is no way to add one without that enum change, a corresponding case in
   `NetPulse.PrivilegedAgent.Host.Program.Run`, and a code review of both.
2. It must implement `IOptimizationAction` with a real, specific `OptimizationDescriptor` -
   not a copy-pasted one with placeholder text.
3. Its `ApplyAsync`/`RollbackAsync` must go through `IElevatedOperationLauncher`, never touch
   the registry/WMI/adapter settings directly from this (unelevated) project.
4. It must log every phase through `IPrivilegedOperationAuditLog`.
5. Nothing here should be implemented by copying commands from internet guides; the
   evidence/risk/rollback contract in `IOptimizationAction` comes first, the code second.

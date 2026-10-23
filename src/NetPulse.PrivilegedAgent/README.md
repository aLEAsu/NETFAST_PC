# NetPulse.PrivilegedAgent

Out of scope for the current build. This project exists so the capability allow-list and
elevation boundary have a fixed home, but it contains no elevated operations yet.

Phase 3 (the current prototype) is diagnostics-only and never requests elevation.
Phase 5 of the roadmap (see `documentation/02-roadmap.md`) is where this project gains:

- A fixed, named list of allowed capabilities (never an arbitrary-command executor).
- Request validation at the privilege boundary (no trusting that a local caller is safe).
- Per-operation audit logging and timeouts.
- A way to stop the agent safely.

Nothing here should be implemented by copying commands from internet guides; each capability
needs the evidence/risk/rollback contract defined by `NetPulse.Domain.Optimization.IOptimizationAction`
before it is added.

# Project Notes

## 2026-04-25 16:00:36 -04:00

### Overview

This repository contains three implementations of the same service:

- [Rust](../Rust)
- [Golang](../Golang)
- [Dotnet](../Dotnet)

The current work focused on understanding why the `.NET` implementation was much slower under `PerfTest`, correcting the most obvious parity and deployment issues, and then creating a separate `Dotnet.Aot` branch of the project for NativeAOT-oriented experimentation.

Relevant perf log:

- [Docs/PerfResults/Dotnet.md](./PerfResults/Dotnet.md)

### What Was Done

#### 1. Reviewed all three service implementations

The Rust, Go, and `.NET` projects were inspected side by side to compare:

- endpoint contracts
- query behavior
- data-access patterns
- deployment topology
- gateway configuration

The first major finding was that `.NET` did not honor the `limit` query parameter used by `PerfTest`, which made requests such as `limit=10` and `limit=10000` behave the same.

#### 2. Fixed the main `.NET` API parity and hot-path issues

The working `Dotnet` service was updated to:

- accept `limit`
- keep `take` as a backward-compatible alias
- support `author_id` filtering on `/api/v1/books`
- reduce hot-path logging noise
- add indexes for `ORDER BY title` paths
- cap PostgreSQL pool size
- switch VM deployment to a local OpenTelemetry Collector pattern
- add a script to watch PostgreSQL connection metrics during perf runs

This brought the `.NET` project closer to the Rust and Go service behavior under load.

#### 3. Verified Nginx was not the root cause

The effective VM Nginx configuration was checked across all three services and found to be equivalent. The remaining high-load differences were therefore more likely to be inside the `.NET` service or in its deployment/runtime topology rather than at the reverse proxy layer.

#### 4. Investigated application CPU behavior under load

After the initial fixes, one important observation remained:

- `.NET` could saturate the VM CPU while PostgreSQL CPU stayed low

That changed the diagnosis from “DB-bound” to “application-CPU-bound”. A dispatcher hot path in the `.NET` application layer was then simplified to remove per-request reflection and `dynamic` dispatch overhead.

#### 5. Created a separate `Dotnet.Aot` project

Rather than converting the main `.NET` service in place, a separate AOT-oriented copy was created:

- [Dotnet.Aot](../Dotnet.Aot)

This new project was refactored to remove the most obvious NativeAOT blockers:

- assembly scanning for DI registration
- runtime generic construction in the dispatcher
- EF Core configuration scanning
- reflection-based JSON metadata fallback
- anonymous minimal API payloads on basic endpoints

### Current State

#### Dotnet

The main [Dotnet](../Dotnet) project remains the active, working implementation and contains the performance and deployment improvements made during this review.

#### Dotnet.Aot

The new [Dotnet.Aot](../Dotnet.Aot) project is an AOT-oriented variant intended for further experimentation. It passes the copied unit and integration tests, but a full NativeAOT publish was not conclusively verified in this sandboxed environment.

### Recommended Next Steps

1. Keep using `Dotnet` as the primary benchmarkable `.NET` service until `Dotnet.Aot` proves publishable and deployable.
2. Run a real `PublishAot=true` publish for `Dotnet.Aot` in a normal connected build environment.
3. If publish succeeds, deploy `Dotnet.Aot` as a separate image and compare it directly against the current `Dotnet`, `Golang`, and `Rust` services.
4. Continue documenting each perf investigation iteration in [Docs/PerfResults/Dotnet.md](./PerfResults/Dotnet.md).

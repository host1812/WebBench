# Perf Tests Summary

## 2026-04-25

### Scope

This summary now covers:

1. the earlier no-health comparison documented in [PerfTestsResults.md](./PerfTestsResults.md)
2. the later health-enabled reruns for:
   - `Dotnet.Aot`
   - `Golang`
   - `Rust`

The PostgreSQL CPU graph referenced in this note is the Azure Monitor screenshot provided alongside these results. The graph shows three visible workload spikes:

- a medium plateau around `12%` to `13%`
- a high plateau around `27%`
- a low plateau around `5%`

Because the screenshot does not encode service names directly, any mapping from spike-to-service must still be treated as an inference from run order and the k6 outputs.

## Current Best Reading Of The Data

### 1. Dotnet.Aot is now the only clean pass in the health-enabled reruns

The health-enabled rerun results are:

- `Dotnet.Aot`: `100%` checks succeeded, `0%` failed requests
- `Golang`: `85.23%` checks succeeded, `14.76%` failed requests
- `Rust`: `84.78%` checks succeeded, `15.21%` failed requests

So under the current script shape, `Dotnet.Aot` is the only service in the captured reruns that completed the full workload successfully.

### 2. Go and Rust data endpoints still succeed, but their health endpoints fail under load

For both Go and Rust:

- `authors` succeeded
- all `books_limit_*` endpoints succeeded
- only `health` failed

The failure counts line up exactly:

- `Rust`: `4820` failed checks total and `4820` failed `health` checks
- `Golang`: `9723` failed checks total and `9723` failed `health` checks

That strongly suggests:

- the rerun failures are entirely explained by `/health`
- the main data paths are still behaving correctly

### 3. Dotnet.Aot currently has the strongest combination of correctness, throughput, and latency

In the current health-enabled reruns, `Dotnet.Aot` shows:

- `230.01` HTTP requests/sec
- `38.34` iterations/sec
- `2.01s` average request duration
- `3.54s` p95 request duration
- `0%` request failure

By comparison:

- `Golang` is slower and fails health under load
- `Rust` is much slower and also fails health under load

Within the currently available health-enabled runs, `Dotnet.Aot` is the strongest overall result.

### 4. The low PostgreSQL CPU reading for the Dotnet.Aot run now looks materially more credible

Previously, low PostgreSQL CPU for the `.NET` run could be explained away by the failed `authors` endpoint.

That explanation is no longer viable as the main interpretation because:

- `Dotnet.Aot` now has a clean rerun
- it still shows very strong throughput and latency
- the PostgreSQL CPU screenshot still suggests the lowest DB CPU plateau for the `.NET`-family run if the inferred ordering is correct

That does not prove a universal winner, but it does strengthen the case that:

- this workload is not primarily limited by raw PostgreSQL CPU
- `Dotnet.Aot` may currently be converting database work into completed HTTP throughput more efficiently than the other captured implementations

## Health Endpoint Interpretation

### 1. Rust and Go health behavior is now the clearest new finding

The most important new information is not just that `Dotnet.Aot` is fast. It is that:

- Go and Rust both degrade specifically at `/health`
- the rest of their checked endpoints still return HTTP `200`

That means the dominant current issue for those reruns is health-check robustness, not broad endpoint failure.

### 2. The health failures look consistent with timeout or pool contention behavior

The health latencies are revealing:

- `Rust health`: avg `2.15s`, med `2.22s`, p95 `2.56s`
- `Golang health`: avg `1.94s`, med `2.05s`, p95 `2.07s`
- `Dotnet.Aot health`: avg `69.21ms`, med `59.14ms`, p95 `120.32ms`

The Go and Rust health timings cluster around the `2s` mark, which is notable because both implementations use a `2s` health timeout in the request path.

This strongly suggests that under high load:

- the health check is often waiting too long for a connection or DB roundtrip
- the request crosses the timeout boundary
- the endpoint returns a non-`200` status

This is an inference from the measured timings and code behavior, but it is the most coherent explanation of the rerun data.

### 3. Dotnet.Aot’s health endpoint is not a bottleneck

`Dotnet.Aot` health is cheap:

- avg `69.21ms`
- p95 `120.32ms`
- p99 `177.72ms`

So for the current AOT run, `/health` is not materially distorting the workload.

## Endpoint-Level Comparison

### Dotnet.Aot

Current health-enabled rerun:

- `authors`: `2.38s` avg
- `books_limit_10`: `2.14s` avg
- `books_limit_100`: `2.07s` avg
- `books_limit_1000`: `2.37s` avg
- `books_limit_10000_default`: `3.04s` avg
- `health`: `69.21ms` avg

This is the most balanced current result in the captured data.

### Golang

Current health-enabled rerun:

- `authors`: `2.62s` avg
- `books_limit_10`: `2.36s` avg
- `books_limit_100`: `2.39s` avg
- `books_limit_1000`: `2.59s` avg
- `books_limit_10000_default`: `3.35s` avg
- `health`: `1.94s` avg, with major failure rate

Ignoring `/health`, Go still looks strong and clearly ahead of Rust. But the current script includes `/health`, so the run does not count as a full pass.

### Rust

Current health-enabled rerun:

- `authors`: `6.83s` avg
- `books_limit_10`: `5.14s` avg
- `books_limit_100`: `4.63s` avg
- `books_limit_1000`: `5.66s` avg
- `books_limit_10000_default`: `8.23s` avg
- `health`: `2.15s` avg, with major failure rate

Rust remains the weakest current performer in the captured set.

## Relationship To The PostgreSQL CPU Screenshot

### Likely mapping

Assuming the graph order was:

1. `Rust`
2. `Golang`
3. `Dotnet.Aot`

then the approximate PostgreSQL CPU plateaus line up as:

- `Rust`: about `12%` to `13%`
- `Golang`: about `27%`
- `Dotnet.Aot`: about `5%`

### Updated interpretation

The current best interpretation is:

- Go appears to push the database hardest
- Rust appears to push it moderately while delivering the weakest total performance
- Dotnet.Aot appears able to complete the workload successfully while driving comparatively low PostgreSQL CPU

That suggests:

- the database is not the dominant bottleneck for this benchmark
- `Dotnet.Aot` may currently be more efficient for this specific workload shape

It is still important not to overclaim from a single screenshot, but the current AOT rerun makes the low DB CPU signal meaningfully stronger than it was before.

## Revised Overall Ranking

### For the current health-enabled captures

1. `Dotnet.Aot`
   - only clean pass
   - best throughput
   - best overall latency
   - low apparent PostgreSQL CPU
2. `Golang`
   - strong data-path performance
   - clear second place on latency and throughput
   - disqualified from a full pass by health failures
3. `Rust`
   - slowest data-path performance
   - health failures under load
   - weakest current result overall

### For the older no-health comparison only

1. `Golang`
2. `Rust`
3. `Dotnet.Aot` initial failed run

That older ranking is still historically accurate for that earlier run shape, but it is no longer the best guide to the current system state.

## Important Caveats

### 1. The health-enabled reruns and the no-health comparison are different workloads

They should not be collapsed into a single strict ranking without calling out the script difference.

### 2. Database CPU is still only one signal

It must be interpreted together with:

- request failure rate
- endpoint coverage
- throughput
- latency distribution

### 3. The current Go and Rust failures may say more about health-check design than about the data endpoints

Because only `health` fails in those reruns, it would be wrong to summarize the result as “Go and Rust are generally broken.” The more accurate statement is:

- their primary workload endpoints succeeded
- their health endpoints were not resilient enough under this load pattern

## Recommended Next Steps

1. Investigate why `Rust` and `Golang` health checks degrade around the `2s` timeout boundary under load.
2. Check whether DB pool contention or connection acquisition delay is the dominant cause of those health failures.
3. Decide whether `/health` should continue to perform a real DB probe on every public request in this benchmark, or whether a less contended readiness pattern is more appropriate.
4. Capture a fresh PostgreSQL CPU screenshot with explicit timestamps and run ordering so the graph-to-service mapping is unambiguous.

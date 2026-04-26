# Perf Tests Summary

## 2026-04-25

### Scope

This summary analyzes the captured no-health perf-test run documented in [PerfTestsResults.md](./PerfTestsResults.md).

The compared services were:

- `Golang`
- `Rust`
- `Dotnet.Aot`

The PostgreSQL CPU graph referenced in this note is the Azure Monitor screenshot provided alongside these results. The graph shows three visible workload spikes:

- a medium plateau around `12%` to `13%`
- a high plateau around `27%`
- a low plateau around `5%`

Because the screenshot does not encode service names directly, any mapping from spike-to-service must be treated as an inference from run order and the k6 outputs.

## Main Conclusions

### 1. Golang is the best fully successful result in this data set

Go completed all checks successfully and clearly outperformed Rust:

- `144.35` HTTP requests/sec vs `68.14`
- `28.87` iterations/sec vs `13.58`
- `3.20s` average request duration vs `6.73s`
- `4.45s` p95 request duration vs `13.77s`

This is the strongest valid result in the set because it combines:

- zero request failures
- materially higher throughput than Rust
- materially lower latency than Rust

### 2. Rust is the slowest successful implementation in this run

Rust also completed all checks successfully, but it did so with:

- the worst average latency
- the worst p95 and p99 latency
- the lowest request rate
- the longest end-to-end iteration duration

The Rust numbers indicate that it drove less total completed workload through the system during the same test window.

### 3. Dotnet.Aot produced the fastest raw timings, but the run is not valid as a pass

`Dotnet.Aot` looks strongest at first glance:

- `218.58` HTTP requests/sec
- `2.11s` average request duration
- `10.58s` average iteration duration

But the run had a structural failure:

- `20%` failed requests
- `15756` failed checks total
- `15756` iterations total
- `0%` success on the `authors` endpoint

That pattern strongly implies:

- every iteration lost exactly one checked request
- the failed request was consistently `GET /api/v1/authors`

So the `Dotnet.Aot` run is not an apples-to-apples success case. It is better described as:

- high throughput on the requests that did succeed
- incomplete workload execution overall

### 4. The PostgreSQL CPU graph does not prove Dotnet.Aot is more efficient than Go or Rust

The low PostgreSQL CPU plateau for the `.NET`-family run must be interpreted together with the failed-request pattern.

If the three graph plateaus correspond to:

1. `Rust`
2. `Golang`
3. `Dotnet.Aot`

then the picture is internally consistent:

- `Rust` drives moderate DB CPU and low throughput
- `Golang` drives the highest DB CPU and the highest successful throughput
- `Dotnet.Aot` drives the lowest DB CPU while also failing one request per iteration

That last point matters. Lower DB CPU here does not automatically mean better DB efficiency. It can also mean:

- fewer successful database-backed operations per logical workload cycle
- early request failure
- less DB work being completed before the response is emitted

In other words, low DB CPU is only a positive signal when the workload is also completing correctly.

### 5. The AOT result suggests the bottleneck was not the PostgreSQL server

The `Dotnet.Aot` run achieved:

- the highest request throughput
- the lowest average request latency
- the lowest observed PostgreSQL CPU plateau

while still failing the `authors` endpoint completely.

That combination suggests the bottleneck for this run was not simply PostgreSQL saturation. More likely causes are in the service/application layer, for example:

- endpoint-specific exception behavior under load
- app-side CPU or memory pressure
- serialization or mapping differences
- provider/runtime issues specific to the AOT variant

This aligns with the earlier investigation trend that not every perf problem was DB-bound.

## Endpoint-Level Interpretation

### Golang

Go shows a stable and coherent latency progression:

- `books_limit_10`: `2.92s`
- `books_limit_100`: `2.96s`
- `books_limit_1000`: `3.15s`
- `books_limit_10000_default`: `3.86s`

That shape is what you would expect from a service that is completing the intended work and paying more as response size grows.

### Rust

Rust also shows the expected directional pattern, but with much larger tail latency:

- `books_limit_10`: `5.76s`
- `books_limit_100`: `5.30s`
- `books_limit_1000`: `6.30s`
- `books_limit_10000_default`: `8.82s`

The spread between median and high-percentile latency is notably larger than in Go, which suggests less stable behavior under pressure.

### Dotnet.Aot

`Dotnet.Aot` also shows a sensible increasing cost for larger result sets on the book endpoints:

- `books_limit_10`: `1.77s`
- `books_limit_100`: `1.77s`
- `books_limit_1000`: `1.99s`
- `books_limit_10000_default`: `3.08s`

But that cannot be read in isolation because `authors` failed on every iteration. The correct interpretation is:

- the books paths that succeeded were fast
- the overall service workload was still failing

## Relationship To The PostgreSQL CPU Screenshot

### Likely mapping

Assuming the graph order was:

1. `Rust`
2. `Golang`
3. `Dotnet.Aot`

then the approximate PostgreSQL CPU plateaus line up well with the k6 data:

- `Rust`: about `12%` to `13%`
- `Golang`: about `27%`
- `Dotnet.Aot`: about `5%`

### What that likely means

- Go completed the most successful DB-backed work and therefore drove the highest PostgreSQL CPU
- Rust completed less work and drove materially less PostgreSQL CPU
- Dotnet.Aot drove the least PostgreSQL CPU because the run did not execute the full logical workload successfully

This is the key reason the graph should not be read as “Dotnet is far more optimized than Go and Rust.”

## Important Caveats

### 1. These are no-health results

The current perf script has `/health` restored, but the captured results do not include health checks. Future runs with `/health` enabled will not be directly identical to the numbers in this document.

### 2. Dotnet.Aot was not a clean pass

Any headline ranking that places `Dotnet.Aot` first must be qualified by the failed `authors` endpoint. Without fixing that issue, the run is not a valid like-for-like comparison against the fully successful Go and Rust runs.

### 3. The PostgreSQL CPU graph should be treated as a correlated signal, not the whole answer

Database CPU alone is not enough to rank service quality. It must be interpreted together with:

- request failure rate
- successful throughput
- endpoint coverage
- latency distribution

## Recommended Next Steps

1. Fix the `Dotnet.Aot` `authors` endpoint failure before using its numbers as a performance winner.
2. Rerun the three-way comparison with the current restored `/health` endpoint so the script and documentation are back in sync.
3. Capture the exact run order in future notes so the PostgreSQL CPU graph can be mapped to services without inference.
4. If `Dotnet.Aot` becomes stable, compare it again against Go on a fully passing workload because those two currently represent the most interesting ends of the throughput spectrum.

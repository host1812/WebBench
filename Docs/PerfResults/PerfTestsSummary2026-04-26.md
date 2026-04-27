# Perf Tests Summary

## 2026-04-26

### Scope

This note adds a new `RustSimple` benchmark result to the prior performance context already captured in:

- [PerfTestsResults.md](./PerfTestsResults.md)
- [PerfTestsSummary.md](./PerfTestsSummary.md)

The important current context is that the active perf script still has `/health` commented out in [PerfTest/perf/books-read.js](../../PerfTest/perf/books-read.js). So this `RustSimple` run should be compared to the earlier no-health runs, not to the later health-enabled reruns.

That distinction matters because:

- the original no-health run shape exercises 5 checked endpoints per iteration
- the later health-enabled reruns exercise 6 checked endpoints per iteration
- `RustSimple` shows 5 successful checks per iteration and does not include actual `/health` traffic, even though the k6 summary still prints a zeroed `endpoint:health` line

## Raw RustSimple Result

```text
TOTAL RESULTS

    checks_total.......: 129385  358.912342/s
    checks_succeeded...: 100.00% 129385 out of 129385
    checks_failed......: 0.00%   0 out of 129385

    ✓ authors returned HTTP 200
    ✓ books_limit_10 returned HTTP 200
    ✓ books_limit_100 returned HTTP 200
    ✓ books_limit_1000 returned HTTP 200
    ✓ books_limit_10000_default returned HTTP 200

    HTTP
    http_req_duration..........................: avg=1.28s    min=46.81ms  med=1.31s    p(90)=1.92s p(95)=2.1s  p(99)=2.5s  max=6.02s
      { endpoint:authors }.....................: avg=1.23s    min=91.89ms  med=1.28s    p(90)=1.8s  p(95)=1.91s p(99)=2.06s max=2.28s
      { endpoint:books_limit_10 }..............: avg=1.18s    min=46.81ms  med=1.26s    p(90)=1.7s  p(95)=1.81s p(99)=1.94s max=2.25s
      { endpoint:books_limit_100 }.............: avg=943.32ms min=91.89ms  med=909.85ms p(90)=1.47s p(95)=1.65s p(99)=1.92s max=2.29s
      { endpoint:books_limit_1000 }............: avg=1.36s    min=94.83ms  med=1.45s    p(90)=1.89s p(95)=2s    p(99)=2.13s max=2.93s
      { endpoint:books_limit_10000_default }...: avg=1.68s    min=118.74ms med=1.73s    p(90)=2.32s p(95)=2.49s p(99)=3.32s max=6.02s
      { endpoint:health }......................: avg=0s       min=0s       med=0s       p(90)=0s    p(95)=0s    p(99)=0s    max=0s
      { expected_response:true }...............: avg=1.28s    min=46.81ms  med=1.31s    p(90)=1.92s p(95)=2.1s  p(99)=2.5s  max=6.02s
    http_req_failed............................: 0.00%  0 out of 129385
    http_reqs..................................: 129385 358.912342/s

    EXECUTION
    iteration_duration.........................: avg=6.41s    min=473.2ms  med=6.78s    p(90)=7.33s p(95)=7.59s p(99)=8.43s max=11.47s
    iterations.................................: 25877  71.782468/s
    vus........................................: 4      min=4           max=500
    vus_max....................................: 500    min=500         max=500

    NETWORK
    data_received..............................: 85 GB  235 MB/s
    data_sent..................................: 108 MB 301 kB/s
```

## Current Best Reading Of The New Data

### 1. RustSimple is the strongest no-health result captured so far

Against the earlier no-health comparison set:

| Service | Checks Succeeded | Failed Requests | HTTP Reqs/s | Avg HTTP Req | P95 HTTP Req | Avg Iteration | Iterations/s | Data Received |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| RustSimple | 100.00% | 0.00% | 358.91/s | 1.28s | 2.10s | 6.41s | 71.78/s | 85 GB |
| Dotnet.Aot initial | 80.00% | 20.00% | 218.58/s | 2.11s | 3.58s | 10.58s | 43.72/s | 57 GB |
| Golang initial | 100.00% | 0.00% | 144.35/s | 3.20s | 4.45s | 16.03s | 28.87/s | 33 GB |
| Rust initial | 100.00% | 0.00% | 68.14/s | 6.73s | 13.77s | 33.69s | 13.58/s | 16 GB |

Within this workload shape, `RustSimple` is clearly first on:

- correctness
- throughput
- average latency
- p95 latency
- iteration throughput

### 2. RustSimple is not just a little faster than Rust; it is a different performance tier

Compared with the earlier `Rust` no-health run:

- HTTP throughput improved from `68.14/s` to `358.91/s`
- average request latency improved from `6.73s` to `1.28s`
- p95 request latency improved from `13.77s` to `2.10s`
- iterations/sec improved from `13.58/s` to `71.78/s`
- average iteration time improved from `33.69s` to `6.41s`

That is too large to treat as normal run noise. The new result strongly suggests that the simpler Rust implementation materially reduced hot-path cost for this benchmark.

### 3. RustSimple also outperforms the earlier clean Go run by a wide margin

Against the earlier successful `Golang` no-health run:

- HTTP throughput is about `2.49x` higher (`358.91/s` vs `144.35/s`)
- average request latency is lower (`1.28s` vs `3.20s`)
- p95 latency is lower (`2.10s` vs `4.45s`)
- iterations/sec is about `2.49x` higher (`71.78/s` vs `28.87/s`)

So within the no-health comparison set, `RustSimple` is not merely better than the original Rust service. It is also substantially ahead of the earlier Go baseline.

### 4. The large-payload endpoint is still the slowest, but it remains well-controlled

The same endpoint shape still appears:

- `books_limit_100` is fastest at `943.32ms` average
- `books_limit_10` and `authors` stay around `1.2s`
- `books_limit_1000` rises to `1.36s`
- `books_limit_10000_default` is slowest at `1.68s`

That progression is normal for this benchmark, and the important point is that even the default `10_000` row path is still materially faster than the other previously captured services under the same no-health script shape.

### 5. The `endpoint:health = 0s` line should not be interpreted as a fast health endpoint

It is almost certainly an artifact of the current k6 script shape, not a measured success:

- `get('health', '/health')` is commented out in `PerfTest/perf/books-read.js`
- the run reports only five successful endpoint checks
- the zeroed `endpoint:health` row is therefore not evidence that `/health` was exercised

So `RustSimple` should not yet be compared to the health-enabled reruns for `Dotnet.Aot`, `Golang`, or `Rust`.

## Relationship To Prior Findings

### 1. The older health-enabled ranking still stands for that different script

The prior conclusion from [PerfTestsSummary.md](./PerfTestsSummary.md) remains valid for the health-enabled reruns:

1. `Dotnet.Aot`
2. `Golang`
3. `Rust`

That ranking should not be overwritten with `RustSimple`, because the workload shapes are different.

### 2. But the no-health comparison now has a new clear leader

For the no-health run set, the best current ranking is now:

1. `RustSimple`
2. `Golang`
3. `Rust`
4. `Dotnet.Aot` initial failed run

This is the most defensible reading because:

- `RustSimple` completed the workload cleanly
- it materially improved on every major latency and throughput metric
- it did so under the same effective five-endpoint script shape as the earlier original comparison

### 3. This result supports the earlier architecture comparison

The earlier code comparison in [RustVsRustSimple.md](../Comparisons/RustVsRustSimple.md) argued that:

- `Rust` was more structured and more maintainable
- `RustSimple` was flatter and more benchmark-oriented

This new benchmark result is consistent with that tradeoff. The simplified service now appears to convert this specific read-heavy workload into completed throughput much more efficiently than the layered Rust version.

That does not prove the layered architecture is wrong. It does suggest that, for this benchmark shape, the extra abstraction and service layering in `Rust` may be carrying significant hot-path cost.

## Practical Interpretation

The current best practical reading is:

- if the goal is strongest performance on the current no-health read benchmark, `RustSimple` is now the best captured implementation
- if the goal is comparison under the health-enabled script, `RustSimple` still needs a rerun with `/health` actually enabled before it can be ranked against the current `Dotnet.Aot` rerun
- the old `Rust` service should no longer be treated as representative of “Rust performance” in this repo, because `RustSimple` demonstrates a radically better result from a different Rust design

## Recommended Next Steps

1. Run `RustSimple` again with `/health` re-enabled in `PerfTest/perf/books-read.js` so it can be compared fairly against the health-enabled `Dotnet.Aot`, `Golang`, and `Rust` reruns.
2. Capture PostgreSQL CPU during the `RustSimple` run, because its `358.91/s` throughput makes the old CPU interpretation incomplete.
3. Compare `RustSimple` and `Rust` under the same health-enabled script to isolate how much of the improvement comes from handler simplicity, health behavior, or connection-use patterns.
4. If `RustSimple` stays strong with `/health` enabled, promote it to the main Rust benchmark candidate and treat the layered `Rust` service as the maintainability-oriented variant rather than the performance reference.

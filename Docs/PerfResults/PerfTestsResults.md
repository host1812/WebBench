# PerfTest Results

## 2026-04-25

### Scope

These results capture a three-service comparison for:

- `Golang`
- `Rust`
- `Dotnet.Aot`

The captured output appears to come from the temporary perf-test variant where `/health` was commented out:

- no `endpoint:health` section appears in the k6 breakdown
- the check totals are consistent with roughly five checked endpoints per iteration
- the active checked endpoints are:
  - `authors`
  - `books_limit_10`
  - `books_limit_100`
  - `books_limit_1000`
  - `books_limit_10000_default`

Important context:

- the current `PerfTest/perf/books-read.js` has since been restored to include `/health`
- the numbers below therefore describe the temporary no-health run, not the current script state

## High-Level Comparison

| Service | Checks Succeeded | Failed Requests | HTTP Reqs/s | Avg HTTP Req | P95 HTTP Req | Avg Iteration | Iterations/s | Data Received |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Golang | 100.00% | 0.00% | 144.35/s | 3.20s | 4.45s | 16.03s | 28.87/s | 33 GB |
| Rust | 100.00% | 0.00% | 68.14/s | 6.73s | 13.77s | 33.69s | 13.58/s | 16 GB |
| Dotnet.Aot | 80.00% | 20.00% | 218.58/s | 2.11s | 3.58s | 10.58s | 43.72/s | 57 GB |

## Golang

### Summary

- completed all checks successfully
- delivered the best throughput among the fully successful runs
- remained materially faster than Rust on both request latency and iteration duration
- moved a large amount of response data, which is consistent with sustaining more completed workload than Rust

### Raw Results

```text
TOTAL RESULTS

    checks_total.......: 52365   144.354431/s
    checks_succeeded...: 100.00% 52365 out of 52365
    checks_failed......: 0.00%   0 out of 52365

    ✓ authors returned HTTP 200
    ✓ books_limit_10 returned HTTP 200
    ✓ books_limit_100 returned HTTP 200
    ✓ books_limit_1000 returned HTTP 200
    ✓ books_limit_10000_default returned HTTP 200

    HTTP
    http_req_duration..........................: avg=3.2s   min=47.11ms  med=3.29s  p(90)=4.16s p(95)=4.45s  p(99)=4.87s  max=7.8s
      { endpoint:authors }.....................: avg=3.11s  min=47.11ms  med=3.28s  p(90)=3.97s p(95)=4.09s  p(99)=4.31s  max=4.63s
      { endpoint:books_limit_10 }..............: avg=2.92s  min=59.4ms   med=3.09s  p(90)=3.79s p(95)=3.92s  p(99)=4.14s  max=4.59s
      { endpoint:books_limit_100 }.............: avg=2.96s  min=61.82ms  med=3.11s  p(90)=3.83s p(95)=3.96s  p(99)=4.17s  max=4.63s
      { endpoint:books_limit_1000 }............: avg=3.15s  min=82.19ms  med=3.33s  p(90)=4s    p(95)=4.12s  p(99)=4.33s  max=4.78s
      { endpoint:books_limit_10000_default }...: avg=3.86s  min=350.97ms med=4.02s  p(90)=4.7s  p(95)=4.87s  p(99)=5.41s  max=7.8s
      { expected_response:true }...............: avg=3.2s   min=47.11ms  med=3.29s  p(90)=4.16s p(95)=4.45s  p(99)=4.87s  max=7.8s
    http_req_failed............................: 0.00% 0 out of 52365
    http_reqs..................................: 52365 144.354431/s

    EXECUTION
    iteration_duration.........................: avg=16.03s min=631.86ms med=17.13s p(90)=17.6s p(95)=17.77s p(99)=18.37s max=20.99s
    iterations.................................: 10473 28.870886/s
    vus........................................: 1     min=1          max=500
    vus_max....................................: 500   min=500        max=500

    NETWORK
    data_received..............................: 33 GB 92 MB/s
    data_sent..................................: 49 MB 134 kB/s
```

## Rust

### Summary

- completed all checks successfully
- was the slowest service in this run set
- showed the highest end-to-end iteration time by a wide margin
- returned less total data than Go because it completed substantially fewer iterations over the same test window

### Raw Results

```text
TOTAL RESULTS

    checks_total.......: 25662   68.143457/s
    checks_succeeded...: 100.00% 25662 out of 25662
    checks_failed......: 0.00%   0 out of 25662

    ✓ authors returned HTTP 200
    ✓ books_limit_10 returned HTTP 200
    ✓ books_limit_100 returned HTTP 200
    ✓ books_limit_1000 returned HTTP 200
    ✓ books_limit_10000_default returned HTTP 200

    HTTP
    http_req_duration..........................: avg=6.73s  min=49.84ms  med=6.07s p(90)=12.4s  p(95)=13.77s p(99)=15.19s max=17.71s
      { endpoint:authors }.....................: avg=7.48s  min=49.84ms  med=7.22s p(90)=12.92s p(95)=14.03s p(99)=14.97s max=15.85s
      { endpoint:books_limit_10 }..............: avg=5.76s  min=61.42ms  med=5.92s p(90)=9s     p(95)=11.64s p(99)=14.3s  max=15.33s
      { endpoint:books_limit_100 }.............: avg=5.3s   min=64.28ms  med=4.48s p(90)=9.27s  p(95)=11.5s  p(99)=14.06s max=15.44s
      { endpoint:books_limit_1000 }............: avg=6.3s   min=87.02ms  med=4.76s p(90)=12.05s p(95)=13.62s p(99)=14.89s max=15.9s
      { endpoint:books_limit_10000_default }...: avg=8.82s  min=501.67ms med=8.41s p(90)=14.04s p(95)=15s    p(99)=15.91s max=17.71s
      { expected_response:true }...............: avg=6.73s  min=49.84ms  med=6.07s p(90)=12.4s  p(95)=13.77s p(99)=15.19s max=17.71s
    http_req_failed............................: 0.00% 0 out of 25662
    http_reqs..................................: 25662 68.143457/s

    EXECUTION
    iteration_duration.........................: avg=33.69s min=1.02s    med=36.5s p(90)=37.93s p(95)=38.28s p(99)=38.83s max=41.76s
    iterations.................................: 5114  13.579832/s
    vus........................................: 12    min=12         max=500
    vus_max....................................: 500   min=500        max=500

    NETWORK
    data_received..............................: 16 GB 43 MB/s
    data_sent..................................: 22 MB 57 kB/s
```

## Dotnet.Aot

### Summary

- had the best raw latency and highest throughput numbers in the set
- did not complete the workload successfully
- failed every `authors` check
- reported exactly `15756` failed checks, which matches the `15756` completed iterations and strongly suggests one failed `authors` request per iteration
- should not be treated as a valid apples-to-apples winner against Go or Rust for this run

### Raw Results

```text
TOTAL RESULTS

    checks_total.......: 78780  218.578374/s
    checks_succeeded...: 80.00% 63024 out of 78780
    checks_failed......: 20.00% 15756 out of 78780

    ✗ authors returned HTTP 200
      ↳  0% — ✓ 0 / ✗ 15756
    ✓ books_limit_10 returned HTTP 200
    ✓ books_limit_100 returned HTTP 200
    ✓ books_limit_1000 returned HTTP 200
    ✓ books_limit_10000_default returned HTTP 200

    HTTP
    http_req_duration..........................: avg=2.11s  min=47.34ms  med=2.07s  p(90)=3.23s  p(95)=3.58s  p(99)=4.35s  max=6.82s
      { endpoint:authors }.....................: avg=1.95s  min=70.39ms  med=2.03s  p(90)=2.71s  p(95)=2.94s  p(99)=3.4s   max=3.85s
      { endpoint:books_limit_10 }..............: avg=1.77s  min=47.34ms  med=1.83s  p(90)=2.39s  p(95)=2.56s  p(99)=2.99s  max=3.78s
      { endpoint:books_limit_100 }.............: avg=1.77s  min=47.83ms  med=1.8s   p(90)=2.42s  p(95)=2.6s   p(99)=3.15s  max=3.77s
      { endpoint:books_limit_1000 }............: avg=1.99s  min=50.41ms  med=2.03s  p(90)=2.66s  p(95)=2.97s  p(99)=3.53s  max=4.02s
      { endpoint:books_limit_10000_default }...: avg=3.08s  min=88.67ms  med=3.14s  p(90)=4.03s  p(95)=4.35s  p(99)=4.94s  max=6.82s
      { expected_response:true }...............: avg=2.15s  min=47.34ms  med=2.08s  p(90)=3.35s  p(95)=3.68s  p(99)=4.45s  max=6.82s
    http_req_failed............................: 20.00% 15756 out of 78780
    http_reqs..................................: 78780  218.578374/s

    EXECUTION
    iteration_duration.........................: avg=10.58s min=312.28ms med=11.28s p(90)=11.68s p(95)=11.83s p(99)=12.23s max=15.27s
    iterations.................................: 15756  43.715675/s
    vus........................................: 7      min=7              max=500
    vus_max....................................: 500    min=500            max=500

    NETWORK
    data_received..............................: 57 GB  159 MB/s
    data_sent..................................: 123 MB 340 kB/
```

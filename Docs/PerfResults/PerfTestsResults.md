# PerfTest Results

## 2026-04-25

### Scope

These notes now capture three related result sets:

1. an earlier comparison run for:
   - `Golang`
   - `Rust`
   - `Dotnet.Aot`
2. a later `Dotnet.Aot` rerun after the service and `/health` behavior were corrected
3. later health-enabled reruns for `Rust` and `Golang`

Important context:

- the earliest comparison run appears to have been taken while `/health` was temporarily commented out of `PerfTest/perf/books-read.js`
- the later reruns for `Dotnet.Aot`, `Rust`, and `Golang` include `/health`
- because of that, the later reruns are the most relevant for the current script, but they are not direct replacements for the earlier no-health numbers

## Run Set A: Original Comparison Without `/health`

### Workload Shape

The earlier captured output appears to come from the temporary perf-test variant where `/health` was commented out:

- no `endpoint:health` section appears in the k6 breakdown
- the check totals are consistent with roughly five checked endpoints per iteration
- the active checked endpoints are:
  - `authors`
  - `books_limit_10`
  - `books_limit_100`
  - `books_limit_1000`
  - `books_limit_10000_default`

## High-Level Comparison

| Service | Checks Succeeded | Failed Requests | HTTP Reqs/s | Avg HTTP Req | P95 HTTP Req | Avg Iteration | Iterations/s | Data Received |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Golang | 100.00% | 0.00% | 144.35/s | 3.20s | 4.45s | 16.03s | 28.87/s | 33 GB |
| Rust | 100.00% | 0.00% | 68.14/s | 6.73s | 13.77s | 33.69s | 13.58/s | 16 GB |
| Dotnet.Aot | 80.00% | 20.00% | 218.58/s | 2.11s | 3.58s | 10.58s | 43.72/s | 57 GB |

## Golang Initial Run

### Summary

- completed all checks successfully
- delivered the best throughput among the fully successful runs in the original comparison
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

## Rust Initial Run

### Summary

- completed all checks successfully
- was the slowest service in the original comparison
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
      { endpoint:books_limit_10000_default }...: avg=8.82s  min=501.67ms med=8.41s  p(90)=14.04s p(95)=15s    p(99)=15.91s max=17.71s
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

## Dotnet.Aot Initial Run

### Summary

- had the best raw latency and highest throughput numbers in the original comparison
- did not complete the workload successfully
- failed every `authors` check
- reported exactly `15756` failed checks, which matched the `15756` completed iterations and strongly suggested one failed `authors` request per iteration
- should not be treated as a valid apples-to-apples winner against Go or Rust in that initial run

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

## Run Set B: Health-Enabled Reruns

### Current Script Shape

The current perf script includes six checked endpoints per iteration:

- `health`
- `authors`
- `books_limit_10`
- `books_limit_100`
- `books_limit_1000`
- `books_limit_10000_default`

### High-Level Comparison

| Service | Checks Succeeded | Failed Requests | HTTP Reqs/s | Avg HTTP Req | P95 HTTP Req | Avg Iteration | Iterations/s | Health Success | Data Received |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Dotnet.Aot | 100.00% | 0.00% | 230.01/s | 2.01s | 3.54s | 12.09s | 38.34/s | 100.00% | 51 GB |
| Golang | 85.23% | 14.76% | 181.89/s | 2.54s | 3.70s | 15.29s | 30.32/s | 11.39% | 35 GB |
| Rust | 84.78% | 15.21% | 84.19/s | 5.44s | 12.01s | 32.65s | 13.99/s | 8.80% | 17 GB |

## Dotnet.Aot Rerun

### Summary

- completed all checks successfully
- restored `authors` correctness to `100%`
- kept very strong latency and throughput even after `/health` was added back into the active script
- established a clean `Dotnet.Aot` baseline for the current health-enabled test script

### Raw Results

```text
TOTAL RESULTS

    checks_total.......: 82854   230.010867/s
    checks_succeeded...: 100.00% 82854 out of 82854
    checks_failed......: 0.00%   0 out of 82854

    ✓ health returned HTTP 200
    ✓ authors returned HTTP 200
    ✓ books_limit_10 returned HTTP 200
    ✓ books_limit_100 returned HTTP 200
    ✓ books_limit_1000 returned HTTP 200
    ✓ books_limit_10000_default returned HTTP 200

    HTTP
    http_req_duration..........................: avg=2.01s   min=44.98ms  med=2.24s   p(90)=3.24s    p(95)=3.54s    p(99)=4.08s    max=7.19s
      { endpoint:authors }.....................: avg=2.38s   min=54.36ms  med=2.46s   p(90)=3.19s    p(95)=3.39s    p(99)=3.74s    max=4.24s
      { endpoint:books_limit_10 }..............: avg=2.14s   min=47.04ms  med=2.21s   p(90)=2.95s    p(95)=3.09s    p(99)=3.49s    max=4.19s
      { endpoint:books_limit_100 }.............: avg=2.07s   min=47.95ms  med=2.11s   p(90)=2.84s    p(95)=3.05s    p(99)=3.45s    max=4.24s
      { endpoint:books_limit_1000 }............: avg=2.37s   min=50.9ms   med=2.45s   p(90)=3.14s    p(95)=3.29s    p(99)=3.69s    max=4.62s
      { endpoint:books_limit_10000_default }...: avg=3.04s   min=88.78ms  med=3.08s   p(90)=3.89s    p(95)=4.13s    p(99)=4.74s    max=7.19s
      { endpoint:health }......................: avg=69.21ms min=44.98ms  med=59.14ms p(90)=102.07ms p(95)=120.32ms p(99)=177.72ms max=359.33ms
      { expected_response:true }...............: avg=2.01s   min=44.98ms  med=2.24s   p(90)=3.24s    p(95)=3.54s    p(99)=4.08s    max=7.19s
    http_req_failed............................: 0.00% 0 out of 82854
    http_reqs..................................: 82854 230.010867/s

    EXECUTION
    iteration_duration.........................: avg=12.09s  min=338.09ms med=12.55s  p(90)=13.21s   p(95)=13.42s   p(99)=14.07s   max=17.65s
    iterations.................................: 13809 38.335145/s
    vus........................................: 6     min=6          max=500
    vus_max....................................: 500   min=500        max=500

    NETWORK
    data_received..............................: 51 GB 141 MB/s
    data_sent..................................: 72 MB 200 kB/s
```

## Rust Rerun

### Summary

- all data endpoints succeeded
- the rerun still failed overall because `/health` collapsed under load
- `health` produced exactly the same number of failures as total failed checks, which strongly suggests every failed check in this rerun was the health endpoint
- the data paths remained much slower than Go and `Dotnet.Aot`

### Raw Results

```text
TOTAL RESULTS

    checks_total.......: 31686  84.189252/s
    checks_succeeded...: 84.78% 26866 out of 31686
    checks_failed......: 15.21% 4820 out of 31686

    ✗ health returned HTTP 200
      ↳  8% — ✓ 464 / ✗ 4820
    ✓ authors returned HTTP 200
    ✓ books_limit_10 returned HTTP 200
    ✓ books_limit_100 returned HTTP 200
    ✓ books_limit_1000 returned HTTP 200
    ✓ books_limit_10000_default returned HTTP 200

    HTTP
    http_req_duration..........................: avg=5.44s  min=47.77ms  med=4.31s  p(90)=10.94s p(95)=12.01s p(99)=13.38s max=15.9s
      { endpoint:authors }.....................: avg=6.83s  min=52.87ms  med=6.41s  p(90)=11.49s p(95)=12.27s p(99)=13.07s max=14.05s
      { endpoint:books_limit_10 }..............: avg=5.14s  min=61.01ms  med=4.27s  p(90)=10.11s p(95)=10.9s  p(99)=12.62s max=13.72s
      { endpoint:books_limit_100 }.............: avg=4.63s  min=62.71ms  med=3.81s  p(90)=9.32s  p(95)=10.39s p(99)=12.16s max=13.13s
      { endpoint:books_limit_1000 }............: avg=5.66s  min=89.94ms  med=4.77s  p(90)=10.32s p(95)=11.16s p(99)=12.79s max=13.51s
      { endpoint:books_limit_10000_default }...: avg=8.23s  min=490.4ms  med=8.08s  p(90)=12.73s p(95)=13.42s p(99)=14.21s max=15.9s
      { endpoint:health }......................: avg=2.15s  min=47.77ms  med=2.22s  p(90)=2.5s   p(95)=2.56s  p(99)=2.69s  max=2.98s
      { expected_response:true }...............: avg=6.01s  min=47.77ms  med=4.85s  p(90)=11.24s p(95)=12.18s p(99)=13.45s max=15.9s
    http_req_failed............................: 15.21% 4820 out of 31686
    http_reqs..................................: 31686  84.189252/s

    EXECUTION
    iteration_duration.........................: avg=32.65s min=979.94ms med=35.47s p(90)=36.43s p(95)=36.82s p(99)=37.47s max=41.99s
    iterations.................................: 5266   13.991687/s
    vus........................................: 6      min=6             max=500
    vus_max....................................: 500    min=500           max=500

    NETWORK
    data_received..............................: 17 GB  44 MB/s
    data_sent..................................: 23 MB  60 kB/s
```

## Golang Rerun

### Summary

- all data endpoints succeeded
- the rerun still failed overall because `/health` collapsed under load
- `health` produced exactly the same number of failures as total failed checks, which strongly suggests every failed check in this rerun was the health endpoint
- among the health-enabled reruns, Go remains much faster than Rust but clearly behind the clean `Dotnet.Aot` rerun

### Raw Results

```text
TOTAL RESULTS

    checks_total.......: 65844  181.890975/s
    checks_succeeded...: 85.23% 56121 out of 65844
    checks_failed......: 14.76% 9723 out of 65844

    ✗ health returned HTTP 200
      ↳  11% — ✓ 1251 / ✗ 9723
    ✓ authors returned HTTP 200
    ✓ books_limit_10 returned HTTP 200
    ✓ books_limit_100 returned HTTP 200
    ✓ books_limit_1000 returned HTTP 200
    ✓ books_limit_10000_default returned HTTP 200

    HTTP
    http_req_duration..........................: avg=2.54s  min=45.98ms  med=2.55s p(90)=3.41s  p(95)=3.7s   p(99)=4.09s  max=7.04s
      { endpoint:authors }.....................: avg=2.62s  min=47.69ms  med=2.7s  p(90)=3.28s  p(95)=3.39s  p(99)=3.57s  max=3.8s
      { endpoint:books_limit_10 }..............: avg=2.36s  min=59.93ms  med=2.47s p(90)=2.73s  p(95)=2.92s  p(99)=3.27s  max=3.75s
      { endpoint:books_limit_100 }.............: avg=2.39s  min=61.81ms  med=2.49s p(90)=2.74s  p(95)=3.01s  p(99)=3.4s   max=3.76s
      { endpoint:books_limit_1000 }............: avg=2.59s  min=83.67ms  med=2.68s p(90)=3.02s  p(95)=3.31s  p(99)=3.61s  max=4.17s
      { endpoint:books_limit_10000_default }...: avg=3.35s  min=336.35ms med=3.43s p(90)=3.98s  p(95)=4.13s  p(99)=4.76s  max=7.04s
      { endpoint:health }......................: avg=1.94s  min=45.98ms  med=2.05s p(90)=2.07s  p(95)=2.07s  p(99)=2.1s   max=2.32s
      { expected_response:true }...............: avg=2.63s  min=45.98ms  med=2.61s p(90)=3.48s  p(95)=3.75s  p(99)=4.12s  max=7.04s
    http_req_failed............................: 14.76% 9723 out of 65844
    http_reqs..................................: 65844  181.890975/s

    EXECUTION
    iteration_duration.........................: avg=15.29s min=654.82ms med=16.2s p(90)=16.62s p(95)=16.79s p(99)=17.53s max=21.35s
    iterations.................................: 10974  30.315163/s
    vus........................................: 1      min=1             max=500
    vus_max....................................: 500    min=500           max=500

    NETWORK
    data_received..............................: 35 GB  96 MB/s
    data_sent..................................: 50 MB  138 kB/s
```

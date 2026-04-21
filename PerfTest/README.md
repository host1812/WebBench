# Books API k6 Performance Tests

This project contains a read-only Grafana k6 OSS test for an existing books/authors API. It is intended to run from a dedicated Azure load-test VM over SSH, using Docker on that VM.

## Prerequisites

- A dedicated Azure load-test VM from the infra project.
- Docker installed and working on the load-test VM.
- SSH access from your workstation to the load-test VM.
- The service VM or service endpoint allows inbound TCP 443 from the load-test VM.
- Local `ssh` and `scp` commands available on PATH.

If the target API uses a self-signed certificate, run with `-SkipTlsVerify`.

## Run

Basic read test:

```powershell
.\scripts\run-k6.ps1 -VmIp <load-test-vm-ip> -BaseUrl https://<service-ip> -SkipTlsVerify
```

With known author and book IDs:

```powershell
.\scripts\run-k6.ps1 -VmIp <load-test-vm-ip> -BaseUrl https://<service-ip> -SkipTlsVerify -AuthorId <author-id> -BookId <book-id>
```

The default workload ramps to 25 VUs over 30 seconds, holds 25 VUs for 5 minutes, then ramps down over 30 seconds. You can override the hold load with:

```powershell
.\scripts\run-k6.ps1 -VmIp <load-test-vm-ip> -BaseUrl https://<service-ip> -Vus 50 -Duration 10m
```

## What It Tests

Every iteration calls:

- `GET /health`
- `GET /api/v1/authors`
- `GET /api/v1/books`

When IDs are provided, it also calls:

- `GET /api/v1/books?author_id=<author-id>`
- `GET /api/v1/books/<book-id>`

Each request is tagged with an `endpoint` value so k6 output can separate latency by endpoint.

## Thresholds

The test fails when any threshold fails:

- Failed HTTP request rate must be below 1%.
- Overall p95 latency must be below 1000 ms.
- Overall p99 latency must be below 2500 ms.
- `/health` p95 latency must be below 300 ms.
- `/health` p99 latency must be below 750 ms.

The PowerShell runner preserves the k6 exit code, so threshold failures fail the script.

## Reading Results

k6 prints summary trend stats for `avg`, `min`, `med`, `p(90)`, `p(95)`, `p(99)`, and `max`.

- `avg` is the average latency across matching requests.
- `p(95)` means 95% of requests were at or below that latency.
- `p(99)` means 99% of requests were at or below that latency.
- `http_req_failed` shows the failed request rate.
- The HTML dashboard report is copied back to `perf/reports/books-read-<timestamp>.html`.

`GET /api/v1/books` returns a large response, so it may dominate overall latency and make the aggregate p95/p99 higher than the smaller endpoints.

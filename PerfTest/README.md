# Books API k6 Performance Tests

This project contains a read-only Grafana k6 OSS test for an existing books/authors API. It is intended to run from a dedicated Azure load-test VM over SSH, using Docker on that VM.

## Prerequisites

- A dedicated Azure load-test VM from the infra project.
- Docker installed and working on the load-test VM.
- SSH access from your workstation to the load-test VM.
- The service VM or service endpoint allows inbound TCP 443 from the load-test VM.
- Local `ssh` and `scp` commands available on PATH.

If the target API uses a self-signed certificate, run with `-SkipTlsVerify`.

`-SkipTlsVerify` sets both the test script's `SKIP_TLS_VERIFY` variable and k6's native `K6_INSECURE_SKIP_TLS_VERIFY` option inside the Docker container.

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
- `GET /api/v1/books?limit=10`
- `GET /api/v1/books?limit=100`
- `GET /api/v1/books?limit=1000`
- `GET /api/v1/books`, which uses the service default limit of 10,000
- `GET /api/v1/books?limit=50000`
- `GET /api/v1/books?limit=100000`

When IDs are provided, it also calls:

- `GET /api/v1/books?author_id=<author-id>`
- `GET /api/v1/books/<book-id>`

Each request is tagged with an `endpoint` value so k6 output can separate latency by endpoint. The limit-based book calls use tags such as `books_limit_10`, `books_limit_10000_default`, and `books_limit_100000`.

## Thresholds

The test fails when any threshold fails:

- Failed HTTP request rate must be below 1%.
- `/health` p95 latency must be below 300 ms.
- `/health` p99 latency must be below 750 ms.
- `/api/v1/authors`, `books_limit_10`, and `book_by_id` p95/p99 must stay below 1000 ms / 2500 ms.
- Larger books-list thresholds scale by response size, up to 60 seconds p95 and 120 seconds p99 for `books_limit_100000`.

By default, the PowerShell runner treats k6 threshold failures as warnings so the script can still finish, copy the HTML report, and return control cleanly. Real execution failures still fail the script.

If you want threshold failures to be fatal, run with:

```powershell
.\scripts\run-k6.ps1 -VmIp <load-test-vm-ip> -BaseUrl https://<service-ip> -FailOnThresholdFailure
```

## Reading Results

k6 prints summary trend stats for `avg`, `min`, `med`, `p(90)`, `p(95)`, `p(99)`, and `max`.

- `avg` is the average latency across matching requests.
- `p(95)` means 95% of requests were at or below that latency.
- `p(99)` means 99% of requests were at or below that latency.
- `http_req_failed` shows the failed request rate.
- The HTML dashboard report is copied back to `perf/reports/books-read-<timestamp>.html`.

The larger `GET /api/v1/books` limit calls return large responses, so they may dominate bandwidth and total test duration. Latency thresholds are endpoint-specific, so a slow `books_limit_100000` result does not get blended into a single aggregate latency gate for the smaller endpoints.

## Self-Signed Certificates

For quick private load tests, use `-SkipTlsVerify`. This disables server certificate verification for k6, which prevents self-signed certificate failures and their repeated request warnings.

For stricter testing, trust the certificate authority used by the service instead of skipping verification. Because k6 runs inside Docker, the certificate must be trusted inside the container, not just on the Azure VM host. The usual production-grade fix is to issue the service certificate from an internal CA and build or use a k6 image that includes that CA certificate in the container trust store.

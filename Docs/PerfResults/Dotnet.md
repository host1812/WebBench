# Dotnet Perf Review

## 2026-04-25 14:50:22 -04:00

### Summary

The current evidence points to an API contract mismatch as the primary reason the .NET service appears much slower than Rust and Go during `PerfTest`. The .NET implementation does not honor the `limit` query parameter used by the perf script, so requests such as `limit=10` and `limit=10000` are both handled as the default `10_000` row query. That explains the flat response time you observed.

There are also secondary .NET-specific costs in the hot path. The .NET list-books query joins authors on every row to populate `AuthorName`, while the Go and Rust implementations list directly from `books`. The .NET service also logs more aggressively under load, including request-level success logs and EF Core SQL command logs at `Information`.

### Findings

#### 1. .NET does not honor the `limit` parameter used by PerfTest

PerfTest sends:

- `GET /api/v1/books?limit=10`
- `GET /api/v1/books?limit=100`
- `GET /api/v1/books?limit=1000`
- `GET /api/v1/books`

Relevant source:

- [PerfTest/perf/books-read.js](../../PerfTest/perf/books-read.js)

The .NET endpoint binds `take`, not `limit`:

- [Dotnet/src/AuthorsBooks.Api/Endpoints/BookEndpoints.cs](../../Dotnet/src/AuthorsBooks.Api/Endpoints/BookEndpoints.cs)

Key behavior:

- `ListBooksAsync(int? take, ...)`
- `new ListBooksQuery(take ?? 10_000)`

Result:

- `/api/v1/books?limit=10` does not bind to `take`
- `take` is `null`
- the service falls back to `10_000`

That means these requests are effectively equivalent on .NET:

- `/api/v1/books?limit=10`
- `/api/v1/books?limit=10000`
- `/api/v1/books`

This is the strongest explanation for the same response times across different requested limits.

#### 2. The .NET route contract has drifted from Go and Rust

Go and Rust use `limit` for list endpoints, and Go also supports `author_id` filtering on `/api/v1/books`.

Relevant sources:

- [Golang/internal/interfaces/http/router.go](../../Golang/internal/interfaces/http/router.go)
- [Rust/src/presentation/http/router.rs](../../Rust/src/presentation/http/router.rs)

.NET differs:

- book list uses `take`
- author book list is exposed through `/authors/{authorId}/books`
- no support was found for `/api/v1/books?author_id=...`

Relevant source:

- [Dotnet/src/AuthorsBooks.Api/Endpoints/BookEndpoints.cs](../../Dotnet/src/AuthorsBooks.Api/Endpoints/BookEndpoints.cs)

This contract drift means PerfTest is not exercising the .NET service in the same way as the Go and Rust services.

#### 3. .NET does more work per book row than Go and Rust

.NET list-books query:

- joins `books` to `authors`
- projects `AuthorName` into every result row

Relevant sources:

- [Dotnet/src/AuthorsBooks.Infrastructure/Persistence/Queries/BookReadRepository.cs](../../Dotnet/src/AuthorsBooks.Infrastructure/Persistence/Queries/BookReadRepository.cs)
- [Dotnet/src/AuthorsBooks.Application/Books/Queries/BookResponse.cs](../../Dotnet/src/AuthorsBooks.Application/Books/Queries/BookResponse.cs)

Go and Rust list queries:

- read directly from `books`
- do not join `authors` for the standard books list

Relevant sources:

- [Golang/internal/infrastructure/postgres/book_repository.go](../../Golang/internal/infrastructure/postgres/book_repository.go)
- [Rust/src/infrastructure/persistence/postgres_book_repository.rs](../../Rust/src/infrastructure/persistence/postgres_book_repository.rs)

This does not explain the flat `limit=10` vs `limit=10000` timing by itself, but it does make the .NET hot path heavier once it is already over-fetching.

#### 4. .NET author list also does extra work during the same perf loop

PerfTest calls `/api/v1/authors` on every iteration before the books endpoints.

Relevant source:

- [PerfTest/perf/books-read.js](../../PerfTest/perf/books-read.js)

The .NET authors list includes `BookCount`, which requires counting related books for each author:

- [Dotnet/src/AuthorsBooks.Infrastructure/Persistence/Queries/AuthorReadRepository.cs](../../Dotnet/src/AuthorsBooks.Infrastructure/Persistence/Queries/AuthorReadRepository.cs)
- [Dotnet/src/AuthorsBooks.Application/Authors/Queries/AuthorResponses.cs](../../Dotnet/src/AuthorsBooks.Application/Authors/Queries/AuthorResponses.cs)

Go and Rust author list paths are lighter and do not include an equivalent count on the standard list response:

- [Golang/internal/infrastructure/postgres/author_repository.go](../../Golang/internal/infrastructure/postgres/author_repository.go)
- [Rust/src/infrastructure/persistence/postgres_author_repository.rs](../../Rust/src/infrastructure/persistence/postgres_author_repository.rs)

This is likely a secondary throughput cost, not the primary cause of the timeout behavior.

#### 5. .NET logging is heavier in the hot path

The .NET service is configured with:

- request success logging at `Information`
- EF Core SQL command logging at `Information`
- console logging enabled

Relevant sources:

- [Dotnet/src/AuthorsBooks.Application/Common/TelemetryBehavior.cs](../../Dotnet/src/AuthorsBooks.Application/Common/TelemetryBehavior.cs)
- [Dotnet/src/AuthorsBooks.Api/appsettings.json](../../Dotnet/src/AuthorsBooks.Api/appsettings.json)
- [Dotnet/src/AuthorsBooks.Api/Program.cs](../../Dotnet/src/AuthorsBooks.Api/Program.cs)

Under k6 concurrency, this can add measurable overhead and increase stdout pressure. It is not the main explanation for equal timings across different limits, but it can worsen the timeout symptoms once the service is already returning very large payloads.

#### 6. .NET tests did not catch the endpoint mismatch

The current .NET integration tests validate `take`, not `limit`:

- [Dotnet/tests/AuthorsBooks.IntegrationTests/Api/AuthorBookApiTests.cs](../../Dotnet/tests/AuthorsBooks.IntegrationTests/Api/AuthorBookApiTests.cs)

Examples in the test suite:

- `/api/v1/authors/{author.Id}/books?take=1`
- `/api/v1/books?take={take}`

By comparison, Go and Rust have route-level tests for `limit`:

- [Golang/internal/interfaces/http/router_test.go](../../Golang/internal/interfaces/http/router_test.go)
- [Rust/src/presentation/http/router.rs](../../Rust/src/presentation/http/router.rs)

This is why the mismatch could exist while the .NET test suite still appeared consistent with itself.

#### 7. Shared database concern: all services sort by `title` without a matching index

All three services use `ORDER BY title ASC` for book list queries.

Relevant sources:

- [Dotnet/src/AuthorsBooks.Infrastructure/Persistence/Queries/BookReadRepository.cs](../../Dotnet/src/AuthorsBooks.Infrastructure/Persistence/Queries/BookReadRepository.cs)
- [Golang/internal/infrastructure/postgres/book_repository.go](../../Golang/internal/infrastructure/postgres/book_repository.go)
- [Rust/src/infrastructure/persistence/postgres_book_repository.rs](../../Rust/src/infrastructure/persistence/postgres_book_repository.rs)

The shared schema defines indexes on:

- `author_id`
- `isbn`

Relevant sources:

- [Dotnet/src/AuthorsBooks.Infrastructure/Migrations/20260424190000_InitialSharedSchema.cs](../../Dotnet/src/AuthorsBooks.Infrastructure/Migrations/20260424190000_InitialSharedSchema.cs)
- [Golang/db-migrations/migrations/001_init.up.sql](../../Golang/db-migrations/migrations/001_init.up.sql)

This is not the primary reason the .NET service is currently timing out relative to the other two, but it is a likely scaling concern for true high-limit reads across all implementations.

### Conclusion

The current review does not support the conclusion that the .NET implementation is inherently slower in the same workload shape. The strongest evidence instead shows that PerfTest is exercising a different effective workload on .NET because the service expects `take` while the benchmark uses `limit`.

The likely sequence is:

1. PerfTest sends `limit`
2. .NET ignores it and defaults to `10_000`
3. every books-list call becomes a large-response call
4. the .NET implementation pays extra per-row cost due to joins and logging
5. total latency rises enough to produce timeouts

### Notes

- This review was based on static inspection of the repository.
- I attempted to run the .NET tests locally, but the environment blocked a clean completion, so the analysis above is based on code inspection rather than a completed runtime verification pass.

## 2026-04-25 15:18:29 -04:00

### Summary

The high-load timeout issue does not appear to be caused by a different Nginx configuration. The effective VM gateway configuration is the same across .NET, Go, and Rust. The more relevant deployment-level differences are:

- .NET did not cap its PostgreSQL connection pool while Go and Rust explicitly capped theirs at `10`
- .NET exported telemetry directly to Azure Monitor, while Go and Rust exported to a local OpenTelemetry Collector
- .NET used a different deployment topology for telemetry and did not include the collector service at all

These are meaningful non-code differences under load and were addressed in this iteration.

### Findings

#### 1. Nginx is effectively identical across all three services

The service-specific `nginx.vm.conf` files are the same, and the deploy scripts all copy the shared Infra gateway config to the VM:

- [Infra/nginx.vm.conf](../../Infra/nginx.vm.conf)
- [Dotnet/scripts/deploy.ps1](../../Dotnet/scripts/deploy.ps1)
- [Golang/scripts/deploy.ps1](../../Golang/scripts/deploy.ps1)
- [Rust/scripts/deploy.ps1](../../Rust/scripts/deploy.ps1)

Conclusion:

- the remaining timeout behavior is not explained by a different Nginx configuration

#### 2. .NET previously had no explicit database pool cap

Go and Rust both cap their database pools at `10` connections:

- [Golang/internal/infrastructure/postgres/pool.go](../../Golang/internal/infrastructure/postgres/pool.go)
- [Rust/src/infrastructure/db.rs](../../Rust/src/infrastructure/db.rs)

Their VM compose files also expose the same limit through environment configuration:

- [Golang/compose.vm.yaml](../../Golang/compose.vm.yaml)
- [Rust/compose.vm.yaml](../../Rust/compose.vm.yaml)

Before this iteration, .NET did not apply an equivalent max-pool setting in:

- [Dotnet/src/AuthorsBooks.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs](../../Dotnet/src/AuthorsBooks.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs)

Risk:

- under higher concurrency, .NET could open a materially different number of database connections than Go and Rust
- that can shift the bottleneck to Azure Database for PostgreSQL and produce client-visible request timeouts

#### 3. .NET telemetry topology differed from Go and Rust

Go and Rust deploy an OpenTelemetry Collector sidecar/service and send traces to it:

- [Golang/compose.vm.yaml](../../Golang/compose.vm.yaml)
- [Rust/compose.vm.yaml](../../Rust/compose.vm.yaml)
- [Golang/otel-collector-config.yaml](../../Golang/otel-collector-config.yaml)
- [Rust/otel-collector-config.yaml](../../Rust/otel-collector-config.yaml)

Before this iteration, .NET had:

- no collector service in `compose.vm.yaml`
- direct Azure Monitor export in application code

Relevant source:

- [Dotnet/src/AuthorsBooks.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs](../../Dotnet/src/AuthorsBooks.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs)

Risk:

- direct exporter behavior can differ from the collector-based path
- that difference may be small under low concurrency but more visible when the service is under sustained request pressure

#### 4. Local secret-bearing `.env` files are present in the working tree

The repository contains local `.env` files in service directories, but they are currently ignored and not tracked in the current index. A history check for the secret-bearing `.env` paths returned no matches, which means the live `.env` files were not present in git history at the time of this review.

Tracked env-related files found in git:

- `Dotnet/.env.example`
- `Golang/.env.example`
- `Rust/.env.example`

No history matches were found for:

- `Dotnet/.env`
- `Golang/.env`
- `Rust/.env`

Conclusion:

- the operational problem is real because live local secret files exist in the workspace
- but a git-history purge of those specific `.env` paths was not required because those paths were not found in repository history

### Changes Implemented

#### 1. Added explicit .NET database max-connections configuration

The .NET service now reads `Database__MaxConnections` and applies it to `NpgsqlConnectionStringBuilder.MaxPoolSize`.

Relevant sources:

- [Dotnet/src/AuthorsBooks.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs](../../Dotnet/src/AuthorsBooks.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs)
- [Dotnet/.env.example](../../Dotnet/.env.example)

Default behavior now matches Go and Rust more closely:

- default max connections: `10`

#### 2. Switched .NET VM deployment to the same collector pattern as Go and Rust

Added:

- collector service to `.NET` VM compose
- collector config file
- OTLP trace export from the application to the local collector

Relevant sources:

- [Dotnet/compose.vm.yaml](../../Dotnet/compose.vm.yaml)
- [Dotnet/otel-collector-config.yaml](../../Dotnet/otel-collector-config.yaml)
- [Dotnet/src/AuthorsBooks.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs](../../Dotnet/src/AuthorsBooks.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs)
- [Dotnet/scripts/deploy.ps1](../../Dotnet/scripts/deploy.ps1)

#### 3. Added a script to watch Azure PostgreSQL connection metrics during perf runs

Added:

- [Dotnet/scripts/watch-postgres-connections.ps1](../../Dotnet/scripts/watch-postgres-connections.ps1)

This script resolves the flexible server resource and queries Azure Monitor metrics including:

- `active_connections`
- `max_connections`
- `connections_failed`

The metric names are based on Microsoft Learn references for Azure Database for PostgreSQL flexible server monitoring.

#### 4. Added repo-root ignore rules for local `.env` files

Added:

- [/.gitignore](../../.gitignore)

Purpose:

- make `.env` ignore behavior explicit at the repository root
- keep local secret-bearing files out of future git tracking across all service directories

### Verification

The implementation in this iteration should be followed by:

1. redeploying the .NET service so the collector topology and pool cap are applied on the VM
2. rerunning PerfTest
3. running `Dotnet/scripts/watch-postgres-connections.ps1` during the load test to correlate timeouts with server-side connection pressure

### Notes

- The `.env` files were verified to be absent from the current git index.
- The `.env` files were also not found in `git log --all` history for the specific secret-bearing paths reviewed in this iteration.

## 2026-04-25 16:00:36 -04:00

### Summary

A separate `Dotnet.Aot` project variant was created to explore NativeAOT readiness without disturbing the working `Dotnet` service. The goal in this iteration was not only to copy the project, but to remove the main code patterns that are hostile to NativeAOT and make the new variant structurally closer to a publishable AOT service.

The copied project now builds and passes the existing unit and integration tests in this environment. A full `PublishAot=true` publish was not conclusively verified here because the sandboxed CLI publish path did not surface actionable compiler diagnostics during the AOT publish attempt.

### Findings

#### 1. A separate `Dotnet.Aot` project is safer than converting the existing `.NET` service in place

The original `Dotnet` service was already functioning and had been improved for perf correctness. Enabling NativeAOT directly in that project would have mixed two concerns:

- fixing the current benchmarked service
- introducing a large deployment/runtime model change

Creating a separate project copy allows:

- side-by-side experimentation
- independent Docker and deployment defaults
- AOT-specific refactors without destabilizing the main `.NET` path

Created project root:

- [Dotnet.Aot](../../Dotnet.Aot)

#### 2. The original `.NET` project had the expected NativeAOT blockers

The key blockers identified before the refactor were:

- assembly scanning for DI registration
- runtime closed-generic construction in the request dispatcher
- EF Core configuration discovery via assembly scanning
- default reflection-based JSON metadata generation
- anonymous response payloads in API endpoints

Representative sources from the original project:

- [Dotnet/src/AuthorsBooks.Application/DependencyInjection.cs](../../Dotnet/src/AuthorsBooks.Application/DependencyInjection.cs)
- [Dotnet/src/AuthorsBooks.Application/Common/RequestDispatcher.cs](../../Dotnet/src/AuthorsBooks.Application/Common/RequestDispatcher.cs)
- [Dotnet/src/AuthorsBooks.Infrastructure/Persistence/ApplicationDbContext.cs](../../Dotnet/src/AuthorsBooks.Infrastructure/Persistence/ApplicationDbContext.cs)

#### 3. The new `Dotnet.Aot` project now uses explicit registrations instead of assembly scanning

In the AOT variant, application registrations were rewritten to be explicit.

Relevant source:

- [Dotnet.Aot/src/AuthorsBooks.Application/DependencyInjection.cs](../../Dotnet.Aot/src/AuthorsBooks.Application/DependencyInjection.cs)

What changed:

- explicit `IRequestHandler<,>` registrations for all commands and queries
- explicit `IValidator<>` registrations
- explicit `RequestExecutor<,>` registrations
- no `Assembly.GetExecutingAssembly()`
- no `DefinedTypes` or `ImplementedInterfaces` scanning

This removes a major runtime reflection dependency from the application layer.

#### 4. The new dispatcher no longer relies on runtime generic construction or `dynamic`

The AOT variant replaces runtime executor resolution with explicit request matching.

Relevant source:

- [Dotnet.Aot/src/AuthorsBooks.Application/Common/RequestDispatcher.cs](../../Dotnet.Aot/src/AuthorsBooks.Application/Common/RequestDispatcher.cs)

What changed:

- removed `MakeGenericType`
- removed `dynamic`
- replaced generic runtime type construction with an explicit `switch` over known request types

This is less elegant than the previous generic dispatcher, but it is much more predictable for NativeAOT analysis.

#### 5. EF Core model configuration is now explicit in the AOT variant

The AOT copy no longer uses assembly scanning to discover entity configurations.

Relevant source:

- [Dotnet.Aot/src/AuthorsBooks.Infrastructure/Persistence/ApplicationDbContext.cs](../../Dotnet.Aot/src/AuthorsBooks.Infrastructure/Persistence/ApplicationDbContext.cs)

What changed:

- removed `ApplyConfigurationsFromAssembly(...)`
- added explicit `ApplyConfiguration(new AuthorConfiguration())`
- added explicit `ApplyConfiguration(new BookConfiguration())`

#### 6. API JSON metadata is now source-generated in the AOT variant

The AOT copy now defines a serializer context and wires it into minimal API JSON options.

Relevant sources:

- [Dotnet.Aot/src/AuthorsBooks.Api/Serialization/AuthorsBooksJsonSerializerContext.cs](../../Dotnet.Aot/src/AuthorsBooks.Api/Serialization/AuthorsBooksJsonSerializerContext.cs)
- [Dotnet.Aot/src/AuthorsBooks.Api/Program.cs](../../Dotnet.Aot/src/AuthorsBooks.Api/Program.cs)
- [Dotnet.Aot/src/AuthorsBooks.Api/Contracts/ServiceStatusResponse.cs](../../Dotnet.Aot/src/AuthorsBooks.Api/Contracts/ServiceStatusResponse.cs)

What changed:

- added source-generated `JsonSerializerContext`
- registered it through `ConfigureHttpJsonOptions(...)`
- replaced anonymous root and health responses with concrete record types
- switched the API host setup to `WebApplication.CreateSlimBuilder(...)`

These changes move the new project closer to the documented ASP.NET Core NativeAOT shape.

#### 7. AOT-oriented build and deployment defaults were added to the copied project

Relevant sources:

- [Dotnet.Aot/src/AuthorsBooks.Api/AuthorsBooks.Api.csproj](../../Dotnet.Aot/src/AuthorsBooks.Api/AuthorsBooks.Api.csproj)
- [Dotnet.Aot/Dockerfile](../../Dotnet.Aot/Dockerfile)
- [Dotnet.Aot/.env.example](../../Dotnet.Aot/.env.example)
- [Dotnet.Aot/scripts/build.ps1](../../Dotnet.Aot/scripts/build.ps1)
- [Dotnet.Aot/scripts/deploy.ps1](../../Dotnet.Aot/scripts/deploy.ps1)

What changed:

- enabled request delegate generation
- disabled default reflection JSON fallback
- changed the Docker publish path to request AOT explicitly
- changed image/deploy defaults to AOT-specific names such as `books-service-dotnet-aot`

Important detail:

- `PublishAot` was not left unconditional in the `.csproj`

Reason:

- making `PublishAot` unconditional forces normal restore/build/test flows to pull the AOT toolchain packages
- that made the project unusable in offline or partially cached environments
- the safer layout is to keep the project AOT-oriented while requesting AOT explicitly during publish

### Verification

The copied project was validated with the existing automated tests:

- `dotnet test Dotnet.Aot\tests\AuthorsBooks.UnitTests\AuthorsBooks.UnitTests.csproj -m:1 -v minimal ...`
- `dotnet test Dotnet.Aot\tests\AuthorsBooks.IntegrationTests\AuthorsBooks.IntegrationTests.csproj -m:1 -v minimal ...`

Results:

- unit tests: `11/11` passed
- integration tests: `7/7` passed

### Limitations

A full NativeAOT publish was not conclusively verified in this environment.

Observed behavior:

- normal build and test flows worked after the refactor
- explicit `dotnet publish ... -p:PublishAot=true` did not produce actionable source-level diagnostics in this sandbox

Current interpretation:

- the `Dotnet.Aot` codebase is materially more AOT-ready than the original
- but it should still be treated as an AOT experiment until a real publish succeeds in a normal connected build environment

### Next Steps

Recommended follow-up outside this sandbox:

1. run `dotnet publish src/AuthorsBooks.Api/AuthorsBooks.Api.csproj -c Release -r linux-x64 -p:PublishAot=true` from `Dotnet.Aot`
2. resolve any remaining AOT/trimming diagnostics that only appear during full publish
3. build and deploy the AOT image separately from the existing `Dotnet` service
4. compare startup time, steady-state CPU, and high-concurrency PerfTest behavior against the current JIT-based `.NET` service

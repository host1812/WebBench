# Rust vs RustSimple

## Overall

Both projects implement the same basic `authors` / `books` CRUD backend, but they follow different engineering styles.

`Rust` is the more structured version: layered modules, explicit domain objects, command/query services, repository traits, infrastructure adapters, health checks, and OTLP telemetry.

`RustSimple` is the flatter version: Axum routes call SQL directly, state is just a `PgPool`, and the binary owns serving, migrations, and TLS setup.

## Code Structure

### Rust

`Rust` is organized around explicit architectural boundaries:

- `src/lib.rs` wires `domain`, `application`, `infrastructure`, and `presentation` together.
- `src/domain/book.rs` and its author equivalent hold validation and invariants such as list limits, required fields, and year ranges.
- `src/presentation/http/router.rs` keeps HTTP thin and delegates to injected services.
- `src/telemetry.rs` and `src/infrastructure/health.rs` show attention to observability and dependency health.

### RustSimple

`RustSimple` is deliberately flatter:

- `src/main.rs` owns CLI parsing, startup, TLS, and migration flow.
- `src/router.rs` defines the top router and middleware.
- `src/resources/authors.rs` and `src/resources/books.rs` contain handler logic plus inline SQL.
- `src/config.rs` is simple env-based config with required TLS and DB variables.

By size, `Rust` is materially larger: about 25 source files / 3183 lines vs `RustSimple` at 10 files / 620 lines.

## Software Engineering View

`Rust` is the stronger codebase from a maintainability and product-engineering perspective.

- It has better separation of concerns.
- It keeps business rules out of handlers.
- It is much easier to test in isolation because dependencies are expressed as traits and injected through services.
- The test story is much better: `cargo test` passed 59 tests, including router, domain, config, error, and telemetry coverage.

For a team project or a service expected to evolve, this is the safer design.

The tradeoff is cost. `Rust` has more indirection and more boilerplate for what is still a small CRUD service. For a benchmark, prototype, or solo-owned service, some of that structure is arguably heavier than necessary.

`RustSimple` is good at being small and readable. The request path is easy to follow, migrations are built in, and direct HTTPS from the process simplifies deployment shape. But it pushes concerns together: HTTP, validation, persistence, and data shape are mostly collapsed into the same modules. That makes it fast to build, but weaker under change.

The main gaps in `RustSimple` are:

- Health is shallow: `/health` returns `"ok"` without checking the database.
- Validation is partial: `title` is required, but `isbn` is optional and `published_year` is not domain-validated.
- Data semantics are looser: author `bio` and book `isbn` are normalized to empty strings at the handler level rather than modeled explicitly.
- Test coverage is minimal: `cargo test` passed only 2 tests, both around the book list limit.
- CORS is fully open by default.

## Practical Read

If the goal is a benchmarkable, minimal Rust service, `RustSimple` is reasonable and closer to the minimum useful implementation.

If the goal is a production-grade service or a codebase multiple engineers will extend, `Rust` is clearly the better-engineered project.

Short version:

- `RustSimple` optimizes for speed and simplicity.
- `Rust` optimizes for correctness boundaries, testability, and operational maturity.

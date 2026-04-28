# syntax=docker/dockerfile:1.7

FROM rust:1.94-bookworm AS builder

WORKDIR /app

COPY Cargo.toml Cargo.lock ./
COPY src ./src

RUN --mount=type=cache,target=/usr/local/cargo/registry \
    --mount=type=cache,target=/app/target \
    cargo build --release --locked \
    && cp /app/target/release/rust_backend_service /usr/local/bin/rust_backend_service

FROM debian:bookworm-slim AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=builder /usr/local/bin/rust_backend_service /app/rust_backend_service
COPY config ./config

ENV BOOKSVC_HTTP_ADDRESS=:8080

EXPOSE 8080

ENTRYPOINT ["/app/rust_backend_service"]

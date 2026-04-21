# syntax=docker/dockerfile:1.7

FROM rust:1.94-alpine AS builder

RUN apk add --no-cache musl-dev

WORKDIR /app

COPY Cargo.toml Cargo.lock ./
COPY src ./src

RUN --mount=type=cache,target=/usr/local/cargo/registry \
    --mount=type=cache,target=/app/target \
    cargo build --release --locked \
    && cp /app/target/release/rust_backend_service /usr/local/bin/rust_backend_service

FROM gcr.io/distroless/static-debian12:nonroot AS runtime

WORKDIR /app

COPY --from=builder /usr/local/bin/rust_backend_service /app/rust_backend_service
COPY config ./config

ENV BOOKSVC_HTTP_ADDRESS=:8080

EXPOSE 8080

USER nonroot:nonroot

ENTRYPOINT ["/app/rust_backend_service"]

# syntax=docker/dockerfile:1

FROM golang:1.26-alpine AS build

WORKDIR /src

RUN apk add --no-cache ca-certificates

COPY go.mod go.sum ./
RUN go mod download

COPY cmd ./cmd

RUN CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build \
    -trimpath \
    -ldflags="-s -w" \
    -o /out/books-service \
    ./cmd/api

# Official distroless runtime images are not Alpine-based. Alpine is used above
# for the builder stage; this final stage keeps the runtime distroless.
FROM gcr.io/distroless/static-debian12:nonroot

WORKDIR /app

COPY --from=build /out/books-service /books-service

EXPOSE 8080

USER nonroot:nonroot

ENTRYPOINT ["/books-service"]

package postgres

import (
	"context"
	"errors"
	"fmt"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgconn"
	"github.com/webbench/golang-service/internal/application"
	"github.com/webbench/golang-service/internal/domain"
	"github.com/webbench/golang-service/internal/telemetry"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/trace"
)

var postgresAuthorTracer = otel.Tracer(telemetry.TracerName("infrastructure/postgres/authors"))

type DB interface {
	Exec(ctx context.Context, sql string, arguments ...any) (pgconn.CommandTag, error)
	Query(ctx context.Context, sql string, args ...any) (pgx.Rows, error)
	QueryRow(ctx context.Context, sql string, args ...any) pgx.Row
}

type AuthorRepository struct {
	pool DB
}

func NewAuthorRepository(pool DB) *AuthorRepository {
	return &AuthorRepository{pool: pool}
}

func (r *AuthorRepository) Create(ctx context.Context, author domain.Author) error {
	ctx, span := startDBSpan(ctx, postgresAuthorTracer, "postgres.authors.insert", "INSERT", "authors")
	defer span.End()
	span.SetAttributes(attribute.String("author.id", author.ID.String()))

	_, err := r.pool.Exec(ctx, `
		INSERT INTO authors (id, name, bio, created_at, updated_at)
		VALUES ($1, $2, $3, $4, $5)
	`, author.ID, author.Name, author.Bio, author.CreatedAt, author.UpdatedAt)
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return fmt.Errorf("insert author: %w", err)
	}
	return nil
}

func (r *AuthorRepository) Update(ctx context.Context, author domain.Author) error {
	ctx, span := startDBSpan(ctx, postgresAuthorTracer, "postgres.authors.update", "UPDATE", "authors")
	defer span.End()
	span.SetAttributes(attribute.String("author.id", author.ID.String()))

	tag, err := r.pool.Exec(ctx, `
		UPDATE authors
		SET name = $2, bio = $3, updated_at = $4
		WHERE id = $1
	`, author.ID, author.Name, author.Bio, author.UpdatedAt)
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return fmt.Errorf("update author: %w", err)
	}
	if tag.RowsAffected() == 0 {
		span.SetAttributes(attribute.Int64("db.rows_affected", 0))
		return application.ErrNotFound
	}
	span.SetAttributes(attribute.Int64("db.rows_affected", tag.RowsAffected()))
	return nil
}

func (r *AuthorRepository) Delete(ctx context.Context, id uuid.UUID) error {
	ctx, span := startDBSpan(ctx, postgresAuthorTracer, "postgres.authors.delete", "DELETE", "authors")
	defer span.End()
	span.SetAttributes(attribute.String("author.id", id.String()))

	tag, err := r.pool.Exec(ctx, `DELETE FROM authors WHERE id = $1`, id)
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return fmt.Errorf("delete author: %w", err)
	}
	if tag.RowsAffected() == 0 {
		span.SetAttributes(attribute.Int64("db.rows_affected", 0))
		return application.ErrNotFound
	}
	span.SetAttributes(attribute.Int64("db.rows_affected", tag.RowsAffected()))
	return nil
}

func (r *AuthorRepository) Get(ctx context.Context, id uuid.UUID) (domain.Author, error) {
	ctx, span := startDBSpan(ctx, postgresAuthorTracer, "postgres.authors.select_one", "SELECT", "authors")
	defer span.End()
	span.SetAttributes(attribute.String("author.id", id.String()))

	var author domain.Author
	err := r.pool.QueryRow(ctx, `
		SELECT id, name, bio, created_at, updated_at
		FROM authors
		WHERE id = $1
	`, id).Scan(&author.ID, &author.Name, &author.Bio, &author.CreatedAt, &author.UpdatedAt)
	if errors.Is(err, pgx.ErrNoRows) {
		return domain.Author{}, application.ErrNotFound
	}
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return domain.Author{}, fmt.Errorf("get author: %w", err)
	}
	return author, nil
}

func (r *AuthorRepository) List(ctx context.Context) ([]domain.Author, error) {
	ctx, span := startDBSpan(ctx, postgresAuthorTracer, "postgres.authors.select", "SELECT", "authors")
	defer span.End()

	rows, err := r.pool.Query(ctx, `
		SELECT id, name, bio, created_at, updated_at
		FROM authors
		ORDER BY name ASC, created_at ASC
	`)
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return nil, fmt.Errorf("list authors: %w", err)
	}
	defer rows.Close()

	authors := make([]domain.Author, 0)
	for rows.Next() {
		var author domain.Author
		if err := rows.Scan(&author.ID, &author.Name, &author.Bio, &author.CreatedAt, &author.UpdatedAt); err != nil {
			telemetry.RecordSpanError(span, err)
			return nil, fmt.Errorf("scan author: %w", err)
		}
		authors = append(authors, author)
	}
	if err := rows.Err(); err != nil {
		telemetry.RecordSpanError(span, err)
		return nil, fmt.Errorf("iterate authors: %w", err)
	}
	span.SetAttributes(attribute.Int("db.rows_returned", len(authors)))
	return authors, nil
}

func startDBSpan(ctx context.Context, tracer trace.Tracer, name string, operation string, table string) (context.Context, trace.Span) {
	return tracer.Start(ctx, name,
		trace.WithSpanKind(trace.SpanKindClient),
		trace.WithAttributes(
			attribute.String("db.system.name", "postgresql"),
			attribute.String("db.operation.name", operation),
			attribute.String("db.collection.name", table),
		),
	)
}

package postgres

import (
	"context"
	"errors"
	"fmt"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/webbench/golang-service/internal/application"
	"github.com/webbench/golang-service/internal/domain"
	"github.com/webbench/golang-service/internal/telemetry"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
)

var postgresBookTracer = otel.Tracer(telemetry.TracerName("infrastructure/postgres/books"))

type BookRepository struct {
	pool DB
}

func NewBookRepository(pool DB) *BookRepository {
	return &BookRepository{pool: pool}
}

func (r *BookRepository) Create(ctx context.Context, book domain.Book) error {
	ctx, span := startDBSpan(ctx, postgresBookTracer, "postgres.books.insert", "INSERT", "books")
	defer span.End()
	span.SetAttributes(
		attribute.String("book.id", book.ID.String()),
		attribute.String("author.id", book.AuthorID.String()),
	)

	_, err := r.pool.Exec(ctx, `
		INSERT INTO books (id, author_id, title, isbn, published_year, created_at, updated_at)
		VALUES ($1, $2, $3, $4, $5, $6, $7)
	`, book.ID, book.AuthorID, book.Title, book.ISBN, book.PublishedYear, book.CreatedAt, book.UpdatedAt)
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return fmt.Errorf("insert book: %w", err)
	}
	return nil
}

func (r *BookRepository) Update(ctx context.Context, book domain.Book) error {
	ctx, span := startDBSpan(ctx, postgresBookTracer, "postgres.books.update", "UPDATE", "books")
	defer span.End()
	span.SetAttributes(
		attribute.String("book.id", book.ID.String()),
		attribute.String("author.id", book.AuthorID.String()),
	)

	tag, err := r.pool.Exec(ctx, `
		UPDATE books
		SET author_id = $2, title = $3, isbn = $4, published_year = $5, updated_at = $6
		WHERE id = $1
	`, book.ID, book.AuthorID, book.Title, book.ISBN, book.PublishedYear, book.UpdatedAt)
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return fmt.Errorf("update book: %w", err)
	}
	if tag.RowsAffected() == 0 {
		span.SetAttributes(attribute.Int64("db.rows_affected", 0))
		return application.ErrNotFound
	}
	span.SetAttributes(attribute.Int64("db.rows_affected", tag.RowsAffected()))
	return nil
}

func (r *BookRepository) Delete(ctx context.Context, id uuid.UUID) error {
	ctx, span := startDBSpan(ctx, postgresBookTracer, "postgres.books.delete", "DELETE", "books")
	defer span.End()
	span.SetAttributes(attribute.String("book.id", id.String()))

	tag, err := r.pool.Exec(ctx, `DELETE FROM books WHERE id = $1`, id)
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return fmt.Errorf("delete book: %w", err)
	}
	if tag.RowsAffected() == 0 {
		span.SetAttributes(attribute.Int64("db.rows_affected", 0))
		return application.ErrNotFound
	}
	span.SetAttributes(attribute.Int64("db.rows_affected", tag.RowsAffected()))
	return nil
}

func (r *BookRepository) Get(ctx context.Context, id uuid.UUID) (domain.Book, error) {
	ctx, span := startDBSpan(ctx, postgresBookTracer, "postgres.books.select_one", "SELECT", "books")
	defer span.End()
	span.SetAttributes(attribute.String("book.id", id.String()))

	var book domain.Book
	var publishedYear pgtype.Int4
	err := r.pool.QueryRow(ctx, `
		SELECT id, author_id, title, isbn, published_year, created_at, updated_at
		FROM books
		WHERE id = $1
	`, id).Scan(&book.ID, &book.AuthorID, &book.Title, &book.ISBN, &publishedYear, &book.CreatedAt, &book.UpdatedAt)
	if errors.Is(err, pgx.ErrNoRows) {
		return domain.Book{}, application.ErrNotFound
	}
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return domain.Book{}, fmt.Errorf("get book: %w", err)
	}
	book.PublishedYear = publishedYearPtr(publishedYear)
	return book, nil
}

func (r *BookRepository) List(ctx context.Context, options application.BookListOptions) ([]domain.Book, error) {
	ctx, span := startDBSpan(ctx, postgresBookTracer, "postgres.books.select", "SELECT", "books")
	defer span.End()
	span.SetAttributes(attribute.Int("book.limit", options.Limit))

	rows, err := r.pool.Query(ctx, `
		SELECT id, author_id, title, isbn, published_year, created_at, updated_at
		FROM books
		ORDER BY title ASC
		LIMIT $1
	`, options.Limit)
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return nil, fmt.Errorf("list books: %w", err)
	}
	defer rows.Close()

	books, err := scanBooks(rows)
	telemetry.RecordSpanError(span, err)
	if err == nil {
		span.SetAttributes(attribute.Int("db.rows_returned", len(books)))
	}
	return books, err
}

func (r *BookRepository) ListByAuthor(ctx context.Context, authorID uuid.UUID, options application.BookListOptions) ([]domain.Book, error) {
	ctx, span := startDBSpan(ctx, postgresBookTracer, "postgres.books.select_by_author", "SELECT", "books")
	defer span.End()
	span.SetAttributes(
		attribute.String("author.id", authorID.String()),
		attribute.Int("book.limit", options.Limit),
	)

	rows, err := r.pool.Query(ctx, `
		SELECT id, author_id, title, isbn, published_year, created_at, updated_at
		FROM books
		WHERE author_id = $1
		ORDER BY title ASC
		LIMIT $2
	`, authorID, options.Limit)
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return nil, fmt.Errorf("list books by author: %w", err)
	}
	defer rows.Close()

	books, err := scanBooks(rows)
	telemetry.RecordSpanError(span, err)
	if err == nil {
		span.SetAttributes(attribute.Int("db.rows_returned", len(books)))
	}
	return books, err
}

func scanBooks(rows pgx.Rows) ([]domain.Book, error) {
	books := make([]domain.Book, 0)
	for rows.Next() {
		var book domain.Book
		var publishedYear pgtype.Int4
		if err := rows.Scan(&book.ID, &book.AuthorID, &book.Title, &book.ISBN, &publishedYear, &book.CreatedAt, &book.UpdatedAt); err != nil {
			return nil, fmt.Errorf("scan book: %w", err)
		}
		book.PublishedYear = publishedYearPtr(publishedYear)
		books = append(books, book)
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("iterate books: %w", err)
	}
	return books, nil
}

func publishedYearPtr(value pgtype.Int4) *int {
	if !value.Valid {
		return nil
	}
	year := int(value.Int32)
	return &year
}

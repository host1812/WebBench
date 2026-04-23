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
)

type BookRepository struct {
	pool DB
}

func NewBookRepository(pool DB) *BookRepository {
	return &BookRepository{pool: pool}
}

func (r *BookRepository) Create(ctx context.Context, book domain.Book) error {
	_, err := r.pool.Exec(ctx, `
		INSERT INTO books (id, author_id, title, isbn, published_year, created_at, updated_at)
		VALUES ($1, $2, $3, $4, $5, $6, $7)
	`, book.ID, book.AuthorID, book.Title, book.ISBN, book.PublishedYear, book.CreatedAt, book.UpdatedAt)
	if err != nil {
		return fmt.Errorf("insert book: %w", err)
	}
	return nil
}

func (r *BookRepository) Update(ctx context.Context, book domain.Book) error {
	tag, err := r.pool.Exec(ctx, `
		UPDATE books
		SET author_id = $2, title = $3, isbn = $4, published_year = $5, updated_at = $6
		WHERE id = $1
	`, book.ID, book.AuthorID, book.Title, book.ISBN, book.PublishedYear, book.UpdatedAt)
	if err != nil {
		return fmt.Errorf("update book: %w", err)
	}
	if tag.RowsAffected() == 0 {
		return application.ErrNotFound
	}
	return nil
}

func (r *BookRepository) Delete(ctx context.Context, id uuid.UUID) error {
	tag, err := r.pool.Exec(ctx, `DELETE FROM books WHERE id = $1`, id)
	if err != nil {
		return fmt.Errorf("delete book: %w", err)
	}
	if tag.RowsAffected() == 0 {
		return application.ErrNotFound
	}
	return nil
}

func (r *BookRepository) Get(ctx context.Context, id uuid.UUID) (domain.Book, error) {
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
		return domain.Book{}, fmt.Errorf("get book: %w", err)
	}
	book.PublishedYear = publishedYearPtr(publishedYear)
	return book, nil
}

func (r *BookRepository) List(ctx context.Context, options application.BookListOptions) ([]domain.Book, error) {
	rows, err := r.pool.Query(ctx, `
		SELECT id, author_id, title, isbn, published_year, created_at, updated_at
		FROM books
		ORDER BY title ASC
		LIMIT $1
	`, options.Limit)
	if err != nil {
		return nil, fmt.Errorf("list books: %w", err)
	}
	defer rows.Close()

	return scanBooks(rows)
}

func (r *BookRepository) ListByAuthor(ctx context.Context, authorID uuid.UUID, options application.BookListOptions) ([]domain.Book, error) {
	rows, err := r.pool.Query(ctx, `
		SELECT id, author_id, title, isbn, published_year, created_at, updated_at
		FROM books
		WHERE author_id = $1
		ORDER BY title ASC
		LIMIT $2
	`, authorID, options.Limit)
	if err != nil {
		return nil, fmt.Errorf("list books by author: %w", err)
	}
	defer rows.Close()

	return scanBooks(rows)
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

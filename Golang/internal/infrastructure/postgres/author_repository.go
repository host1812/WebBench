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
)

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
	_, err := r.pool.Exec(ctx, `
		INSERT INTO authors (id, name, bio, created_at, updated_at)
		VALUES ($1, $2, $3, $4, $5)
	`, author.ID, author.Name, author.Bio, author.CreatedAt, author.UpdatedAt)
	if err != nil {
		return fmt.Errorf("insert author: %w", err)
	}
	return nil
}

func (r *AuthorRepository) Update(ctx context.Context, author domain.Author) error {
	tag, err := r.pool.Exec(ctx, `
		UPDATE authors
		SET name = $2, bio = $3, updated_at = $4
		WHERE id = $1
	`, author.ID, author.Name, author.Bio, author.UpdatedAt)
	if err != nil {
		return fmt.Errorf("update author: %w", err)
	}
	if tag.RowsAffected() == 0 {
		return application.ErrNotFound
	}
	return nil
}

func (r *AuthorRepository) Delete(ctx context.Context, id uuid.UUID) error {
	tag, err := r.pool.Exec(ctx, `DELETE FROM authors WHERE id = $1`, id)
	if err != nil {
		return fmt.Errorf("delete author: %w", err)
	}
	if tag.RowsAffected() == 0 {
		return application.ErrNotFound
	}
	return nil
}

func (r *AuthorRepository) Get(ctx context.Context, id uuid.UUID) (domain.Author, error) {
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
		return domain.Author{}, fmt.Errorf("get author: %w", err)
	}
	return author, nil
}

func (r *AuthorRepository) List(ctx context.Context) ([]domain.Author, error) {
	rows, err := r.pool.Query(ctx, `
		SELECT id, name, bio, created_at, updated_at
		FROM authors
		ORDER BY name ASC, created_at ASC
	`)
	if err != nil {
		return nil, fmt.Errorf("list authors: %w", err)
	}
	defer rows.Close()

	authors := make([]domain.Author, 0)
	for rows.Next() {
		var author domain.Author
		if err := rows.Scan(&author.ID, &author.Name, &author.Bio, &author.CreatedAt, &author.UpdatedAt); err != nil {
			return nil, fmt.Errorf("scan author: %w", err)
		}
		authors = append(authors, author)
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("iterate authors: %w", err)
	}
	return authors, nil
}

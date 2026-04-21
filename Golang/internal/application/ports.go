package application

import (
	"context"

	"github.com/google/uuid"
	"github.com/webbench/golang-service/internal/domain"
)

type AuthorCommandStore interface {
	Create(ctx context.Context, author domain.Author) error
	Update(ctx context.Context, author domain.Author) error
	Delete(ctx context.Context, id uuid.UUID) error
}

type AuthorQueryStore interface {
	Get(ctx context.Context, id uuid.UUID) (domain.Author, error)
	List(ctx context.Context) ([]domain.Author, error)
}

type BookCommandStore interface {
	Create(ctx context.Context, book domain.Book) error
	Update(ctx context.Context, book domain.Book) error
	Delete(ctx context.Context, id uuid.UUID) error
}

type BookQueryStore interface {
	Get(ctx context.Context, id uuid.UUID) (domain.Book, error)
	List(ctx context.Context, options BookListOptions) ([]domain.Book, error)
	ListByAuthor(ctx context.Context, authorID uuid.UUID, options BookListOptions) ([]domain.Book, error)
}

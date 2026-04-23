package application

import (
	"context"

	"github.com/google/uuid"
	"github.com/webbench/golang-service/internal/domain"
)

type AuthorQueryHandler struct {
	authors AuthorQueryStore
	books   BookQueryStore
}

func NewAuthorQueryHandler(authors AuthorQueryStore, books BookQueryStore) *AuthorQueryHandler {
	return &AuthorQueryHandler{authors: authors, books: books}
}

func (h *AuthorQueryHandler) Get(ctx context.Context, id uuid.UUID) (domain.Author, error) {
	return h.authors.Get(ctx, id)
}

func (h *AuthorQueryHandler) List(ctx context.Context) ([]domain.Author, error) {
	return h.authors.List(ctx)
}

func (h *AuthorQueryHandler) Books(ctx context.Context, id uuid.UUID) ([]domain.Book, error) {
	return h.BooksWithOptions(ctx, id, DefaultBookListOptions())
}

func (h *AuthorQueryHandler) BooksWithOptions(ctx context.Context, id uuid.UUID, options BookListOptions) ([]domain.Book, error) {
	options, err := NormalizeBookListOptions(options)
	if err != nil {
		return nil, err
	}

	if _, err := h.authors.Get(ctx, id); err != nil {
		return nil, err
	}
	return h.books.ListByAuthor(ctx, id, options)
}

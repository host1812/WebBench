package application

import (
	"context"
	"fmt"

	"github.com/google/uuid"
	"github.com/webbench/golang-service/internal/domain"
)

const (
	DefaultBookListLimit = 10000
	MinBookListLimit     = 1
	MaxBookListLimit     = 100000
)

type BookListOptions struct {
	Limit int
}

func DefaultBookListOptions() BookListOptions {
	return BookListOptions{Limit: DefaultBookListLimit}
}

func NormalizeBookListOptions(options BookListOptions) (BookListOptions, error) {
	if options.Limit == 0 {
		return DefaultBookListOptions(), nil
	}
	if options.Limit < MinBookListLimit || options.Limit > MaxBookListLimit {
		return BookListOptions{}, fmt.Errorf("%w: limit must be between 1 and 100000", ErrInvalidInput)
	}
	return options, nil
}

type BookQueryHandler struct {
	books BookQueryStore
}

func NewBookQueryHandler(books BookQueryStore) *BookQueryHandler {
	return &BookQueryHandler{books: books}
}

func (h *BookQueryHandler) Get(ctx context.Context, id uuid.UUID) (domain.Book, error) {
	return h.books.Get(ctx, id)
}

func (h *BookQueryHandler) List(ctx context.Context, options BookListOptions) ([]domain.Book, error) {
	options, err := NormalizeBookListOptions(options)
	if err != nil {
		return nil, err
	}

	return h.books.List(ctx, options)
}

func (h *BookQueryHandler) ListByAuthor(ctx context.Context, authorID uuid.UUID, options BookListOptions) ([]domain.Book, error) {
	options, err := NormalizeBookListOptions(options)
	if err != nil {
		return nil, err
	}

	return h.books.ListByAuthor(ctx, authorID, options)
}

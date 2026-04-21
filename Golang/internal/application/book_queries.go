package application

import (
	"context"
	"fmt"

	"github.com/google/uuid"
	"github.com/webbench/golang-service/internal/domain"
	"github.com/webbench/golang-service/internal/telemetry"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
)

var bookQueryTracer = otel.Tracer(telemetry.TracerName("application/books/queries"))

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
	ctx, span := bookQueryTracer.Start(ctx, "BookQuery.Get")
	defer span.End()
	span.SetAttributes(attribute.String("book.id", id.String()))

	book, err := h.books.Get(ctx, id)
	telemetry.RecordSpanErrorUnless(span, err, ErrNotFound)
	return book, err
}

func (h *BookQueryHandler) List(ctx context.Context, options BookListOptions) ([]domain.Book, error) {
	ctx, span := bookQueryTracer.Start(ctx, "BookQuery.List")
	defer span.End()

	options, err := NormalizeBookListOptions(options)
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return nil, err
	}
	span.SetAttributes(attribute.Int("book.limit", options.Limit))

	books, err := h.books.List(ctx, options)
	telemetry.RecordSpanError(span, err)
	if err == nil {
		span.SetAttributes(attribute.Int("book.count", len(books)))
	}
	return books, err
}

func (h *BookQueryHandler) ListByAuthor(ctx context.Context, authorID uuid.UUID, options BookListOptions) ([]domain.Book, error) {
	ctx, span := bookQueryTracer.Start(ctx, "BookQuery.ListByAuthor")
	defer span.End()

	options, err := NormalizeBookListOptions(options)
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return nil, err
	}
	span.SetAttributes(
		attribute.String("author.id", authorID.String()),
		attribute.Int("book.limit", options.Limit),
	)

	books, err := h.books.ListByAuthor(ctx, authorID, options)
	telemetry.RecordSpanError(span, err)
	if err == nil {
		span.SetAttributes(attribute.Int("book.count", len(books)))
	}
	return books, err
}

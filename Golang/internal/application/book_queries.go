package application

import (
	"context"

	"github.com/google/uuid"
	"github.com/webbench/golang-service/internal/domain"
	"github.com/webbench/golang-service/internal/telemetry"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
)

var bookQueryTracer = otel.Tracer(telemetry.TracerName("application/books/queries"))

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

func (h *BookQueryHandler) List(ctx context.Context) ([]domain.Book, error) {
	ctx, span := bookQueryTracer.Start(ctx, "BookQuery.List")
	defer span.End()

	books, err := h.books.List(ctx)
	telemetry.RecordSpanError(span, err)
	if err == nil {
		span.SetAttributes(attribute.Int("book.count", len(books)))
	}
	return books, err
}

func (h *BookQueryHandler) ListByAuthor(ctx context.Context, authorID uuid.UUID) ([]domain.Book, error) {
	ctx, span := bookQueryTracer.Start(ctx, "BookQuery.ListByAuthor")
	defer span.End()
	span.SetAttributes(attribute.String("author.id", authorID.String()))

	books, err := h.books.ListByAuthor(ctx, authorID)
	telemetry.RecordSpanError(span, err)
	if err == nil {
		span.SetAttributes(attribute.Int("book.count", len(books)))
	}
	return books, err
}

package application

import (
	"context"

	"github.com/google/uuid"
	"github.com/webbench/golang-service/internal/domain"
	"github.com/webbench/golang-service/internal/telemetry"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
)

var authorQueryTracer = otel.Tracer(telemetry.TracerName("application/authors/queries"))

type AuthorQueryHandler struct {
	authors AuthorQueryStore
	books   BookQueryStore
}

func NewAuthorQueryHandler(authors AuthorQueryStore, books BookQueryStore) *AuthorQueryHandler {
	return &AuthorQueryHandler{authors: authors, books: books}
}

func (h *AuthorQueryHandler) Get(ctx context.Context, id uuid.UUID) (domain.Author, error) {
	ctx, span := authorQueryTracer.Start(ctx, "AuthorQuery.Get")
	defer span.End()
	span.SetAttributes(attribute.String("author.id", id.String()))

	author, err := h.authors.Get(ctx, id)
	telemetry.RecordSpanErrorUnless(span, err, ErrNotFound)
	return author, err
}

func (h *AuthorQueryHandler) List(ctx context.Context) ([]domain.Author, error) {
	ctx, span := authorQueryTracer.Start(ctx, "AuthorQuery.List")
	defer span.End()

	authors, err := h.authors.List(ctx)
	telemetry.RecordSpanError(span, err)
	if err == nil {
		span.SetAttributes(attribute.Int("author.count", len(authors)))
	}
	return authors, err
}

func (h *AuthorQueryHandler) Books(ctx context.Context, id uuid.UUID) ([]domain.Book, error) {
	return h.BooksWithOptions(ctx, id, DefaultBookListOptions())
}

func (h *AuthorQueryHandler) BooksWithOptions(ctx context.Context, id uuid.UUID, options BookListOptions) ([]domain.Book, error) {
	ctx, span := authorQueryTracer.Start(ctx, "AuthorQuery.Books")
	defer span.End()

	options, err := NormalizeBookListOptions(options)
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return nil, err
	}
	span.SetAttributes(
		attribute.String("author.id", id.String()),
		attribute.Int("book.limit", options.Limit),
	)

	if _, err := h.authors.Get(ctx, id); err != nil {
		telemetry.RecordSpanErrorUnless(span, err, ErrNotFound)
		return nil, err
	}
	books, err := h.books.ListByAuthor(ctx, id, options)
	telemetry.RecordSpanError(span, err)
	if err == nil {
		span.SetAttributes(attribute.Int("book.count", len(books)))
	}
	return books, err
}

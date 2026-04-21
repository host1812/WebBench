package application

import (
	"context"

	"github.com/google/uuid"
	"github.com/webbench/golang-service/internal/domain"
	"github.com/webbench/golang-service/internal/telemetry"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
)

var bookCommandTracer = otel.Tracer(telemetry.TracerName("application/books/commands"))

type CreateBookCommand struct {
	AuthorID      uuid.UUID
	Title         string
	ISBN          string
	PublishedYear *int
}

type UpdateBookCommand struct {
	ID            uuid.UUID
	AuthorID      uuid.UUID
	Title         string
	ISBN          string
	PublishedYear *int
}

type DeleteBookCommand struct {
	ID uuid.UUID
}

type BookCommandHandler struct {
	commands BookCommandStore
	queries  BookQueryStore
	authors  AuthorQueryStore
	clock    Clock
}

func NewBookCommandHandler(commands BookCommandStore, queries BookQueryStore, authors AuthorQueryStore, clock Clock) *BookCommandHandler {
	return &BookCommandHandler{commands: commands, queries: queries, authors: authors, clock: clock}
}

func (h *BookCommandHandler) Create(ctx context.Context, cmd CreateBookCommand) (domain.Book, error) {
	ctx, span := bookCommandTracer.Start(ctx, "BookCommand.Create")
	defer span.End()
	span.SetAttributes(
		attribute.String("author.id", cmd.AuthorID.String()),
		attribute.String("book.title", cmd.Title),
	)

	if _, err := h.authors.Get(ctx, cmd.AuthorID); err != nil {
		telemetry.RecordSpanErrorUnless(span, err, ErrNotFound)
		return domain.Book{}, err
	}

	book, err := domain.NewBook(uuid.New(), cmd.AuthorID, cmd.Title, cmd.ISBN, cmd.PublishedYear, h.clock.Now())
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return domain.Book{}, err
	}
	if err := h.commands.Create(ctx, book); err != nil {
		telemetry.RecordSpanError(span, err)
		return domain.Book{}, err
	}
	span.SetAttributes(attribute.String("book.id", book.ID.String()))
	return book, nil
}

func (h *BookCommandHandler) Update(ctx context.Context, cmd UpdateBookCommand) (domain.Book, error) {
	ctx, span := bookCommandTracer.Start(ctx, "BookCommand.Update")
	defer span.End()
	span.SetAttributes(
		attribute.String("book.id", cmd.ID.String()),
		attribute.String("author.id", cmd.AuthorID.String()),
	)

	if _, err := h.authors.Get(ctx, cmd.AuthorID); err != nil {
		telemetry.RecordSpanErrorUnless(span, err, ErrNotFound)
		return domain.Book{}, err
	}
	existing, err := h.queries.Get(ctx, cmd.ID)
	if err != nil {
		telemetry.RecordSpanErrorUnless(span, err, ErrNotFound)
		return domain.Book{}, err
	}

	book, err := domain.UpdateBook(cmd.ID, cmd.AuthorID, cmd.Title, cmd.ISBN, cmd.PublishedYear, h.clock.Now())
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return domain.Book{}, err
	}
	book.CreatedAt = existing.CreatedAt
	if err := h.commands.Update(ctx, book); err != nil {
		telemetry.RecordSpanErrorUnless(span, err, ErrNotFound)
		return domain.Book{}, err
	}
	return book, nil
}

func (h *BookCommandHandler) Delete(ctx context.Context, cmd DeleteBookCommand) error {
	ctx, span := bookCommandTracer.Start(ctx, "BookCommand.Delete")
	defer span.End()
	span.SetAttributes(attribute.String("book.id", cmd.ID.String()))

	err := h.commands.Delete(ctx, cmd.ID)
	telemetry.RecordSpanErrorUnless(span, err, ErrNotFound)
	return err
}

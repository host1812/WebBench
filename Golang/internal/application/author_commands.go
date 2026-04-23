package application

import (
	"context"

	"github.com/google/uuid"
	"github.com/webbench/golang-service/internal/domain"
)

type CreateAuthorCommand struct {
	Name string
	Bio  string
}

type UpdateAuthorCommand struct {
	ID   uuid.UUID
	Name string
	Bio  string
}

type DeleteAuthorCommand struct {
	ID uuid.UUID
}

type AuthorCommandHandler struct {
	commands AuthorCommandStore
	queries  AuthorQueryStore
	clock    Clock
}

func NewAuthorCommandHandler(commands AuthorCommandStore, queries AuthorQueryStore, clock Clock) *AuthorCommandHandler {
	return &AuthorCommandHandler{commands: commands, queries: queries, clock: clock}
}

func (h *AuthorCommandHandler) Create(ctx context.Context, cmd CreateAuthorCommand) (domain.Author, error) {
	author, err := domain.NewAuthor(uuid.New(), cmd.Name, cmd.Bio, h.clock.Now())
	if err != nil {
		return domain.Author{}, err
	}
	if err := h.commands.Create(ctx, author); err != nil {
		return domain.Author{}, err
	}
	return author, nil
}

func (h *AuthorCommandHandler) Update(ctx context.Context, cmd UpdateAuthorCommand) (domain.Author, error) {
	existing, err := h.queries.Get(ctx, cmd.ID)
	if err != nil {
		return domain.Author{}, err
	}

	author, err := domain.UpdateAuthor(cmd.ID, cmd.Name, cmd.Bio, h.clock.Now())
	if err != nil {
		return domain.Author{}, err
	}
	author.CreatedAt = existing.CreatedAt
	if err := h.commands.Update(ctx, author); err != nil {
		return domain.Author{}, err
	}
	return author, nil
}

func (h *AuthorCommandHandler) Delete(ctx context.Context, cmd DeleteAuthorCommand) error {
	return h.commands.Delete(ctx, cmd.ID)
}

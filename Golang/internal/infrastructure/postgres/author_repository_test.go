package postgres_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	pgxmock "github.com/pashagolub/pgxmock/v4"
	"github.com/stretchr/testify/require"
	"github.com/webbench/golang-service/internal/application"
	"github.com/webbench/golang-service/internal/domain"
	"github.com/webbench/golang-service/internal/infrastructure/postgres"
)

func TestAuthorRepositoryCreateGetListUpdateDelete(t *testing.T) {
	db, err := pgxmock.NewPool()
	require.NoError(t, err)
	defer db.Close()

	repo := postgres.NewAuthorRepository(db)
	ctx := context.Background()
	now := time.Date(2026, 4, 19, 12, 0, 0, 0, time.UTC)
	author := domain.Author{
		ID:        uuid.New(),
		Name:      "N. K. Jemisin",
		Bio:       "Author",
		CreatedAt: now,
		UpdatedAt: now,
	}

	db.ExpectExec("INSERT INTO authors").
		WithArgs(author.ID, author.Name, author.Bio, author.CreatedAt, author.UpdatedAt).
		WillReturnResult(pgxmock.NewResult("INSERT", 1))

	require.NoError(t, repo.Create(ctx, author))

	db.ExpectQuery("SELECT id, name, bio, created_at, updated_at").
		WithArgs(author.ID).
		WillReturnRows(pgxmock.NewRows([]string{"id", "name", "bio", "created_at", "updated_at"}).
			AddRow(author.ID, author.Name, author.Bio, author.CreatedAt, author.UpdatedAt))

	found, err := repo.Get(ctx, author.ID)
	require.NoError(t, err)
	require.Equal(t, author, found)

	db.ExpectQuery("SELECT id, name, bio, created_at, updated_at").
		WillReturnRows(pgxmock.NewRows([]string{"id", "name", "bio", "created_at", "updated_at"}).
			AddRow(author.ID, author.Name, author.Bio, author.CreatedAt, author.UpdatedAt))

	list, err := repo.List(ctx)
	require.NoError(t, err)
	require.Equal(t, []domain.Author{author}, list)

	author.Name = "Updated"
	author.UpdatedAt = now.Add(time.Hour)
	db.ExpectExec("UPDATE authors").
		WithArgs(author.ID, author.Name, author.Bio, author.UpdatedAt).
		WillReturnResult(pgxmock.NewResult("UPDATE", 1))

	require.NoError(t, repo.Update(ctx, author))

	db.ExpectExec("DELETE FROM authors").
		WithArgs(author.ID).
		WillReturnResult(pgxmock.NewResult("DELETE", 1))

	require.NoError(t, repo.Delete(ctx, author.ID))
	require.NoError(t, db.ExpectationsWereMet())
}

func TestAuthorRepositoryMapsMissingRowsToNotFound(t *testing.T) {
	db, err := pgxmock.NewPool()
	require.NoError(t, err)
	defer db.Close()

	repo := postgres.NewAuthorRepository(db)
	id := uuid.New()

	db.ExpectQuery("SELECT id, name, bio, created_at, updated_at").
		WithArgs(id).
		WillReturnError(pgx.ErrNoRows)

	_, err = repo.Get(context.Background(), id)
	require.True(t, errors.Is(err, application.ErrNotFound))

	missing := domain.Author{ID: id, Name: "Missing", UpdatedAt: time.Now()}
	db.ExpectExec("UPDATE authors").
		WithArgs(missing.ID, missing.Name, missing.Bio, missing.UpdatedAt).
		WillReturnResult(pgxmock.NewResult("UPDATE", 0))

	err = repo.Update(context.Background(), missing)
	require.True(t, errors.Is(err, application.ErrNotFound))
	require.NoError(t, db.ExpectationsWereMet())
}

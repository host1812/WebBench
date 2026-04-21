package postgres_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	pgxmock "github.com/pashagolub/pgxmock/v4"
	"github.com/stretchr/testify/require"
	"github.com/webbench/golang-service/internal/application"
	"github.com/webbench/golang-service/internal/domain"
	"github.com/webbench/golang-service/internal/infrastructure/postgres"
)

func TestBookRepositoryCreateGetListByAuthorUpdateDelete(t *testing.T) {
	db, err := pgxmock.NewPool()
	require.NoError(t, err)
	defer db.Close()

	repo := postgres.NewBookRepository(db)
	ctx := context.Background()
	now := time.Date(2026, 4, 19, 12, 0, 0, 0, time.UTC)
	year := 2015
	book := domain.Book{
		ID:            uuid.New(),
		AuthorID:      uuid.New(),
		Title:         "The Fifth Season",
		ISBN:          "9780316229296",
		PublishedYear: &year,
		CreatedAt:     now,
		UpdatedAt:     now,
	}

	db.ExpectExec("INSERT INTO books").
		WithArgs(book.ID, book.AuthorID, book.Title, book.ISBN, book.PublishedYear, book.CreatedAt, book.UpdatedAt).
		WillReturnResult(pgxmock.NewResult("INSERT", 1))

	require.NoError(t, repo.Create(ctx, book))

	db.ExpectQuery("SELECT id, author_id, title, isbn, published_year, created_at, updated_at").
		WithArgs(book.ID).
		WillReturnRows(bookRows(book))

	found, err := repo.Get(ctx, book.ID)
	require.NoError(t, err)
	require.Equal(t, book, found)

	db.ExpectQuery("SELECT id, author_id, title, isbn, published_year, created_at, updated_at").
		WithArgs(application.DefaultBookListLimit).
		WillReturnRows(bookRows(book))

	list, err := repo.List(ctx, application.DefaultBookListOptions())
	require.NoError(t, err)
	require.Equal(t, []domain.Book{book}, list)

	db.ExpectQuery("SELECT id, author_id, title, isbn, published_year, created_at, updated_at").
		WithArgs(book.AuthorID, application.DefaultBookListLimit).
		WillReturnRows(bookRows(book))

	byAuthor, err := repo.ListByAuthor(ctx, book.AuthorID, application.DefaultBookListOptions())
	require.NoError(t, err)
	require.Equal(t, []domain.Book{book}, byAuthor)

	book.Title = "Updated"
	book.UpdatedAt = now.Add(time.Hour)
	db.ExpectExec("UPDATE books").
		WithArgs(book.ID, book.AuthorID, book.Title, book.ISBN, book.PublishedYear, book.UpdatedAt).
		WillReturnResult(pgxmock.NewResult("UPDATE", 1))

	require.NoError(t, repo.Update(ctx, book))

	db.ExpectExec("DELETE FROM books").
		WithArgs(book.ID).
		WillReturnResult(pgxmock.NewResult("DELETE", 1))

	require.NoError(t, repo.Delete(ctx, book.ID))
	require.NoError(t, db.ExpectationsWereMet())
}

func TestBookRepositoryMapsMissingRowsToNotFound(t *testing.T) {
	db, err := pgxmock.NewPool()
	require.NoError(t, err)
	defer db.Close()

	repo := postgres.NewBookRepository(db)
	id := uuid.New()

	db.ExpectExec("DELETE FROM books").
		WithArgs(id).
		WillReturnResult(pgxmock.NewResult("DELETE", 0))

	err = repo.Delete(context.Background(), id)
	require.True(t, errors.Is(err, application.ErrNotFound))
	require.NoError(t, db.ExpectationsWereMet())
}

func bookRows(book domain.Book) *pgxmock.Rows {
	year := any(nil)
	if book.PublishedYear != nil {
		year = pgtype.Int4{Int32: int32(*book.PublishedYear), Valid: true}
	}
	return pgxmock.NewRows([]string{"id", "author_id", "title", "isbn", "published_year", "created_at", "updated_at"}).
		AddRow(book.ID, book.AuthorID, book.Title, book.ISBN, year, book.CreatedAt, book.UpdatedAt)
}

package postgres_test

import (
	"context"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	pgxmock "github.com/pashagolub/pgxmock/v4"
	"github.com/stretchr/testify/require"
	"github.com/webbench/golang-service/internal/domain"
	"github.com/webbench/golang-service/internal/infrastructure/postgres"
)

func TestStoreRepositoryListIncludesInventory(t *testing.T) {
	db, err := pgxmock.NewPool()
	require.NoError(t, err)
	defer db.Close()

	repo := postgres.NewStoreRepository(db)
	now := time.Date(2026, 4, 19, 12, 0, 0, 0, time.UTC)
	website := "https://northside-books.example.com"
	store := domain.Store{
		ID:          uuid.New(),
		Name:        "Northside Books",
		Description: "Neighborhood bookstore",
		Address:     "101 N Meridian St",
		PhoneNumber: "+1-317-555-0101",
		Website:     &website,
		CreatedAt:   now,
		UpdatedAt:   now,
	}
	year := 1987
	book := domain.Book{
		ID:            uuid.New(),
		AuthorID:      uuid.New(),
		Title:         "Beloved",
		ISBN:          "9781400033416",
		PublishedYear: &year,
		CreatedAt:     now,
		UpdatedAt:     now,
	}

	db.ExpectQuery("SELECT").
		WillReturnRows(storeRows(store, book))

	stores, err := repo.List(context.Background())

	require.NoError(t, err)
	require.Len(t, stores, 1)
	require.Equal(t, store.ID, stores[0].ID)
	require.Equal(t, store.Website, stores[0].Website)
	require.Equal(t, []domain.Book{book}, stores[0].Inventory)
	require.NoError(t, db.ExpectationsWereMet())
}

func storeRows(store domain.Store, book domain.Book) *pgxmock.Rows {
	return pgxmock.NewRows([]string{
		"store_id",
		"name",
		"description",
		"address",
		"phone_number",
		"website",
		"store_created_at",
		"store_updated_at",
		"book_id",
		"author_id",
		"title",
		"isbn",
		"published_year",
		"book_created_at",
		"book_updated_at",
	}).AddRow(
		store.ID,
		store.Name,
		store.Description,
		store.Address,
		store.PhoneNumber,
		*store.Website,
		store.CreatedAt,
		store.UpdatedAt,
		pgtype.UUID{Bytes: [16]byte(book.ID), Valid: true},
		pgtype.UUID{Bytes: [16]byte(book.AuthorID), Valid: true},
		book.Title,
		book.ISBN,
		pgtype.Int4{Int32: int32(*book.PublishedYear), Valid: true},
		book.CreatedAt,
		book.UpdatedAt,
	)
}

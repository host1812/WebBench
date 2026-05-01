package application_test

import (
	"context"
	"errors"
	"fmt"
	"sort"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/require"
	"github.com/webbench/golang-service/internal/application"
	"github.com/webbench/golang-service/internal/domain"
)

type fixedClock struct {
	now time.Time
}

func (c fixedClock) Now() time.Time {
	return c.now
}

type healthPinger struct {
	err error
}

func (p healthPinger) Ping(ctx context.Context) error {
	return p.err
}

type memoryStore struct {
	authors map[uuid.UUID]domain.Author
	books   map[uuid.UUID]domain.Book
	stores  map[uuid.UUID]domain.Store
}

func newMemoryStore() *memoryStore {
	return &memoryStore{
		authors: map[uuid.UUID]domain.Author{},
		books:   map[uuid.UUID]domain.Book{},
		stores:  map[uuid.UUID]domain.Store{},
	}
}

func (s *memoryStore) Create(ctx context.Context, author domain.Author) error {
	s.authors[author.ID] = author
	return nil
}

func (s *memoryStore) Update(ctx context.Context, author domain.Author) error {
	if _, ok := s.authors[author.ID]; !ok {
		return application.ErrNotFound
	}
	s.authors[author.ID] = author
	return nil
}

func (s *memoryStore) Delete(ctx context.Context, id uuid.UUID) error {
	if _, ok := s.authors[id]; !ok {
		return application.ErrNotFound
	}
	delete(s.authors, id)
	for bookID, book := range s.books {
		if book.AuthorID == id {
			delete(s.books, bookID)
		}
	}
	return nil
}

func (s *memoryStore) Get(ctx context.Context, id uuid.UUID) (domain.Author, error) {
	author, ok := s.authors[id]
	if !ok {
		return domain.Author{}, application.ErrNotFound
	}
	return author, nil
}

func (s *memoryStore) List(ctx context.Context) ([]domain.Author, error) {
	authors := make([]domain.Author, 0, len(s.authors))
	for _, author := range s.authors {
		authors = append(authors, author)
	}
	sort.Slice(authors, func(i, j int) bool {
		return authors[i].Name < authors[j].Name
	})
	return authors, nil
}

type memoryBookStore struct {
	*memoryStore
}

func (s memoryBookStore) Create(ctx context.Context, book domain.Book) error {
	s.books[book.ID] = book
	return nil
}

func (s memoryBookStore) Update(ctx context.Context, book domain.Book) error {
	if _, ok := s.books[book.ID]; !ok {
		return application.ErrNotFound
	}
	s.books[book.ID] = book
	return nil
}

func (s memoryBookStore) Delete(ctx context.Context, id uuid.UUID) error {
	if _, ok := s.books[id]; !ok {
		return application.ErrNotFound
	}
	delete(s.books, id)
	return nil
}

func (s memoryBookStore) Get(ctx context.Context, id uuid.UUID) (domain.Book, error) {
	book, ok := s.books[id]
	if !ok {
		return domain.Book{}, application.ErrNotFound
	}
	return book, nil
}

func (s memoryBookStore) List(ctx context.Context, options application.BookListOptions) ([]domain.Book, error) {
	books := make([]domain.Book, 0, len(s.books))
	for _, book := range s.books {
		books = append(books, book)
		if len(books) == options.Limit {
			break
		}
	}
	return books, nil
}

func (s memoryBookStore) ListByAuthor(ctx context.Context, authorID uuid.UUID, options application.BookListOptions) ([]domain.Book, error) {
	books := make([]domain.Book, 0)
	for _, book := range s.books {
		if book.AuthorID == authorID {
			books = append(books, book)
			if len(books) == options.Limit {
				break
			}
		}
	}
	return books, nil
}

type memoryStoreQueryStore struct {
	*memoryStore
}

func (s memoryStoreQueryStore) List(ctx context.Context) ([]domain.Store, error) {
	stores := make([]domain.Store, 0, len(s.stores))
	for _, store := range s.stores {
		stores = append(stores, store)
	}
	sort.Slice(stores, func(i, j int) bool {
		return stores[i].Name < stores[j].Name
	})
	return stores, nil
}

func TestCQRSFlowCreatesAndQueriesAuthorBooks(t *testing.T) {
	ctx := context.Background()
	now := time.Date(2026, 4, 19, 12, 0, 0, 0, time.UTC)
	store := newMemoryStore()
	bookStore := memoryBookStore{memoryStore: store}
	authorCommands := application.NewAuthorCommandHandler(store, store, fixedClock{now: now})
	authorQueries := application.NewAuthorQueryHandler(store, bookStore)
	bookCommands := application.NewBookCommandHandler(bookStore, bookStore, store, fixedClock{now: now.Add(time.Hour)})

	author, err := authorCommands.Create(ctx, application.CreateAuthorCommand{Name: "Toni Morrison", Bio: "Novelist"})
	require.NoError(t, err)

	year := 1987
	book, err := bookCommands.Create(ctx, application.CreateBookCommand{
		AuthorID:      author.ID,
		Title:         "Beloved",
		ISBN:          "9781400033416",
		PublishedYear: &year,
	})
	require.NoError(t, err)

	books, err := authorQueries.Books(ctx, author.ID)
	require.NoError(t, err)
	require.Len(t, books, 1)
	require.Equal(t, book.ID, books[0].ID)
}

func TestUpdatePreservesCreatedAt(t *testing.T) {
	ctx := context.Background()
	createdAt := time.Date(2026, 4, 19, 12, 0, 0, 0, time.UTC)
	updatedAt := createdAt.Add(time.Hour)
	store := newMemoryStore()
	authorCommands := application.NewAuthorCommandHandler(store, store, fixedClock{now: createdAt})

	author, err := authorCommands.Create(ctx, application.CreateAuthorCommand{Name: "Original"})
	require.NoError(t, err)

	authorCommands = application.NewAuthorCommandHandler(store, store, fixedClock{now: updatedAt})
	updated, err := authorCommands.Update(ctx, application.UpdateAuthorCommand{ID: author.ID, Name: "Updated"})

	require.NoError(t, err)
	require.Equal(t, createdAt, updated.CreatedAt)
	require.Equal(t, updatedAt, updated.UpdatedAt)
}

func TestBookQueriesUpdateAndDelete(t *testing.T) {
	ctx := context.Background()
	createdAt := time.Date(2026, 4, 19, 12, 0, 0, 0, time.UTC)
	updatedAt := createdAt.Add(time.Hour)
	store := newMemoryStore()
	bookStore := memoryBookStore{memoryStore: store}
	authorCommands := application.NewAuthorCommandHandler(store, store, fixedClock{now: createdAt})
	bookCommands := application.NewBookCommandHandler(bookStore, bookStore, store, fixedClock{now: createdAt})
	bookQueries := application.NewBookQueryHandler(bookStore)

	author, err := authorCommands.Create(ctx, application.CreateAuthorCommand{Name: "James Baldwin"})
	require.NoError(t, err)
	book, err := bookCommands.Create(ctx, application.CreateBookCommand{AuthorID: author.ID, Title: "Go Tell It on the Mountain"})
	require.NoError(t, err)

	found, err := bookQueries.Get(ctx, book.ID)
	require.NoError(t, err)
	require.Equal(t, book.ID, found.ID)

	books, err := bookQueries.List(ctx, application.DefaultBookListOptions())
	require.NoError(t, err)
	require.Len(t, books, 1)

	bookCommands = application.NewBookCommandHandler(bookStore, bookStore, store, fixedClock{now: updatedAt})
	updated, err := bookCommands.Update(ctx, application.UpdateBookCommand{
		ID:       book.ID,
		AuthorID: author.ID,
		Title:    "Updated Title",
	})
	require.NoError(t, err)
	require.Equal(t, createdAt, updated.CreatedAt)
	require.Equal(t, updatedAt, updated.UpdatedAt)

	require.NoError(t, bookCommands.Delete(ctx, application.DeleteBookCommand{ID: book.ID}))
	_, err = bookQueries.Get(ctx, book.ID)
	require.True(t, errors.Is(err, application.ErrNotFound))
}

func TestBookQueriesValidateLimit(t *testing.T) {
	store := newMemoryStore()
	bookQueries := application.NewBookQueryHandler(memoryBookStore{memoryStore: store})

	_, err := bookQueries.List(context.Background(), application.BookListOptions{Limit: application.MaxBookListLimit + 1})

	require.True(t, errors.Is(err, application.ErrInvalidInput))
}

func TestStoreQueriesListStoresWithInventory(t *testing.T) {
	ctx := context.Background()
	store := newMemoryStore()
	now := time.Date(2026, 4, 19, 12, 0, 0, 0, time.UTC)
	book := domain.Book{
		ID:        uuid.New(),
		AuthorID:  uuid.New(),
		Title:     "Beloved",
		CreatedAt: now,
		UpdatedAt: now,
	}
	store.stores[uuid.New()] = domain.Store{
		ID:          uuid.New(),
		Name:        "Northside Books",
		Description: "Neighborhood shop",
		Address:     "101 N Meridian St",
		PhoneNumber: "+1-317-555-0101",
		Inventory:   []domain.Book{book},
		CreatedAt:   now,
		UpdatedAt:   now,
	}
	queries := application.NewStoreQueryHandler(memoryStoreQueryStore{memoryStore: store})

	stores, err := queries.List(ctx)

	require.NoError(t, err)
	require.Len(t, stores, 1)
	require.Equal(t, "Northside Books", stores[0].Name)
	require.Equal(t, book.ID, stores[0].Inventory[0].ID)
}

func TestCreateBookRequiresExistingAuthor(t *testing.T) {
	ctx := context.Background()
	store := newMemoryStore()
	bookStore := memoryBookStore{memoryStore: store}
	bookCommands := application.NewBookCommandHandler(bookStore, bookStore, store, fixedClock{now: time.Now()})

	_, err := bookCommands.Create(ctx, application.CreateBookCommand{
		AuthorID: uuid.New(),
		Title:    "No Author",
	})

	require.True(t, errors.Is(err, application.ErrNotFound))
}

func TestAuthorCommandsValidateInput(t *testing.T) {
	store := newMemoryStore()
	commands := application.NewAuthorCommandHandler(store, store, fixedClock{now: time.Now()})

	_, err := commands.Create(context.Background(), application.CreateAuthorCommand{Name: " "})

	require.True(t, errors.Is(err, domain.ErrValidation))
}

func TestHealthCheckReportsHealthyDatabase(t *testing.T) {
	handler := application.NewHealthQueryHandler(healthPinger{})

	result := handler.Check(context.Background())

	require.Equal(t, application.HealthStatusOK, result.Status)
	require.Equal(t, "books-service", result.Service)
	require.Equal(t, application.HealthStatusOK, result.Database.Status)
	require.Empty(t, result.Database.Error)
}

func TestHealthCheckReportsDegradedDatabase(t *testing.T) {
	handler := application.NewHealthQueryHandler(healthPinger{err: fmt.Errorf("database unavailable")})

	result := handler.Check(context.Background())

	require.Equal(t, application.HealthStatusDegraded, result.Status)
	require.Equal(t, application.HealthStatusDegraded, result.Database.Status)
	require.Equal(t, "database unavailable", result.Database.Error)
}

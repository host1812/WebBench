package httpapi_test

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/stretchr/testify/require"
	"github.com/webbench/golang-service/internal/application"
	"github.com/webbench/golang-service/internal/domain"
	httpapi "github.com/webbench/golang-service/internal/interfaces/http"
)

type testClock struct {
	now time.Time
}

func (c testClock) Now() time.Time {
	return c.now
}

type testPinger struct {
	err error
}

func (p testPinger) Ping(ctx context.Context) error {
	return p.err
}

type authorStore struct {
	authors map[uuid.UUID]domain.Author
}

func newAuthorStore() *authorStore {
	return &authorStore{authors: map[uuid.UUID]domain.Author{}}
}

func (s *authorStore) Create(ctx context.Context, author domain.Author) error {
	s.authors[author.ID] = author
	return nil
}

func (s *authorStore) Update(ctx context.Context, author domain.Author) error {
	if _, ok := s.authors[author.ID]; !ok {
		return application.ErrNotFound
	}
	s.authors[author.ID] = author
	return nil
}

func (s *authorStore) Delete(ctx context.Context, id uuid.UUID) error {
	if _, ok := s.authors[id]; !ok {
		return application.ErrNotFound
	}
	delete(s.authors, id)
	return nil
}

func (s *authorStore) Get(ctx context.Context, id uuid.UUID) (domain.Author, error) {
	author, ok := s.authors[id]
	if !ok {
		return domain.Author{}, application.ErrNotFound
	}
	return author, nil
}

func (s *authorStore) List(ctx context.Context) ([]domain.Author, error) {
	authors := make([]domain.Author, 0, len(s.authors))
	for _, author := range s.authors {
		authors = append(authors, author)
	}
	return authors, nil
}

type bookStore struct {
	books map[uuid.UUID]domain.Book
}

func newBookStore() *bookStore {
	return &bookStore{books: map[uuid.UUID]domain.Book{}}
}

func (s *bookStore) Create(ctx context.Context, book domain.Book) error {
	s.books[book.ID] = book
	return nil
}

func (s *bookStore) Update(ctx context.Context, book domain.Book) error {
	if _, ok := s.books[book.ID]; !ok {
		return application.ErrNotFound
	}
	s.books[book.ID] = book
	return nil
}

func (s *bookStore) Delete(ctx context.Context, id uuid.UUID) error {
	if _, ok := s.books[id]; !ok {
		return application.ErrNotFound
	}
	delete(s.books, id)
	return nil
}

func (s *bookStore) Get(ctx context.Context, id uuid.UUID) (domain.Book, error) {
	book, ok := s.books[id]
	if !ok {
		return domain.Book{}, application.ErrNotFound
	}
	return book, nil
}

func (s *bookStore) List(ctx context.Context, options application.BookListOptions) ([]domain.Book, error) {
	books := make([]domain.Book, 0, len(s.books))
	for _, book := range s.books {
		books = append(books, book)
		if len(books) == options.Limit {
			break
		}
	}
	return books, nil
}

func (s *bookStore) ListByAuthor(ctx context.Context, authorID uuid.UUID, options application.BookListOptions) ([]domain.Book, error) {
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

type authorResponse struct {
	ID   string `json:"id"`
	Name string `json:"name"`
}

type bookResponse struct {
	ID       string `json:"id"`
	AuthorID string `json:"author_id"`
	Title    string `json:"title"`
}

type healthResponse struct {
	Status  string `json:"status"`
	Service string `json:"service"`
	Checks  map[string]struct {
		Status string `json:"status"`
		Error  string `json:"error"`
	} `json:"checks"`
}

func newTestRouter() *gin.Engine {
	return newTestRouterWithHealthError(nil)
}

func newTestRouterWithHealthError(healthErr error) *gin.Engine {
	gin.SetMode(gin.TestMode)
	now := time.Date(2026, 4, 19, 12, 0, 0, 0, time.UTC)
	authors := newAuthorStore()
	books := newBookStore()
	return httpapi.NewRouter(
		application.NewAuthorCommandHandler(authors, authors, testClock{now: now}),
		application.NewAuthorQueryHandler(authors, books),
		application.NewBookCommandHandler(books, books, authors, testClock{now: now}),
		application.NewBookQueryHandler(books),
		application.NewHealthQueryHandler(testPinger{err: healthErr}),
	)
}

func TestRouterHealthReturnsOKWhenDatabaseIsHealthy(t *testing.T) {
	router := newTestRouter()

	recorder := performRequest(router, http.MethodGet, "/health", "")

	require.Equal(t, http.StatusOK, recorder.Code)
	var response healthResponse
	require.NoError(t, json.Unmarshal(recorder.Body.Bytes(), &response))
	require.Equal(t, application.HealthStatusOK, response.Status)
	require.Equal(t, "books-service", response.Service)
	require.Equal(t, application.HealthStatusOK, response.Checks["database"].Status)
}

func TestRouterHealthReturnsUnavailableWhenDatabaseIsUnhealthy(t *testing.T) {
	router := newTestRouterWithHealthError(fmt.Errorf("ping failed"))

	recorder := performRequest(router, http.MethodGet, "/api/v1/health", "")

	require.Equal(t, http.StatusServiceUnavailable, recorder.Code)
	var response healthResponse
	require.NoError(t, json.Unmarshal(recorder.Body.Bytes(), &response))
	require.Equal(t, application.HealthStatusDegraded, response.Status)
	require.Equal(t, application.HealthStatusDegraded, response.Checks["database"].Status)
	require.Equal(t, "ping failed", response.Checks["database"].Error)
}

func TestRouterCreatesAndListsAuthorBooks(t *testing.T) {
	router := newTestRouter()
	createAuthor := `{"name":"Ursula K. Le Guin","bio":"Author"}`
	authorRecorder := performRequest(router, http.MethodPost, "/api/v1/authors", createAuthor)
	require.Equal(t, http.StatusCreated, authorRecorder.Code)

	var author authorResponse
	require.NoError(t, json.Unmarshal(authorRecorder.Body.Bytes(), &author))
	require.NotEmpty(t, author.ID)

	createBook := `{"author_id":"` + author.ID + `","title":"A Wizard of Earthsea","isbn":"9780547773742","published_year":1968}`
	bookRecorder := performRequest(router, http.MethodPost, "/api/v1/books", createBook)
	require.Equal(t, http.StatusCreated, bookRecorder.Code)

	var book bookResponse
	require.NoError(t, json.Unmarshal(bookRecorder.Body.Bytes(), &book))
	require.Equal(t, author.ID, book.AuthorID)
	require.Equal(t, "A Wizard of Earthsea", book.Title)

	listRecorder := performRequest(router, http.MethodGet, "/api/v1/authors/"+author.ID+"/books", "")
	require.Equal(t, http.StatusOK, listRecorder.Code)

	var books []bookResponse
	require.NoError(t, json.Unmarshal(listRecorder.Body.Bytes(), &books))
	require.Len(t, books, 1)
	require.Equal(t, book.ID, books[0].ID)
}

func TestRouterReturnsBadRequestForInvalidID(t *testing.T) {
	router := newTestRouter()

	recorder := performRequest(router, http.MethodGet, "/api/v1/authors/not-a-uuid", "")

	require.Equal(t, http.StatusBadRequest, recorder.Code)
}

func TestRouterSupportsGetListUpdateAndDelete(t *testing.T) {
	router := newTestRouter()
	author := createAuthor(t, router)
	book := createBook(t, router, author.ID)

	getAuthor := performRequest(router, http.MethodGet, "/api/v1/authors/"+author.ID, "")
	require.Equal(t, http.StatusOK, getAuthor.Code)

	listAuthors := performRequest(router, http.MethodGet, "/api/v1/authors", "")
	require.Equal(t, http.StatusOK, listAuthors.Code)
	var authors []authorResponse
	require.NoError(t, json.Unmarshal(listAuthors.Body.Bytes(), &authors))
	require.Len(t, authors, 1)

	updateAuthor := performRequest(router, http.MethodPut, "/api/v1/authors/"+author.ID, `{"name":"Updated","bio":"Changed"}`)
	require.Equal(t, http.StatusOK, updateAuthor.Code)
	require.NoError(t, json.Unmarshal(updateAuthor.Body.Bytes(), &author))
	require.Equal(t, "Updated", author.Name)

	getBook := performRequest(router, http.MethodGet, "/api/v1/books/"+book.ID, "")
	require.Equal(t, http.StatusOK, getBook.Code)

	listBooks := performRequest(router, http.MethodGet, "/api/v1/books", "")
	require.Equal(t, http.StatusOK, listBooks.Code)
	var books []bookResponse
	require.NoError(t, json.Unmarshal(listBooks.Body.Bytes(), &books))
	require.Len(t, books, 1)

	filteredBooks := performRequest(router, http.MethodGet, "/api/v1/books?author_id="+author.ID, "")
	require.Equal(t, http.StatusOK, filteredBooks.Code)

	updateBookBody := `{"author_id":"` + author.ID + `","title":"Updated Book","isbn":"9780547773742","published_year":1968}`
	updateBook := performRequest(router, http.MethodPut, "/api/v1/books/"+book.ID, updateBookBody)
	require.Equal(t, http.StatusOK, updateBook.Code)
	require.NoError(t, json.Unmarshal(updateBook.Body.Bytes(), &book))
	require.Equal(t, "Updated Book", book.Title)

	deleteBook := performRequest(router, http.MethodDelete, "/api/v1/books/"+book.ID, "")
	require.Equal(t, http.StatusNoContent, deleteBook.Code)

	missingBook := performRequest(router, http.MethodGet, "/api/v1/books/"+book.ID, "")
	require.Equal(t, http.StatusNotFound, missingBook.Code)

	deleteAuthor := performRequest(router, http.MethodDelete, "/api/v1/authors/"+author.ID, "")
	require.Equal(t, http.StatusNoContent, deleteAuthor.Code)
}

func TestRouterSupportsBookListLimit(t *testing.T) {
	router := newTestRouter()
	author := createAuthor(t, router)
	createBookWithTitle(t, router, author.ID, "Book One")
	createBookWithTitle(t, router, author.ID, "Book Two")

	listBooks := performRequest(router, http.MethodGet, "/api/v1/books?limit=1", "")
	require.Equal(t, http.StatusOK, listBooks.Code)

	var books []bookResponse
	require.NoError(t, json.Unmarshal(listBooks.Body.Bytes(), &books))
	require.Len(t, books, 1)

	filteredBooks := performRequest(router, http.MethodGet, "/api/v1/books?author_id="+author.ID+"&limit=1", "")
	require.Equal(t, http.StatusOK, filteredBooks.Code)

	require.NoError(t, json.Unmarshal(filteredBooks.Body.Bytes(), &books))
	require.Len(t, books, 1)

	authorBooks := performRequest(router, http.MethodGet, "/api/v1/authors/"+author.ID+"/books?limit=1", "")
	require.Equal(t, http.StatusOK, authorBooks.Code)

	require.NoError(t, json.Unmarshal(authorBooks.Body.Bytes(), &books))
	require.Len(t, books, 1)
}

func TestRouterReturnsBadRequestForInvalidBodyAndQuery(t *testing.T) {
	router := newTestRouter()

	badBody := performRequest(router, http.MethodPost, "/api/v1/authors", `{`)
	require.Equal(t, http.StatusBadRequest, badBody.Code)

	badQuery := performRequest(router, http.MethodGet, "/api/v1/books?author_id=bad", "")
	require.Equal(t, http.StatusBadRequest, badQuery.Code)

	badLimit := performRequest(router, http.MethodGet, "/api/v1/books?limit=0", "")
	require.Equal(t, http.StatusBadRequest, badLimit.Code)

	tooLargeLimit := performRequest(router, http.MethodGet, "/api/v1/books?limit=100001", "")
	require.Equal(t, http.StatusBadRequest, tooLargeLimit.Code)

	nonNumericLimit := performRequest(router, http.MethodGet, "/api/v1/books?limit=abc", "")
	require.Equal(t, http.StatusBadRequest, nonNumericLimit.Code)
}

func createAuthor(t *testing.T, router http.Handler) authorResponse {
	t.Helper()

	recorder := performRequest(router, http.MethodPost, "/api/v1/authors", `{"name":"Ursula K. Le Guin","bio":"Author"}`)
	require.Equal(t, http.StatusCreated, recorder.Code)

	var author authorResponse
	require.NoError(t, json.Unmarshal(recorder.Body.Bytes(), &author))
	require.NotEmpty(t, author.ID)
	return author
}

func createBook(t *testing.T, router http.Handler, authorID string) bookResponse {
	t.Helper()

	return createBookWithTitle(t, router, authorID, "A Wizard of Earthsea")
}

func createBookWithTitle(t *testing.T, router http.Handler, authorID string, title string) bookResponse {
	t.Helper()

	body := `{"author_id":"` + authorID + `","title":"` + title + `","isbn":"9780547773742","published_year":1968}`
	recorder := performRequest(router, http.MethodPost, "/api/v1/books", body)
	require.Equal(t, http.StatusCreated, recorder.Code)

	var book bookResponse
	require.NoError(t, json.Unmarshal(recorder.Body.Bytes(), &book))
	require.NotEmpty(t, book.ID)
	return book
}

func performRequest(router http.Handler, method string, path string, body string) *httptest.ResponseRecorder {
	req := httptest.NewRequest(method, path, bytes.NewBufferString(body))
	req.Header.Set("Content-Type", "application/json")
	recorder := httptest.NewRecorder()
	router.ServeHTTP(recorder, req)
	return recorder
}

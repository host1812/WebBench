package main

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"os"
	"runtime/debug"
	"strconv"
	"strings"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgconn"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/jackc/pgx/v5/pgxpool"
)

const (
	defaultHTTPAddress    = ":8080"
	defaultServiceName    = "books-service"
	defaultMaxConnections = int32(10)
	defaultBookListLimit  = 10000
	minBookListLimit      = 1
	maxBookListLimit      = 100000
	maxAuthorBioLength    = 4000
	maxISBNLength         = 32
	healthStatusOK        = "ok"
	healthStatusDegraded  = "degraded"
	postgresFKViolation   = "23503"
)

var (
	errNotFound   = errors.New("not found")
	errValidation = errors.New("validation failed")
)

type config struct {
	httpAddress    string
	databaseURL    string
	maxConnections int32
	serviceName    string
}

type app struct {
	db          *pgxpool.Pool
	logger      *slog.Logger
	serviceName string
	now         func() time.Time
}

type authorResponse struct {
	ID        string    `json:"id"`
	Name      string    `json:"name"`
	Bio       string    `json:"bio"`
	CreatedAt time.Time `json:"created_at"`
	UpdatedAt time.Time `json:"updated_at"`
}

type bookResponse struct {
	ID            string    `json:"id"`
	AuthorID      string    `json:"author_id"`
	Title         string    `json:"title"`
	ISBN          string    `json:"isbn"`
	PublishedYear *int      `json:"published_year,omitempty"`
	CreatedAt     time.Time `json:"created_at"`
	UpdatedAt     time.Time `json:"updated_at"`
}

type storeResponse struct {
	ID          string         `json:"id"`
	Name        string         `json:"name"`
	Description string         `json:"description"`
	Address     string         `json:"address"`
	PhoneNumber string         `json:"phone_number"`
	Website     *string        `json:"website,omitempty"`
	Inventory   []bookResponse `json:"inventory"`
	CreatedAt   time.Time      `json:"created_at"`
	UpdatedAt   time.Time      `json:"updated_at"`
}

type componentHealthResponse struct {
	Status string `json:"status"`
	Error  string `json:"error,omitempty"`
}

type healthResponse struct {
	Status  string                             `json:"status"`
	Service string                             `json:"service"`
	Time    time.Time                          `json:"time"`
	Checks  map[string]componentHealthResponse `json:"checks"`
}

type errorResponse struct {
	Error string `json:"error"`
}

type createAuthorRequest struct {
	Name string `json:"name"`
	Bio  string `json:"bio"`
}

type updateAuthorRequest struct {
	Name string `json:"name"`
	Bio  string `json:"bio"`
}

type createBookRequest struct {
	AuthorID      string `json:"author_id"`
	Title         string `json:"title"`
	ISBN          string `json:"isbn"`
	PublishedYear *int   `json:"published_year"`
}

type updateBookRequest struct {
	AuthorID      string `json:"author_id"`
	Title         string `json:"title"`
	ISBN          string `json:"isbn"`
	PublishedYear *int   `json:"published_year"`
}

type statusRecorder struct {
	http.ResponseWriter
	statusCode int
}

func (r *statusRecorder) WriteHeader(statusCode int) {
	r.statusCode = statusCode
	r.ResponseWriter.WriteHeader(statusCode)
}

func main() {
	logger := slog.New(slog.NewTextHandler(os.Stdout, &slog.HandlerOptions{Level: slog.LevelInfo}))

	cfg, err := loadConfig()
	if err != nil {
		logger.Error("load config failed", "error", err)
		os.Exit(1)
	}

	poolConfig, err := pgxpool.ParseConfig(cfg.databaseURL)
	if err != nil {
		logger.Error("parse database config failed", "error", err)
		os.Exit(1)
	}
	poolConfig.MaxConns = cfg.maxConnections
	poolConfig.ConnConfig.DefaultQueryExecMode = pgx.QueryExecModeCacheStatement

	pool, err := pgxpool.NewWithConfig(context.Background(), poolConfig)
	if err != nil {
		logger.Error("connect database failed", "error", err)
		os.Exit(1)
	}
	defer pool.Close()

	startupCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := pool.Ping(startupCtx); err != nil {
		logger.Error("database ping failed", "error", err)
		os.Exit(1)
	}

	logger.Info("database connection ready", "max_connections", cfg.maxConnections)

	server := &http.Server{
		Addr:              cfg.httpAddress,
		Handler:           newApp(pool, logger, cfg.serviceName).routes(),
		ReadHeaderTimeout: 5 * time.Second,
	}

	logger.Info("service starting", "service", cfg.serviceName, "address", cfg.httpAddress)
	if err := server.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
		logger.Error("server failed", "error", err)
		os.Exit(1)
	}
}

func newApp(db *pgxpool.Pool, logger *slog.Logger, serviceName string) *app {
	return &app{
		db:          db,
		logger:      logger,
		serviceName: serviceName,
		now: func() time.Time {
			return time.Now().UTC()
		},
	}
}

func (a *app) routes() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /health", a.health)
	mux.HandleFunc("GET /api/v1/health", a.health)

	mux.HandleFunc("POST /api/v1/authors", a.createAuthor)
	mux.HandleFunc("GET /api/v1/authors", a.listAuthors)
	mux.HandleFunc("GET /api/v1/authors/{id}", a.getAuthor)
	mux.HandleFunc("PUT /api/v1/authors/{id}", a.updateAuthor)
	mux.HandleFunc("DELETE /api/v1/authors/{id}", a.deleteAuthor)
	mux.HandleFunc("GET /api/v1/authors/{id}/books", a.listAuthorBooks)

	mux.HandleFunc("POST /api/v1/books", a.createBook)
	mux.HandleFunc("GET /api/v1/books", a.listBooks)
	mux.HandleFunc("GET /api/v1/books/{id}", a.getBook)
	mux.HandleFunc("PUT /api/v1/books/{id}", a.updateBook)
	mux.HandleFunc("DELETE /api/v1/books/{id}", a.deleteBook)

	mux.HandleFunc("GET /api/v1/stores", a.listStores)
	mux.HandleFunc("GET /api/v1/stores/{id}", a.getStore)

	return requestLogging(a.logger, recoverJSON(a.logger, mux))
}

func recoverJSON(logger *slog.Logger, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		defer func() {
			if recovered := recover(); recovered != nil {
				logger.Error("request panicked",
					"method", r.Method,
					"path", r.URL.Path,
					"query", r.URL.RawQuery,
					"remote_addr", r.RemoteAddr,
					"panic", recovered,
					"stack", string(debug.Stack()),
				)
				writeJSON(w, http.StatusInternalServerError, errorResponse{Error: "internal server error"})
			}
		}()
		next.ServeHTTP(w, r)
	})
}

func requestLogging(logger *slog.Logger, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		started := time.Now()
		recorder := &statusRecorder{
			ResponseWriter: w,
			statusCode:     http.StatusOK,
		}

		next.ServeHTTP(recorder, r)

		logger.Info("request completed",
			"method", r.Method,
			"path", r.URL.Path,
			"query", r.URL.RawQuery,
			"status", recorder.statusCode,
			"duration_ms", time.Since(started).Milliseconds(),
			"remote_addr", r.RemoteAddr,
		)
	})
}

func (a *app) health(w http.ResponseWriter, r *http.Request) {
	ctx, cancel := context.WithTimeout(r.Context(), 2*time.Second)
	defer cancel()

	response := healthResponse{
		Status:  healthStatusOK,
		Service: a.serviceName,
		Time:    a.now(),
		Checks: map[string]componentHealthResponse{
			"database": {Status: healthStatusOK},
		},
	}

	statusCode := http.StatusOK
	if err := a.db.Ping(ctx); err != nil {
		a.logger.Warn("health check database ping failed", "error", err)
		statusCode = http.StatusServiceUnavailable
		response.Status = healthStatusDegraded
		response.Checks["database"] = componentHealthResponse{
			Status: healthStatusDegraded,
			Error:  err.Error(),
		}
	}

	writeJSON(w, statusCode, response)
}

func (a *app) createAuthor(w http.ResponseWriter, r *http.Request) {
	var req createAuthorRequest
	if err := decodeJSON(r, &req); err != nil {
		a.writeError(w, r, validationError("invalid request body"), "create author decode json")
		return
	}

	name, bio, err := validateAuthorInput(req.Name, req.Bio)
	if err != nil {
		a.writeError(w, r, err, "create author validate input")
		return
	}

	now := a.now()
	response := authorResponse{
		ID:        uuid.NewString(),
		Name:      name,
		Bio:       bio,
		CreatedAt: now,
		UpdatedAt: now,
	}

	_, err = a.db.Exec(r.Context(), `
		INSERT INTO authors (id, name, bio, created_at, updated_at)
		VALUES ($1, $2, $3, $4, $5)
	`, response.ID, response.Name, response.Bio, response.CreatedAt, response.UpdatedAt)
	if err != nil {
		a.writeError(w, r, err, "create author insert")
		return
	}

	writeJSON(w, http.StatusCreated, response)
}

func (a *app) listAuthors(w http.ResponseWriter, r *http.Request) {
	rows, err := a.db.Query(r.Context(), `
		SELECT id, name, bio, created_at, updated_at
		FROM authors
		ORDER BY name ASC, created_at ASC
	`)
	if err != nil {
		a.writeError(w, r, err, "list authors query")
		return
	}
	defer rows.Close()

	authors := make([]authorResponse, 0)
	for rows.Next() {
		var author authorResponse
		if err := rows.Scan(&author.ID, &author.Name, &author.Bio, &author.CreatedAt, &author.UpdatedAt); err != nil {
			a.writeError(w, r, err, "list authors scan")
			return
		}
		authors = append(authors, author)
	}
	if err := rows.Err(); err != nil {
		a.writeError(w, r, err, "list authors iterate")
		return
	}

	writeJSON(w, http.StatusOK, authors)
}

func (a *app) getAuthor(w http.ResponseWriter, r *http.Request) {
	id, err := parseID(r.PathValue("id"), "invalid id")
	if err != nil {
		a.writeError(w, r, err, "get author parse id")
		return
	}

	var author authorResponse
	err = a.db.QueryRow(r.Context(), `
		SELECT id, name, bio, created_at, updated_at
		FROM authors
		WHERE id = $1
	`, id).Scan(&author.ID, &author.Name, &author.Bio, &author.CreatedAt, &author.UpdatedAt)
	if errors.Is(err, pgx.ErrNoRows) {
		a.writeError(w, r, errNotFound, "get author not found")
		return
	}
	if err != nil {
		a.writeError(w, r, err, "get author query")
		return
	}

	writeJSON(w, http.StatusOK, author)
}

func (a *app) updateAuthor(w http.ResponseWriter, r *http.Request) {
	id, err := parseID(r.PathValue("id"), "invalid id")
	if err != nil {
		a.writeError(w, r, err, "update author parse id")
		return
	}

	var req updateAuthorRequest
	if err := decodeJSON(r, &req); err != nil {
		a.writeError(w, r, validationError("invalid request body"), "update author decode json")
		return
	}

	name, bio, err := validateAuthorInput(req.Name, req.Bio)
	if err != nil {
		a.writeError(w, r, err, "update author validate input")
		return
	}

	now := a.now()
	response := authorResponse{
		ID:        id.String(),
		Name:      name,
		Bio:       bio,
		UpdatedAt: now,
	}

	err = a.db.QueryRow(r.Context(), `
		UPDATE authors
		SET name = $2, bio = $3, updated_at = $4
		WHERE id = $1
		RETURNING created_at, updated_at
	`, id, response.Name, response.Bio, response.UpdatedAt).Scan(&response.CreatedAt, &response.UpdatedAt)
	if errors.Is(err, pgx.ErrNoRows) {
		a.writeError(w, r, errNotFound, "update author not found")
		return
	}
	if err != nil {
		a.writeError(w, r, err, "update author query")
		return
	}

	writeJSON(w, http.StatusOK, response)
}

func (a *app) deleteAuthor(w http.ResponseWriter, r *http.Request) {
	id, err := parseID(r.PathValue("id"), "invalid id")
	if err != nil {
		a.writeError(w, r, err, "delete author parse id")
		return
	}

	tag, err := a.db.Exec(r.Context(), `DELETE FROM authors WHERE id = $1`, id)
	if err != nil {
		a.writeError(w, r, err, "delete author query")
		return
	}
	if tag.RowsAffected() == 0 {
		a.writeError(w, r, errNotFound, "delete author not found")
		return
	}

	w.WriteHeader(http.StatusNoContent)
}

func (a *app) listAuthorBooks(w http.ResponseWriter, r *http.Request) {
	id, err := parseID(r.PathValue("id"), "invalid id")
	if err != nil {
		a.writeError(w, r, err, "list author books parse id")
		return
	}

	limit, err := parseLimit(r)
	if err != nil {
		a.writeError(w, r, err, "list author books parse limit")
		return
	}

	books, err := a.queryBooks(r.Context(), `
		SELECT id, author_id, title, isbn, published_year, created_at, updated_at
		FROM books
		WHERE author_id = $1
		ORDER BY title ASC
		LIMIT $2
	`, id, limit)
	if err != nil {
		a.writeError(w, r, err, "list author books query")
		return
	}

	if len(books) == 0 {
		exists, err := a.authorExists(r.Context(), id)
		if err != nil {
			a.writeError(w, r, err, "list author books author exists")
			return
		}
		if !exists {
			a.writeError(w, r, errNotFound, "list author books author missing")
			return
		}
	}

	writeJSON(w, http.StatusOK, books)
}

func (a *app) createBook(w http.ResponseWriter, r *http.Request) {
	var req createBookRequest
	if err := decodeJSON(r, &req); err != nil {
		a.writeError(w, r, validationError("invalid request body"), "create book decode json")
		return
	}

	authorID, title, isbn, publishedYear, err := validateBookInput(req.AuthorID, req.Title, req.ISBN, req.PublishedYear)
	if err != nil {
		a.writeError(w, r, err, "create book validate input")
		return
	}

	now := a.now()
	response := bookResponse{
		ID:            uuid.NewString(),
		AuthorID:      authorID.String(),
		Title:         title,
		ISBN:          isbn,
		PublishedYear: publishedYear,
		CreatedAt:     now,
		UpdatedAt:     now,
	}

	_, err = a.db.Exec(r.Context(), `
		INSERT INTO books (id, author_id, title, isbn, published_year, created_at, updated_at)
		VALUES ($1, $2, $3, $4, $5, $6, $7)
	`, response.ID, authorID, response.Title, response.ISBN, response.PublishedYear, response.CreatedAt, response.UpdatedAt)
	if err != nil {
		a.writeError(w, r, normalizeWriteError(err), "create book insert")
		return
	}

	writeJSON(w, http.StatusCreated, response)
}

func (a *app) listBooks(w http.ResponseWriter, r *http.Request) {
	limit, err := parseLimit(r)
	if err != nil {
		a.writeError(w, r, err, "list books parse limit")
		return
	}

	authorIDValue := strings.TrimSpace(r.URL.Query().Get("author_id"))
	if authorIDValue == "" {
		books, err := a.queryBooks(r.Context(), `
			SELECT id, author_id, title, isbn, published_year, created_at, updated_at
			FROM books
			ORDER BY title ASC
			LIMIT $1
		`, limit)
		if err != nil {
			a.writeError(w, r, err, "list books query")
			return
		}
		writeJSON(w, http.StatusOK, books)
		return
	}

	authorID, err := parseID(authorIDValue, "invalid author id")
	if err != nil {
		a.writeError(w, r, err, "list books parse author id")
		return
	}

	books, err := a.queryBooks(r.Context(), `
		SELECT id, author_id, title, isbn, published_year, created_at, updated_at
		FROM books
		WHERE author_id = $1
		ORDER BY title ASC
		LIMIT $2
	`, authorID, limit)
	if err != nil {
		a.writeError(w, r, err, "list books by author query")
		return
	}

	writeJSON(w, http.StatusOK, books)
}

func (a *app) getBook(w http.ResponseWriter, r *http.Request) {
	id, err := parseID(r.PathValue("id"), "invalid id")
	if err != nil {
		a.writeError(w, r, err, "get book parse id")
		return
	}

	book, err := a.queryOneBook(r.Context(), `
		SELECT id, author_id, title, isbn, published_year, created_at, updated_at
		FROM books
		WHERE id = $1
	`, id)
	if errors.Is(err, pgx.ErrNoRows) {
		a.writeError(w, r, errNotFound, "get book not found")
		return
	}
	if err != nil {
		a.writeError(w, r, err, "get book query")
		return
	}

	writeJSON(w, http.StatusOK, book)
}

func (a *app) updateBook(w http.ResponseWriter, r *http.Request) {
	id, err := parseID(r.PathValue("id"), "invalid id")
	if err != nil {
		a.writeError(w, r, err, "update book parse id")
		return
	}

	var req updateBookRequest
	if err := decodeJSON(r, &req); err != nil {
		a.writeError(w, r, validationError("invalid request body"), "update book decode json")
		return
	}

	authorID, title, isbn, publishedYear, err := validateBookInput(req.AuthorID, req.Title, req.ISBN, req.PublishedYear)
	if err != nil {
		a.writeError(w, r, err, "update book validate input")
		return
	}

	response := bookResponse{
		ID:            id.String(),
		AuthorID:      authorID.String(),
		Title:         title,
		ISBN:          isbn,
		PublishedYear: publishedYear,
		UpdatedAt:     a.now(),
	}

	err = a.db.QueryRow(r.Context(), `
		UPDATE books
		SET author_id = $2, title = $3, isbn = $4, published_year = $5, updated_at = $6
		WHERE id = $1
		RETURNING created_at, updated_at
	`, id, authorID, response.Title, response.ISBN, response.PublishedYear, response.UpdatedAt).
		Scan(&response.CreatedAt, &response.UpdatedAt)
	if errors.Is(err, pgx.ErrNoRows) {
		a.writeError(w, r, errNotFound, "update book not found")
		return
	}
	if err != nil {
		a.writeError(w, r, normalizeWriteError(err), "update book query")
		return
	}

	writeJSON(w, http.StatusOK, response)
}

func (a *app) deleteBook(w http.ResponseWriter, r *http.Request) {
	id, err := parseID(r.PathValue("id"), "invalid id")
	if err != nil {
		a.writeError(w, r, err, "delete book parse id")
		return
	}

	tag, err := a.db.Exec(r.Context(), `DELETE FROM books WHERE id = $1`, id)
	if err != nil {
		a.writeError(w, r, err, "delete book query")
		return
	}
	if tag.RowsAffected() == 0 {
		a.writeError(w, r, errNotFound, "delete book not found")
		return
	}

	w.WriteHeader(http.StatusNoContent)
}

func (a *app) listStores(w http.ResponseWriter, r *http.Request) {
	stores, err := a.queryStores(r.Context(), `
		SELECT id, name, description, address, phone_number, website, created_at, updated_at
		FROM stores
		ORDER BY name ASC, created_at ASC
	`)
	if err != nil {
		a.writeError(w, r, err, "list stores query")
		return
	}

	if err := a.loadStoreInventory(r.Context(), stores, `
		SELECT sb.store_id, b.id, b.author_id, b.title, b.isbn, b.published_year, b.created_at, b.updated_at
		FROM store_books sb
		JOIN books b ON b.id = sb.book_id
		ORDER BY sb.store_id ASC, b.title ASC
	`); err != nil {
		a.writeError(w, r, err, "list stores inventory query")
		return
	}

	writeJSON(w, http.StatusOK, stores)
}

func (a *app) getStore(w http.ResponseWriter, r *http.Request) {
	id, err := parseID(r.PathValue("id"), "invalid id")
	if err != nil {
		a.writeError(w, r, err, "get store parse id")
		return
	}

	stores, err := a.queryStores(r.Context(), `
		SELECT id, name, description, address, phone_number, website, created_at, updated_at
		FROM stores
		WHERE id = $1
	`, id)
	if err != nil {
		a.writeError(w, r, err, "get store query")
		return
	}
	if len(stores) == 0 {
		a.writeError(w, r, errNotFound, "get store not found")
		return
	}

	if err := a.loadStoreInventory(r.Context(), stores, `
		SELECT sb.store_id, b.id, b.author_id, b.title, b.isbn, b.published_year, b.created_at, b.updated_at
		FROM store_books sb
		JOIN books b ON b.id = sb.book_id
		WHERE sb.store_id = $1
		ORDER BY b.title ASC
	`, id); err != nil {
		a.writeError(w, r, err, "get store inventory query")
		return
	}

	writeJSON(w, http.StatusOK, stores[0])
}

func (a *app) authorExists(ctx context.Context, id uuid.UUID) (bool, error) {
	var exists int
	err := a.db.QueryRow(ctx, `SELECT 1 FROM authors WHERE id = $1`, id).Scan(&exists)
	if errors.Is(err, pgx.ErrNoRows) {
		return false, nil
	}
	if err != nil {
		return false, err
	}
	return true, nil
}

func (a *app) queryOneBook(ctx context.Context, sql string, args ...any) (bookResponse, error) {
	var (
		book          bookResponse
		publishedYear pgtype.Int4
	)

	err := a.db.QueryRow(ctx, sql, args...).Scan(
		&book.ID,
		&book.AuthorID,
		&book.Title,
		&book.ISBN,
		&publishedYear,
		&book.CreatedAt,
		&book.UpdatedAt,
	)
	if err != nil {
		return bookResponse{}, err
	}

	book.PublishedYear = publishedYearPtr(publishedYear)
	return book, nil
}

func (a *app) queryBooks(ctx context.Context, sql string, args ...any) ([]bookResponse, error) {
	rows, err := a.db.Query(ctx, sql, args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	books := make([]bookResponse, 0)
	for rows.Next() {
		var (
			book          bookResponse
			publishedYear pgtype.Int4
		)
		if err := rows.Scan(
			&book.ID,
			&book.AuthorID,
			&book.Title,
			&book.ISBN,
			&publishedYear,
			&book.CreatedAt,
			&book.UpdatedAt,
		); err != nil {
			return nil, err
		}
		book.PublishedYear = publishedYearPtr(publishedYear)
		books = append(books, book)
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}

	return books, nil
}

func (a *app) queryStores(ctx context.Context, sql string, args ...any) ([]storeResponse, error) {
	rows, err := a.db.Query(ctx, sql, args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	stores := make([]storeResponse, 0)
	for rows.Next() {
		var (
			store   storeResponse
			website pgtype.Text
		)
		if err := rows.Scan(
			&store.ID,
			&store.Name,
			&store.Description,
			&store.Address,
			&store.PhoneNumber,
			&website,
			&store.CreatedAt,
			&store.UpdatedAt,
		); err != nil {
			return nil, err
		}
		store.Website = textPtr(website)
		store.Inventory = make([]bookResponse, 0)
		stores = append(stores, store)
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}

	return stores, nil
}

func (a *app) loadStoreInventory(ctx context.Context, stores []storeResponse, sql string, args ...any) error {
	if len(stores) == 0 {
		return nil
	}

	storeIndexes := make(map[string]int, len(stores))
	for i := range stores {
		storeIndexes[stores[i].ID] = i
	}

	rows, err := a.db.Query(ctx, sql, args...)
	if err != nil {
		return err
	}
	defer rows.Close()

	for rows.Next() {
		var (
			storeID       string
			book          bookResponse
			publishedYear pgtype.Int4
		)
		if err := rows.Scan(
			&storeID,
			&book.ID,
			&book.AuthorID,
			&book.Title,
			&book.ISBN,
			&publishedYear,
			&book.CreatedAt,
			&book.UpdatedAt,
		); err != nil {
			return err
		}
		storeIndex, ok := storeIndexes[storeID]
		if !ok {
			continue
		}
		book.PublishedYear = publishedYearPtr(publishedYear)
		stores[storeIndex].Inventory = append(stores[storeIndex].Inventory, book)
	}
	if err := rows.Err(); err != nil {
		return err
	}

	return nil
}

func loadConfig() (config, error) {
	cfg := config{
		httpAddress:    firstNonEmpty(strings.TrimSpace(os.Getenv("BOOKSVC_HTTP_ADDRESS")), defaultHTTPAddress),
		databaseURL:    strings.TrimSpace(os.Getenv("BOOKSVC_DATABASE_CONNECTION_STRING")),
		maxConnections: defaultMaxConnections,
		serviceName:    firstNonEmpty(strings.TrimSpace(os.Getenv("BOOKSVC_SERVICE_NAME")), defaultServiceName),
	}

	if cfg.databaseURL == "" {
		return config{}, fmt.Errorf("BOOKSVC_DATABASE_CONNECTION_STRING is required")
	}

	if value := strings.TrimSpace(os.Getenv("BOOKSVC_DATABASE_MAX_CONNECTIONS")); value != "" {
		parsed, err := strconv.ParseInt(value, 10, 32)
		if err != nil || parsed < 1 {
			return config{}, fmt.Errorf("BOOKSVC_DATABASE_MAX_CONNECTIONS must be a positive integer")
		}
		cfg.maxConnections = int32(parsed)
	}

	return cfg, nil
}

func validateAuthorInput(name string, bio string) (string, string, error) {
	name = strings.TrimSpace(name)
	bio = strings.TrimSpace(bio)

	if name == "" {
		return "", "", validationError("author name is required")
	}
	if len(bio) > maxAuthorBioLength {
		return "", "", validationError(fmt.Sprintf("author bio must be %d characters or fewer", maxAuthorBioLength))
	}

	return name, bio, nil
}

func validateBookInput(authorIDValue string, title string, isbn string, publishedYear *int) (uuid.UUID, string, string, *int, error) {
	authorID, err := parseID(authorIDValue, "invalid author id")
	if err != nil {
		return uuid.Nil, "", "", nil, err
	}

	title = strings.TrimSpace(title)
	isbn = strings.TrimSpace(isbn)
	if title == "" {
		return uuid.Nil, "", "", nil, validationError("book title is required")
	}
	if len(isbn) > maxISBNLength {
		return uuid.Nil, "", "", nil, validationError(fmt.Sprintf("isbn must be %d characters or fewer", maxISBNLength))
	}
	if publishedYear != nil {
		year := *publishedYear
		if year < 1450 || year > time.Now().Year()+1 {
			return uuid.Nil, "", "", nil, validationError("published year is out of range")
		}
	}

	return authorID, title, isbn, publishedYear, nil
}

func parseID(value string, message string) (uuid.UUID, error) {
	id, err := uuid.Parse(strings.TrimSpace(value))
	if err != nil {
		return uuid.Nil, validationError(message)
	}
	return id, nil
}

func parseLimit(r *http.Request) (int, error) {
	value := strings.TrimSpace(r.URL.Query().Get("limit"))
	if value == "" {
		return defaultBookListLimit, nil
	}

	limit, err := strconv.Atoi(value)
	if err != nil {
		return 0, validationError("invalid limit")
	}
	if limit < minBookListLimit || limit > maxBookListLimit {
		return 0, validationError("limit must be between 1 and 100000")
	}

	return limit, nil
}

func normalizeWriteError(err error) error {
	var pgError *pgconn.PgError
	if errors.As(err, &pgError) && pgError.Code == postgresFKViolation {
		return errNotFound
	}
	return err
}

func publishedYearPtr(value pgtype.Int4) *int {
	if !value.Valid {
		return nil
	}
	year := int(value.Int32)
	return &year
}

func textPtr(value pgtype.Text) *string {
	if !value.Valid {
		return nil
	}
	text := value.String
	return &text
}

func decodeJSON(r *http.Request, dst any) error {
	decoder := json.NewDecoder(r.Body)
	if err := decoder.Decode(dst); err != nil {
		return err
	}
	var extra any
	if err := decoder.Decode(&extra); !errors.Is(err, io.EOF) {
		if err == nil {
			return fmt.Errorf("unexpected trailing JSON")
		}
		return err
	}
	return nil
}

func (a *app) writeError(w http.ResponseWriter, r *http.Request, err error, operation string) {
	attrs := []any{
		"operation", operation,
		"method", r.Method,
		"path", r.URL.Path,
		"query", r.URL.RawQuery,
	}

	switch {
	case errors.Is(err, errNotFound):
		a.logger.Info("request returned not found", append(attrs, "error", err.Error(), "status", http.StatusNotFound)...)
		writeJSON(w, http.StatusNotFound, errorResponse{Error: "resource not found"})
	case errors.Is(err, errValidation):
		a.logger.Info("request returned validation error", append(attrs, "error", err.Error(), "status", http.StatusBadRequest)...)
		writeJSON(w, http.StatusBadRequest, errorResponse{Error: err.Error()})
	default:
		a.logger.Error("request returned internal error", append(attrs, "error", err.Error(), "status", http.StatusInternalServerError)...)
		writeJSON(w, http.StatusInternalServerError, errorResponse{Error: "internal server error"})
	}
}

func writeJSON(w http.ResponseWriter, statusCode int, payload any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(statusCode)
	encoder := json.NewEncoder(w)
	encoder.SetEscapeHTML(false)
	_ = encoder.Encode(payload)
}

func validationError(message string) error {
	return fmt.Errorf("%w: %s", errValidation, message)
}

func firstNonEmpty(values ...string) string {
	for _, value := range values {
		if strings.TrimSpace(value) != "" {
			return value
		}
	}
	return ""
}

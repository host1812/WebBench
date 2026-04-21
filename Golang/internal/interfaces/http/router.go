package httpapi

import (
	"context"
	"errors"
	"fmt"
	"net/http"
	"strconv"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/webbench/golang-service/internal/application"
	"github.com/webbench/golang-service/internal/config"
	"github.com/webbench/golang-service/internal/domain"
	"go.opentelemetry.io/contrib/instrumentation/github.com/gin-gonic/gin/otelgin"
	"go.uber.org/fx"
)

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

type healthResponse struct {
	Status  string                        `json:"status"`
	Service string                        `json:"service"`
	Time    time.Time                     `json:"time"`
	Checks  map[string]componentHealthDTO `json:"checks"`
}

type componentHealthDTO struct {
	Status string `json:"status"`
	Error  string `json:"error,omitempty"`
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

type handlers struct {
	authorCommands *application.AuthorCommandHandler
	authorQueries  *application.AuthorQueryHandler
	bookCommands   *application.BookCommandHandler
	bookQueries    *application.BookQueryHandler
	healthQueries  *application.HealthQueryHandler
}

func NewRouter(
	cfg config.Config,
	authorCommands *application.AuthorCommandHandler,
	authorQueries *application.AuthorQueryHandler,
	bookCommands *application.BookCommandHandler,
	bookQueries *application.BookQueryHandler,
	healthQueries *application.HealthQueryHandler,
) *gin.Engine {
	router := gin.New()
	router.Use(gin.Recovery())
	router.Use(otelgin.Middleware(cfg.Telemetry.ServiceName))

	h := handlers{
		authorCommands: authorCommands,
		authorQueries:  authorQueries,
		bookCommands:   bookCommands,
		bookQueries:    bookQueries,
		healthQueries:  healthQueries,
	}

	router.GET("/health", h.health)

	v1 := router.Group("/api/v1")
	v1.GET("/health", h.health)

	authors := v1.Group("/authors")
	authors.POST("", h.createAuthor)
	authors.GET("", h.listAuthors)
	authors.GET("/:id", h.getAuthor)
	authors.PUT("/:id", h.updateAuthor)
	authors.DELETE("/:id", h.deleteAuthor)
	authors.GET("/:id/books", h.listAuthorBooks)

	books := v1.Group("/books")
	books.POST("", h.createBook)
	books.GET("", h.listBooks)
	books.GET("/:id", h.getBook)
	books.PUT("/:id", h.updateBook)
	books.DELETE("/:id", h.deleteBook)

	return router
}

func NewHTTPServer(cfg config.Config, router *gin.Engine) *http.Server {
	return &http.Server{
		Addr:              cfg.HTTP.Address,
		Handler:           router,
		ReadHeaderTimeout: 5 * time.Second,
	}
}

func RegisterServerLifecycle(lc fx.Lifecycle, server *http.Server) {
	lc.Append(fx.Hook{
		OnStart: func(context.Context) error {
			go func() {
				if err := server.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
					panic(err)
				}
			}()
			return nil
		},
		OnStop: func(ctx context.Context) error {
			return server.Shutdown(ctx)
		},
	})
}

func (h handlers) health(c *gin.Context) {
	ctx, cancel := context.WithTimeout(c.Request.Context(), 2*time.Second)
	defer cancel()

	result := h.healthQueries.Check(ctx)
	statusCode := http.StatusOK
	if result.Status != application.HealthStatusOK {
		statusCode = http.StatusServiceUnavailable
	}

	c.JSON(statusCode, healthResponse{
		Status:  result.Status,
		Service: result.Service,
		Time:    time.Now().UTC(),
		Checks: map[string]componentHealthDTO{
			"database": {
				Status: result.Database.Status,
				Error:  result.Database.Error,
			},
		},
	})
}

func (h handlers) createAuthor(c *gin.Context) {
	var req createAuthorRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		writeError(c, validationError("invalid request body"))
		return
	}

	author, err := h.authorCommands.Create(c.Request.Context(), application.CreateAuthorCommand{
		Name: req.Name,
		Bio:  req.Bio,
	})
	if err != nil {
		writeError(c, err)
		return
	}

	c.JSON(http.StatusCreated, toAuthorResponse(author))
}

func (h handlers) listAuthors(c *gin.Context) {
	authors, err := h.authorQueries.List(c.Request.Context())
	if err != nil {
		writeError(c, err)
		return
	}

	response := make([]authorResponse, 0, len(authors))
	for _, author := range authors {
		response = append(response, toAuthorResponse(author))
	}
	c.JSON(http.StatusOK, response)
}

func (h handlers) getAuthor(c *gin.Context) {
	id, ok := parseID(c, "id")
	if !ok {
		return
	}

	author, err := h.authorQueries.Get(c.Request.Context(), id)
	if err != nil {
		writeError(c, err)
		return
	}

	c.JSON(http.StatusOK, toAuthorResponse(author))
}

func (h handlers) updateAuthor(c *gin.Context) {
	id, ok := parseID(c, "id")
	if !ok {
		return
	}

	var req updateAuthorRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		writeError(c, validationError("invalid request body"))
		return
	}

	author, err := h.authorCommands.Update(c.Request.Context(), application.UpdateAuthorCommand{
		ID:   id,
		Name: req.Name,
		Bio:  req.Bio,
	})
	if err != nil {
		writeError(c, err)
		return
	}

	c.JSON(http.StatusOK, toAuthorResponse(author))
}

func (h handlers) deleteAuthor(c *gin.Context) {
	id, ok := parseID(c, "id")
	if !ok {
		return
	}

	if err := h.authorCommands.Delete(c.Request.Context(), application.DeleteAuthorCommand{ID: id}); err != nil {
		writeError(c, err)
		return
	}
	c.Status(http.StatusNoContent)
}

func (h handlers) listAuthorBooks(c *gin.Context) {
	id, ok := parseID(c, "id")
	if !ok {
		return
	}

	options, ok := parseBookListOptions(c)
	if !ok {
		return
	}

	books, err := h.authorQueries.BooksWithOptions(c.Request.Context(), id, options)
	if err != nil {
		writeError(c, err)
		return
	}
	c.JSON(http.StatusOK, toBookResponses(books))
}

func (h handlers) createBook(c *gin.Context) {
	var req createBookRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		writeError(c, validationError("invalid request body"))
		return
	}

	authorID, err := uuid.Parse(req.AuthorID)
	if err != nil {
		writeError(c, validationError("invalid author id"))
		return
	}

	book, err := h.bookCommands.Create(c.Request.Context(), application.CreateBookCommand{
		AuthorID:      authorID,
		Title:         req.Title,
		ISBN:          req.ISBN,
		PublishedYear: req.PublishedYear,
	})
	if err != nil {
		writeError(c, err)
		return
	}

	c.JSON(http.StatusCreated, toBookResponse(book))
}

func (h handlers) listBooks(c *gin.Context) {
	options, ok := parseBookListOptions(c)
	if !ok {
		return
	}

	authorIDValue := c.Query("author_id")
	if authorIDValue != "" {
		authorID, err := uuid.Parse(authorIDValue)
		if err != nil {
			writeError(c, validationError("invalid author id"))
			return
		}
		books, err := h.bookQueries.ListByAuthor(c.Request.Context(), authorID, options)
		if err != nil {
			writeError(c, err)
			return
		}
		c.JSON(http.StatusOK, toBookResponses(books))
		return
	}

	books, err := h.bookQueries.List(c.Request.Context(), options)
	if err != nil {
		writeError(c, err)
		return
	}
	c.JSON(http.StatusOK, toBookResponses(books))
}

func (h handlers) getBook(c *gin.Context) {
	id, ok := parseID(c, "id")
	if !ok {
		return
	}

	book, err := h.bookQueries.Get(c.Request.Context(), id)
	if err != nil {
		writeError(c, err)
		return
	}
	c.JSON(http.StatusOK, toBookResponse(book))
}

func (h handlers) updateBook(c *gin.Context) {
	id, ok := parseID(c, "id")
	if !ok {
		return
	}

	var req updateBookRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		writeError(c, validationError("invalid request body"))
		return
	}
	authorID, err := uuid.Parse(req.AuthorID)
	if err != nil {
		writeError(c, validationError("invalid author id"))
		return
	}

	book, err := h.bookCommands.Update(c.Request.Context(), application.UpdateBookCommand{
		ID:            id,
		AuthorID:      authorID,
		Title:         req.Title,
		ISBN:          req.ISBN,
		PublishedYear: req.PublishedYear,
	})
	if err != nil {
		writeError(c, err)
		return
	}
	c.JSON(http.StatusOK, toBookResponse(book))
}

func (h handlers) deleteBook(c *gin.Context) {
	id, ok := parseID(c, "id")
	if !ok {
		return
	}

	if err := h.bookCommands.Delete(c.Request.Context(), application.DeleteBookCommand{ID: id}); err != nil {
		writeError(c, err)
		return
	}
	c.Status(http.StatusNoContent)
}

func parseID(c *gin.Context, param string) (uuid.UUID, bool) {
	id, err := uuid.Parse(c.Param(param))
	if err != nil {
		writeError(c, validationError("invalid id"))
		return uuid.Nil, false
	}
	return id, true
}

func parseBookListOptions(c *gin.Context) (application.BookListOptions, bool) {
	options := application.DefaultBookListOptions()
	value := c.Query("limit")
	if value == "" {
		return options, true
	}

	limit, err := strconv.Atoi(value)
	if err != nil {
		writeError(c, validationError("invalid limit"))
		return application.BookListOptions{}, false
	}
	if limit < application.MinBookListLimit || limit > application.MaxBookListLimit {
		writeError(c, validationError("limit must be between 1 and 100000"))
		return application.BookListOptions{}, false
	}

	options.Limit = limit
	return options, true
}

func validationError(message string) error {
	return fmt.Errorf("%w: %s", domain.ErrValidation, message)
}

func writeError(c *gin.Context, err error) {
	switch {
	case errors.Is(err, application.ErrNotFound):
		c.JSON(http.StatusNotFound, gin.H{"error": "resource not found"})
	case errors.Is(err, application.ErrInvalidInput), errors.Is(err, domain.ErrValidation):
		c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
	default:
		c.JSON(http.StatusInternalServerError, gin.H{"error": "internal server error"})
	}
}

func toAuthorResponse(author domain.Author) authorResponse {
	return authorResponse{
		ID:        author.ID.String(),
		Name:      author.Name,
		Bio:       author.Bio,
		CreatedAt: author.CreatedAt,
		UpdatedAt: author.UpdatedAt,
	}
}

func toBookResponse(book domain.Book) bookResponse {
	return bookResponse{
		ID:            book.ID.String(),
		AuthorID:      book.AuthorID.String(),
		Title:         book.Title,
		ISBN:          book.ISBN,
		PublishedYear: book.PublishedYear,
		CreatedAt:     book.CreatedAt,
		UpdatedAt:     book.UpdatedAt,
	}
}

func toBookResponses(books []domain.Book) []bookResponse {
	response := make([]bookResponse, 0, len(books))
	for _, book := range books {
		response = append(response, toBookResponse(book))
	}
	return response
}

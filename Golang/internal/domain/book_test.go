package domain_test

import (
	"errors"
	"strings"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/require"
	"github.com/webbench/golang-service/internal/domain"
)

func TestNewBookTrimsAndValidates(t *testing.T) {
	now := time.Date(2026, 4, 19, 12, 0, 0, 0, time.UTC)
	year := 1969

	book, err := domain.NewBook(uuid.New(), uuid.New(), "  Slaughterhouse-Five  ", "  9780440180296  ", &year, now)

	require.NoError(t, err)
	require.Equal(t, "Slaughterhouse-Five", book.Title)
	require.Equal(t, "9780440180296", book.ISBN)
	require.Equal(t, &year, book.PublishedYear)
	require.Equal(t, now, book.CreatedAt)
	require.Equal(t, now, book.UpdatedAt)
}

func TestNewBookRejectsMissingAuthor(t *testing.T) {
	_, err := domain.NewBook(uuid.New(), uuid.Nil, "Title", "", nil, time.Now())

	require.True(t, errors.Is(err, domain.ErrValidation))
}

func TestNewBookRejectsInvalidYearAndLongISBN(t *testing.T) {
	year := 1200
	_, err := domain.NewBook(uuid.New(), uuid.New(), "Title", strings.Repeat("1", 33), &year, time.Now())

	require.True(t, errors.Is(err, domain.ErrValidation))
}

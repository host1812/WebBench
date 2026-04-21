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

func TestNewAuthorTrimsAndValidates(t *testing.T) {
	now := time.Date(2026, 4, 19, 12, 0, 0, 0, time.UTC)
	id := uuid.New()

	author, err := domain.NewAuthor(id, "  Octavia Butler  ", "  Sci-fi author  ", now)

	require.NoError(t, err)
	require.Equal(t, id, author.ID)
	require.Equal(t, "Octavia Butler", author.Name)
	require.Equal(t, "Sci-fi author", author.Bio)
	require.Equal(t, now, author.CreatedAt)
	require.Equal(t, now, author.UpdatedAt)
}

func TestNewAuthorRejectsMissingName(t *testing.T) {
	_, err := domain.NewAuthor(uuid.New(), " ", "", time.Now())

	require.True(t, errors.Is(err, domain.ErrValidation))
}

func TestAuthorRejectsOverlongBio(t *testing.T) {
	_, err := domain.NewAuthor(uuid.New(), "Name", strings.Repeat("x", 4001), time.Now())

	require.True(t, errors.Is(err, domain.ErrValidation))
}

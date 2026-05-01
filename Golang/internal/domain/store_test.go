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

func TestNewStoreTrimsAndValidates(t *testing.T) {
	now := time.Date(2026, 4, 19, 12, 0, 0, 0, time.UTC)
	website := "  https://example.com  "

	store, err := domain.NewStore(
		uuid.New(),
		"  Northside Books  ",
		"  Neighborhood shop  ",
		"  101 N Meridian St  ",
		"  +1-317-555-0101  ",
		&website,
		nil,
		now,
	)

	require.NoError(t, err)
	require.Equal(t, "Northside Books", store.Name)
	require.Equal(t, "Neighborhood shop", store.Description)
	require.Equal(t, "101 N Meridian St", store.Address)
	require.Equal(t, "+1-317-555-0101", store.PhoneNumber)
	require.Equal(t, "https://example.com", *store.Website)
	require.Equal(t, now, store.CreatedAt)
	require.Equal(t, now, store.UpdatedAt)
}

func TestNewStoreRejectsMissingRequiredFields(t *testing.T) {
	_, err := domain.NewStore(uuid.New(), " ", "", "Address", "+1-317-555-0101", nil, nil, time.Now())

	require.True(t, errors.Is(err, domain.ErrValidation))
}

func TestNewStoreRejectsLongDescriptionPhoneAndWebsite(t *testing.T) {
	website := strings.Repeat("x", 513)

	_, err := domain.NewStore(
		uuid.New(),
		"Northside Books",
		strings.Repeat("x", 4001),
		"Address",
		strings.Repeat("1", 65),
		&website,
		nil,
		time.Now(),
	)

	require.True(t, errors.Is(err, domain.ErrValidation))
}

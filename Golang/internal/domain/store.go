package domain

import (
	"fmt"
	"strings"
	"time"

	"github.com/google/uuid"
)

const (
	maxStoreDescriptionLength = 4000
	maxStorePhoneNumberLength = 64
	maxStoreWebsiteLength     = 512
)

type Store struct {
	ID          uuid.UUID
	Name        string
	Description string
	Address     string
	PhoneNumber string
	Website     *string
	Inventory   []Book
	CreatedAt   time.Time
	UpdatedAt   time.Time
}

func NewStore(id uuid.UUID, name string, description string, address string, phoneNumber string, website *string, inventory []Book, now time.Time) (Store, error) {
	store := Store{
		ID:          id,
		Name:        strings.TrimSpace(name),
		Description: strings.TrimSpace(description),
		Address:     strings.TrimSpace(address),
		PhoneNumber: strings.TrimSpace(phoneNumber),
		Website:     normalizeOptionalString(website),
		Inventory:   inventory,
		CreatedAt:   now,
		UpdatedAt:   now,
	}
	if err := store.Validate(); err != nil {
		return Store{}, err
	}
	return store, nil
}

func (s Store) Validate() error {
	if s.ID == uuid.Nil {
		return fmt.Errorf("%w: store id is required", ErrValidation)
	}
	if s.Name == "" {
		return fmt.Errorf("%w: store name is required", ErrValidation)
	}
	if s.Address == "" {
		return fmt.Errorf("%w: store address is required", ErrValidation)
	}
	if s.PhoneNumber == "" {
		return fmt.Errorf("%w: store phone number is required", ErrValidation)
	}
	if len(s.Description) > maxStoreDescriptionLength {
		return fmt.Errorf("%w: store description must be %d characters or fewer", ErrValidation, maxStoreDescriptionLength)
	}
	if len(s.PhoneNumber) > maxStorePhoneNumberLength {
		return fmt.Errorf("%w: store phone number must be %d characters or fewer", ErrValidation, maxStorePhoneNumberLength)
	}
	if s.Website != nil && len(*s.Website) > maxStoreWebsiteLength {
		return fmt.Errorf("%w: store website must be %d characters or fewer", ErrValidation, maxStoreWebsiteLength)
	}
	return nil
}

func normalizeOptionalString(value *string) *string {
	if value == nil {
		return nil
	}
	trimmed := strings.TrimSpace(*value)
	if trimmed == "" {
		return nil
	}
	return &trimmed
}

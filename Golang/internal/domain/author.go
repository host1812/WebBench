package domain

import (
	"fmt"
	"strings"
	"time"

	"github.com/google/uuid"
)

const maxAuthorBioLength = 4000

type Author struct {
	ID        uuid.UUID
	Name      string
	Bio       string
	CreatedAt time.Time
	UpdatedAt time.Time
}

func NewAuthor(id uuid.UUID, name string, bio string, now time.Time) (Author, error) {
	author := Author{
		ID:        id,
		Name:      strings.TrimSpace(name),
		Bio:       strings.TrimSpace(bio),
		CreatedAt: now,
		UpdatedAt: now,
	}
	if err := author.Validate(); err != nil {
		return Author{}, err
	}
	return author, nil
}

func UpdateAuthor(id uuid.UUID, name string, bio string, now time.Time) (Author, error) {
	author := Author{
		ID:        id,
		Name:      strings.TrimSpace(name),
		Bio:       strings.TrimSpace(bio),
		UpdatedAt: now,
	}
	if err := author.Validate(); err != nil {
		return Author{}, err
	}
	return author, nil
}

func (a Author) Validate() error {
	if a.ID == uuid.Nil {
		return fmt.Errorf("%w: author id is required", ErrValidation)
	}
	if a.Name == "" {
		return fmt.Errorf("%w: author name is required", ErrValidation)
	}
	if len(a.Bio) > maxAuthorBioLength {
		return fmt.Errorf("%w: author bio must be %d characters or fewer", ErrValidation, maxAuthorBioLength)
	}
	return nil
}

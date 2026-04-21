package domain

import (
	"fmt"
	"strings"
	"time"

	"github.com/google/uuid"
)

const maxISBNLength = 32

type Book struct {
	ID            uuid.UUID
	AuthorID      uuid.UUID
	Title         string
	ISBN          string
	PublishedYear *int
	CreatedAt     time.Time
	UpdatedAt     time.Time
}

func NewBook(id uuid.UUID, authorID uuid.UUID, title string, isbn string, publishedYear *int, now time.Time) (Book, error) {
	book := Book{
		ID:            id,
		AuthorID:      authorID,
		Title:         strings.TrimSpace(title),
		ISBN:          strings.TrimSpace(isbn),
		PublishedYear: publishedYear,
		CreatedAt:     now,
		UpdatedAt:     now,
	}
	if err := book.Validate(); err != nil {
		return Book{}, err
	}
	return book, nil
}

func UpdateBook(id uuid.UUID, authorID uuid.UUID, title string, isbn string, publishedYear *int, now time.Time) (Book, error) {
	book := Book{
		ID:            id,
		AuthorID:      authorID,
		Title:         strings.TrimSpace(title),
		ISBN:          strings.TrimSpace(isbn),
		PublishedYear: publishedYear,
		UpdatedAt:     now,
	}
	if err := book.Validate(); err != nil {
		return Book{}, err
	}
	return book, nil
}

func (b Book) Validate() error {
	if b.ID == uuid.Nil {
		return fmt.Errorf("%w: book id is required", ErrValidation)
	}
	if b.AuthorID == uuid.Nil {
		return fmt.Errorf("%w: author id is required", ErrValidation)
	}
	if b.Title == "" {
		return fmt.Errorf("%w: book title is required", ErrValidation)
	}
	if len(b.ISBN) > maxISBNLength {
		return fmt.Errorf("%w: isbn must be %d characters or fewer", ErrValidation, maxISBNLength)
	}
	if b.PublishedYear != nil && (*b.PublishedYear < 1450 || *b.PublishedYear > time.Now().Year()+1) {
		return fmt.Errorf("%w: published year is out of range", ErrValidation)
	}
	return nil
}

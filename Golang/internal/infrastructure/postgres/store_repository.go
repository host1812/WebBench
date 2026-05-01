package postgres

import (
	"context"
	"fmt"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/webbench/golang-service/internal/domain"
	"github.com/webbench/golang-service/internal/telemetry"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
)

var postgresStoreTracer = otel.Tracer(telemetry.TracerName("infrastructure/postgres/stores"))

type StoreRepository struct {
	pool DB
}

func NewStoreRepository(pool DB) *StoreRepository {
	return &StoreRepository{pool: pool}
}

func (r *StoreRepository) List(ctx context.Context) ([]domain.Store, error) {
	ctx, span := startDBSpan(ctx, postgresStoreTracer, "postgres.stores.select", "SELECT", "stores")
	defer span.End()

	rows, err := r.pool.Query(ctx, `
		SELECT
			s.id, s.name, s.description, s.address, s.phone_number, s.website, s.created_at, s.updated_at,
			b.id, b.author_id, b.title, b.isbn, b.published_year, b.created_at, b.updated_at
		FROM stores s
		LEFT JOIN store_books sb ON sb.store_id = s.id
		LEFT JOIN books b ON b.id = sb.book_id
		ORDER BY s.name ASC, s.created_at ASC, b.title ASC
	`)
	if err != nil {
		telemetry.RecordSpanError(span, err)
		return nil, fmt.Errorf("list stores: %w", err)
	}
	defer rows.Close()

	stores, err := scanStores(rows)
	telemetry.RecordSpanError(span, err)
	if err == nil {
		span.SetAttributes(attribute.Int("db.rows_returned", len(stores)))
	}
	return stores, err
}

func scanStores(rows pgx.Rows) ([]domain.Store, error) {
	stores := make([]domain.Store, 0)
	storeIndex := map[uuid.UUID]int{}
	for rows.Next() {
		var store domain.Store
		var website pgtype.Text
		var bookID pgtype.UUID
		var authorID pgtype.UUID
		var title pgtype.Text
		var isbn pgtype.Text
		var publishedYear pgtype.Int4
		var bookCreatedAt pgtype.Timestamptz
		var bookUpdatedAt pgtype.Timestamptz

		if err := rows.Scan(
			&store.ID,
			&store.Name,
			&store.Description,
			&store.Address,
			&store.PhoneNumber,
			&website,
			&store.CreatedAt,
			&store.UpdatedAt,
			&bookID,
			&authorID,
			&title,
			&isbn,
			&publishedYear,
			&bookCreatedAt,
			&bookUpdatedAt,
		); err != nil {
			return nil, fmt.Errorf("scan store: %w", err)
		}

		index, ok := storeIndex[store.ID]
		if !ok {
			store.Website = textPtr(website)
			store.Inventory = make([]domain.Book, 0)
			stores = append(stores, store)
			index = len(stores) - 1
			storeIndex[store.ID] = index
		}

		if bookID.Valid {
			stores[index].Inventory = append(stores[index].Inventory, domain.Book{
				ID:            uuid.UUID(bookID.Bytes),
				AuthorID:      uuid.UUID(authorID.Bytes),
				Title:         title.String,
				ISBN:          isbn.String,
				PublishedYear: publishedYearPtr(publishedYear),
				CreatedAt:     bookCreatedAt.Time,
				UpdatedAt:     bookUpdatedAt.Time,
			})
		}
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("iterate stores: %w", err)
	}
	return stores, nil
}

func textPtr(value pgtype.Text) *string {
	if !value.Valid {
		return nil
	}
	return &value.String
}

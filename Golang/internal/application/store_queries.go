package application

import (
	"context"

	"github.com/webbench/golang-service/internal/domain"
	"github.com/webbench/golang-service/internal/telemetry"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
)

var storeQueryTracer = otel.Tracer(telemetry.TracerName("application/stores/queries"))

type StoreQueryHandler struct {
	stores StoreQueryStore
}

func NewStoreQueryHandler(stores StoreQueryStore) *StoreQueryHandler {
	return &StoreQueryHandler{stores: stores}
}

func (h *StoreQueryHandler) List(ctx context.Context) ([]domain.Store, error) {
	ctx, span := storeQueryTracer.Start(ctx, "StoreQuery.List")
	defer span.End()

	stores, err := h.stores.List(ctx)
	telemetry.RecordSpanError(span, err)
	if err == nil {
		span.SetAttributes(attribute.Int("store.count", len(stores)))
	}
	return stores, err
}

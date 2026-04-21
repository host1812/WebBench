package application

import (
	"context"

	"github.com/webbench/golang-service/internal/telemetry"
	"go.opentelemetry.io/otel"
)

var healthTracer = otel.Tracer(telemetry.TracerName("application/health"))

const (
	HealthStatusOK       = "ok"
	HealthStatusDegraded = "degraded"
)

type DatabasePinger interface {
	Ping(ctx context.Context) error
}

type HealthResult struct {
	Status   string
	Service  string
	Database ComponentHealth
}

type ComponentHealth struct {
	Status string
	Error  string
}

type HealthQueryHandler struct {
	database DatabasePinger
}

func NewHealthQueryHandler(database DatabasePinger) *HealthQueryHandler {
	return &HealthQueryHandler{database: database}
}

func (h *HealthQueryHandler) Check(ctx context.Context) HealthResult {
	ctx, span := healthTracer.Start(ctx, "Health.Check")
	defer span.End()

	result := HealthResult{
		Status:  HealthStatusOK,
		Service: "books-service",
		Database: ComponentHealth{
			Status: HealthStatusOK,
		},
	}

	if err := h.database.Ping(ctx); err != nil {
		telemetry.RecordSpanError(span, err)
		result.Status = HealthStatusDegraded
		result.Database.Status = HealthStatusDegraded
		result.Database.Error = err.Error()
	}

	return result
}

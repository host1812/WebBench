package telemetry

import (
	"context"
	"fmt"
	"net/url"
	"strings"
	"time"

	"github.com/webbench/golang-service/internal/config"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracehttp"
	"go.opentelemetry.io/otel/propagation"
	"go.opentelemetry.io/otel/sdk/resource"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	"go.uber.org/fx"
)

const instrumentationName = "github.com/webbench/golang-service"

func TracerName(packageName string) string {
	return instrumentationName + "/" + packageName
}

func Register(lc fx.Lifecycle, cfg config.Config) error {
	otel.SetTextMapPropagator(propagation.NewCompositeTextMapPropagator(
		propagation.TraceContext{},
		propagation.Baggage{},
	))

	tracerProvider, err := newTracerProvider(context.Background(), cfg)
	if err != nil {
		return err
	}

	otel.SetTracerProvider(tracerProvider)
	lc.Append(fx.Hook{
		OnStop: func(ctx context.Context) error {
			shutdownCtx, cancel := context.WithTimeout(ctx, 5*time.Second)
			defer cancel()
			return tracerProvider.Shutdown(shutdownCtx)
		},
	})

	return nil
}

func newTracerProvider(ctx context.Context, cfg config.Config) (*sdktrace.TracerProvider, error) {
	res, err := resource.Merge(
		resource.Default(),
		resource.NewWithAttributes(
			"",
			attribute.String("service.name", cfg.Telemetry.ServiceName),
			attribute.String("deployment.environment.name", cfg.Telemetry.Environment),
			attribute.String("telemetry.destination", "azure-application-insights"),
		),
	)
	if err != nil {
		return nil, fmt.Errorf("create telemetry resource: %w", err)
	}

	if !cfg.Telemetry.Enabled {
		return sdktrace.NewTracerProvider(
			sdktrace.WithResource(res),
			sdktrace.WithSampler(sdktrace.NeverSample()),
		), nil
	}

	exporterOptions, err := otlpHTTPOptions(cfg.Telemetry.OTLPEndpoint)
	if err != nil {
		return nil, err
	}
	exporter, err := otlptracehttp.New(ctx, exporterOptions...)
	if err != nil {
		return nil, fmt.Errorf("create OTLP trace exporter: %w", err)
	}

	return sdktrace.NewTracerProvider(
		sdktrace.WithResource(res),
		sdktrace.WithSampler(sdktrace.AlwaysSample()),
		sdktrace.WithBatcher(exporter),
	), nil
}

func otlpHTTPOptions(endpoint string) ([]otlptracehttp.Option, error) {
	value := strings.TrimSpace(endpoint)
	if value == "" {
		return nil, fmt.Errorf("telemetry.otlp_endpoint is required")
	}

	if strings.HasPrefix(value, "http://") || strings.HasPrefix(value, "https://") {
		parsed, err := url.Parse(value)
		if err != nil {
			return nil, fmt.Errorf("parse telemetry.otlp_endpoint: %w", err)
		}

		options := []otlptracehttp.Option{
			otlptracehttp.WithEndpoint(parsed.Host),
		}
		if parsed.Scheme == "http" {
			options = append(options, otlptracehttp.WithInsecure())
		}
		if parsed.Path != "" && parsed.Path != "/" {
			options = append(options, otlptracehttp.WithURLPath(parsed.Path))
		}
		return options, nil
	}

	return []otlptracehttp.Option{
		otlptracehttp.WithEndpoint(value),
		otlptracehttp.WithInsecure(),
	}, nil
}

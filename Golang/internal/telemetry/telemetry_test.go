package telemetry

import (
	"context"
	"testing"

	"github.com/stretchr/testify/require"
	"github.com/webbench/golang-service/internal/config"
)

func TestNewTracerProviderDisabled(t *testing.T) {
	provider, err := newTracerProvider(context.Background(), config.Config{
		Telemetry: config.TelemetryConfig{
			Enabled:     false,
			ServiceName: "books-service",
			Environment: "test",
		},
	})

	require.NoError(t, err)
	require.NotNil(t, provider)
}

func TestNewTracerProviderEnabledWithOTLPEndpoint(t *testing.T) {
	provider, err := newTracerProvider(context.Background(), config.Config{
		Telemetry: config.TelemetryConfig{
			Enabled:                             true,
			ServiceName:                         "books-service",
			Environment:                         "test",
			OTLPEndpoint:                        "http://collector:4318",
			ApplicationInsightsConnectionString: "InstrumentationKey=00000000-0000-0000-0000-000000000000",
		},
	})

	require.NoError(t, err)
	require.NotNil(t, provider)
}

func TestOTLPHTTPOptionsRejectsEmptyEndpoint(t *testing.T) {
	_, err := otlpHTTPOptions("")

	require.Error(t, err)
	require.Contains(t, err.Error(), "telemetry.otlp_endpoint")
}

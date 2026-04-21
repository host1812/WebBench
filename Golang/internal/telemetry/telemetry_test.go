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
			ServiceName: "books-service-test",
			Environment: "test",
		},
	})
	require.NoError(t, err)
	require.NotNil(t, provider)
	require.NoError(t, provider.Shutdown(context.Background()))
}

func TestNewTracerProviderEnabledWithOTLPEndpoint(t *testing.T) {
	provider, err := newTracerProvider(context.Background(), config.Config{
		Telemetry: config.TelemetryConfig{
			Enabled:                             true,
			ServiceName:                         "books-service-test",
			Environment:                         "test",
			OTLPEndpoint:                        "http://collector:4318/v1/traces",
			ApplicationInsightsConnectionString: "InstrumentationKey=00000000-0000-0000-0000-000000000000",
		},
	})
	require.NoError(t, err)
	require.NotNil(t, provider)
	require.NoError(t, provider.Shutdown(context.Background()))
}

func TestOTLPHTTPOptionsRejectsEmptyEndpoint(t *testing.T) {
	options, err := otlpHTTPOptions(" ")

	require.Nil(t, options)
	require.Error(t, err)
	require.Contains(t, err.Error(), "telemetry.otlp_endpoint")
}

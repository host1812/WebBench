package config_test

import (
	"os"
	"path/filepath"
	"testing"

	"github.com/stretchr/testify/require"
	"github.com/webbench/golang-service/internal/config"
)

func TestLoadReadsConfigFile(t *testing.T) {
	path := filepath.Join(t.TempDir(), "config.yaml")
	content := []byte(`
http:
  address: ":9090"
database:
  connection_string: "postgres://user:pass@localhost:5432/app?sslmode=disable"
`)
	require.NoError(t, os.WriteFile(path, content, 0o600))

	cfg, err := config.Load(path)

	require.NoError(t, err)
	require.Equal(t, ":9090", cfg.HTTP.Address)
	require.Equal(t, "postgres://user:pass@localhost:5432/app?sslmode=disable", cfg.Database.ConnectionString)
}

func TestLoadAllowsEnvironmentOverride(t *testing.T) {
	path := filepath.Join(t.TempDir(), "config.yaml")
	content := []byte(`
http:
  address: ":9090"
database:
  connection_string: "postgres://file"
`)
	require.NoError(t, os.WriteFile(path, content, 0o600))
	t.Setenv("BOOKSVC_DATABASE_CONNECTION_STRING", "postgres://env")

	cfg, err := config.Load(path)

	require.NoError(t, err)
	require.Equal(t, "postgres://env", cfg.Database.ConnectionString)
}

func TestLoadReadsTelemetryConfig(t *testing.T) {
	path := filepath.Join(t.TempDir(), "config.yaml")
	content := []byte(`
http:
  address: ":9090"
database:
  connection_string: "postgres://file"
telemetry:
  enabled: true
  service_name: "books-service-test"
  environment: "test"
  otlp_endpoint: "collector:4318"
  application_insights_connection_string: "InstrumentationKey=00000000-0000-0000-0000-000000000000"
`)
	require.NoError(t, os.WriteFile(path, content, 0o600))

	cfg, err := config.Load(path)

	require.NoError(t, err)
	require.True(t, cfg.Telemetry.Enabled)
	require.Equal(t, "books-service-test", cfg.Telemetry.ServiceName)
	require.Equal(t, "test", cfg.Telemetry.Environment)
	require.Equal(t, "collector:4318", cfg.Telemetry.OTLPEndpoint)
	require.Contains(t, cfg.Telemetry.ApplicationInsightsConnectionString, "InstrumentationKey=")
}

func TestValidateRequiresDatabaseConnectionString(t *testing.T) {
	err := config.Config{
		HTTP: config.HTTPConfig{Address: ":8080"},
	}.Validate()

	require.Error(t, err)
	require.Contains(t, err.Error(), "database.connection_string")
}

func TestValidateRequiresApplicationInsightsConnectionStringWhenTelemetryEnabled(t *testing.T) {
	err := config.Config{
		HTTP:     config.HTTPConfig{Address: ":8080"},
		Database: config.DatabaseConfig{ConnectionString: "postgres://file"},
		Telemetry: config.TelemetryConfig{
			Enabled:      true,
			ServiceName:  "books-service",
			Environment:  "test",
			OTLPEndpoint: "collector:4318",
		},
	}.Validate()

	require.Error(t, err)
	require.Contains(t, err.Error(), "telemetry.application_insights_connection_string")
}

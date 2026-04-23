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
  max_connections: 12
`)
	require.NoError(t, os.WriteFile(path, content, 0o600))

	cfg, err := config.Load(path)

	require.NoError(t, err)
	require.Equal(t, ":9090", cfg.HTTP.Address)
	require.Equal(t, "postgres://user:pass@localhost:5432/app?sslmode=disable", cfg.Database.ConnectionString)
	require.Equal(t, int32(12), cfg.Database.MaxConnections)
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
	t.Setenv("BOOKSVC_DATABASE_MAX_CONNECTIONS", "14")

	cfg, err := config.Load(path)

	require.NoError(t, err)
	require.Equal(t, "postgres://env", cfg.Database.ConnectionString)
	require.Equal(t, int32(14), cfg.Database.MaxConnections)
}

func TestValidateRequiresDatabaseConnectionString(t *testing.T) {
	err := config.Config{
		HTTP: config.HTTPConfig{Address: ":8080"},
	}.Validate()

	require.Error(t, err)
	require.Contains(t, err.Error(), "database.connection_string")
}

func TestValidateRequiresPositiveDatabaseMaxConnections(t *testing.T) {
	err := config.Config{
		HTTP: config.HTTPConfig{Address: ":8080"},
		Database: config.DatabaseConfig{
			ConnectionString: "postgres://file",
			MaxConnections:   0,
		},
	}.Validate()

	require.Error(t, err)
	require.Contains(t, err.Error(), "database.max_connections")
}

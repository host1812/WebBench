package bootstrap_test

import (
	"testing"

	"github.com/stretchr/testify/require"
	"github.com/webbench/golang-service/internal/bootstrap"
	"github.com/webbench/golang-service/internal/config"
	"go.uber.org/fx"
)

func TestProductionDependencyGraphValidates(t *testing.T) {
	err := fx.ValidateApp(
		bootstrap.Module(),
		fx.Supply(config.Config{
			HTTP: config.HTTPConfig{Address: ":0"},
			Database: config.DatabaseConfig{
				ConnectionString: "postgres://postgres:postgres@localhost:5432/books?sslmode=disable",
			},
		}),
		fx.NopLogger,
	)

	require.NoError(t, err)
}

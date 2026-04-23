package postgres

import (
	"context"

	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/webbench/golang-service/internal/config"
	"go.uber.org/fx"
)

func NewPool(lc fx.Lifecycle, cfg config.Config) (*pgxpool.Pool, error) {
	poolConfig, err := pgxpool.ParseConfig(cfg.Database.ConnectionString)
	if err != nil {
		return nil, err
	}
	poolConfig.MaxConns = cfg.Database.MaxConnections

	pool, err := pgxpool.NewWithConfig(context.Background(), poolConfig)
	if err != nil {
		return nil, err
	}

	lc.Append(fx.Hook{
		OnStart: func(ctx context.Context) error {
			return pool.Ping(ctx)
		},
		OnStop: func(ctx context.Context) error {
			pool.Close()
			return nil
		},
	})

	return pool, nil
}

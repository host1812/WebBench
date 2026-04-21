package postgres

import (
	"context"

	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/webbench/golang-service/internal/config"
	"go.uber.org/fx"
)

func NewPool(lc fx.Lifecycle, cfg config.Config) (*pgxpool.Pool, error) {
	pool, err := pgxpool.New(context.Background(), cfg.Database.ConnectionString)
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

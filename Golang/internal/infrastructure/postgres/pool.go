package postgres

import (
	"context"
	"log"

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
			if err := pool.Ping(ctx); err != nil {
				return err
			}
			stat := pool.Stat()
			log.Printf(
				"database pool ready: max_connections=%d total_connections=%d idle_connections=%d acquired_connections=%d",
				stat.MaxConns(),
				stat.TotalConns(),
				stat.IdleConns(),
				stat.AcquiredConns(),
			)
			return nil
		},
		OnStop: func(ctx context.Context) error {
			pool.Close()
			return nil
		},
	})

	return pool, nil
}

package migrator

import (
	"context"
	"fmt"
	"log/slog"
	"os"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgconn"
)

const advisoryLockID int64 = 880011230001

type Options struct {
	DatabaseURL    string
	MigrationsPath string
	Steps          int
}

type Runner struct {
	conn       *pgx.Conn
	options    Options
	logger     *slog.Logger
	migrations []Migration
}

func NewRunner(ctx context.Context, options Options, logger *slog.Logger) (*Runner, error) {
	if options.MigrationsPath == "" {
		options.MigrationsPath = "migrations"
	}
	if options.Steps <= 0 {
		options.Steps = 1
	}
	if options.DatabaseURL == "" {
		options.DatabaseURL = firstEnv("MIGRATIONS_DATABASE_CONNECTION_STRING", "BOOKSVC_DATABASE_CONNECTION_STRING", "LOCAL_DATABASE_CONNECTION_STRING")
	}
	if options.DatabaseURL == "" {
		return nil, fmt.Errorf("database URL is required")
	}

	config, err := pgx.ParseConfig(options.DatabaseURL)
	if err != nil {
		return nil, fmt.Errorf("parse database URL: %w", err)
	}
	config.RuntimeParams["application_name"] = "books-migrator"
	config.OnNotice = func(_ *pgconn.PgConn, notice *pgconn.Notice) {
		logger.Info("postgres notice", "message", notice.Message, "detail", notice.Detail)
	}

	migrations, err := LoadMigrations(options.MigrationsPath)
	if err != nil {
		return nil, err
	}

	logger.Info("connecting to postgres", "database", config.Database, "host", config.Host, "migrations", options.MigrationsPath)
	conn, err := pgx.ConnectConfig(ctx, config)
	if err != nil {
		return nil, fmt.Errorf("connect postgres: %w", err)
	}

	return &Runner{conn: conn, options: options, logger: logger, migrations: migrations}, nil
}

func (r *Runner) Close(ctx context.Context) {
	if r.conn != nil {
		_ = r.conn.Close(ctx)
	}
}

func (r *Runner) Up(ctx context.Context) error {
	if err := r.withLock(ctx); err != nil {
		return err
	}
	defer r.unlock(context.Background())

	if err := r.ensureSchema(ctx); err != nil {
		return err
	}

	version, dirty, err := r.currentVersion(ctx)
	if err != nil {
		return err
	}
	if dirty {
		return fmt.Errorf("database is dirty at version %d; inspect state and run force if needed", version)
	}

	applied := 0
	for _, migration := range r.migrations {
		if migration.Version <= version {
			continue
		}
		if err := r.apply(ctx, migration, migration.UpPath, migration.Version, "up"); err != nil {
			return err
		}
		applied++
	}

	if applied == 0 {
		r.logger.Info("no pending migrations", "version", version)
	}
	return nil
}

func (r *Runner) Down(ctx context.Context) error {
	if err := r.withLock(ctx); err != nil {
		return err
	}
	defer r.unlock(context.Background())

	if err := r.ensureSchema(ctx); err != nil {
		return err
	}

	version, dirty, err := r.currentVersion(ctx)
	if err != nil {
		return err
	}
	if dirty {
		return fmt.Errorf("database is dirty at version %d; inspect state and run force if needed", version)
	}
	if version == 0 {
		r.logger.Info("database already at version 0")
		return nil
	}

	byVersion := map[int]Migration{}
	for _, migration := range r.migrations {
		byVersion[migration.Version] = migration
	}

	for step := 0; step < r.options.Steps && version > 0; step++ {
		migration, ok := byVersion[version]
		if !ok {
			return fmt.Errorf("missing migration file for version %d", version)
		}
		nextVersion := previousVersion(r.migrations, version)
		if err := r.apply(ctx, migration, migration.DownPath, nextVersion, "down"); err != nil {
			return err
		}
		version = nextVersion
	}

	return nil
}

func (r *Runner) Status(ctx context.Context) error {
	if err := r.ensureSchema(ctx); err != nil {
		return err
	}
	version, dirty, err := r.currentVersion(ctx)
	if err != nil {
		return err
	}
	r.logger.Info("migration status", "version", version, "dirty", dirty)
	for _, migration := range r.migrations {
		status := "pending"
		if migration.Version <= version {
			status = "applied"
		}
		r.logger.Info("migration", "version", migration.Version, "name", migration.Name, "status", status)
	}
	return nil
}

func (r *Runner) Version(ctx context.Context) error {
	if err := r.ensureSchema(ctx); err != nil {
		return err
	}
	version, dirty, err := r.currentVersion(ctx)
	if err != nil {
		return err
	}
	r.logger.Info("migration version", "version", version, "dirty", dirty)
	return nil
}

func (r *Runner) Force(ctx context.Context, version int, dirty bool) error {
	if err := r.withLock(ctx); err != nil {
		return err
	}
	defer r.unlock(context.Background())

	if err := r.ensureSchema(ctx); err != nil {
		return err
	}
	if err := r.setVersion(ctx, version, dirty); err != nil {
		return err
	}
	r.logger.Warn("forced migration version", "version", version, "dirty", dirty)
	return nil
}

func (r *Runner) apply(ctx context.Context, migration Migration, path string, finalVersion int, direction string) error {
	started := time.Now()
	r.logger.Info("migration started", "version", migration.Version, "name", migration.Name, "direction", direction, "path", path)

	content, err := os.ReadFile(path)
	if err != nil {
		return fmt.Errorf("read migration %s: %w", path, err)
	}
	statements := SplitSQL(string(content))

	if err := r.setVersion(ctx, migration.Version, true); err != nil {
		return err
	}

	tx, err := r.conn.Begin(ctx)
	if err != nil {
		return fmt.Errorf("begin migration transaction: %w", err)
	}
	defer tx.Rollback(context.Background())

	for index, statement := range statements {
		statementStart := time.Now()
		r.logger.Info("statement started", "migration", migration.Version, "statement", index+1, "total", len(statements), "summary", SummarizeSQL(statement))
		if _, err := tx.Exec(ctx, statement); err != nil {
			return fmt.Errorf("execute migration %d statement %d: %w", migration.Version, index+1, err)
		}
		r.logger.Info("statement completed", "migration", migration.Version, "statement", index+1, "duration", time.Since(statementStart))
	}

	if _, err := tx.Exec(ctx, `DELETE FROM schema_migrations`); err != nil {
		return fmt.Errorf("clear schema_migrations: %w", err)
	}
	if _, err := tx.Exec(ctx, `INSERT INTO schema_migrations (version, dirty) VALUES ($1, false)`, finalVersion); err != nil {
		return fmt.Errorf("write schema_migrations: %w", err)
	}
	if err := tx.Commit(ctx); err != nil {
		return fmt.Errorf("commit migration: %w", err)
	}

	r.logger.Info("migration completed", "version", migration.Version, "direction", direction, "final_version", finalVersion, "duration", time.Since(started))
	return nil
}

func (r *Runner) ensureSchema(ctx context.Context) error {
	if _, err := r.conn.Exec(ctx, `CREATE TABLE IF NOT EXISTS schema_migrations (version bigint NOT NULL, dirty boolean NOT NULL)`); err != nil {
		return fmt.Errorf("ensure schema_migrations: %w", err)
	}

	var count int
	if err := r.conn.QueryRow(ctx, `SELECT COUNT(*) FROM schema_migrations`).Scan(&count); err != nil {
		return fmt.Errorf("count schema_migrations: %w", err)
	}
	if count == 0 {
		if _, err := r.conn.Exec(ctx, `INSERT INTO schema_migrations (version, dirty) VALUES (0, false)`); err != nil {
			return fmt.Errorf("initialize schema_migrations: %w", err)
		}
	}
	if count > 1 {
		return fmt.Errorf("schema_migrations contains %d rows; expected 1", count)
	}
	return nil
}

func (r *Runner) currentVersion(ctx context.Context) (int, bool, error) {
	var version int
	var dirty bool
	if err := r.conn.QueryRow(ctx, `SELECT version, dirty FROM schema_migrations LIMIT 1`).Scan(&version, &dirty); err != nil {
		return 0, false, fmt.Errorf("read schema_migrations: %w", err)
	}
	return version, dirty, nil
}

func (r *Runner) setVersion(ctx context.Context, version int, dirty bool) error {
	if _, err := r.conn.Exec(ctx, `DELETE FROM schema_migrations`); err != nil {
		return fmt.Errorf("clear schema_migrations: %w", err)
	}
	if _, err := r.conn.Exec(ctx, `INSERT INTO schema_migrations (version, dirty) VALUES ($1, $2)`, version, dirty); err != nil {
		return fmt.Errorf("write schema_migrations: %w", err)
	}
	return nil
}

func (r *Runner) withLock(ctx context.Context) error {
	r.logger.Info("acquiring migration lock")
	if _, err := r.conn.Exec(ctx, `SELECT pg_advisory_lock($1)`, advisoryLockID); err != nil {
		return fmt.Errorf("acquire advisory lock: %w", err)
	}
	r.logger.Info("migration lock acquired")
	return nil
}

func (r *Runner) unlock(ctx context.Context) {
	if _, err := r.conn.Exec(ctx, `SELECT pg_advisory_unlock($1)`, advisoryLockID); err != nil {
		r.logger.Warn("release migration lock failed", "error", err)
		return
	}
	r.logger.Info("migration lock released")
}

func firstEnv(names ...string) string {
	for _, name := range names {
		if value := os.Getenv(name); value != "" {
			return value
		}
	}
	return ""
}

func previousVersion(migrations []Migration, current int) int {
	previous := 0
	for _, migration := range migrations {
		if migration.Version >= current {
			return previous
		}
		previous = migration.Version
	}
	return previous
}

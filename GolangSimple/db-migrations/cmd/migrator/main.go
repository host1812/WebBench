package main

import (
	"context"
	"flag"
	"fmt"
	"log/slog"
	"os"
	"time"

	"github.com/webbench/books-migrations/internal/migrator"
)

func main() {
	os.Exit(run())
}

func run() int {
	var options migrator.Options
	var envPath string
	var forceVersion int
	var forceDirty bool

	flags := flag.NewFlagSet("migrator", flag.ExitOnError)
	flags.StringVar(&options.DatabaseURL, "database-url", "", "PostgreSQL connection string. Defaults to MIGRATIONS_DATABASE_CONNECTION_STRING, BOOKSVC_DATABASE_CONNECTION_STRING, then LOCAL_DATABASE_CONNECTION_STRING.")
	flags.StringVar(&options.MigrationsPath, "migrations", "migrations", "Path to SQL migration files.")
	flags.StringVar(&envPath, "env", ".env", "Path to .env file. Use empty string to skip loading a file.")
	flags.IntVar(&options.Steps, "steps", 1, "Number of migrations for down.")
	flags.IntVar(&forceVersion, "version", 0, "Version for force command.")
	flags.BoolVar(&forceDirty, "dirty", false, "Dirty value for force command.")

	if err := flags.Parse(os.Args[1:]); err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 2
	}

	if flags.NArg() != 1 {
		fmt.Fprintln(os.Stderr, "usage: migrator [flags] up|down|status|version|force")
		return 2
	}
	command := flags.Arg(0)

	logger := slog.New(slog.NewTextHandler(os.Stdout, &slog.HandlerOptions{Level: slog.LevelInfo}))
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	if envPath != "" {
		if err := migrator.LoadEnvFile(envPath); err != nil && !os.IsNotExist(err) {
			logger.Error("load env failed", "path", envPath, "error", err)
			return 1
		}
	}

	runner, err := migrator.NewRunner(ctx, options, logger)
	if err != nil {
		logger.Error("create migrator failed", "error", err)
		return 1
	}
	defer runner.Close(ctx)

	started := time.Now()
	logger.Info("migration command started", "command", command)

	switch command {
	case "up":
		err = runner.Up(ctx)
	case "down":
		err = runner.Down(ctx)
	case "status":
		err = runner.Status(ctx)
	case "version":
		err = runner.Version(ctx)
	case "force":
		err = runner.Force(ctx, forceVersion, forceDirty)
	default:
		err = fmt.Errorf("unknown command %q", command)
	}

	if err != nil {
		logger.Error("migration command failed", "command", command, "duration", time.Since(started), "error", err)
		return 1
	}

	logger.Info("migration command completed", "command", command, "duration", time.Since(started))
	return 0
}

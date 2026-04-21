package migrator

import (
	"os"
	"path/filepath"
	"testing"
)

func TestLoadMigrationsPairsFiles(t *testing.T) {
	dir := t.TempDir()
	files := []string{
		"001_init.up.sql",
		"001_init.down.sql",
		"002_seed_catalog.up.sql",
		"002_seed_catalog.down.sql",
	}
	for _, file := range files {
		if err := os.WriteFile(filepath.Join(dir, file), []byte("SELECT 1;"), 0o600); err != nil {
			t.Fatal(err)
		}
	}

	migrations, err := LoadMigrations(dir)
	if err != nil {
		t.Fatal(err)
	}
	if len(migrations) != 2 {
		t.Fatalf("expected 2 migrations, got %d", len(migrations))
	}
	if migrations[0].Version != 1 || migrations[0].Name != "init" {
		t.Fatalf("unexpected first migration: %+v", migrations[0])
	}
	if migrations[1].Version != 2 || migrations[1].Name != "seed_catalog" {
		t.Fatalf("unexpected second migration: %+v", migrations[1])
	}
}

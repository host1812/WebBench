package migrator

import (
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"sort"
	"strconv"
)

type Migration struct {
	Version int
	Name    string
	UpPath  string
	DownPath string
}

var migrationFilePattern = regexp.MustCompile(`^([0-9]+)_(.+)\.(up|down)\.sql$`)

func LoadMigrations(path string) ([]Migration, error) {
	entries, err := os.ReadDir(path)
	if err != nil {
		return nil, fmt.Errorf("read migrations directory: %w", err)
	}

	byVersion := map[int]*Migration{}
	for _, entry := range entries {
		if entry.IsDir() {
			continue
		}

		matches := migrationFilePattern.FindStringSubmatch(entry.Name())
		if matches == nil {
			continue
		}

		version, err := strconv.Atoi(matches[1])
		if err != nil {
			return nil, fmt.Errorf("parse migration version %q: %w", entry.Name(), err)
		}

		migration := byVersion[version]
		if migration == nil {
			migration = &Migration{Version: version, Name: matches[2]}
			byVersion[version] = migration
		}

		fullPath := filepath.Join(path, entry.Name())
		switch matches[3] {
		case "up":
			migration.UpPath = fullPath
		case "down":
			migration.DownPath = fullPath
		}
	}

	migrations := make([]Migration, 0, len(byVersion))
	for _, migration := range byVersion {
		if migration.UpPath == "" {
			return nil, fmt.Errorf("migration %03d_%s is missing up file", migration.Version, migration.Name)
		}
		if migration.DownPath == "" {
			return nil, fmt.Errorf("migration %03d_%s is missing down file", migration.Version, migration.Name)
		}
		migrations = append(migrations, *migration)
	}

	sort.Slice(migrations, func(i, j int) bool {
		return migrations[i].Version < migrations[j].Version
	})

	return migrations, nil
}

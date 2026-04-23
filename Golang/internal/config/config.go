package config

import (
	"fmt"
	"strings"

	"github.com/spf13/viper"
)

type Config struct {
	HTTP     HTTPConfig     `mapstructure:"http"`
	Database DatabaseConfig `mapstructure:"database"`
}

type HTTPConfig struct {
	Address string `mapstructure:"address"`
}

type DatabaseConfig struct {
	ConnectionString string `mapstructure:"connection_string"`
	MaxConnections   int32  `mapstructure:"max_connections"`
}

func Load(path string) (Config, error) {
	if strings.TrimSpace(path) == "" {
		path = "config.yaml"
	}

	reader := viper.New()
	reader.SetConfigFile(path)
	reader.SetDefault("http.address", ":8080")
	reader.SetDefault("database.max_connections", 10)
	reader.SetEnvPrefix("BOOKSVC")
	reader.SetEnvKeyReplacer(strings.NewReplacer(".", "_"))
	reader.AutomaticEnv()
	_ = reader.BindEnv("http.address")
	_ = reader.BindEnv("database.connection_string")
	_ = reader.BindEnv("database.max_connections")

	if err := reader.ReadInConfig(); err != nil {
		return Config{}, fmt.Errorf("read config: %w", err)
	}

	var cfg Config
	if err := reader.Unmarshal(&cfg); err != nil {
		return Config{}, fmt.Errorf("parse config: %w", err)
	}
	if err := cfg.Validate(); err != nil {
		return Config{}, err
	}

	return cfg, nil
}

func (c Config) Validate() error {
	if strings.TrimSpace(c.HTTP.Address) == "" {
		return fmt.Errorf("http.address is required")
	}
	if strings.TrimSpace(c.Database.ConnectionString) == "" {
		return fmt.Errorf("database.connection_string is required")
	}
	if c.Database.MaxConnections < 1 {
		return fmt.Errorf("database.max_connections must be 1 or greater")
	}
	return nil
}

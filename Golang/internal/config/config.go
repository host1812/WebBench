package config

import (
	"fmt"
	"strings"

	"github.com/spf13/viper"
)

type Config struct {
	HTTP      HTTPConfig      `mapstructure:"http"`
	Database  DatabaseConfig  `mapstructure:"database"`
	Telemetry TelemetryConfig `mapstructure:"telemetry"`
}

type HTTPConfig struct {
	Address string `mapstructure:"address"`
}

type DatabaseConfig struct {
	ConnectionString string `mapstructure:"connection_string"`
}

type TelemetryConfig struct {
	Enabled                             bool   `mapstructure:"enabled"`
	ServiceName                         string `mapstructure:"service_name"`
	Environment                         string `mapstructure:"environment"`
	OTLPEndpoint                        string `mapstructure:"otlp_endpoint"`
	ApplicationInsightsConnectionString string `mapstructure:"application_insights_connection_string"`
}

func Load(path string) (Config, error) {
	if strings.TrimSpace(path) == "" {
		path = "config.yaml"
	}

	reader := viper.New()
	reader.SetConfigFile(path)
	reader.SetDefault("http.address", ":8080")
	reader.SetDefault("telemetry.enabled", false)
	reader.SetDefault("telemetry.service_name", "books-service")
	reader.SetDefault("telemetry.environment", "local")
	reader.SetDefault("telemetry.otlp_endpoint", "otel-collector:4318")
	reader.SetEnvPrefix("BOOKSVC")
	reader.SetEnvKeyReplacer(strings.NewReplacer(".", "_"))
	reader.AutomaticEnv()
	_ = reader.BindEnv("http.address")
	_ = reader.BindEnv("database.connection_string")
	_ = reader.BindEnv("telemetry.enabled")
	_ = reader.BindEnv("telemetry.service_name")
	_ = reader.BindEnv("telemetry.environment")
	_ = reader.BindEnv("telemetry.otlp_endpoint")
	_ = reader.BindEnv("telemetry.application_insights_connection_string")

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
	if strings.TrimSpace(c.Telemetry.ServiceName) == "" {
		return fmt.Errorf("telemetry.service_name is required")
	}
	if strings.TrimSpace(c.Telemetry.Environment) == "" {
		return fmt.Errorf("telemetry.environment is required")
	}
	if c.Telemetry.Enabled && strings.TrimSpace(c.Telemetry.ApplicationInsightsConnectionString) == "" {
		return fmt.Errorf("telemetry.application_insights_connection_string is required when telemetry is enabled")
	}
	if c.Telemetry.Enabled && strings.TrimSpace(c.Telemetry.OTLPEndpoint) == "" {
		return fmt.Errorf("telemetry.otlp_endpoint is required when telemetry is enabled")
	}
	return nil
}

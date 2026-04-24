use serde::Deserialize;

use crate::error::AppError;

#[derive(Debug, Clone, Deserialize)]
pub struct AppConfig {
    #[serde(default)]
    pub server: ServerConfig,
    pub database: DatabaseConfig,
    #[serde(default)]
    pub observability: ObservabilityConfig,
}

impl AppConfig {
    pub fn load() -> Result<Self, AppError> {
        let config = config::Config::builder()
            .add_source(config::File::with_name("config/default.toml"))
            .add_source(config::Environment::with_prefix("APP").separator("__"))
            .build()?;

        let mut config = config.try_deserialize()?;
        apply_booksvc_environment(&mut config)?;
        validate_config(&config)?;

        Ok(config)
    }
}

#[derive(Debug, Clone, Deserialize)]
pub struct ServerConfig {
    #[serde(default = "default_host")]
    pub host: String,
    #[serde(default = "default_port")]
    pub port: u16,
}

impl Default for ServerConfig {
    fn default() -> Self {
        Self {
            host: default_host(),
            port: default_port(),
        }
    }
}

#[derive(Debug, Clone, Deserialize)]
pub struct DatabaseConfig {
    #[serde(default)]
    pub connection_string: String,
    #[serde(default = "default_max_connections")]
    pub max_connections: u32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ObservabilityConfig {
    #[serde(default = "default_log_filter")]
    pub log_filter: String,
    pub telemetry_enabled: Option<bool>,
    #[serde(default = "default_service_name")]
    pub service_name: String,
    #[serde(default = "default_environment")]
    pub environment: String,
    pub otlp_endpoint: Option<String>,
}

impl Default for ObservabilityConfig {
    fn default() -> Self {
        Self {
            log_filter: default_log_filter(),
            telemetry_enabled: None,
            service_name: default_service_name(),
            environment: default_environment(),
            otlp_endpoint: None,
        }
    }
}

fn apply_booksvc_environment(config: &mut AppConfig) -> Result<(), AppError> {
    if let Ok(value) = std::env::var("BOOKSVC_HTTP_ADDRESS") {
        let (host, port) = parse_http_address(&value)?;
        config.server.host = host;
        config.server.port = port;
    }

    if let Ok(value) = std::env::var("LOCAL_DATABASE_CONNECTION_STRING") {
        config.database.connection_string = value;
    }

    if let Ok(value) = std::env::var("BOOKSVC_DATABASE_CONNECTION_STRING") {
        config.database.connection_string = value;
    }

    if let Ok(value) = std::env::var("BOOKSVC_DATABASE_MAX_CONNECTIONS") {
        config.database.max_connections = value.trim().parse::<u32>().map_err(|_| {
            AppError::Validation(format!(
                "BOOKSVC_DATABASE_MAX_CONNECTIONS must be a positive integer, got '{value}'"
            ))
        })?;
    }

    if let Ok(value) = std::env::var("BOOKSVC_TELEMETRY_ENABLED") {
        config.observability.telemetry_enabled = Some(parse_bool(&value)?);
    }

    if let Ok(value) = std::env::var("BOOKSVC_TELEMETRY_SERVICE_NAME") {
        config.observability.service_name = value;
    }

    if let Ok(value) = std::env::var("BOOKSVC_TELEMETRY_ENVIRONMENT") {
        config.observability.environment = value;
    }

    if let Ok(value) = std::env::var("BOOKSVC_TELEMETRY_OTLP_ENDPOINT") {
        config.observability.otlp_endpoint = Some(value);
    }

    Ok(())
}

fn validate_config(config: &AppConfig) -> Result<(), AppError> {
    if config.database.connection_string.trim().is_empty() {
        return Err(AppError::Validation(
            "BOOKSVC_DATABASE_CONNECTION_STRING or LOCAL_DATABASE_CONNECTION_STRING is required"
                .to_owned(),
        ));
    }

    if config.observability.telemetry_enabled == Some(true)
        && config
            .observability
            .otlp_endpoint
            .as_deref()
            .map(str::trim)
            .filter(|value| !value.is_empty())
            .is_none()
    {
        return Err(AppError::Validation(
            "BOOKSVC_TELEMETRY_OTLP_ENDPOINT is required when telemetry is enabled".to_owned(),
        ));
    }

    Ok(())
}

fn parse_http_address(value: &str) -> Result<(String, u16), AppError> {
    let trimmed = value.trim();

    if let Some(port) = trimmed.strip_prefix(':') {
        return Ok(("0.0.0.0".to_owned(), parse_port(port, value)?));
    }

    if let Some((host, port)) = trimmed.rsplit_once(':') {
        let host = if host.trim().is_empty() {
            "0.0.0.0"
        } else {
            host.trim()
        };

        return Ok((host.to_owned(), parse_port(port, value)?));
    }

    Ok(("0.0.0.0".to_owned(), parse_port(trimmed, value)?))
}

fn parse_port(port: &str, original: &str) -> Result<u16, AppError> {
    port.trim().parse::<u16>().map_err(|_| {
        AppError::Validation(format!(
            "BOOKSVC_HTTP_ADDRESS must contain a valid port, got '{original}'"
        ))
    })
}

fn parse_bool(value: &str) -> Result<bool, AppError> {
    match value.trim().to_ascii_lowercase().as_str() {
        "1" | "true" | "yes" | "y" | "on" => Ok(true),
        "0" | "false" | "no" | "n" | "off" => Ok(false),
        _ => Err(AppError::Validation(format!(
            "BOOKSVC_TELEMETRY_ENABLED must be true or false, got '{value}'"
        ))),
    }
}

fn default_host() -> String {
    "127.0.0.1".to_owned()
}

fn default_port() -> u16 {
    8080
}

fn default_max_connections() -> u32 {
    10
}

fn default_log_filter() -> String {
    "info".to_owned()
}

fn default_service_name() -> String {
    "rust_backend_service".to_owned()
}

fn default_environment() -> String {
    "local".to_owned()
}

#[cfg(test)]
mod tests {
    use super::{
        AppConfig, DatabaseConfig, ObservabilityConfig, ServerConfig, parse_bool,
        parse_http_address, validate_config,
    };

    #[test]
    fn parse_http_address_supports_port_only_format() {
        let (host, port) = parse_http_address(":8080").unwrap();

        assert_eq!(host, "0.0.0.0");
        assert_eq!(port, 8080);
    }

    #[test]
    fn parse_http_address_supports_host_and_port_format() {
        let (host, port) = parse_http_address("127.0.0.1:8081").unwrap();

        assert_eq!(host, "127.0.0.1");
        assert_eq!(port, 8081);
    }

    #[test]
    fn parse_http_address_rejects_invalid_port() {
        let error = parse_http_address(":not-a-port").unwrap_err();

        assert!(error.to_string().contains("BOOKSVC_HTTP_ADDRESS"));
    }

    #[test]
    fn parse_bool_supports_common_boolean_formats() {
        assert!(parse_bool("true").unwrap());
        assert!(parse_bool("1").unwrap());
        assert!(!parse_bool("false").unwrap());
        assert!(!parse_bool("0").unwrap());
    }

    #[test]
    fn parse_bool_rejects_invalid_values() {
        let error = parse_bool("maybe").unwrap_err();

        assert!(error.to_string().contains("BOOKSVC_TELEMETRY_ENABLED"));
    }

    #[test]
    fn validate_config_accepts_database_connection_string_after_env_mapping() {
        let config = test_config("postgres://postgres:postgres@postgres:5432/books");

        validate_config(&config).unwrap();
    }

    #[test]
    fn validate_config_rejects_missing_database_connection_string() {
        let error = validate_config(&test_config("")).unwrap_err();

        assert!(
            error
                .to_string()
                .contains("BOOKSVC_DATABASE_CONNECTION_STRING")
        );
    }

    fn test_config(connection_string: &str) -> AppConfig {
        AppConfig {
            server: ServerConfig::default(),
            database: DatabaseConfig {
                connection_string: connection_string.to_owned(),
                max_connections: 10,
            },
            observability: ObservabilityConfig::default(),
        }
    }
}

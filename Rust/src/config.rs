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

        Ok(config.try_deserialize()?)
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
    pub connection_string: String,
    #[serde(default = "default_max_connections")]
    pub max_connections: u32,
    #[serde(default = "default_run_migrations_on_startup")]
    pub run_migrations_on_startup: bool,
}

#[derive(Debug, Clone, Deserialize)]
pub struct ObservabilityConfig {
    #[serde(default = "default_log_filter")]
    pub log_filter: String,
    #[serde(default = "default_service_name")]
    pub service_name: String,
    #[serde(default = "default_environment")]
    pub environment: String,
    pub application_insights_connection_string: Option<String>,
}

impl Default for ObservabilityConfig {
    fn default() -> Self {
        Self {
            log_filter: default_log_filter(),
            service_name: default_service_name(),
            environment: default_environment(),
            application_insights_connection_string: None,
        }
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

fn default_run_migrations_on_startup() -> bool {
    false
}

fn default_log_filter() -> String {
    "info,rust_backend_service=debug,tower_http=info".to_owned()
}

fn default_service_name() -> String {
    "rust_backend_service".to_owned()
}

fn default_environment() -> String {
    "local".to_owned()
}

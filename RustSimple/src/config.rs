use std::{
    env,
    net::{IpAddr, Ipv4Addr, SocketAddr},
    path::PathBuf,
};

use thiserror::Error;

#[derive(Debug, Clone)]
pub struct Config {
    pub server: ServerConfig,
    pub database: DatabaseConfig,
    pub tls: TlsConfig,
}

#[derive(Debug, Clone)]
pub struct ServerConfig {
    pub host: IpAddr,
    pub port: u16,
}

#[derive(Debug, Clone)]
pub struct DatabaseConfig {
    pub url: String,
    pub max_connections: u32,
}

#[derive(Debug, Clone)]
pub struct TlsConfig {
    pub cert_path: PathBuf,
    pub key_path: PathBuf,
}

#[derive(Debug, Error)]
pub enum ConfigError {
    #[error("missing required environment variable `{0}`")]
    MissingEnv(&'static str),
    #[error("invalid environment variable `{key}`: {message}")]
    InvalidEnv { key: &'static str, message: String },
}

impl Config {
    pub fn from_env() -> Result<Self, ConfigError> {
        dotenvy::dotenv().ok();

        let database_url =
            env::var("DATABASE_URL").map_err(|_| ConfigError::MissingEnv("DATABASE_URL"))?;
        let tls_cert_path = env::var("TLS_CERT_PATH")
            .map(PathBuf::from)
            .map_err(|_| ConfigError::MissingEnv("TLS_CERT_PATH"))?;
        let tls_key_path = env::var("TLS_KEY_PATH")
            .map(PathBuf::from)
            .map_err(|_| ConfigError::MissingEnv("TLS_KEY_PATH"))?;

        let database_max_connections = parse_env::<u32>("DATABASE_MAX_CONNECTIONS")?.unwrap_or(10);
        let server_host =
            parse_env::<IpAddr>("SERVER_HOST")?.unwrap_or(IpAddr::V4(Ipv4Addr::UNSPECIFIED));
        let server_port = parse_env::<u16>("SERVER_PORT")?.unwrap_or(8443);

        Ok(Self {
            server: ServerConfig {
                host: server_host,
                port: server_port,
            },
            database: DatabaseConfig {
                url: database_url,
                max_connections: database_max_connections,
            },
            tls: TlsConfig {
                cert_path: tls_cert_path,
                key_path: tls_key_path,
            },
        })
    }
}

impl ServerConfig {
    pub fn socket_addr(&self) -> SocketAddr {
        SocketAddr::new(self.host, self.port)
    }
}

fn parse_env<T>(key: &'static str) -> Result<Option<T>, ConfigError>
where
    T: std::str::FromStr,
    T::Err: std::fmt::Display,
{
    match env::var(key) {
        Ok(raw) => raw
            .parse::<T>()
            .map(Some)
            .map_err(|error| ConfigError::InvalidEnv {
                key,
                message: error.to_string(),
            }),
        Err(env::VarError::NotPresent) => Ok(None),
        Err(env::VarError::NotUnicode(_)) => Err(ConfigError::InvalidEnv {
            key,
            message: "value is not valid unicode".to_string(),
        }),
    }
}

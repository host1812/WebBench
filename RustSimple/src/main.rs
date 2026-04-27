use std::net::SocketAddr;

use anyhow::Context;
use axum_server::{Handle, tls_rustls::RustlsConfig};
use clap::{Parser, Subcommand};
use tracing::info;
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt};

use rust_simple::{config::Config, db, router, state::AppState};

#[derive(Debug, Parser)]
#[command(author, version, about)]
struct Cli {
    #[command(subcommand)]
    command: Option<Command>,
}

#[derive(Debug, Clone, Copy, Subcommand)]
enum Command {
    Serve,
    Migrate,
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    install_crypto_provider();
    init_tracing();

    let cli = Cli::parse();
    let config = Config::from_env().context("failed to load application config")?;

    match cli.command.unwrap_or(Command::Serve) {
        Command::Serve => serve(config).await,
        Command::Migrate => migrate(config).await,
    }
}

fn install_crypto_provider() {
    let _ = rustls::crypto::aws_lc_rs::default_provider().install_default();
}

fn init_tracing() {
    tracing_subscriber::registry()
        .with(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| "rust_simple=debug,tower_http=info".into()),
        )
        .with(tracing_subscriber::fmt::layer())
        .init();
}

fn display_https_addr(addr: SocketAddr) -> String {
    format!("https://{addr}")
}

async fn shutdown_signal(handle: Handle) {
    if let Err(error) = tokio::signal::ctrl_c().await {
        tracing::error!(%error, "failed to listen for shutdown signal");
        return;
    }

    handle.graceful_shutdown(None);
}

async fn serve(config: Config) -> anyhow::Result<()> {
    let pool = db::connect(&config)
        .await
        .context("failed to connect to postgres")?;
    db::migrate(&pool)
        .await
        .context("failed to run database migrations")?;

    let state = AppState { pool };
    let addr = config.server.socket_addr();
    let tls_config = RustlsConfig::from_pem_file(&config.tls.cert_path, &config.tls.key_path)
        .await
        .context("failed to load TLS certificate and key")?;
    let handle = Handle::new();

    tokio::spawn(shutdown_signal(handle.clone()));

    info!(address = %display_https_addr(addr), "service listening");

    axum_server::bind_rustls(addr, tls_config)
        .handle(handle)
        .serve(router::build(state).into_make_service())
        .await
        .context("server exited unexpectedly")?;

    Ok(())
}

async fn migrate(config: Config) -> anyhow::Result<()> {
    let pool = db::connect(&config)
        .await
        .context("failed to connect to postgres")?;

    db::migrate(&pool)
        .await
        .context("failed to run database migrations")?;

    info!("database migrations applied successfully");

    Ok(())
}

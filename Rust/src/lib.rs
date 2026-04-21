pub mod application;
pub mod config;
pub mod domain;
pub mod error;
pub mod infrastructure;
pub mod presentation;
pub mod telemetry;

use std::{net::SocketAddr, sync::Arc};

use application::{
    authors::{
        commands::{AuthorCommandHandler, AuthorCommandService},
        queries::{AuthorQueryHandler, AuthorQueryService},
    },
    books::{
        commands::{BookCommandHandler, BookCommandService},
        queries::{BookQueryHandler, BookQueryService},
    },
    health::{HealthQueryHandler, HealthQueryService},
};
use config::AppConfig;
use error::AppError;
use infrastructure::{
    db::create_pool,
    health::PostgresHealthCheck,
    persistence::{
        postgres_author_repository::PostgresAuthorRepository,
        postgres_book_repository::PostgresBookRepository,
    },
};
use presentation::http::{AppState, router::build_router};
use tracing::info;

#[tracing::instrument(name = "app.run", err)]
pub async fn run() -> Result<(), AppError> {
    let config = AppConfig::load()?;
    let _telemetry = telemetry::init_tracing(&config)?;

    info!("starting service");
    let pool = create_pool(&config.database).await?;

    let author_repository = Arc::new(PostgresAuthorRepository::new(pool.clone()));
    let book_repository = Arc::new(PostgresBookRepository::new(pool.clone()));
    let database_health_check = Arc::new(PostgresHealthCheck::new(pool));

    let health_queries: Arc<dyn HealthQueryService> =
        Arc::new(HealthQueryHandler::new(database_health_check));
    let author_commands: Arc<dyn AuthorCommandService> = Arc::new(AuthorCommandHandler::new(
        author_repository.clone(),
        author_repository.clone(),
    ));
    let author_queries: Arc<dyn AuthorQueryService> = Arc::new(AuthorQueryHandler::new(
        author_repository.clone(),
        book_repository.clone(),
    ));
    let book_commands: Arc<dyn BookCommandService> = Arc::new(BookCommandHandler::new(
        book_repository.clone(),
        book_repository.clone(),
        author_repository.clone(),
    ));
    let book_queries: Arc<dyn BookQueryService> = Arc::new(BookQueryHandler::new(
        book_repository.clone(),
        author_repository,
    ));

    let state = AppState::new(
        health_queries,
        author_commands,
        author_queries,
        book_commands,
        book_queries,
    );
    let app = build_router(state);

    let address = SocketAddr::new(config.server.host.parse()?, config.server.port);
    let listener = tokio::net::TcpListener::bind(address).await?;

    info!("listening on {}", listener.local_addr()?);

    axum::serve(listener, app)
        .with_graceful_shutdown(shutdown_signal())
        .await?;

    Ok(())
}

async fn shutdown_signal() {
    let ctrl_c = async {
        if let Err(error) = tokio::signal::ctrl_c().await {
            tracing::error!(%error, "failed to listen for ctrl-c signal");
        }
    };

    #[cfg(unix)]
    let terminate = async {
        use tokio::signal::unix::{SignalKind, signal};

        match signal(SignalKind::terminate()) {
            Ok(mut signal) => {
                signal.recv().await;
            }
            Err(error) => {
                tracing::error!(%error, "failed to listen for terminate signal");
            }
        }
    };

    #[cfg(not(unix))]
    let terminate = std::future::pending::<()>();

    tokio::select! {
        _ = ctrl_c => {}
        _ = terminate => {}
    }
}

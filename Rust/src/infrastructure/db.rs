use sqlx::{PgPool, postgres::PgPoolOptions};

use crate::{config::DatabaseConfig, error::AppError};

static MIGRATOR: sqlx::migrate::Migrator = sqlx::migrate!("./migrations");

#[tracing::instrument(name = "db.create_pool", skip(config), err)]
pub async fn create_pool(config: &DatabaseConfig) -> Result<PgPool, AppError> {
    PgPoolOptions::new()
        .max_connections(config.max_connections)
        .connect(&config.connection_string)
        .await
        .map_err(AppError::from)
}

#[tracing::instrument(name = "db.run_migrations", skip(pool), err)]
pub async fn run_migrations(pool: &PgPool) -> Result<(), AppError> {
    MIGRATOR.run(pool).await?;
    Ok(())
}

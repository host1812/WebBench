use sqlx::{PgPool, postgres::PgPoolOptions};

use crate::{config::DatabaseConfig, error::AppError};

#[tracing::instrument(name = "db.create_pool", skip(config), err)]
pub async fn create_pool(config: &DatabaseConfig) -> Result<PgPool, AppError> {
    PgPoolOptions::new()
        .max_connections(config.max_connections)
        .connect(&config.connection_string)
        .await
        .map_err(AppError::from)
}

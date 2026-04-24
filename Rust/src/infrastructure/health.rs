use async_trait::async_trait;
use sqlx::PgPool;

use crate::application::health::{DependencyHealthCheck, DependencyHealthDto};

pub struct PostgresHealthCheck {
    pool: PgPool,
}

impl PostgresHealthCheck {
    pub fn new(pool: PgPool) -> Self {
        Self { pool }
    }
}

#[async_trait]
impl DependencyHealthCheck for PostgresHealthCheck {
    async fn check(&self) -> DependencyHealthDto {
        match sqlx::query_scalar::<_, i32>("SELECT 1")
            .fetch_one(&self.pool)
            .await
        {
            Ok(1) => DependencyHealthDto::healthy(),
            Ok(_) => DependencyHealthDto::unhealthy("database returned an unexpected health value"),
            Err(_) => DependencyHealthDto::unhealthy("database health check failed"),
        }
    }
}

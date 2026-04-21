use std::sync::Arc;

use async_trait::async_trait;
use serde::Serialize;
use time::OffsetDateTime;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum HealthStatus {
    Healthy,
    Unhealthy,
}

#[derive(Debug, Clone, Serialize)]
pub struct HealthReportDto {
    pub service: String,
    pub status: HealthStatus,
    pub checks: HealthChecksDto,
    #[serde(with = "time::serde::rfc3339")]
    pub timestamp: OffsetDateTime,
}

impl HealthReportDto {
    pub fn is_healthy(&self) -> bool {
        self.status == HealthStatus::Healthy
    }
}

#[derive(Debug, Clone, Serialize)]
pub struct HealthChecksDto {
    pub database: DependencyHealthDto,
}

#[derive(Debug, Clone, Serialize)]
pub struct DependencyHealthDto {
    pub status: HealthStatus,
    pub message: Option<String>,
}

impl DependencyHealthDto {
    pub fn healthy() -> Self {
        Self {
            status: HealthStatus::Healthy,
            message: None,
        }
    }

    pub fn unhealthy(message: impl Into<String>) -> Self {
        Self {
            status: HealthStatus::Unhealthy,
            message: Some(message.into()),
        }
    }
}

#[async_trait]
pub trait HealthQueryService: Send + Sync {
    async fn check_health(&self) -> HealthReportDto;
}

#[async_trait]
pub trait DependencyHealthCheck: Send + Sync {
    async fn check(&self) -> DependencyHealthDto;
}

pub struct HealthQueryHandler {
    database: Arc<dyn DependencyHealthCheck>,
}

impl HealthQueryHandler {
    pub fn new(database: Arc<dyn DependencyHealthCheck>) -> Self {
        Self { database }
    }
}

#[async_trait]
impl HealthQueryService for HealthQueryHandler {
    #[tracing::instrument(name = "health.check", skip(self))]
    async fn check_health(&self) -> HealthReportDto {
        let database = self.database.check().await;
        let status = if database.status == HealthStatus::Healthy {
            HealthStatus::Healthy
        } else {
            HealthStatus::Unhealthy
        };

        HealthReportDto {
            service: "rust_backend_service".to_owned(),
            status,
            checks: HealthChecksDto { database },
            timestamp: OffsetDateTime::now_utc(),
        }
    }
}

#[cfg(test)]
mod tests {
    use async_trait::async_trait;

    use super::{
        DependencyHealthCheck, DependencyHealthDto, HealthQueryHandler, HealthQueryService,
        HealthStatus,
    };

    #[tokio::test]
    async fn check_health_returns_healthy_when_database_is_healthy() {
        let handler = HealthQueryHandler::new(std::sync::Arc::new(FakeDependency {
            result: DependencyHealthDto::healthy(),
        }));

        let report = handler.check_health().await;

        assert_eq!(report.status, HealthStatus::Healthy);
        assert!(report.is_healthy());
        assert_eq!(report.checks.database.status, HealthStatus::Healthy);
    }

    #[tokio::test]
    async fn check_health_returns_unhealthy_when_database_is_unhealthy() {
        let handler = HealthQueryHandler::new(std::sync::Arc::new(FakeDependency {
            result: DependencyHealthDto::unhealthy("database unavailable"),
        }));

        let report = handler.check_health().await;

        assert_eq!(report.status, HealthStatus::Unhealthy);
        assert!(!report.is_healthy());
        assert_eq!(report.checks.database.status, HealthStatus::Unhealthy);
    }

    struct FakeDependency {
        result: DependencyHealthDto,
    }

    #[async_trait]
    impl DependencyHealthCheck for FakeDependency {
        async fn check(&self) -> DependencyHealthDto {
            self.result.clone()
        }
    }
}

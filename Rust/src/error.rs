use axum::{
    Json,
    http::StatusCode,
    response::{IntoResponse, Response},
};
use serde::Serialize;
use thiserror::Error;

#[derive(Debug, Error)]
pub enum AppError {
    #[error("resource not found: {0}")]
    NotFound(String),
    #[error("validation failed: {0}")]
    Validation(String),
    #[error("conflict: {0}")]
    Conflict(String),
    #[error("telemetry initialization failed: {0}")]
    Telemetry(String),
    #[error(transparent)]
    Config(#[from] config::ConfigError),
    #[error(transparent)]
    Io(#[from] std::io::Error),
    #[error(transparent)]
    AddressParse(#[from] std::net::AddrParseError),
    #[error(transparent)]
    Migrate(#[from] sqlx::migrate::MigrateError),
    #[error(transparent)]
    Sqlx(#[from] sqlx::Error),
}

impl IntoResponse for AppError {
    fn into_response(self) -> Response {
        let status = match self {
            Self::NotFound(_) => StatusCode::NOT_FOUND,
            Self::Validation(_) => StatusCode::BAD_REQUEST,
            Self::Conflict(_) => StatusCode::CONFLICT,
            Self::Telemetry(_)
            | Self::Config(_)
            | Self::Io(_)
            | Self::AddressParse(_)
            | Self::Migrate(_)
            | Self::Sqlx(_) => StatusCode::INTERNAL_SERVER_ERROR,
        };

        let body = Json(ErrorResponse {
            message: self.to_string(),
        });

        (status, body).into_response()
    }
}

#[derive(Debug, Serialize)]
struct ErrorResponse {
    message: String,
}

#[cfg(test)]
mod tests {
    use axum::{body::to_bytes, http::StatusCode, response::IntoResponse};
    use serde_json::Value;

    use super::AppError;

    #[tokio::test]
    async fn not_found_maps_to_404_json_response() {
        let response = AppError::NotFound("author 1".to_owned()).into_response();

        assert_eq!(response.status(), StatusCode::NOT_FOUND);
        assert_eq!(
            response_body(response).await["message"],
            "resource not found: author 1"
        );
    }

    #[tokio::test]
    async fn validation_maps_to_400_json_response() {
        let response = AppError::Validation("name cannot be empty".to_owned()).into_response();

        assert_eq!(response.status(), StatusCode::BAD_REQUEST);
        assert_eq!(
            response_body(response).await["message"],
            "validation failed: name cannot be empty"
        );
    }

    #[tokio::test]
    async fn conflict_maps_to_409_json_response() {
        let response = AppError::Conflict("orphan book".to_owned()).into_response();

        assert_eq!(response.status(), StatusCode::CONFLICT);
        assert_eq!(
            response_body(response).await["message"],
            "conflict: orphan book"
        );
    }

    async fn response_body(response: axum::response::Response) -> Value {
        let bytes = to_bytes(response.into_body(), usize::MAX).await.unwrap();
        serde_json::from_slice(&bytes).unwrap()
    }
}

use axum::{
    Json,
    http::StatusCode,
    response::{IntoResponse, Response},
};
use serde::Serialize;
use tracing::error;

#[derive(Debug)]
pub enum AppError {
    NotFound(String),
    Validation(String),
    Database(sqlx::Error),
}

#[derive(Debug, Serialize)]
struct ErrorResponse {
    error: &'static str,
    message: String,
}

impl AppError {
    pub fn not_found(message: impl Into<String>) -> Self {
        Self::NotFound(message.into())
    }

    pub fn validation(message: impl Into<String>) -> Self {
        Self::Validation(message.into())
    }
}

impl From<sqlx::Error> for AppError {
    fn from(error: sqlx::Error) -> Self {
        match error {
            sqlx::Error::RowNotFound => Self::not_found("resource not found"),
            other => Self::Database(other),
        }
    }
}

impl IntoResponse for AppError {
    fn into_response(self) -> Response {
        match self {
            Self::NotFound(message) => (
                StatusCode::NOT_FOUND,
                Json(ErrorResponse {
                    error: "not_found",
                    message,
                }),
            )
                .into_response(),
            Self::Validation(message) => (
                StatusCode::BAD_REQUEST,
                Json(ErrorResponse {
                    error: "validation_error",
                    message,
                }),
            )
                .into_response(),
            Self::Database(error) => {
                if is_foreign_key_violation(&error) {
                    return (
                        StatusCode::BAD_REQUEST,
                        Json(ErrorResponse {
                            error: "constraint_violation",
                            message: "referenced resource does not exist".to_string(),
                        }),
                    )
                        .into_response();
                }

                error!(%error, "database request failed");

                (
                    StatusCode::INTERNAL_SERVER_ERROR,
                    Json(ErrorResponse {
                        error: "internal_server_error",
                        message: "unexpected server error".to_string(),
                    }),
                )
                    .into_response()
            }
        }
    }
}

fn is_foreign_key_violation(error: &sqlx::Error) -> bool {
    matches!(
        error,
        sqlx::Error::Database(db_error) if db_error.code().as_deref() == Some("23503")
    )
}

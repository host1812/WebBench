use axum::{
    Json, Router,
    extract::{Path, State},
    http::StatusCode,
    routing::get,
};
use serde::{Deserialize, Serialize};
use sqlx::FromRow;
use time::OffsetDateTime;

use crate::{error::AppError, state::AppState};

pub fn router() -> Router<AppState> {
    Router::new()
        .route("/", get(list_authors).post(create_author))
        .route(
            "/:id",
            get(get_author).put(update_author).delete(delete_author),
        )
}

#[derive(Debug, Serialize, FromRow)]
struct Author {
    id: i64,
    name: String,
    bio: Option<String>,
    created_at: OffsetDateTime,
    updated_at: OffsetDateTime,
}

#[derive(Debug, Deserialize)]
struct CreateAuthorRequest {
    name: String,
    bio: Option<String>,
}

#[derive(Debug, Deserialize)]
struct UpdateAuthorRequest {
    name: String,
    bio: Option<String>,
}

async fn list_authors(State(state): State<AppState>) -> Result<Json<Vec<Author>>, AppError> {
    let authors = sqlx::query_as::<_, Author>(
        r#"
        SELECT id, name, bio, created_at, updated_at
        FROM authors
        ORDER BY id
        "#,
    )
    .fetch_all(&state.pool)
    .await?;

    Ok(Json(authors))
}

async fn create_author(
    State(state): State<AppState>,
    Json(payload): Json<CreateAuthorRequest>,
) -> Result<(StatusCode, Json<Author>), AppError> {
    validate_required_text("name", &payload.name)?;

    let author = sqlx::query_as::<_, Author>(
        r#"
        INSERT INTO authors (name, bio)
        VALUES ($1, $2)
        RETURNING id, name, bio, created_at, updated_at
        "#,
    )
    .bind(payload.name.trim())
    .bind(payload.bio)
    .fetch_one(&state.pool)
    .await?;

    Ok((StatusCode::CREATED, Json(author)))
}

async fn get_author(
    State(state): State<AppState>,
    Path(id): Path<i64>,
) -> Result<Json<Author>, AppError> {
    let author = sqlx::query_as::<_, Author>(
        r#"
        SELECT id, name, bio, created_at, updated_at
        FROM authors
        WHERE id = $1
        "#,
    )
    .bind(id)
    .fetch_one(&state.pool)
    .await?;

    Ok(Json(author))
}

async fn update_author(
    State(state): State<AppState>,
    Path(id): Path<i64>,
    Json(payload): Json<UpdateAuthorRequest>,
) -> Result<Json<Author>, AppError> {
    validate_required_text("name", &payload.name)?;

    let author = sqlx::query_as::<_, Author>(
        r#"
        UPDATE authors
        SET name = $2, bio = $3
        WHERE id = $1
        RETURNING id, name, bio, created_at, updated_at
        "#,
    )
    .bind(id)
    .bind(payload.name.trim())
    .bind(payload.bio)
    .fetch_one(&state.pool)
    .await?;

    Ok(Json(author))
}

async fn delete_author(
    State(state): State<AppState>,
    Path(id): Path<i64>,
) -> Result<StatusCode, AppError> {
    let result = sqlx::query(
        r#"
        DELETE FROM authors
        WHERE id = $1
        "#,
    )
    .bind(id)
    .execute(&state.pool)
    .await?;

    if result.rows_affected() == 0 {
        return Err(AppError::not_found(format!("author {id} was not found")));
    }

    Ok(StatusCode::NO_CONTENT)
}

fn validate_required_text(field_name: &str, value: &str) -> Result<(), AppError> {
    if value.trim().is_empty() {
        return Err(AppError::validation(format!(
            "{field_name} cannot be empty"
        )));
    }

    Ok(())
}

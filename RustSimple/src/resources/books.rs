use axum::{
    Json, Router,
    extract::{Path, Query, State},
    http::StatusCode,
    routing::get,
};
use serde::{Deserialize, Serialize};
use sqlx::FromRow;
use time::OffsetDateTime;

use crate::{error::AppError, state::AppState};

const DEFAULT_BOOK_LIMIT: u32 = 10_000;
const MAX_BOOK_LIMIT: u32 = 100_000;

pub fn router() -> Router<AppState> {
    Router::new()
        .route("/", get(list_books).post(create_book))
        .route("/:id", get(get_book).put(update_book).delete(delete_book))
}

#[derive(Debug, Serialize, FromRow)]
struct Book {
    id: i64,
    author_id: i64,
    title: String,
    description: Option<String>,
    published_year: Option<i32>,
    created_at: OffsetDateTime,
    updated_at: OffsetDateTime,
}

#[derive(Debug, Deserialize)]
struct CreateBookRequest {
    author_id: i64,
    title: String,
    description: Option<String>,
    published_year: Option<i32>,
}

#[derive(Debug, Deserialize)]
struct UpdateBookRequest {
    author_id: i64,
    title: String,
    description: Option<String>,
    published_year: Option<i32>,
}

#[derive(Debug, Deserialize)]
struct BookListQuery {
    limit: Option<u32>,
}

impl BookListQuery {
    fn parsed_limit(&self) -> Result<i64, AppError> {
        let limit = self.limit.unwrap_or(DEFAULT_BOOK_LIMIT);

        if !(1..=MAX_BOOK_LIMIT).contains(&limit) {
            return Err(AppError::validation(format!(
                "limit must be between 1 and {MAX_BOOK_LIMIT}"
            )));
        }

        Ok(i64::from(limit))
    }
}

async fn list_books(
    State(state): State<AppState>,
    Query(params): Query<BookListQuery>,
) -> Result<Json<Vec<Book>>, AppError> {
    let limit = params.parsed_limit()?;

    let books = sqlx::query_as::<_, Book>(
        r#"
        SELECT id, author_id, title, description, published_year, created_at, updated_at
        FROM books
        ORDER BY id
        LIMIT $1
        "#,
    )
    .bind(limit)
    .fetch_all(&state.pool)
    .await?;

    Ok(Json(books))
}

async fn create_book(
    State(state): State<AppState>,
    Json(payload): Json<CreateBookRequest>,
) -> Result<(StatusCode, Json<Book>), AppError> {
    validate_required_text("title", &payload.title)?;

    let book = sqlx::query_as::<_, Book>(
        r#"
        INSERT INTO books (author_id, title, description, published_year)
        VALUES ($1, $2, $3, $4)
        RETURNING id, author_id, title, description, published_year, created_at, updated_at
        "#,
    )
    .bind(payload.author_id)
    .bind(payload.title.trim())
    .bind(payload.description)
    .bind(payload.published_year)
    .fetch_one(&state.pool)
    .await?;

    Ok((StatusCode::CREATED, Json(book)))
}

async fn get_book(
    State(state): State<AppState>,
    Path(id): Path<i64>,
) -> Result<Json<Book>, AppError> {
    let book = sqlx::query_as::<_, Book>(
        r#"
        SELECT id, author_id, title, description, published_year, created_at, updated_at
        FROM books
        WHERE id = $1
        "#,
    )
    .bind(id)
    .fetch_one(&state.pool)
    .await?;

    Ok(Json(book))
}

async fn update_book(
    State(state): State<AppState>,
    Path(id): Path<i64>,
    Json(payload): Json<UpdateBookRequest>,
) -> Result<Json<Book>, AppError> {
    validate_required_text("title", &payload.title)?;

    let book = sqlx::query_as::<_, Book>(
        r#"
        UPDATE books
        SET author_id = $2, title = $3, description = $4, published_year = $5
        WHERE id = $1
        RETURNING id, author_id, title, description, published_year, created_at, updated_at
        "#,
    )
    .bind(id)
    .bind(payload.author_id)
    .bind(payload.title.trim())
    .bind(payload.description)
    .bind(payload.published_year)
    .fetch_one(&state.pool)
    .await?;

    Ok(Json(book))
}

async fn delete_book(
    State(state): State<AppState>,
    Path(id): Path<i64>,
) -> Result<StatusCode, AppError> {
    let result = sqlx::query(
        r#"
        DELETE FROM books
        WHERE id = $1
        "#,
    )
    .bind(id)
    .execute(&state.pool)
    .await?;

    if result.rows_affected() == 0 {
        return Err(AppError::not_found(format!("book {id} was not found")));
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

#[cfg(test)]
mod tests {
    use super::{BookListQuery, DEFAULT_BOOK_LIMIT};

    #[test]
    fn uses_default_limit_when_missing() {
        let params = BookListQuery { limit: None };

        assert_eq!(
            params.parsed_limit().unwrap(),
            i64::from(DEFAULT_BOOK_LIMIT)
        );
    }

    #[test]
    fn rejects_limits_above_max() {
        let params = BookListQuery {
            limit: Some(100_001),
        };

        assert!(params.parsed_limit().is_err());
    }
}

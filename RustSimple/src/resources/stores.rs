use std::collections::HashMap;

use axum::{Json, Router, extract::State, routing::get};
use serde::Serialize;
use sqlx::FromRow;
use time::OffsetDateTime;
use uuid::Uuid;

use crate::{error::AppError, state::AppState};

pub fn router() -> Router<AppState> {
    Router::new().route("/", get(list_stores))
}

#[derive(Debug, Serialize)]
struct Store {
    id: Uuid,
    name: String,
    address: String,
    description: String,
    phone_number: String,
    website: Option<String>,
    created_at: OffsetDateTime,
    updated_at: OffsetDateTime,
    books: Vec<StoreBook>,
}

#[derive(Debug, FromRow)]
struct StoreRow {
    id: Uuid,
    name: String,
    address: String,
    description: String,
    phone_number: String,
    website: Option<String>,
    created_at: OffsetDateTime,
    updated_at: OffsetDateTime,
}

#[derive(Debug, Serialize)]
struct StoreBook {
    id: Uuid,
    author_id: Uuid,
    title: String,
    isbn: String,
    published_year: Option<i32>,
    created_at: OffsetDateTime,
    updated_at: OffsetDateTime,
}

#[derive(Debug, FromRow)]
struct StoreBookRow {
    store_id: Uuid,
    id: Uuid,
    author_id: Uuid,
    title: String,
    isbn: String,
    published_year: Option<i32>,
    created_at: OffsetDateTime,
    updated_at: OffsetDateTime,
}

async fn list_stores(State(state): State<AppState>) -> Result<Json<Vec<Store>>, AppError> {
    let store_rows = sqlx::query_as::<_, StoreRow>(
        r#"
        SELECT id, name, address, description, phone_number, website, created_at, updated_at
        FROM stores
        ORDER BY id
        "#,
    )
    .fetch_all(&state.pool)
    .await?;

    let store_ids = store_rows.iter().map(|store| store.id).collect::<Vec<_>>();
    let mut books_by_store = fetch_books_by_store(&state, &store_ids).await?;

    let stores = store_rows
        .into_iter()
        .map(|store| Store {
            id: store.id,
            name: store.name,
            address: store.address,
            description: store.description,
            phone_number: store.phone_number,
            website: store.website,
            created_at: store.created_at,
            updated_at: store.updated_at,
            books: books_by_store.remove(&store.id).unwrap_or_default(),
        })
        .collect();

    Ok(Json(stores))
}

async fn fetch_books_by_store(
    state: &AppState,
    store_ids: &[Uuid],
) -> Result<HashMap<Uuid, Vec<StoreBook>>, AppError> {
    if store_ids.is_empty() {
        return Ok(HashMap::new());
    }

    let book_rows = sqlx::query_as::<_, StoreBookRow>(
        r#"
        SELECT
            store_books.store_id,
            books.id,
            books.author_id,
            books.title,
            books.isbn,
            books.published_year,
            books.created_at,
            books.updated_at
        FROM store_books
        JOIN books
            ON books.id = store_books.book_id
        WHERE store_books.store_id = ANY($1)
        ORDER BY store_books.store_id, books.id
        "#,
    )
    .bind(store_ids)
    .fetch_all(&state.pool)
    .await?;

    let mut books_by_store = HashMap::new();

    for row in book_rows {
        books_by_store
            .entry(row.store_id)
            .or_insert_with(Vec::new)
            .push(StoreBook {
                id: row.id,
                author_id: row.author_id,
                title: row.title,
                isbn: row.isbn,
                published_year: row.published_year,
                created_at: row.created_at,
                updated_at: row.updated_at,
            });
    }

    Ok(books_by_store)
}

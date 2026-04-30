use std::collections::HashMap;

use async_trait::async_trait;
use sqlx::{FromRow, PgPool};
use time::OffsetDateTime;
use uuid::Uuid;

use crate::{
    domain::{
        author::AuthorId,
        book::{Book, BookId},
        store::{Store, StoreId, StoreQueryRepository},
    },
    error::AppError,
};

#[derive(Clone)]
pub struct PostgresStoreRepository {
    pool: PgPool,
}

impl PostgresStoreRepository {
    pub fn new(pool: PgPool) -> Self {
        Self { pool }
    }
}

#[async_trait]
impl StoreQueryRepository for PostgresStoreRepository {
    async fn list(&self) -> Result<Vec<Store>, AppError> {
        let store_rows = sqlx::query_as::<_, StoreRow>(
            r#"
            SELECT id, name, description, address, phone_number, web_site, created_at, updated_at
            FROM stores
            ORDER BY id ASC
            "#,
        )
        .fetch_all(&self.pool)
        .await?;

        if store_rows.is_empty() {
            return Ok(Vec::new());
        }

        let store_ids: Vec<Uuid> = store_rows.iter().map(|row| row.id).collect();
        let inventory_rows = sqlx::query_as::<_, InventoryBookRow>(
            r#"
            SELECT
                inventory.store_id,
                books.id,
                books.author_id,
                books.title,
                books.isbn,
                books.published_year,
                books.created_at,
                books.updated_at
            FROM store_inventory inventory
            INNER JOIN books ON books.id = inventory.book_id
            WHERE inventory.store_id = ANY($1)
            ORDER BY inventory.store_id ASC, books.id ASC
            "#,
        )
        .bind(&store_ids)
        .fetch_all(&self.pool)
        .await?;

        let mut inventory_by_store: HashMap<Uuid, Vec<Book>> = HashMap::new();
        for row in inventory_rows {
            inventory_by_store
                .entry(row.store_id)
                .or_default()
                .push(row.into_book());
        }

        Ok(store_rows
            .into_iter()
            .map(|row| {
                let inventory = inventory_by_store.remove(&row.id).unwrap_or_default();
                row.into_store(inventory)
            })
            .collect())
    }
}

#[derive(Debug, FromRow)]
struct StoreRow {
    id: Uuid,
    name: String,
    description: String,
    address: String,
    phone_number: String,
    web_site: Option<String>,
    created_at: OffsetDateTime,
    updated_at: OffsetDateTime,
}

impl StoreRow {
    fn into_store(self, inventory: Vec<Book>) -> Store {
        Store {
            id: StoreId(self.id),
            name: self.name,
            description: self.description,
            address: self.address,
            phone_number: self.phone_number,
            web_site: self.web_site.and_then(|value| {
                let trimmed = value.trim();
                (!trimmed.is_empty()).then(|| trimmed.to_owned())
            }),
            inventory,
            created_at: self.created_at,
            updated_at: self.updated_at,
        }
    }
}

#[derive(Debug, FromRow)]
struct InventoryBookRow {
    store_id: Uuid,
    id: Uuid,
    author_id: Uuid,
    title: String,
    isbn: String,
    published_year: Option<i32>,
    created_at: OffsetDateTime,
    updated_at: OffsetDateTime,
}

impl InventoryBookRow {
    fn into_book(self) -> Book {
        Book {
            id: BookId(self.id),
            author_id: AuthorId(self.author_id),
            title: self.title,
            isbn: self.isbn,
            published_year: self.published_year,
            created_at: self.created_at,
            updated_at: self.updated_at,
        }
    }
}

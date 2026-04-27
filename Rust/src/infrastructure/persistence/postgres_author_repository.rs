use async_trait::async_trait;
use sqlx::{FromRow, PgPool};
use time::OffsetDateTime;
use uuid::Uuid;

use crate::{
    domain::author::{Author, AuthorCommandRepository, AuthorId, AuthorQueryRepository},
    error::AppError,
};

#[derive(Clone)]
pub struct PostgresAuthorRepository {
    pool: PgPool,
}

impl PostgresAuthorRepository {
    pub fn new(pool: PgPool) -> Self {
        Self { pool }
    }
}

#[async_trait]
impl AuthorCommandRepository for PostgresAuthorRepository {
    async fn create(&self, author: Author) -> Result<Author, AppError> {
        let row = sqlx::query_as::<_, AuthorRow>(
            r#"
            INSERT INTO authors (id, name, bio, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5)
            RETURNING id, name, bio, created_at, updated_at
            "#,
        )
        .bind(author.id.0)
        .bind(&author.name)
        .bind(author.bio.as_deref().unwrap_or(""))
        .bind(author.created_at)
        .bind(author.updated_at)
        .fetch_one(&self.pool)
        .await?;

        Ok(row.into())
    }

    async fn update(&self, author: Author) -> Result<Author, AppError> {
        let row = sqlx::query_as::<_, AuthorRow>(
            r#"
            UPDATE authors
            SET name = $2, bio = $3, updated_at = $4
            WHERE id = $1
            RETURNING id, name, bio, created_at, updated_at
            "#,
        )
        .bind(author.id.0)
        .bind(&author.name)
        .bind(author.bio.as_deref().unwrap_or(""))
        .bind(author.updated_at)
        .fetch_optional(&self.pool)
        .await?;

        match row {
            Some(row) => Ok(row.into()),
            None => Err(AppError::NotFound(format!("author {}", author.id))),
        }
    }

    async fn delete(&self, author_id: AuthorId) -> Result<(), AppError> {
        let result = sqlx::query(
            r#"
            DELETE FROM authors
            WHERE id = $1
            "#,
        )
        .bind(author_id.0)
        .execute(&self.pool)
        .await?;

        if result.rows_affected() == 0 {
            return Err(AppError::NotFound(format!("author {author_id}")));
        }

        Ok(())
    }
}

#[async_trait]
impl AuthorQueryRepository for PostgresAuthorRepository {
    async fn list(&self) -> Result<Vec<Author>, AppError> {
        let rows = sqlx::query_as::<_, AuthorRow>(
            r#"
            SELECT id, name, bio, created_at, updated_at
            FROM authors
            ORDER BY id ASC
            "#,
        )
        .fetch_all(&self.pool)
        .await?;

        Ok(rows.into_iter().map(Into::into).collect())
    }

    async fn get(&self, author_id: AuthorId) -> Result<Option<Author>, AppError> {
        let row = sqlx::query_as::<_, AuthorRow>(
            r#"
            SELECT id, name, bio, created_at, updated_at
            FROM authors
            WHERE id = $1
            "#,
        )
        .bind(author_id.0)
        .fetch_optional(&self.pool)
        .await?;

        Ok(row.map(Into::into))
    }
}

#[derive(Debug, FromRow)]
struct AuthorRow {
    id: Uuid,
    name: String,
    bio: String,
    created_at: OffsetDateTime,
    updated_at: OffsetDateTime,
}

impl From<AuthorRow> for Author {
    fn from(value: AuthorRow) -> Self {
        Self {
            id: AuthorId(value.id),
            name: value.name,
            bio: (!value.bio.trim().is_empty()).then_some(value.bio),
            created_at: value.created_at,
            updated_at: value.updated_at,
        }
    }
}

use async_trait::async_trait;
use sqlx::{FromRow, PgPool};
use time::OffsetDateTime;
use uuid::Uuid;

use crate::{
    domain::{
        author::AuthorId,
        book::{Book, BookCommandRepository, BookId, BookQueryRepository},
    },
    error::AppError,
};

#[derive(Clone)]
pub struct PostgresBookRepository {
    pool: PgPool,
}

impl PostgresBookRepository {
    pub fn new(pool: PgPool) -> Self {
        Self { pool }
    }
}

#[async_trait]
impl BookCommandRepository for PostgresBookRepository {
    #[tracing::instrument(name = "postgres.books.create", skip(self, book), fields(book.id = %book.id, author.id = %book.author_id), err)]
    async fn create(&self, book: Book) -> Result<Book, AppError> {
        let row = sqlx::query_as::<_, BookRow>(
            r#"
            INSERT INTO books (id, author_id, title, description, published_year, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            RETURNING id, author_id, title, description, published_year, created_at, updated_at
            "#,
        )
        .bind(book.id.0)
        .bind(book.author_id.0)
        .bind(&book.title)
        .bind(&book.description)
        .bind(book.published_year)
        .bind(book.created_at)
        .bind(book.updated_at)
        .fetch_one(&self.pool)
        .await
        .map_err(map_database_error)?;

        Ok(row.into())
    }

    #[tracing::instrument(name = "postgres.books.update", skip(self, book), fields(book.id = %book.id, author.id = %book.author_id), err)]
    async fn update(&self, book: Book) -> Result<Book, AppError> {
        let row = sqlx::query_as::<_, BookRow>(
            r#"
            UPDATE books
            SET author_id = $2, title = $3, description = $4, published_year = $5, updated_at = $6
            WHERE id = $1
            RETURNING id, author_id, title, description, published_year, created_at, updated_at
            "#,
        )
        .bind(book.id.0)
        .bind(book.author_id.0)
        .bind(&book.title)
        .bind(&book.description)
        .bind(book.published_year)
        .bind(book.updated_at)
        .fetch_optional(&self.pool)
        .await
        .map_err(map_database_error)?;

        match row {
            Some(row) => Ok(row.into()),
            None => Err(AppError::NotFound(format!("book {}", book.id))),
        }
    }

    #[tracing::instrument(name = "postgres.books.delete", skip(self), fields(book.id = %book_id), err)]
    async fn delete(&self, book_id: BookId) -> Result<(), AppError> {
        let result = sqlx::query(
            r#"
            DELETE FROM books
            WHERE id = $1
            "#,
        )
        .bind(book_id.0)
        .execute(&self.pool)
        .await?;

        if result.rows_affected() == 0 {
            return Err(AppError::NotFound(format!("book {book_id}")));
        }

        Ok(())
    }
}

#[async_trait]
impl BookQueryRepository for PostgresBookRepository {
    #[tracing::instrument(name = "postgres.books.list", skip(self), err)]
    async fn list(&self) -> Result<Vec<Book>, AppError> {
        let rows = sqlx::query_as::<_, BookRow>(
            r#"
            SELECT id, author_id, title, description, published_year, created_at, updated_at
            FROM books
            ORDER BY title ASC
            "#,
        )
        .fetch_all(&self.pool)
        .await?;

        Ok(rows.into_iter().map(Into::into).collect())
    }

    #[tracing::instrument(name = "postgres.books.list_by_author", skip(self), fields(author.id = %author_id), err)]
    async fn list_by_author(&self, author_id: AuthorId) -> Result<Vec<Book>, AppError> {
        let rows = sqlx::query_as::<_, BookRow>(
            r#"
            SELECT id, author_id, title, description, published_year, created_at, updated_at
            FROM books
            WHERE author_id = $1
            ORDER BY title ASC
            "#,
        )
        .bind(author_id.0)
        .fetch_all(&self.pool)
        .await?;

        Ok(rows.into_iter().map(Into::into).collect())
    }

    #[tracing::instrument(name = "postgres.books.get", skip(self), fields(book.id = %book_id), err)]
    async fn get(&self, book_id: BookId) -> Result<Option<Book>, AppError> {
        let row = sqlx::query_as::<_, BookRow>(
            r#"
            SELECT id, author_id, title, description, published_year, created_at, updated_at
            FROM books
            WHERE id = $1
            "#,
        )
        .bind(book_id.0)
        .fetch_optional(&self.pool)
        .await?;

        Ok(row.map(Into::into))
    }
}

#[derive(Debug, FromRow)]
struct BookRow {
    id: Uuid,
    author_id: Uuid,
    title: String,
    description: Option<String>,
    published_year: Option<i32>,
    created_at: OffsetDateTime,
    updated_at: OffsetDateTime,
}

impl From<BookRow> for Book {
    fn from(value: BookRow) -> Self {
        Self {
            id: BookId(value.id),
            author_id: AuthorId(value.author_id),
            title: value.title,
            description: value.description,
            published_year: value.published_year,
            created_at: value.created_at,
            updated_at: value.updated_at,
        }
    }
}

fn map_database_error(error: sqlx::Error) -> AppError {
    if let sqlx::Error::Database(database_error) = &error {
        if database_error.code().as_deref() == Some("23503") {
            return AppError::Validation("author_id must reference an existing author".to_owned());
        }
    }

    AppError::Sqlx(error)
}

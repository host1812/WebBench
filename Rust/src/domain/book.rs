use std::fmt::{Display, Formatter};

use async_trait::async_trait;
use serde::{Deserialize, Serialize};
use time::OffsetDateTime;
use uuid::Uuid;

use crate::{domain::author::AuthorId, error::AppError};

pub const DEFAULT_BOOK_LIST_LIMIT: u32 = 10_000;
pub const MIN_BOOK_LIST_LIMIT: u32 = 1;
pub const MAX_BOOK_LIST_LIMIT: u32 = 100_000;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(transparent)]
pub struct BookId(pub Uuid);

impl BookId {
    pub fn new() -> Self {
        Self(Uuid::now_v7())
    }
}

impl Display for BookId {
    fn fmt(&self, f: &mut Formatter<'_>) -> std::fmt::Result {
        self.0.fmt(f)
    }
}

impl From<Uuid> for BookId {
    fn from(value: Uuid) -> Self {
        Self(value)
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct BookListLimit(u32);

impl BookListLimit {
    pub fn new(value: u32) -> Result<Self, AppError> {
        if !(MIN_BOOK_LIST_LIMIT..=MAX_BOOK_LIST_LIMIT).contains(&value) {
            return Err(AppError::Validation(format!(
                "limit must be between {MIN_BOOK_LIST_LIMIT} and {MAX_BOOK_LIST_LIMIT}"
            )));
        }

        Ok(Self(value))
    }

    pub fn value(self) -> u32 {
        self.0
    }
}

impl Default for BookListLimit {
    fn default() -> Self {
        Self(DEFAULT_BOOK_LIST_LIMIT)
    }
}

#[derive(Debug, Clone, Serialize)]
pub struct Book {
    pub id: BookId,
    pub author_id: AuthorId,
    pub title: String,
    pub isbn: String,
    pub published_year: Option<i32>,
    pub created_at: OffsetDateTime,
    pub updated_at: OffsetDateTime,
}

impl Book {
    pub fn create(
        author_id: AuthorId,
        title: String,
        isbn: String,
        published_year: Option<i32>,
    ) -> Result<Self, AppError> {
        validate_published_year(published_year)?;

        let now = OffsetDateTime::now_utc();
        Ok(Self {
            id: BookId::new(),
            author_id,
            title: normalize_required(title, "book title")?,
            isbn: normalize_required(isbn, "book isbn")?,
            published_year,
            created_at: now,
            updated_at: now,
        })
    }

    pub fn revise(
        &mut self,
        author_id: AuthorId,
        title: String,
        isbn: String,
        published_year: Option<i32>,
    ) -> Result<(), AppError> {
        validate_published_year(published_year)?;

        self.author_id = author_id;
        self.title = normalize_required(title, "book title")?;
        self.isbn = normalize_required(isbn, "book isbn")?;
        self.published_year = published_year;
        self.updated_at = OffsetDateTime::now_utc();

        Ok(())
    }
}

#[async_trait]
pub trait BookCommandRepository: Send + Sync {
    async fn create(&self, book: Book) -> Result<Book, AppError>;
    async fn update(&self, book: Book) -> Result<Book, AppError>;
    async fn delete(&self, book_id: BookId) -> Result<(), AppError>;
}

#[async_trait]
pub trait BookQueryRepository: Send + Sync {
    async fn list(&self, limit: BookListLimit) -> Result<Vec<Book>, AppError>;
    async fn list_by_author(
        &self,
        author_id: AuthorId,
        limit: BookListLimit,
    ) -> Result<Vec<Book>, AppError>;
    async fn get(&self, book_id: BookId) -> Result<Option<Book>, AppError>;
}

fn normalize_required(value: String, field_name: &str) -> Result<String, AppError> {
    let trimmed = value.trim();

    if trimmed.is_empty() {
        return Err(AppError::Validation(format!(
            "{field_name} cannot be empty"
        )));
    }

    Ok(trimmed.to_owned())
}

fn validate_published_year(value: Option<i32>) -> Result<(), AppError> {
    if let Some(year) = value {
        if !(0..=9999).contains(&year) {
            return Err(AppError::Validation(
                "published_year must be between 0 and 9999".to_owned(),
            ));
        }
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use uuid::Uuid;

    use crate::{domain::author::AuthorId, error::AppError};

    use super::{Book, BookListLimit, DEFAULT_BOOK_LIST_LIMIT};

    #[test]
    fn create_trims_title_and_isbn() {
        let author_id = author_id();
        let book = Book::create(
            author_id,
            "  The Left Hand of Darkness  ".to_owned(),
            "  9780441478125  ".to_owned(),
            Some(1969),
        )
        .expect("book should be valid");

        assert_eq!(book.author_id, author_id);
        assert_eq!(book.title, "The Left Hand of Darkness");
        assert_eq!(book.isbn, "9780441478125");
        assert_eq!(book.published_year, Some(1969));
        assert_eq!(book.created_at, book.updated_at);
    }

    #[test]
    fn create_rejects_empty_title() {
        let error = Book::create(author_id(), " ".to_owned(), "isbn".to_owned(), None)
            .expect_err("title is invalid");

        assert!(matches!(error, AppError::Validation(_)));
        assert_eq!(
            error.to_string(),
            "validation failed: book title cannot be empty"
        );
    }

    #[test]
    fn create_rejects_empty_isbn() {
        let error = Book::create(author_id(), "Book".to_owned(), " ".to_owned(), None)
            .expect_err("isbn is invalid");

        assert!(matches!(error, AppError::Validation(_)));
        assert_eq!(
            error.to_string(),
            "validation failed: book isbn cannot be empty"
        );
    }

    #[test]
    fn create_rejects_out_of_range_year() {
        let error = Book::create(
            author_id(),
            "Book".to_owned(),
            "isbn".to_owned(),
            Some(10_000),
        )
        .expect_err("year is invalid");

        assert!(matches!(error, AppError::Validation(_)));
        assert_eq!(
            error.to_string(),
            "validation failed: published_year must be between 0 and 9999"
        );
    }

    #[test]
    fn revise_updates_all_mutable_fields() {
        let original_author_id = author_id();
        let new_author_id =
            AuthorId(Uuid::parse_str("22222222-2222-2222-2222-222222222222").unwrap());
        let mut book = Book::create(
            original_author_id,
            "Old".to_owned(),
            "old-isbn".to_owned(),
            None,
        )
        .unwrap();
        let original_updated_at = book.updated_at;

        book.revise(
            new_author_id,
            "  New  ".to_owned(),
            "  new-isbn  ".to_owned(),
            Some(2024),
        )
        .unwrap();

        assert_eq!(book.author_id, new_author_id);
        assert_eq!(book.title, "New");
        assert_eq!(book.isbn, "new-isbn");
        assert_eq!(book.published_year, Some(2024));
        assert!(book.updated_at >= original_updated_at);
    }

    #[test]
    fn list_limit_defaults_to_ten_thousand() {
        assert_eq!(BookListLimit::default().value(), DEFAULT_BOOK_LIST_LIMIT);
    }

    #[test]
    fn list_limit_accepts_range_boundaries() {
        assert_eq!(BookListLimit::new(1).unwrap().value(), 1);
        assert_eq!(BookListLimit::new(100_000).unwrap().value(), 100_000);
    }

    #[test]
    fn list_limit_rejects_out_of_range_values() {
        assert!(matches!(
            BookListLimit::new(0),
            Err(AppError::Validation(_))
        ));
        assert!(matches!(
            BookListLimit::new(100_001),
            Err(AppError::Validation(_))
        ));
    }

    fn author_id() -> AuthorId {
        AuthorId(Uuid::parse_str("11111111-1111-1111-1111-111111111111").unwrap())
    }
}

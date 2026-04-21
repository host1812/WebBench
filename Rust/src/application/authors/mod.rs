pub mod commands;
pub mod queries;

use serde::{Deserialize, Serialize};
use time::OffsetDateTime;
use uuid::Uuid;

use crate::domain::{author::Author, book::Book};

#[derive(Debug, Clone, Deserialize)]
pub struct CreateAuthorInput {
    pub name: String,
    pub bio: Option<String>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct UpdateAuthorInput {
    pub name: String,
    pub bio: Option<String>,
}

#[derive(Debug, Clone, Serialize)]
pub struct AuthorDto {
    pub id: Uuid,
    pub name: String,
    pub bio: Option<String>,
    #[serde(with = "time::serde::rfc3339")]
    pub created_at: OffsetDateTime,
    #[serde(with = "time::serde::rfc3339")]
    pub updated_at: OffsetDateTime,
}

#[derive(Debug, Clone, Serialize)]
pub struct AuthorDetailsDto {
    pub author: AuthorDto,
    pub books: Vec<BookDto>,
}

#[derive(Debug, Clone, Serialize)]
pub struct BookDto {
    pub id: Uuid,
    pub author_id: Uuid,
    pub title: String,
    pub isbn: String,
    pub published_year: Option<i32>,
    #[serde(with = "time::serde::rfc3339")]
    pub created_at: OffsetDateTime,
    #[serde(with = "time::serde::rfc3339")]
    pub updated_at: OffsetDateTime,
}

impl From<Author> for AuthorDto {
    fn from(author: Author) -> Self {
        Self {
            id: author.id.0,
            name: author.name,
            bio: author.bio,
            created_at: author.created_at,
            updated_at: author.updated_at,
        }
    }
}

impl From<Book> for BookDto {
    fn from(book: Book) -> Self {
        Self {
            id: book.id.0,
            author_id: book.author_id.0,
            title: book.title,
            isbn: book.isbn,
            published_year: book.published_year,
            created_at: book.created_at,
            updated_at: book.updated_at,
        }
    }
}

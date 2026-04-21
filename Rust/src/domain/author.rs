use std::fmt::{Display, Formatter};

use async_trait::async_trait;
use serde::{Deserialize, Serialize};
use time::OffsetDateTime;
use uuid::Uuid;

use crate::error::AppError;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(transparent)]
pub struct AuthorId(pub Uuid);

impl AuthorId {
    pub fn new() -> Self {
        Self(Uuid::now_v7())
    }
}

impl Display for AuthorId {
    fn fmt(&self, f: &mut Formatter<'_>) -> std::fmt::Result {
        self.0.fmt(f)
    }
}

impl From<Uuid> for AuthorId {
    fn from(value: Uuid) -> Self {
        Self(value)
    }
}

#[derive(Debug, Clone, Serialize)]
pub struct Author {
    pub id: AuthorId,
    pub name: String,
    pub bio: Option<String>,
    pub created_at: OffsetDateTime,
    pub updated_at: OffsetDateTime,
}

impl Author {
    pub fn create(name: String, bio: Option<String>) -> Result<Self, AppError> {
        let now = OffsetDateTime::now_utc();

        Ok(Self {
            id: AuthorId::new(),
            name: normalize_required(name, "author name")?,
            bio: normalize_optional(bio),
            created_at: now,
            updated_at: now,
        })
    }

    pub fn rename(&mut self, name: String) -> Result<(), AppError> {
        self.name = normalize_required(name, "author name")?;
        self.updated_at = OffsetDateTime::now_utc();
        Ok(())
    }

    pub fn set_bio(&mut self, bio: Option<String>) {
        self.bio = normalize_optional(bio);
        self.updated_at = OffsetDateTime::now_utc();
    }
}

#[async_trait]
pub trait AuthorCommandRepository: Send + Sync {
    async fn create(&self, author: Author) -> Result<Author, AppError>;
    async fn update(&self, author: Author) -> Result<Author, AppError>;
    async fn delete(&self, author_id: AuthorId) -> Result<(), AppError>;
}

#[async_trait]
pub trait AuthorQueryRepository: Send + Sync {
    async fn list(&self) -> Result<Vec<Author>, AppError>;
    async fn get(&self, author_id: AuthorId) -> Result<Option<Author>, AppError>;
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

fn normalize_optional(value: Option<String>) -> Option<String> {
    value.and_then(|inner| {
        let trimmed = inner.trim();
        (!trimmed.is_empty()).then(|| trimmed.to_owned())
    })
}

#[cfg(test)]
mod tests {
    use crate::error::AppError;

    use super::Author;

    #[test]
    fn create_trims_name_and_empty_bio() {
        let author = Author::create("  Ursula Le Guin  ".to_owned(), Some("   ".to_owned()))
            .expect("author should be valid");

        assert_eq!(author.name, "Ursula Le Guin");
        assert_eq!(author.bio, None);
        assert_eq!(author.created_at, author.updated_at);
    }

    #[test]
    fn create_rejects_empty_name() {
        let error = Author::create("   ".to_owned(), None).expect_err("name should be invalid");

        assert!(matches!(error, AppError::Validation(_)));
        assert_eq!(
            error.to_string(),
            "validation failed: author name cannot be empty"
        );
    }

    #[test]
    fn rename_updates_name_and_timestamp() {
        let mut author = Author::create("Old Name".to_owned(), None).unwrap();
        let original_updated_at = author.updated_at;

        author.rename("  New Name  ".to_owned()).unwrap();

        assert_eq!(author.name, "New Name");
        assert!(author.updated_at >= original_updated_at);
    }

    #[test]
    fn set_bio_trims_and_removes_empty_values() {
        let mut author = Author::create("Author".to_owned(), None).unwrap();

        author.set_bio(Some("  Biography  ".to_owned()));
        assert_eq!(author.bio.as_deref(), Some("Biography"));

        author.set_bio(Some("   ".to_owned()));
        assert_eq!(author.bio, None);
    }
}

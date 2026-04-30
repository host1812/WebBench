use std::fmt::{Display, Formatter};

use async_trait::async_trait;
use serde::{Deserialize, Serialize};
use time::OffsetDateTime;
use uuid::Uuid;

use crate::{domain::book::Book, error::AppError};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(transparent)]
pub struct StoreId(pub Uuid);

impl StoreId {
    pub fn new() -> Self {
        Self(Uuid::now_v7())
    }
}

impl Display for StoreId {
    fn fmt(&self, f: &mut Formatter<'_>) -> std::fmt::Result {
        self.0.fmt(f)
    }
}

impl From<Uuid> for StoreId {
    fn from(value: Uuid) -> Self {
        Self(value)
    }
}

#[derive(Debug, Clone, Serialize)]
pub struct Store {
    pub id: StoreId,
    pub name: String,
    pub description: String,
    pub address: String,
    pub phone_number: String,
    pub web_site: Option<String>,
    pub inventory: Vec<Book>,
    pub created_at: OffsetDateTime,
    pub updated_at: OffsetDateTime,
}

impl Store {
    pub fn create(
        name: String,
        description: String,
        address: String,
        phone_number: String,
        web_site: Option<String>,
        inventory: Vec<Book>,
    ) -> Result<Self, AppError> {
        let now = OffsetDateTime::now_utc();

        Ok(Self {
            id: StoreId::new(),
            name: normalize_required(name, "store name")?,
            description: normalize_required(description, "store description")?,
            address: normalize_required(address, "store address")?,
            phone_number: normalize_required(phone_number, "store phone_number")?,
            web_site: normalize_optional(web_site),
            inventory,
            created_at: now,
            updated_at: now,
        })
    }
}

#[async_trait]
pub trait StoreQueryRepository: Send + Sync {
    async fn list(&self) -> Result<Vec<Store>, AppError>;
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
    use crate::{
        domain::{
            author::AuthorId,
            book::{Book, BookId},
        },
        error::AppError,
    };
    use time::OffsetDateTime;
    use uuid::Uuid;

    use super::Store;

    #[test]
    fn create_trims_required_fields_and_optional_web_site() {
        let store = Store::create(
            "  City Books  ".to_owned(),
            "  Neighborhood bookstore  ".to_owned(),
            "  123 Main St  ".to_owned(),
            "  555-0100  ".to_owned(),
            Some("  https://city-books.example  ".to_owned()),
            vec![book()],
        )
        .expect("store should be valid");

        assert_eq!(store.name, "City Books");
        assert_eq!(store.description, "Neighborhood bookstore");
        assert_eq!(store.address, "123 Main St");
        assert_eq!(store.phone_number, "555-0100");
        assert_eq!(
            store.web_site.as_deref(),
            Some("https://city-books.example")
        );
        assert_eq!(store.inventory.len(), 1);
        assert_eq!(store.created_at, store.updated_at);
    }

    #[test]
    fn create_removes_empty_web_site() {
        let store = Store::create(
            "Store".to_owned(),
            "Description".to_owned(),
            "Address".to_owned(),
            "Phone".to_owned(),
            Some("   ".to_owned()),
            vec![],
        )
        .expect("store should be valid");

        assert_eq!(store.web_site, None);
    }

    #[test]
    fn create_rejects_empty_name() {
        let error = Store::create(
            "   ".to_owned(),
            "Description".to_owned(),
            "Address".to_owned(),
            "Phone".to_owned(),
            None,
            vec![],
        )
        .expect_err("name is invalid");

        assert!(matches!(error, AppError::Validation(_)));
        assert_eq!(
            error.to_string(),
            "validation failed: store name cannot be empty"
        );
    }

    fn book() -> Book {
        Book {
            id: BookId(Uuid::parse_str("22222222-2222-2222-2222-222222222222").unwrap()),
            author_id: AuthorId(Uuid::parse_str("11111111-1111-1111-1111-111111111111").unwrap()),
            title: "Book".to_owned(),
            isbn: "isbn".to_owned(),
            published_year: None,
            created_at: OffsetDateTime::UNIX_EPOCH,
            updated_at: OffsetDateTime::UNIX_EPOCH,
        }
    }
}

use std::sync::Arc;

use async_trait::async_trait;

use crate::{application::stores::StoreDto, domain::store::StoreQueryRepository, error::AppError};

#[async_trait]
pub trait StoreQueryService: Send + Sync {
    async fn list_stores(&self) -> Result<Vec<StoreDto>, AppError>;
}

pub struct StoreQueryHandler {
    stores: Arc<dyn StoreQueryRepository>,
}

impl StoreQueryHandler {
    pub fn new(stores: Arc<dyn StoreQueryRepository>) -> Self {
        Self { stores }
    }
}

#[async_trait]
impl StoreQueryService for StoreQueryHandler {
    async fn list_stores(&self) -> Result<Vec<StoreDto>, AppError> {
        let stores = self.stores.list().await?;
        Ok(stores.into_iter().map(Into::into).collect())
    }
}

#[cfg(test)]
mod tests {
    use std::sync::Arc;

    use async_trait::async_trait;
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

    use super::{StoreQueryHandler, StoreQueryService};

    #[tokio::test]
    async fn list_stores_maps_domain_stores_to_dtos() {
        let handler = StoreQueryHandler::new(Arc::new(FakeStores::new(vec![store()])));

        let stores = handler.list_stores().await.unwrap();

        assert_eq!(stores.len(), 1);
        assert_eq!(stores[0].name, "City Books");
        assert_eq!(stores[0].inventory.len(), 1);
        assert_eq!(stores[0].inventory[0].title, "Book One");
    }

    struct FakeStores {
        stores: Vec<Store>,
    }

    impl FakeStores {
        fn new(stores: Vec<Store>) -> Self {
            Self { stores }
        }
    }

    #[async_trait]
    impl StoreQueryRepository for FakeStores {
        async fn list(&self) -> Result<Vec<Store>, AppError> {
            Ok(self.stores.clone())
        }
    }

    fn store() -> Store {
        Store {
            id: StoreId(Uuid::parse_str("33333333-3333-3333-3333-333333333333").unwrap()),
            name: "City Books".to_owned(),
            description: "Neighborhood bookstore".to_owned(),
            address: "123 Main St".to_owned(),
            phone_number: "555-0100".to_owned(),
            web_site: Some("https://city-books.example".to_owned()),
            inventory: vec![book()],
            created_at: OffsetDateTime::UNIX_EPOCH,
            updated_at: OffsetDateTime::UNIX_EPOCH,
        }
    }

    fn book() -> Book {
        Book {
            id: BookId(Uuid::parse_str("22222222-2222-2222-2222-222222222222").unwrap()),
            author_id: AuthorId(Uuid::parse_str("11111111-1111-1111-1111-111111111111").unwrap()),
            title: "Book One".to_owned(),
            isbn: "isbn".to_owned(),
            published_year: Some(2024),
            created_at: OffsetDateTime::UNIX_EPOCH,
            updated_at: OffsetDateTime::UNIX_EPOCH,
        }
    }
}

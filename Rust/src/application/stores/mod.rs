pub mod queries;

use serde::Serialize;
use time::OffsetDateTime;
use uuid::Uuid;

use crate::{application::authors::BookDto, domain::store::Store};

#[derive(Debug, Clone, Serialize)]
pub struct StoreDto {
    pub id: Uuid,
    pub name: String,
    pub description: String,
    pub address: String,
    pub phone_number: String,
    pub web_site: Option<String>,
    pub inventory: Vec<BookDto>,
    #[serde(with = "time::serde::rfc3339")]
    pub created_at: OffsetDateTime,
    #[serde(with = "time::serde::rfc3339")]
    pub updated_at: OffsetDateTime,
}

impl From<Store> for StoreDto {
    fn from(store: Store) -> Self {
        Self {
            id: store.id.0,
            name: store.name,
            description: store.description,
            address: store.address,
            phone_number: store.phone_number,
            web_site: store.web_site,
            inventory: store.inventory.into_iter().map(BookDto::from).collect(),
            created_at: store.created_at,
            updated_at: store.updated_at,
        }
    }
}

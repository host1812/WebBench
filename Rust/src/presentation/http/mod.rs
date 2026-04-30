pub mod router;

use std::sync::Arc;

use crate::application::{
    authors::{commands::AuthorCommandService, queries::AuthorQueryService},
    books::{commands::BookCommandService, queries::BookQueryService},
    health::HealthQueryService,
    stores::queries::StoreQueryService,
};

#[derive(Clone)]
pub struct AppState {
    pub health_queries: Arc<dyn HealthQueryService>,
    pub author_commands: Arc<dyn AuthorCommandService>,
    pub author_queries: Arc<dyn AuthorQueryService>,
    pub book_commands: Arc<dyn BookCommandService>,
    pub book_queries: Arc<dyn BookQueryService>,
    pub store_queries: Arc<dyn StoreQueryService>,
}

impl AppState {
    pub fn new(
        health_queries: Arc<dyn HealthQueryService>,
        author_commands: Arc<dyn AuthorCommandService>,
        author_queries: Arc<dyn AuthorQueryService>,
        book_commands: Arc<dyn BookCommandService>,
        book_queries: Arc<dyn BookQueryService>,
        store_queries: Arc<dyn StoreQueryService>,
    ) -> Self {
        Self {
            health_queries,
            author_commands,
            author_queries,
            book_commands,
            book_queries,
            store_queries,
        }
    }
}

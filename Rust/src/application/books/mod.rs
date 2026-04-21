pub mod commands;
pub mod queries;

use serde::Deserialize;

#[derive(Debug, Clone, Deserialize)]
pub struct CreateBookInput {
    pub author_id: uuid::Uuid,
    pub title: String,
    pub isbn: String,
    pub published_year: Option<i32>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct UpdateBookInput {
    pub author_id: uuid::Uuid,
    pub title: String,
    pub isbn: String,
    pub published_year: Option<i32>,
}

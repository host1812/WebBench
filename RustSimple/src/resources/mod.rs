pub mod authors;
pub mod books;
pub mod stores;

use axum::Router;

use crate::state::AppState;

pub fn router() -> Router<AppState> {
    Router::new()
        .nest("/authors", authors::router())
        .nest("/books", books::router())
        .nest("/stores", stores::router())
}

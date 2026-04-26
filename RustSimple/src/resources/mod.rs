pub mod authors;
pub mod books;

use axum::Router;

use crate::state::AppState;

pub fn router() -> Router<AppState> {
    Router::new()
        .nest("/authors", authors::router())
        .nest("/books", books::router())
}

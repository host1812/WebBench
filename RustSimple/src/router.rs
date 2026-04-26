use axum::{Json, Router, routing::get};
use serde::Serialize;
use tower_http::{cors::CorsLayer, trace::TraceLayer};

use crate::{resources, state::AppState};

pub fn build(state: AppState) -> Router {
    Router::new()
        .route("/health", get(health))
        .nest("/api/v1", resources::router())
        .layer(CorsLayer::permissive())
        .layer(TraceLayer::new_for_http())
        .with_state(state)
}

#[derive(Debug, Serialize)]
struct HealthResponse {
    status: &'static str,
}

async fn health() -> Json<HealthResponse> {
    Json(HealthResponse { status: "ok" })
}

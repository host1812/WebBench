use rust_backend_service::{migrate, run};

#[tokio::main]
async fn main() -> Result<(), rust_backend_service::error::AppError> {
    match std::env::args().nth(1).as_deref() {
        Some("migrate") => migrate().await,
        Some("serve") | Some("server") | None => run().await,
        Some(command) => Err(rust_backend_service::error::AppError::Validation(format!(
            "unknown command '{command}'. expected 'serve' or 'migrate'"
        ))),
    }
}

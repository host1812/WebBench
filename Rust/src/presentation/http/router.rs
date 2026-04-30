use axum::{
    Json, Router,
    extract::{Path, Query, State},
    http::StatusCode,
    response::IntoResponse,
    routing::get,
};
use serde::Deserialize;
use time::OffsetDateTime;
use tokio::time::{Duration, timeout};
use uuid::Uuid;

use crate::{
    application::{
        authors::{CreateAuthorInput, UpdateAuthorInput},
        books::{CreateBookInput, UpdateBookInput},
        health::{DependencyHealthDto, HealthChecksDto, HealthReportDto, HealthStatus},
    },
    domain::{
        author::AuthorId,
        book::{BookId, BookListLimit},
    },
    error::AppError,
    presentation::http::AppState,
};

pub fn build_router(state: AppState) -> Router {
    Router::new()
        .route("/health", get(health))
        .nest("/api/v1", resource_routes())
        .merge(resource_routes())
        .with_state(state)
}

fn resource_routes() -> Router<AppState> {
    Router::new()
        .route("/authors", get(list_authors).post(create_author))
        .route(
            "/authors/{author_id}",
            get(get_author).put(update_author).delete(delete_author),
        )
        .route("/authors/{author_id}/books", get(list_author_books))
        .route("/books", get(list_books).post(create_book))
        .route(
            "/books/{book_id}",
            get(get_book).put(update_book).delete(delete_book),
        )
        .route("/stores", get(list_stores))
}

async fn health(State(state): State<AppState>) -> impl IntoResponse {
    let report = match timeout(health_timeout(), state.health_queries.check_health()).await {
        Ok(report) => report,
        Err(_) => timed_out_health_report(),
    };
    let status = if report.is_healthy() {
        StatusCode::OK
    } else {
        StatusCode::SERVICE_UNAVAILABLE
    };

    (status, Json(report))
}

async fn create_author(
    State(state): State<AppState>,
    Json(input): Json<CreateAuthorInput>,
) -> Result<impl IntoResponse, AppError> {
    let author = state.author_commands.create_author(input).await?;
    Ok((StatusCode::CREATED, Json(author)))
}

async fn list_authors(State(state): State<AppState>) -> Result<impl IntoResponse, AppError> {
    let authors = state.author_queries.list_authors().await?;
    Ok(Json(authors))
}

async fn get_author(
    State(state): State<AppState>,
    Path(author_id): Path<Uuid>,
) -> Result<impl IntoResponse, AppError> {
    let author = state.author_queries.get_author(author_id.into()).await?;
    Ok(Json(author))
}

async fn update_author(
    State(state): State<AppState>,
    Path(author_id): Path<Uuid>,
    Json(input): Json<UpdateAuthorInput>,
) -> Result<impl IntoResponse, AppError> {
    let author = state
        .author_commands
        .update_author(AuthorId::from(author_id), input)
        .await?;
    Ok(Json(author))
}

async fn delete_author(
    State(state): State<AppState>,
    Path(author_id): Path<Uuid>,
) -> Result<impl IntoResponse, AppError> {
    state
        .author_commands
        .delete_author(author_id.into())
        .await?;
    Ok(StatusCode::NO_CONTENT)
}

async fn list_author_books(
    State(state): State<AppState>,
    Path(author_id): Path<Uuid>,
    Query(params): Query<BookListParams>,
) -> Result<impl IntoResponse, AppError> {
    let limit = params.limit()?;
    let books = state
        .author_queries
        .list_books_for_author(author_id.into(), limit)
        .await?;
    Ok(Json(books))
}

async fn create_book(
    State(state): State<AppState>,
    Json(input): Json<CreateBookInput>,
) -> Result<impl IntoResponse, AppError> {
    let book = state.book_commands.create_book(input).await?;
    Ok((StatusCode::CREATED, Json(book)))
}

async fn list_books(
    State(state): State<AppState>,
    Query(params): Query<BookListParams>,
) -> Result<impl IntoResponse, AppError> {
    let books = state.book_queries.list_books(params.limit()?).await?;
    Ok(Json(books))
}

async fn get_book(
    State(state): State<AppState>,
    Path(book_id): Path<Uuid>,
) -> Result<impl IntoResponse, AppError> {
    let book = state.book_queries.get_book(book_id.into()).await?;
    Ok(Json(book))
}

async fn update_book(
    State(state): State<AppState>,
    Path(book_id): Path<Uuid>,
    Json(input): Json<UpdateBookInput>,
) -> Result<impl IntoResponse, AppError> {
    let book = state
        .book_commands
        .update_book(BookId::from(book_id), input)
        .await?;
    Ok(Json(book))
}

async fn delete_book(
    State(state): State<AppState>,
    Path(book_id): Path<Uuid>,
) -> Result<impl IntoResponse, AppError> {
    state.book_commands.delete_book(book_id.into()).await?;
    Ok(StatusCode::NO_CONTENT)
}

async fn list_stores(State(state): State<AppState>) -> Result<impl IntoResponse, AppError> {
    let stores = state.store_queries.list_stores().await?;
    Ok(Json(stores))
}

fn health_timeout() -> Duration {
    Duration::from_secs(2)
}

fn timed_out_health_report() -> HealthReportDto {
    HealthReportDto {
        service: "rust_backend_service".to_owned(),
        status: HealthStatus::Unhealthy,
        checks: HealthChecksDto {
            database: DependencyHealthDto::unhealthy("database health check timed out"),
        },
        timestamp: OffsetDateTime::now_utc(),
    }
}

#[derive(Debug, Default, Deserialize)]
struct BookListParams {
    limit: Option<u32>,
}

impl BookListParams {
    fn limit(self) -> Result<BookListLimit, AppError> {
        match self.limit {
            Some(limit) => BookListLimit::new(limit),
            None => Ok(BookListLimit::default()),
        }
    }
}

#[cfg(test)]
mod tests {
    use std::{
        sync::{Arc, Mutex},
        vec,
    };

    use async_trait::async_trait;
    use axum::{
        Router,
        body::{Body, to_bytes},
        http::{Method, Request, StatusCode, header},
        response::Response,
    };
    use serde_json::{Value, json};
    use time::OffsetDateTime;
    use tokio::time::{Duration, sleep};
    use tower::ServiceExt;
    use uuid::Uuid;

    use crate::{
        application::{
            authors::{
                AuthorDetailsDto, AuthorDto, BookDto, CreateAuthorInput, UpdateAuthorInput,
                commands::AuthorCommandService, queries::AuthorQueryService,
            },
            books::{
                CreateBookInput, UpdateBookInput, commands::BookCommandService,
                queries::BookQueryService,
            },
            health::{
                DependencyHealthDto, HealthChecksDto, HealthQueryService, HealthReportDto,
                HealthStatus,
            },
            stores::{StoreDto, queries::StoreQueryService},
        },
        domain::{
            author::AuthorId,
            book::{BookId, BookListLimit},
        },
        error::AppError,
        presentation::http::{AppState, router::build_router},
    };

    #[tokio::test]
    async fn health_route_returns_healthy_report() {
        let app = test_app(Calls::default());

        let response = app
            .oneshot(empty_request(Method::GET, "/health"))
            .await
            .unwrap();

        assert_eq!(response.status(), StatusCode::OK);

        let body = response_json(response).await;
        assert_eq!(body["service"], json!("rust_backend_service"));
        assert_eq!(body["status"], json!("healthy"));
        assert_eq!(body["checks"]["database"]["status"], json!("healthy"));
    }

    #[tokio::test]
    async fn health_route_returns_unavailable_when_database_is_unhealthy() {
        let app = test_app_with_health(
            Calls::default(),
            DependencyHealthDto::unhealthy("database health check failed"),
        );

        let response = app
            .oneshot(empty_request(Method::GET, "/health"))
            .await
            .unwrap();

        assert_eq!(response.status(), StatusCode::SERVICE_UNAVAILABLE);

        let body = response_json(response).await;
        assert_eq!(body["status"], json!("unhealthy"));
        assert_eq!(body["checks"]["database"]["status"], json!("unhealthy"));
    }

    #[tokio::test]
    async fn health_route_times_out_after_two_seconds() {
        let app = test_app_with_slow_health(
            Calls::default(),
            super::health_timeout() + Duration::from_millis(50),
        );

        let response = app
            .oneshot(empty_request(Method::GET, "/health"))
            .await
            .unwrap();

        assert_eq!(response.status(), StatusCode::SERVICE_UNAVAILABLE);

        let body = response_json(response).await;
        assert_eq!(body["status"], json!("unhealthy"));
        assert_eq!(
            body["checks"]["database"]["message"],
            json!("database health check timed out")
        );
    }

    #[tokio::test]
    async fn author_routes_call_injected_services_and_return_json() {
        assert_author_routes_call_injected_services_and_return_json("").await;
    }

    #[tokio::test]
    async fn versioned_author_routes_call_injected_services_and_return_json() {
        assert_author_routes_call_injected_services_and_return_json("/api/v1").await;
    }

    async fn assert_author_routes_call_injected_services_and_return_json(prefix: &str) {
        let calls = Calls::default();
        let app = test_app(calls.clone());
        let author_id = author_id();

        let create_response = app
            .clone()
            .oneshot(json_request(
                Method::POST,
                &format!("{prefix}/authors"),
                json!({ "name": "Octavia Butler", "bio": "Speculative fiction" }),
            ))
            .await
            .unwrap();
        assert_eq!(create_response.status(), StatusCode::CREATED);
        assert_eq!(
            response_json(create_response).await["name"],
            json!("Octavia Butler")
        );

        let list_response = app
            .clone()
            .oneshot(empty_request(Method::GET, &format!("{prefix}/authors")))
            .await
            .unwrap();
        assert_eq!(list_response.status(), StatusCode::OK);
        assert_eq!(
            response_json(list_response).await[0]["name"],
            json!("Listed Author")
        );

        let get_response = app
            .clone()
            .oneshot(empty_request(
                Method::GET,
                &format!("{prefix}/authors/{author_id}"),
            ))
            .await
            .unwrap();
        assert_eq!(get_response.status(), StatusCode::OK);
        assert_eq!(
            response_json(get_response).await["author"]["id"],
            json!(author_id.to_string())
        );

        let update_response = app
            .clone()
            .oneshot(json_request(
                Method::PUT,
                &format!("{prefix}/authors/{author_id}"),
                json!({ "name": "Updated Author", "bio": null }),
            ))
            .await
            .unwrap();
        assert_eq!(update_response.status(), StatusCode::OK);
        assert_eq!(
            response_json(update_response).await["name"],
            json!("Updated Author")
        );

        let books_response = app
            .clone()
            .oneshot(empty_request(
                Method::GET,
                &format!("{prefix}/authors/{author_id}/books"),
            ))
            .await
            .unwrap();
        assert_eq!(books_response.status(), StatusCode::OK);
        assert_eq!(
            response_json(books_response).await[0]["author_id"],
            json!(author_id.to_string())
        );

        let delete_response = app
            .oneshot(empty_request(
                Method::DELETE,
                &format!("{prefix}/authors/{author_id}"),
            ))
            .await
            .unwrap();
        assert_eq!(delete_response.status(), StatusCode::NO_CONTENT);

        assert_eq!(
            calls.snapshot(),
            vec![
                "author_commands.create_author:Octavia Butler".to_owned(),
                "author_queries.list_authors".to_owned(),
                format!("author_queries.get_author:{author_id}"),
                format!("author_commands.update_author:{author_id}:Updated Author"),
                format!("author_queries.list_books_for_author:{author_id}:10000"),
                format!("author_commands.delete_author:{author_id}"),
            ]
        );
    }

    #[tokio::test]
    async fn book_routes_call_injected_services_and_return_json() {
        assert_book_routes_call_injected_services_and_return_json("").await;
    }

    #[tokio::test]
    async fn versioned_book_routes_call_injected_services_and_return_json() {
        assert_book_routes_call_injected_services_and_return_json("/api/v1").await;
    }

    async fn assert_book_routes_call_injected_services_and_return_json(prefix: &str) {
        let calls = Calls::default();
        let app = test_app(calls.clone());
        let author_id = author_id();
        let book_id = book_id();

        let create_response = app
            .clone()
            .oneshot(json_request(
                Method::POST,
                &format!("{prefix}/books"),
                json!({
                    "author_id": author_id,
                    "title": "Kindred",
                    "isbn": "9780807083697",
                    "published_year": 1979
                }),
            ))
            .await
            .unwrap();
        assert_eq!(create_response.status(), StatusCode::CREATED);
        assert_eq!(
            response_json(create_response).await["title"],
            json!("Kindred")
        );

        let list_response = app
            .clone()
            .oneshot(empty_request(Method::GET, &format!("{prefix}/books")))
            .await
            .unwrap();
        assert_eq!(list_response.status(), StatusCode::OK);
        assert_eq!(
            response_json(list_response).await[0]["title"],
            json!("Listed Book")
        );

        let get_response = app
            .clone()
            .oneshot(empty_request(
                Method::GET,
                &format!("{prefix}/books/{book_id}"),
            ))
            .await
            .unwrap();
        assert_eq!(get_response.status(), StatusCode::OK);
        assert_eq!(
            response_json(get_response).await["id"],
            json!(book_id.to_string())
        );

        let update_response = app
            .clone()
            .oneshot(json_request(
                Method::PUT,
                &format!("{prefix}/books/{book_id}"),
                json!({
                    "author_id": author_id,
                    "title": "Updated Book",
                    "isbn": "updated-isbn",
                    "published_year": 1980
                }),
            ))
            .await
            .unwrap();
        assert_eq!(update_response.status(), StatusCode::OK);
        assert_eq!(
            response_json(update_response).await["title"],
            json!("Updated Book")
        );

        let delete_response = app
            .oneshot(empty_request(
                Method::DELETE,
                &format!("{prefix}/books/{book_id}"),
            ))
            .await
            .unwrap();
        assert_eq!(delete_response.status(), StatusCode::NO_CONTENT);

        assert_eq!(
            calls.snapshot(),
            vec![
                "book_commands.create_book:Kindred".to_owned(),
                "book_queries.list_books:10000".to_owned(),
                format!("book_queries.get_book:{book_id}"),
                format!("book_commands.update_book:{book_id}:Updated Book"),
                format!("book_commands.delete_book:{book_id}"),
            ]
        );
    }

    #[tokio::test]
    async fn store_routes_call_injected_services_and_return_json() {
        assert_store_routes_call_injected_services_and_return_json("").await;
    }

    #[tokio::test]
    async fn versioned_store_routes_call_injected_services_and_return_json() {
        assert_store_routes_call_injected_services_and_return_json("/api/v1").await;
    }

    async fn assert_store_routes_call_injected_services_and_return_json(prefix: &str) {
        let calls = Calls::default();
        let app = test_app(calls.clone());

        let list_response = app
            .oneshot(empty_request(Method::GET, &format!("{prefix}/stores")))
            .await
            .unwrap();

        assert_eq!(list_response.status(), StatusCode::OK);
        let body = response_json(list_response).await;
        assert_eq!(body[0]["name"], json!("Listed Store"));
        assert_eq!(body[0]["inventory"][0]["title"], json!("Listed Book"));

        assert_eq!(calls.snapshot(), vec!["store_queries.list_stores"]);
    }

    #[tokio::test]
    async fn book_list_route_passes_requested_limit() {
        let calls = Calls::default();
        let app = test_app(calls.clone());

        let response = app
            .oneshot(empty_request(Method::GET, "/api/v1/books?limit=25"))
            .await
            .unwrap();

        assert_eq!(response.status(), StatusCode::OK);
        assert_eq!(calls.snapshot(), vec!["book_queries.list_books:25"]);
    }

    #[tokio::test]
    async fn author_books_route_passes_requested_limit() {
        let calls = Calls::default();
        let app = test_app(calls.clone());
        let author_id = author_id();

        let response = app
            .oneshot(empty_request(
                Method::GET,
                &format!("/api/v1/authors/{author_id}/books?limit=25"),
            ))
            .await
            .unwrap();

        assert_eq!(response.status(), StatusCode::OK);
        assert_eq!(
            calls.snapshot(),
            vec![format!(
                "author_queries.list_books_for_author:{author_id}:25"
            )]
        );
    }

    #[tokio::test]
    async fn book_list_route_rejects_limit_below_minimum() {
        let app = test_app(Calls::default());

        let response = app
            .oneshot(empty_request(Method::GET, "/api/v1/books?limit=0"))
            .await
            .unwrap();

        assert_eq!(response.status(), StatusCode::BAD_REQUEST);
        assert!(
            response_json(response).await["message"]
                .as_str()
                .unwrap()
                .contains("limit must be between 1 and 100000")
        );
    }

    #[tokio::test]
    async fn book_list_route_rejects_limit_above_maximum() {
        let app = test_app(Calls::default());

        let response = app
            .oneshot(empty_request(Method::GET, "/api/v1/books?limit=100001"))
            .await
            .unwrap();

        assert_eq!(response.status(), StatusCode::BAD_REQUEST);
        assert!(
            response_json(response).await["message"]
                .as_str()
                .unwrap()
                .contains("limit must be between 1 and 100000")
        );
    }

    fn test_app(calls: Calls) -> Router {
        test_app_with_health(calls, DependencyHealthDto::healthy())
    }

    fn test_app_with_health(calls: Calls, database_health: DependencyHealthDto) -> Router {
        test_app_with_health_service(
            calls,
            Arc::new(MockHealthQueryService::new(database_health)),
        )
    }

    fn test_app_with_slow_health(calls: Calls, delay: Duration) -> Router {
        test_app_with_health_service(
            calls,
            Arc::new(MockHealthQueryService::with_delay(
                DependencyHealthDto::healthy(),
                delay,
            )),
        )
    }

    fn test_app_with_health_service(
        calls: Calls,
        health_queries: Arc<dyn HealthQueryService>,
    ) -> Router {
        let author_commands = Arc::new(MockAuthorCommandService {
            calls: calls.clone(),
        });
        let author_queries = Arc::new(MockAuthorQueryService {
            calls: calls.clone(),
        });
        let book_commands = Arc::new(MockBookCommandService {
            calls: calls.clone(),
        });
        let book_queries = Arc::new(MockBookQueryService {
            calls: calls.clone(),
        });
        let store_queries = Arc::new(MockStoreQueryService { calls });

        build_router(AppState::new(
            health_queries,
            author_commands,
            author_queries,
            book_commands,
            book_queries,
            store_queries,
        ))
    }

    fn json_request(method: Method, uri: &str, body: Value) -> Request<Body> {
        Request::builder()
            .method(method)
            .uri(uri)
            .header(header::CONTENT_TYPE, "application/json")
            .body(Body::from(body.to_string()))
            .unwrap()
    }

    fn empty_request(method: Method, uri: &str) -> Request<Body> {
        Request::builder()
            .method(method)
            .uri(uri)
            .body(Body::empty())
            .unwrap()
    }

    async fn response_json(response: Response) -> Value {
        let body = to_bytes(response.into_body(), usize::MAX).await.unwrap();
        serde_json::from_slice(&body).unwrap()
    }

    #[derive(Clone, Default)]
    struct Calls {
        entries: Arc<Mutex<Vec<String>>>,
    }

    impl Calls {
        fn record(&self, value: impl Into<String>) {
            self.entries.lock().unwrap().push(value.into());
        }

        fn snapshot(&self) -> Vec<String> {
            self.entries.lock().unwrap().clone()
        }
    }

    struct MockHealthQueryService {
        database_health: DependencyHealthDto,
        delay: Duration,
    }

    impl MockHealthQueryService {
        fn new(database_health: DependencyHealthDto) -> Self {
            Self {
                database_health,
                delay: Duration::ZERO,
            }
        }

        fn with_delay(database_health: DependencyHealthDto, delay: Duration) -> Self {
            Self {
                database_health,
                delay,
            }
        }
    }

    #[async_trait]
    impl HealthQueryService for MockHealthQueryService {
        async fn check_health(&self) -> HealthReportDto {
            if !self.delay.is_zero() {
                sleep(self.delay).await;
            }

            let status = if self.database_health.status == HealthStatus::Healthy {
                HealthStatus::Healthy
            } else {
                HealthStatus::Unhealthy
            };

            HealthReportDto {
                service: "rust_backend_service".to_owned(),
                status,
                checks: HealthChecksDto {
                    database: self.database_health.clone(),
                },
                timestamp: timestamp(),
            }
        }
    }

    struct MockAuthorCommandService {
        calls: Calls,
    }

    #[async_trait]
    impl AuthorCommandService for MockAuthorCommandService {
        async fn create_author(&self, input: CreateAuthorInput) -> Result<AuthorDto, AppError> {
            self.calls
                .record(format!("author_commands.create_author:{}", input.name));
            Ok(author_dto(author_id(), &input.name, input.bio))
        }

        async fn update_author(
            &self,
            author_id: AuthorId,
            input: UpdateAuthorInput,
        ) -> Result<AuthorDto, AppError> {
            self.calls.record(format!(
                "author_commands.update_author:{}:{}",
                author_id, input.name
            ));
            Ok(author_dto(author_id.0, &input.name, input.bio))
        }

        async fn delete_author(&self, author_id: AuthorId) -> Result<(), AppError> {
            self.calls
                .record(format!("author_commands.delete_author:{author_id}"));
            Ok(())
        }
    }

    struct MockAuthorQueryService {
        calls: Calls,
    }

    #[async_trait]
    impl AuthorQueryService for MockAuthorQueryService {
        async fn list_authors(&self) -> Result<Vec<AuthorDto>, AppError> {
            self.calls.record("author_queries.list_authors");
            Ok(vec![author_dto(
                author_id(),
                "Listed Author",
                Some("From fake query service".to_owned()),
            )])
        }

        async fn get_author(&self, author_id: AuthorId) -> Result<AuthorDetailsDto, AppError> {
            self.calls
                .record(format!("author_queries.get_author:{author_id}"));
            Ok(AuthorDetailsDto {
                author: author_dto(author_id.0, "Detailed Author", None),
                books: vec![book_dto(
                    book_id(),
                    author_id.0,
                    "Book For Author",
                    "author-book-isbn",
                    None,
                )],
            })
        }

        async fn list_books_for_author(
            &self,
            author_id: AuthorId,
            limit: BookListLimit,
        ) -> Result<Vec<BookDto>, AppError> {
            self.calls.record(format!(
                "author_queries.list_books_for_author:{author_id}:{}",
                limit.value()
            ));
            Ok(vec![book_dto(
                book_id(),
                author_id.0,
                "Book For Author",
                "author-book-isbn",
                None,
            )])
        }
    }

    struct MockBookCommandService {
        calls: Calls,
    }

    #[async_trait]
    impl BookCommandService for MockBookCommandService {
        async fn create_book(&self, input: CreateBookInput) -> Result<BookDto, AppError> {
            self.calls
                .record(format!("book_commands.create_book:{}", input.title));
            Ok(book_dto(
                book_id(),
                input.author_id,
                &input.title,
                &input.isbn,
                input.published_year,
            ))
        }

        async fn update_book(
            &self,
            book_id: BookId,
            input: UpdateBookInput,
        ) -> Result<BookDto, AppError> {
            self.calls.record(format!(
                "book_commands.update_book:{book_id}:{}",
                input.title
            ));
            Ok(book_dto(
                book_id.0,
                input.author_id,
                &input.title,
                &input.isbn,
                input.published_year,
            ))
        }

        async fn delete_book(&self, book_id: BookId) -> Result<(), AppError> {
            self.calls
                .record(format!("book_commands.delete_book:{book_id}"));
            Ok(())
        }
    }

    struct MockBookQueryService {
        calls: Calls,
    }

    #[async_trait]
    impl BookQueryService for MockBookQueryService {
        async fn list_books(&self, limit: BookListLimit) -> Result<Vec<BookDto>, AppError> {
            self.calls
                .record(format!("book_queries.list_books:{}", limit.value()));
            Ok(vec![book_dto(
                book_id(),
                author_id(),
                "Listed Book",
                "listed-isbn",
                Some(2024),
            )])
        }

        async fn get_book(&self, book_id: BookId) -> Result<BookDto, AppError> {
            self.calls
                .record(format!("book_queries.get_book:{book_id}"));
            Ok(book_dto(
                book_id.0,
                author_id(),
                "Detailed Book",
                "detailed-isbn",
                Some(2025),
            ))
        }
    }

    struct MockStoreQueryService {
        calls: Calls,
    }

    #[async_trait]
    impl StoreQueryService for MockStoreQueryService {
        async fn list_stores(&self) -> Result<Vec<StoreDto>, AppError> {
            self.calls.record("store_queries.list_stores");
            Ok(vec![store_dto()])
        }
    }

    fn author_dto(id: Uuid, name: &str, bio: Option<String>) -> AuthorDto {
        AuthorDto {
            id,
            name: name.to_owned(),
            bio,
            created_at: timestamp(),
            updated_at: timestamp(),
        }
    }

    fn book_dto(
        id: Uuid,
        author_id: Uuid,
        title: &str,
        isbn: &str,
        published_year: Option<i32>,
    ) -> BookDto {
        BookDto {
            id,
            author_id,
            title: title.to_owned(),
            isbn: isbn.to_owned(),
            published_year,
            created_at: timestamp(),
            updated_at: timestamp(),
        }
    }

    fn store_dto() -> StoreDto {
        StoreDto {
            id: store_id(),
            name: "Listed Store".to_owned(),
            description: "Neighborhood bookstore".to_owned(),
            address: "123 Main St".to_owned(),
            phone_number: "555-0100".to_owned(),
            web_site: Some("https://listed-store.example".to_owned()),
            inventory: vec![book_dto(
                book_id(),
                author_id(),
                "Listed Book",
                "listed-isbn",
                Some(2024),
            )],
            created_at: timestamp(),
            updated_at: timestamp(),
        }
    }

    fn author_id() -> Uuid {
        Uuid::parse_str("11111111-1111-1111-1111-111111111111").unwrap()
    }

    fn book_id() -> Uuid {
        Uuid::parse_str("22222222-2222-2222-2222-222222222222").unwrap()
    }

    fn store_id() -> Uuid {
        Uuid::parse_str("33333333-3333-3333-3333-333333333333").unwrap()
    }

    fn timestamp() -> OffsetDateTime {
        OffsetDateTime::UNIX_EPOCH
    }
}

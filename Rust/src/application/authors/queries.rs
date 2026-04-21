use std::sync::Arc;

use async_trait::async_trait;

use crate::{
    application::authors::{AuthorDetailsDto, AuthorDto, BookDto},
    domain::{
        author::{AuthorId, AuthorQueryRepository},
        book::BookQueryRepository,
    },
    error::AppError,
};

#[async_trait]
pub trait AuthorQueryService: Send + Sync {
    async fn list_authors(&self) -> Result<Vec<AuthorDto>, AppError>;
    async fn get_author(&self, author_id: AuthorId) -> Result<AuthorDetailsDto, AppError>;
    async fn list_books_for_author(&self, author_id: AuthorId) -> Result<Vec<BookDto>, AppError>;
}

pub struct AuthorQueryHandler {
    authors: Arc<dyn AuthorQueryRepository>,
    books: Arc<dyn BookQueryRepository>,
}

impl AuthorQueryHandler {
    pub fn new(
        authors: Arc<dyn AuthorQueryRepository>,
        books: Arc<dyn BookQueryRepository>,
    ) -> Self {
        Self { authors, books }
    }
}

#[async_trait]
impl AuthorQueryService for AuthorQueryHandler {
    #[tracing::instrument(name = "authors.query.list", skip(self), err)]
    async fn list_authors(&self) -> Result<Vec<AuthorDto>, AppError> {
        let authors = self.authors.list().await?;
        Ok(authors.into_iter().map(Into::into).collect())
    }

    #[tracing::instrument(name = "authors.query.get", skip(self), fields(author.id = %author_id), err)]
    async fn get_author(&self, author_id: AuthorId) -> Result<AuthorDetailsDto, AppError> {
        let author = self
            .authors
            .get(author_id)
            .await?
            .ok_or_else(|| AppError::NotFound(format!("author {author_id}")))?;

        let books = self.books.list_by_author(author_id).await?;

        Ok(AuthorDetailsDto {
            author: author.into(),
            books: books.into_iter().map(Into::into).collect(),
        })
    }

    #[tracing::instrument(name = "authors.query.list_books", skip(self), fields(author.id = %author_id), err)]
    async fn list_books_for_author(&self, author_id: AuthorId) -> Result<Vec<BookDto>, AppError> {
        if self.authors.get(author_id).await?.is_none() {
            return Err(AppError::NotFound(format!("author {author_id}")));
        }

        let books = self.books.list_by_author(author_id).await?;
        Ok(books.into_iter().map(Into::into).collect())
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
            author::{Author, AuthorId, AuthorQueryRepository},
            book::{Book, BookId, BookQueryRepository},
        },
        error::AppError,
    };

    use super::{AuthorQueryHandler, AuthorQueryService};

    #[tokio::test]
    async fn list_authors_maps_domain_authors_to_dtos() {
        let handler = AuthorQueryHandler::new(
            Arc::new(FakeAuthors::new(Some(author("Author One")))),
            Arc::new(FakeBooks::new(vec![])),
        );

        let authors = handler.list_authors().await.unwrap();

        assert_eq!(authors.len(), 1);
        assert_eq!(authors[0].name, "Author One");
    }

    #[tokio::test]
    async fn get_author_returns_author_details_with_books() {
        let handler = AuthorQueryHandler::new(
            Arc::new(FakeAuthors::new(Some(author("Author One")))),
            Arc::new(FakeBooks::new(vec![book("Book One")])),
        );

        let details = handler.get_author(author_id()).await.unwrap();

        assert_eq!(details.author.name, "Author One");
        assert_eq!(details.books.len(), 1);
        assert_eq!(details.books[0].title, "Book One");
    }

    #[tokio::test]
    async fn get_author_returns_not_found_when_missing() {
        let handler = AuthorQueryHandler::new(
            Arc::new(FakeAuthors::new(None)),
            Arc::new(FakeBooks::new(vec![])),
        );

        let error = handler
            .get_author(author_id())
            .await
            .expect_err("missing author should fail");

        assert!(matches!(error, AppError::NotFound(_)));
    }

    #[tokio::test]
    async fn list_books_for_author_checks_author_exists() {
        let handler = AuthorQueryHandler::new(
            Arc::new(FakeAuthors::new(None)),
            Arc::new(FakeBooks::new(vec![book("Hidden Book")])),
        );

        let error = handler
            .list_books_for_author(author_id())
            .await
            .expect_err("missing author should fail");

        assert!(matches!(error, AppError::NotFound(_)));
    }

    struct FakeAuthors {
        author: Option<Author>,
    }

    impl FakeAuthors {
        fn new(author: Option<Author>) -> Self {
            Self { author }
        }
    }

    #[async_trait]
    impl AuthorQueryRepository for FakeAuthors {
        async fn list(&self) -> Result<Vec<Author>, AppError> {
            Ok(self.author.clone().into_iter().collect())
        }

        async fn get(&self, _author_id: AuthorId) -> Result<Option<Author>, AppError> {
            Ok(self.author.clone())
        }
    }

    struct FakeBooks {
        books: Vec<Book>,
    }

    impl FakeBooks {
        fn new(books: Vec<Book>) -> Self {
            Self { books }
        }
    }

    #[async_trait]
    impl BookQueryRepository for FakeBooks {
        async fn list(&self) -> Result<Vec<Book>, AppError> {
            Ok(self.books.clone())
        }

        async fn list_by_author(&self, _author_id: AuthorId) -> Result<Vec<Book>, AppError> {
            Ok(self.books.clone())
        }

        async fn get(&self, _book_id: BookId) -> Result<Option<Book>, AppError> {
            Ok(self.books.first().cloned())
        }
    }

    fn author(name: &str) -> Author {
        Author {
            id: author_id(),
            name: name.to_owned(),
            bio: None,
            created_at: OffsetDateTime::UNIX_EPOCH,
            updated_at: OffsetDateTime::UNIX_EPOCH,
        }
    }

    fn book(title: &str) -> Book {
        Book {
            id: book_id(),
            author_id: author_id(),
            title: title.to_owned(),
            isbn: "isbn".to_owned(),
            published_year: None,
            created_at: OffsetDateTime::UNIX_EPOCH,
            updated_at: OffsetDateTime::UNIX_EPOCH,
        }
    }

    fn author_id() -> AuthorId {
        AuthorId(Uuid::parse_str("11111111-1111-1111-1111-111111111111").unwrap())
    }

    fn book_id() -> BookId {
        BookId(Uuid::parse_str("22222222-2222-2222-2222-222222222222").unwrap())
    }
}

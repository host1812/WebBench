use std::sync::Arc;

use async_trait::async_trait;

use crate::{
    application::authors::BookDto,
    domain::{
        author::AuthorQueryRepository,
        book::{BookId, BookListLimit, BookQueryRepository},
    },
    error::AppError,
};

#[async_trait]
pub trait BookQueryService: Send + Sync {
    async fn list_books(&self, limit: BookListLimit) -> Result<Vec<BookDto>, AppError>;
    async fn get_book(&self, book_id: BookId) -> Result<BookDto, AppError>;
}

pub struct BookQueryHandler {
    books: Arc<dyn BookQueryRepository>,
    authors: Arc<dyn AuthorQueryRepository>,
}

impl BookQueryHandler {
    pub fn new(
        books: Arc<dyn BookQueryRepository>,
        authors: Arc<dyn AuthorQueryRepository>,
    ) -> Self {
        Self { books, authors }
    }
}

#[async_trait]
impl BookQueryService for BookQueryHandler {
    async fn list_books(&self, limit: BookListLimit) -> Result<Vec<BookDto>, AppError> {
        let books = self.books.list(limit).await?;
        Ok(books.into_iter().map(Into::into).collect())
    }

    async fn get_book(&self, book_id: BookId) -> Result<BookDto, AppError> {
        let book = self
            .books
            .get(book_id)
            .await?
            .ok_or_else(|| AppError::NotFound(format!("book {book_id}")))?;

        if self.authors.get(book.author_id).await?.is_none() {
            return Err(AppError::Conflict(format!(
                "book {book_id} references a missing author"
            )));
        }

        Ok(book.into())
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
            book::{Book, BookId, BookListLimit, BookQueryRepository},
        },
        error::AppError,
    };

    use super::{BookQueryHandler, BookQueryService};

    #[tokio::test]
    async fn list_books_maps_domain_books_to_dtos() {
        let handler = BookQueryHandler::new(
            Arc::new(FakeBooks::new(Some(book("Book One")))),
            Arc::new(FakeAuthors::new(Some(author()))),
        );

        let books = handler.list_books(BookListLimit::default()).await.unwrap();

        assert_eq!(books.len(), 1);
        assert_eq!(books[0].title, "Book One");
    }

    #[tokio::test]
    async fn get_book_returns_book_when_author_exists() {
        let handler = BookQueryHandler::new(
            Arc::new(FakeBooks::new(Some(book("Book One")))),
            Arc::new(FakeAuthors::new(Some(author()))),
        );

        let book = handler.get_book(book_id()).await.unwrap();

        assert_eq!(book.id, book_id().0);
        assert_eq!(book.author_id, author_id().0);
    }

    #[tokio::test]
    async fn get_book_returns_not_found_when_book_is_missing() {
        let handler = BookQueryHandler::new(
            Arc::new(FakeBooks::new(None)),
            Arc::new(FakeAuthors::new(Some(author()))),
        );

        let error = handler
            .get_book(book_id())
            .await
            .expect_err("missing book should fail");

        assert!(matches!(error, AppError::NotFound(_)));
    }

    #[tokio::test]
    async fn get_book_returns_conflict_when_author_reference_is_missing() {
        let handler = BookQueryHandler::new(
            Arc::new(FakeBooks::new(Some(book("Orphan Book")))),
            Arc::new(FakeAuthors::new(None)),
        );

        let error = handler
            .get_book(book_id())
            .await
            .expect_err("missing author reference should fail");

        assert!(matches!(error, AppError::Conflict(_)));
    }

    struct FakeBooks {
        book: Option<Book>,
    }

    impl FakeBooks {
        fn new(book: Option<Book>) -> Self {
            Self { book }
        }
    }

    #[async_trait]
    impl BookQueryRepository for FakeBooks {
        async fn list(&self, _limit: BookListLimit) -> Result<Vec<Book>, AppError> {
            Ok(self.book.clone().into_iter().collect())
        }

        async fn list_by_author(
            &self,
            _author_id: AuthorId,
            _limit: BookListLimit,
        ) -> Result<Vec<Book>, AppError> {
            Ok(self.book.clone().into_iter().collect())
        }

        async fn get(&self, _book_id: BookId) -> Result<Option<Book>, AppError> {
            Ok(self.book.clone())
        }
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

    fn author() -> Author {
        Author {
            id: author_id(),
            name: "Author".to_owned(),
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
            published_year: Some(2024),
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

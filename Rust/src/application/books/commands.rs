use std::sync::Arc;

use async_trait::async_trait;

use crate::{
    application::{
        authors::BookDto,
        books::{CreateBookInput, UpdateBookInput},
    },
    domain::{
        author::{AuthorId, AuthorQueryRepository},
        book::{Book, BookCommandRepository, BookId, BookQueryRepository},
    },
    error::AppError,
};

#[async_trait]
pub trait BookCommandService: Send + Sync {
    async fn create_book(&self, input: CreateBookInput) -> Result<BookDto, AppError>;
    async fn update_book(
        &self,
        book_id: BookId,
        input: UpdateBookInput,
    ) -> Result<BookDto, AppError>;
    async fn delete_book(&self, book_id: BookId) -> Result<(), AppError>;
}

pub struct BookCommandHandler {
    commands: Arc<dyn BookCommandRepository>,
    queries: Arc<dyn BookQueryRepository>,
    authors: Arc<dyn AuthorQueryRepository>,
}

impl BookCommandHandler {
    pub fn new(
        commands: Arc<dyn BookCommandRepository>,
        queries: Arc<dyn BookQueryRepository>,
        authors: Arc<dyn AuthorQueryRepository>,
    ) -> Self {
        Self {
            commands,
            queries,
            authors,
        }
    }
}

#[async_trait]
impl BookCommandService for BookCommandHandler {
    #[tracing::instrument(name = "books.command.create", skip(self, input), fields(author.id = %input.author_id, book.title = %input.title), err)]
    async fn create_book(&self, input: CreateBookInput) -> Result<BookDto, AppError> {
        let author_id = AuthorId::from(input.author_id);
        ensure_author_exists(self.authors.as_ref(), author_id).await?;

        let book = Book::create(
            author_id,
            input.title,
            input.description,
            input.published_year,
        )?;
        let created = self.commands.create(book).await?;
        Ok(created.into())
    }

    #[tracing::instrument(name = "books.command.update", skip(self, input), fields(book.id = %book_id, author.id = %input.author_id, book.title = %input.title), err)]
    async fn update_book(
        &self,
        book_id: BookId,
        input: UpdateBookInput,
    ) -> Result<BookDto, AppError> {
        let author_id = AuthorId::from(input.author_id);
        ensure_author_exists(self.authors.as_ref(), author_id).await?;

        let mut book = self
            .queries
            .get(book_id)
            .await?
            .ok_or_else(|| AppError::NotFound(format!("book {book_id}")))?;

        book.revise(
            author_id,
            input.title,
            input.description,
            input.published_year,
        )?;

        let updated = self.commands.update(book).await?;
        Ok(updated.into())
    }

    #[tracing::instrument(name = "books.command.delete", skip(self), fields(book.id = %book_id), err)]
    async fn delete_book(&self, book_id: BookId) -> Result<(), AppError> {
        if self.queries.get(book_id).await?.is_none() {
            return Err(AppError::NotFound(format!("book {book_id}")));
        }

        self.commands.delete(book_id).await
    }
}

#[tracing::instrument(name = "books.command.ensure_author_exists", skip(repository), fields(author.id = %author_id), err)]
async fn ensure_author_exists(
    repository: &dyn AuthorQueryRepository,
    author_id: AuthorId,
) -> Result<(), AppError> {
    if repository.get(author_id).await?.is_none() {
        return Err(AppError::Validation(format!(
            "cannot assign book to missing author {author_id}"
        )));
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use std::sync::{Arc, Mutex};

    use async_trait::async_trait;
    use time::OffsetDateTime;
    use uuid::Uuid;

    use crate::{
        application::books::{CreateBookInput, UpdateBookInput},
        domain::{
            author::{Author, AuthorId, AuthorQueryRepository},
            book::{Book, BookCommandRepository, BookId, BookQueryRepository},
        },
        error::AppError,
    };

    use super::{BookCommandHandler, BookCommandService};

    #[tokio::test]
    async fn create_book_checks_author_and_persists_book() {
        let books = Arc::new(FakeBookRepository::with_book(None));
        let authors = Arc::new(FakeAuthorRepository::with_author(Some(author())));
        let handler = BookCommandHandler::new(books.clone(), books.clone(), authors);

        let book = handler
            .create_book(CreateBookInput {
                author_id: author_id().0,
                title: "  Parable of the Sower  ".to_owned(),
                description: Some("  Novel  ".to_owned()),
                published_year: Some(1993),
            })
            .await
            .unwrap();

        assert_eq!(book.title, "Parable of the Sower");
        assert_eq!(book.description.as_deref(), Some("Novel"));
        assert_eq!(books.created_count(), 1);
    }

    #[tokio::test]
    async fn create_book_returns_validation_when_author_is_missing() {
        let books = Arc::new(FakeBookRepository::with_book(None));
        let authors = Arc::new(FakeAuthorRepository::with_author(None));
        let handler = BookCommandHandler::new(books.clone(), books, authors);

        let error = handler
            .create_book(CreateBookInput {
                author_id: author_id().0,
                title: "Book".to_owned(),
                description: None,
                published_year: None,
            })
            .await
            .expect_err("missing author should fail");

        assert!(matches!(error, AppError::Validation(_)));
    }

    #[tokio::test]
    async fn update_book_mutates_loaded_book_and_persists_it() {
        let books = Arc::new(FakeBookRepository::with_book(Some(book("Old Title"))));
        let authors = Arc::new(FakeAuthorRepository::with_author(Some(author())));
        let handler = BookCommandHandler::new(books.clone(), books.clone(), authors);

        let book = handler
            .update_book(
                book_id(),
                UpdateBookInput {
                    author_id: author_id().0,
                    title: "  New Title  ".to_owned(),
                    description: None,
                    published_year: Some(2020),
                },
            )
            .await
            .unwrap();

        assert_eq!(book.title, "New Title");
        assert_eq!(book.published_year, Some(2020));
        assert_eq!(books.updated_count(), 1);
    }

    #[tokio::test]
    async fn update_book_returns_not_found_when_book_is_missing() {
        let books = Arc::new(FakeBookRepository::with_book(None));
        let authors = Arc::new(FakeAuthorRepository::with_author(Some(author())));
        let handler = BookCommandHandler::new(books.clone(), books, authors);

        let error = handler
            .update_book(
                book_id(),
                UpdateBookInput {
                    author_id: author_id().0,
                    title: "Missing".to_owned(),
                    description: None,
                    published_year: None,
                },
            )
            .await
            .expect_err("missing book should fail");

        assert!(matches!(error, AppError::NotFound(_)));
    }

    #[tokio::test]
    async fn delete_book_checks_existence_before_delete() {
        let books = Arc::new(FakeBookRepository::with_book(Some(book("Existing"))));
        let authors = Arc::new(FakeAuthorRepository::with_author(Some(author())));
        let handler = BookCommandHandler::new(books.clone(), books.clone(), authors);

        handler.delete_book(book_id()).await.unwrap();

        assert_eq!(books.deleted_count(), 1);
    }

    struct FakeBookRepository {
        book: Mutex<Option<Book>>,
        created: Mutex<Vec<Book>>,
        updated: Mutex<Vec<Book>>,
        deleted: Mutex<Vec<BookId>>,
    }

    impl FakeBookRepository {
        fn with_book(book: Option<Book>) -> Self {
            Self {
                book: Mutex::new(book),
                created: Mutex::new(Vec::new()),
                updated: Mutex::new(Vec::new()),
                deleted: Mutex::new(Vec::new()),
            }
        }

        fn created_count(&self) -> usize {
            self.created.lock().unwrap().len()
        }

        fn updated_count(&self) -> usize {
            self.updated.lock().unwrap().len()
        }

        fn deleted_count(&self) -> usize {
            self.deleted.lock().unwrap().len()
        }
    }

    #[async_trait]
    impl BookCommandRepository for FakeBookRepository {
        async fn create(&self, book: Book) -> Result<Book, AppError> {
            self.created.lock().unwrap().push(book.clone());
            Ok(book)
        }

        async fn update(&self, book: Book) -> Result<Book, AppError> {
            self.updated.lock().unwrap().push(book.clone());
            *self.book.lock().unwrap() = Some(book.clone());
            Ok(book)
        }

        async fn delete(&self, book_id: BookId) -> Result<(), AppError> {
            self.deleted.lock().unwrap().push(book_id);
            Ok(())
        }
    }

    #[async_trait]
    impl BookQueryRepository for FakeBookRepository {
        async fn list(&self) -> Result<Vec<Book>, AppError> {
            Ok(self.book.lock().unwrap().clone().into_iter().collect())
        }

        async fn list_by_author(&self, _author_id: AuthorId) -> Result<Vec<Book>, AppError> {
            Ok(self.book.lock().unwrap().clone().into_iter().collect())
        }

        async fn get(&self, _book_id: BookId) -> Result<Option<Book>, AppError> {
            Ok(self.book.lock().unwrap().clone())
        }
    }

    struct FakeAuthorRepository {
        author: Option<Author>,
    }

    impl FakeAuthorRepository {
        fn with_author(author: Option<Author>) -> Self {
            Self { author }
        }
    }

    #[async_trait]
    impl AuthorQueryRepository for FakeAuthorRepository {
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
            description: None,
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

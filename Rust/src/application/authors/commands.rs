use std::sync::Arc;

use async_trait::async_trait;

use crate::{
    application::authors::{AuthorDto, CreateAuthorInput, UpdateAuthorInput},
    domain::author::{Author, AuthorCommandRepository, AuthorId, AuthorQueryRepository},
    error::AppError,
};

#[async_trait]
pub trait AuthorCommandService: Send + Sync {
    async fn create_author(&self, input: CreateAuthorInput) -> Result<AuthorDto, AppError>;
    async fn update_author(
        &self,
        author_id: AuthorId,
        input: UpdateAuthorInput,
    ) -> Result<AuthorDto, AppError>;
    async fn delete_author(&self, author_id: AuthorId) -> Result<(), AppError>;
}

pub struct AuthorCommandHandler {
    commands: Arc<dyn AuthorCommandRepository>,
    queries: Arc<dyn AuthorQueryRepository>,
}

impl AuthorCommandHandler {
    pub fn new(
        commands: Arc<dyn AuthorCommandRepository>,
        queries: Arc<dyn AuthorQueryRepository>,
    ) -> Self {
        Self { commands, queries }
    }
}

#[async_trait]
impl AuthorCommandService for AuthorCommandHandler {
    async fn create_author(&self, input: CreateAuthorInput) -> Result<AuthorDto, AppError> {
        let author = Author::create(input.name, input.bio)?;
        let created = self.commands.create(author).await?;
        Ok(created.into())
    }

    async fn update_author(
        &self,
        author_id: AuthorId,
        input: UpdateAuthorInput,
    ) -> Result<AuthorDto, AppError> {
        let mut author = self
            .queries
            .get(author_id)
            .await?
            .ok_or_else(|| AppError::NotFound(format!("author {author_id}")))?;

        author.rename(input.name)?;
        author.set_bio(input.bio);

        let updated = self.commands.update(author).await?;
        Ok(updated.into())
    }

    async fn delete_author(&self, author_id: AuthorId) -> Result<(), AppError> {
        if self.queries.get(author_id).await?.is_none() {
            return Err(AppError::NotFound(format!("author {author_id}")));
        }

        self.commands.delete(author_id).await
    }
}

#[cfg(test)]
mod tests {
    use std::sync::{Arc, Mutex};

    use async_trait::async_trait;
    use time::OffsetDateTime;
    use uuid::Uuid;

    use crate::{
        application::authors::{CreateAuthorInput, UpdateAuthorInput},
        domain::author::{Author, AuthorCommandRepository, AuthorId, AuthorQueryRepository},
        error::AppError,
    };

    use super::{AuthorCommandHandler, AuthorCommandService};

    #[tokio::test]
    async fn create_author_validates_and_persists_author() {
        let repository = Arc::new(FakeAuthorRepository::with_author(None));
        let handler = AuthorCommandHandler::new(repository.clone(), repository.clone());

        let author = handler
            .create_author(CreateAuthorInput {
                name: "  N. K. Jemisin  ".to_owned(),
                bio: Some("  Fantasy author  ".to_owned()),
            })
            .await
            .unwrap();

        assert_eq!(author.name, "N. K. Jemisin");
        assert_eq!(author.bio.as_deref(), Some("Fantasy author"));
        assert_eq!(repository.created_count(), 1);
    }

    #[tokio::test]
    async fn update_author_returns_not_found_when_author_is_missing() {
        let repository = Arc::new(FakeAuthorRepository::with_author(None));
        let handler = AuthorCommandHandler::new(repository.clone(), repository);

        let error = handler
            .update_author(
                author_id(),
                UpdateAuthorInput {
                    name: "Missing".to_owned(),
                    bio: None,
                },
            )
            .await
            .expect_err("missing author should fail");

        assert!(matches!(error, AppError::NotFound(_)));
    }

    #[tokio::test]
    async fn update_author_mutates_loaded_author_and_persists_it() {
        let repository = Arc::new(FakeAuthorRepository::with_author(Some(author("Old Name"))));
        let handler = AuthorCommandHandler::new(repository.clone(), repository.clone());

        let author = handler
            .update_author(
                author_id(),
                UpdateAuthorInput {
                    name: "  New Name  ".to_owned(),
                    bio: Some("  New Bio  ".to_owned()),
                },
            )
            .await
            .unwrap();

        assert_eq!(author.name, "New Name");
        assert_eq!(author.bio.as_deref(), Some("New Bio"));
        assert_eq!(repository.updated_count(), 1);
    }

    #[tokio::test]
    async fn delete_author_checks_existence_before_delete() {
        let repository = Arc::new(FakeAuthorRepository::with_author(Some(author("Existing"))));
        let handler = AuthorCommandHandler::new(repository.clone(), repository.clone());

        handler.delete_author(author_id()).await.unwrap();

        assert_eq!(repository.deleted_count(), 1);
    }

    #[tokio::test]
    async fn delete_author_returns_not_found_when_author_is_missing() {
        let repository = Arc::new(FakeAuthorRepository::with_author(None));
        let handler = AuthorCommandHandler::new(repository.clone(), repository.clone());

        let error = handler
            .delete_author(author_id())
            .await
            .expect_err("missing author should fail");

        assert!(matches!(error, AppError::NotFound(_)));
        assert_eq!(repository.deleted_count(), 0);
    }

    struct FakeAuthorRepository {
        author: Mutex<Option<Author>>,
        created: Mutex<Vec<Author>>,
        updated: Mutex<Vec<Author>>,
        deleted: Mutex<Vec<AuthorId>>,
    }

    impl FakeAuthorRepository {
        fn with_author(author: Option<Author>) -> Self {
            Self {
                author: Mutex::new(author),
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
    impl AuthorCommandRepository for FakeAuthorRepository {
        async fn create(&self, author: Author) -> Result<Author, AppError> {
            self.created.lock().unwrap().push(author.clone());
            Ok(author)
        }

        async fn update(&self, author: Author) -> Result<Author, AppError> {
            self.updated.lock().unwrap().push(author.clone());
            *self.author.lock().unwrap() = Some(author.clone());
            Ok(author)
        }

        async fn delete(&self, author_id: AuthorId) -> Result<(), AppError> {
            self.deleted.lock().unwrap().push(author_id);
            Ok(())
        }
    }

    #[async_trait]
    impl AuthorQueryRepository for FakeAuthorRepository {
        async fn list(&self) -> Result<Vec<Author>, AppError> {
            Ok(self.author.lock().unwrap().clone().into_iter().collect())
        }

        async fn get(&self, _author_id: AuthorId) -> Result<Option<Author>, AppError> {
            Ok(self.author.lock().unwrap().clone())
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

    fn author_id() -> AuthorId {
        AuthorId(Uuid::parse_str("11111111-1111-1111-1111-111111111111").unwrap())
    }
}

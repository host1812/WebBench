CREATE INDEX IF NOT EXISTS idx_books_author_id_title ON books(author_id, title);
CREATE INDEX IF NOT EXISTS idx_books_title ON books(title);

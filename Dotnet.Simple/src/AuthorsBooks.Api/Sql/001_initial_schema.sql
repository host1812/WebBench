CREATE TABLE IF NOT EXISTS authors (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    bio text NOT NULL DEFAULT '',
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL
);

CREATE TABLE IF NOT EXISTS books (
    id uuid PRIMARY KEY,
    author_id uuid NOT NULL REFERENCES authors(id) ON DELETE CASCADE,
    title text NOT NULL,
    published_year integer NULL,
    isbn text NOT NULL DEFAULT '',
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_books_author_id ON books(author_id);
CREATE INDEX IF NOT EXISTS idx_books_title ON books(title);
CREATE INDEX IF NOT EXISTS idx_books_author_id_title ON books(author_id, title);
CREATE INDEX IF NOT EXISTS idx_books_isbn ON books(isbn);

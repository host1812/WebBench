CREATE TABLE IF NOT EXISTS authors (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    bio text NOT NULL DEFAULT '',
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS books (
    id uuid PRIMARY KEY,
    author_id uuid NOT NULL REFERENCES authors(id) ON DELETE CASCADE,
    title text NOT NULL,
    isbn text NOT NULL DEFAULT '',
    published_year integer,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_books_author_id ON books(author_id);

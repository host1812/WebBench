CREATE TABLE IF NOT EXISTS authors (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL,
    bio TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS books (
    id UUID PRIMARY KEY,
    author_id UUID NOT NULL REFERENCES authors(id) ON DELETE CASCADE,
    title TEXT NOT NULL,
    description TEXT NULL,
    published_year INTEGER NULL,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    CONSTRAINT books_published_year_range CHECK (published_year IS NULL OR published_year BETWEEN 0 AND 9999)
);

CREATE INDEX IF NOT EXISTS idx_authors_name ON authors (name);
CREATE INDEX IF NOT EXISTS idx_books_author_id ON books (author_id);
CREATE INDEX IF NOT EXISTS idx_books_title ON books (title);

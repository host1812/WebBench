CREATE TABLE authors__new (
    legacy_id BIGINT UNIQUE,
    id UUID PRIMARY KEY,
    name TEXT NOT NULL,
    bio TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO authors__new (legacy_id, id, name, bio, created_at, updated_at)
SELECT
    id,
    ('00000000-0000-0001-0000-' || lpad(id::text, 12, '0'))::uuid,
    name,
    COALESCE(bio, ''),
    created_at,
    updated_at
FROM authors;

CREATE TABLE books__new (
    legacy_id BIGINT UNIQUE,
    id UUID PRIMARY KEY,
    author_id UUID NOT NULL REFERENCES authors__new(id) ON DELETE CASCADE,
    title TEXT NOT NULL,
    published_year INTEGER,
    isbn TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO books__new (legacy_id, id, author_id, title, published_year, isbn, created_at, updated_at)
SELECT
    books.id,
    ('00000000-0000-0002-0000-' || lpad(books.id::text, 12, '0'))::uuid,
    authors__new.id,
    books.title,
    books.published_year,
    '',
    books.created_at,
    books.updated_at
FROM books
JOIN authors__new
    ON authors__new.legacy_id = books.author_id;

DROP TABLE books;
DROP TABLE authors;

ALTER TABLE authors__new DROP COLUMN legacy_id;
ALTER TABLE books__new DROP COLUMN legacy_id;

ALTER TABLE authors__new RENAME TO authors;
ALTER TABLE books__new RENAME TO books;

CREATE INDEX idx_books_author_id ON books(author_id);
CREATE INDEX idx_books_isbn ON books(isbn);

CREATE TRIGGER trg_authors_updated_at
BEFORE UPDATE ON authors
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_books_updated_at
BEFORE UPDATE ON books
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

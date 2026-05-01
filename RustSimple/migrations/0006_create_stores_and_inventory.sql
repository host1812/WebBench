CREATE TABLE stores (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL,
    address TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    phone_number TEXT NOT NULL,
    website TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE store_books (
    store_id UUID NOT NULL REFERENCES stores(id) ON DELETE CASCADE,
    book_id UUID NOT NULL REFERENCES books(id) ON DELETE CASCADE,
    PRIMARY KEY (store_id, book_id)
);

CREATE INDEX idx_store_books_book_id ON store_books(book_id);
CREATE INDEX idx_stores_name ON stores(name);

CREATE TRIGGER trg_stores_updated_at
BEFORE UPDATE ON stores
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

WITH store_seed(id, name, address, description, phone_number, website) AS (
    VALUES
        (
            '30000000-0000-0000-0000-000000000001'::uuid,
            'Northwind Books',
            '101 Market Street, Indianapolis, IN 46204',
            'Downtown general-interest bookshop with a broad fiction catalog.',
            '+1-317-555-0101',
            'https://northwind-books.example'
        ),
        (
            '30000000-0000-0000-0000-000000000002'::uuid,
            'Lighthouse Reading Room',
            '245 Meridian Avenue, Indianapolis, IN 46202',
            'Neighborhood store focused on classics, essays, and staff picks.',
            '+1-317-555-0102',
            'https://lighthouse-reading.example'
        ),
        (
            '30000000-0000-0000-0000-000000000003'::uuid,
            'Page & Parcel',
            '78 College Row, Bloomington, IN 47401',
            'Campus-adjacent shop carrying literature, history, and new releases.',
            '+1-812-555-0103',
            NULL
        ),
        (
            '30000000-0000-0000-0000-000000000004'::uuid,
            'Riverbend Booksellers',
            '512 Main Street, Lafayette, IN 47901',
            'Independent bookseller with deep shelves in literary fiction.',
            '+1-765-555-0104',
            'https://riverbend-books.example'
        )
)
INSERT INTO stores (id, name, address, description, phone_number, website, created_at, updated_at)
SELECT id, name, address, description, phone_number, website, NOW(), NOW()
FROM store_seed seed
WHERE NOT EXISTS (
    SELECT 1
    FROM stores existing
    WHERE existing.id = seed.id OR existing.name = seed.name
);

WITH
ranked_books AS (
    SELECT
        id,
        row_number() OVER (ORDER BY id) AS rn
    FROM books
),
inventory_seed(store_id, first_book, last_book) AS (
    VALUES
        ('30000000-0000-0000-0000-000000000001'::uuid, 1, 25),
        ('30000000-0000-0000-0000-000000000002'::uuid, 26, 50),
        ('30000000-0000-0000-0000-000000000003'::uuid, 51, 75),
        ('30000000-0000-0000-0000-000000000004'::uuid, 76, 100)
),
inventory_rows AS (
    SELECT
        stores.id AS store_id,
        ranked_books.id AS book_id
    FROM inventory_seed
    JOIN stores
        ON stores.id = inventory_seed.store_id
    JOIN ranked_books
        ON ranked_books.rn BETWEEN inventory_seed.first_book AND inventory_seed.last_book
)
INSERT INTO store_books (store_id, book_id)
SELECT store_id, book_id
FROM inventory_rows seed
WHERE NOT EXISTS (
    SELECT 1
    FROM store_books existing
    WHERE existing.store_id = seed.store_id
      AND existing.book_id = seed.book_id
);

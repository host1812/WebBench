CREATE TABLE IF NOT EXISTS stores (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    description text NOT NULL DEFAULT '',
    address text NOT NULL,
    phone_number text NOT NULL,
    website text,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS store_books (
    store_id uuid NOT NULL REFERENCES stores(id) ON DELETE CASCADE,
    book_id uuid NOT NULL REFERENCES books(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL,
    PRIMARY KEY (store_id, book_id)
);

CREATE INDEX IF NOT EXISTS idx_stores_name ON stores(name);
CREATE INDEX IF NOT EXISTS idx_store_books_book_id ON store_books(book_id);

WITH store_seed(id, name, description, address, phone_number, website) AS (
    VALUES
        (
            '30000000-0000-0000-0000-000000000001'::uuid,
            'Northside Books',
            'Neighborhood bookstore with a broad fiction and classics catalog.',
            '1201 North Meridian Street, Indianapolis, IN 46202',
            '+1-317-555-0101',
            'https://northside-books.example.com'
        ),
        (
            '30000000-0000-0000-0000-000000000002'::uuid,
            'Market Street Books',
            'Downtown shop focused on new releases and literary events.',
            '48 East Market Street, Indianapolis, IN 46204',
            '+1-317-555-0102',
            'https://market-street-books.example.com'
        ),
        (
            '30000000-0000-0000-0000-000000000003'::uuid,
            'Canal Reading Room',
            'Independent bookseller with used, rare, and academic titles.',
            '331 West Ohio Street, Indianapolis, IN 46202',
            '+1-317-555-0103',
            NULL
        )
)
INSERT INTO stores (id, name, description, address, phone_number, website, created_at, updated_at)
SELECT id, name, description, address, phone_number, website, NOW(), NOW()
FROM store_seed seed
WHERE NOT EXISTS (
    SELECT 1
    FROM stores existing
    WHERE existing.id = seed.id
);

WITH
store_inventory(store_id, start_rank, end_rank) AS (
    VALUES
        ('30000000-0000-0000-0000-000000000001'::uuid, 1, 40),
        ('30000000-0000-0000-0000-000000000002'::uuid, 21, 70),
        ('30000000-0000-0000-0000-000000000003'::uuid, 51, 100)
),
book_rank AS (
    SELECT
        id,
        row_number() OVER (ORDER BY title ASC, id ASC) AS rn
    FROM books
)
INSERT INTO store_books (store_id, book_id, created_at)
SELECT inventory.store_id, books.id, NOW()
FROM store_inventory inventory
JOIN book_rank books
    ON books.rn BETWEEN inventory.start_rank AND inventory.end_rank
WHERE NOT EXISTS (
    SELECT 1
    FROM store_books existing
    WHERE existing.store_id = inventory.store_id
      AND existing.book_id = books.id
);

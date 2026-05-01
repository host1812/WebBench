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
    created_at timestamptz NOT NULL DEFAULT NOW(),
    PRIMARY KEY (store_id, book_id)
);

CREATE INDEX IF NOT EXISTS idx_stores_name ON stores(name);
CREATE INDEX IF NOT EXISTS idx_store_books_book_id ON store_books(book_id);

WITH store_seed(id, name, description, address, phone_number, website) AS (
    VALUES
        (
            '30000000-0000-0000-0000-000000000001'::uuid,
            'Northside Books',
            'Neighborhood bookstore with a broad literary catalog.',
            '101 N Meridian St, Indianapolis, IN 46204',
            '+1-317-555-0101',
            'https://northside-books.example.com'
        ),
        (
            '30000000-0000-0000-0000-000000000002'::uuid,
            'Market Street Books',
            'Downtown shop focused on classics, science fiction, and local readers.',
            '220 W Market St, Indianapolis, IN 46204',
            '+1-317-555-0102',
            NULL
        ),
        (
            '30000000-0000-0000-0000-000000000003'::uuid,
            'Canal Walk Books',
            'Independent bookstore carrying fiction, essays, and historical titles.',
            '350 Canal Walk, Indianapolis, IN 46202',
            '+1-317-555-0103',
            'https://canal-walk-books.example.com'
        )
)
INSERT INTO stores (id, name, description, address, phone_number, website, created_at, updated_at)
SELECT id, name, description, address, phone_number, website, NOW(), NOW()
FROM store_seed seed
WHERE NOT EXISTS (
    SELECT 1
    FROM stores existing
    WHERE existing.id = seed.id OR existing.name = seed.name
);

WITH inventory_seed(store_id, first_isbn, last_isbn) AS (
    VALUES
        ('30000000-0000-0000-0000-000000000001'::uuid, '9780000000001', '9780000000010'),
        ('30000000-0000-0000-0000-000000000002'::uuid, '9780000000011', '9780000000020'),
        ('30000000-0000-0000-0000-000000000003'::uuid, '9780000000021', '9780000000030')
)
INSERT INTO store_books (store_id, book_id, created_at)
SELECT inventory_seed.store_id, books.id, NOW()
FROM inventory_seed
JOIN stores ON stores.id = inventory_seed.store_id
JOIN books ON books.isbn >= inventory_seed.first_isbn
    AND books.isbn <= inventory_seed.last_isbn
WHERE NOT EXISTS (
    SELECT 1
    FROM store_books existing
    WHERE existing.store_id = inventory_seed.store_id
      AND existing.book_id = books.id
);

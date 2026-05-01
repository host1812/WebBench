CREATE TABLE IF NOT EXISTS stores (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    description text NOT NULL DEFAULT '',
    address text NOT NULL,
    phone_number text NOT NULL,
    web_site text NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL
);

CREATE TABLE IF NOT EXISTS store_books (
    store_id uuid NOT NULL REFERENCES stores(id) ON DELETE CASCADE,
    book_id uuid NOT NULL REFERENCES books(id) ON DELETE CASCADE,
    created_at timestamp with time zone NOT NULL,
    PRIMARY KEY (store_id, book_id)
);

CREATE INDEX IF NOT EXISTS idx_stores_name ON stores(name);
CREATE INDEX IF NOT EXISTS idx_store_books_book_id ON store_books(book_id);

WITH store_seed(id, name, description, address, phone_number, web_site) AS (
    VALUES
        (
            '30000000-0000-0000-0000-000000000001'::uuid,
            'North Star Books',
            'Neighborhood shop with a broad classic literature selection.',
            '101 Market Street, Indianapolis, IN 46204',
            '+1-317-555-0101',
            'https://northstarbooks.example'
        ),
        (
            '30000000-0000-0000-0000-000000000002'::uuid,
            'Riverbend Reading Room',
            'Independent bookstore focused on fiction, essays, and local events.',
            '42 River Road, Bloomington, IN 47401',
            '+1-812-555-0102',
            'https://riverbendreading.example'
        ),
        (
            '30000000-0000-0000-0000-000000000003'::uuid,
            'Catalog Corner',
            'Compact storefront carrying high-turnover paperbacks and library orders.',
            '700 Library Lane, Carmel, IN 46032',
            '+1-317-555-0103',
            NULL
        ),
        (
            '30000000-0000-0000-0000-000000000004'::uuid,
            'Ink & Index',
            'Curated store for world literature and literary criticism.',
            '18 College Avenue, West Lafayette, IN 47906',
            '+1-765-555-0104',
            'https://inkandindex.example'
        ),
        (
            '30000000-0000-0000-0000-000000000005'::uuid,
            'The Book Ledger',
            'Order-driven bookshop serving schools, clubs, and private collections.',
            '525 Main Street, Fort Wayne, IN 46802',
            '+1-260-555-0105',
            NULL
        )
)
INSERT INTO stores (id, name, description, address, phone_number, web_site, created_at, updated_at)
SELECT id, name, description, address, phone_number, web_site, NOW(), NOW()
FROM store_seed seed
WHERE NOT EXISTS (
    SELECT 1
    FROM stores existing
    WHERE existing.id = seed.id OR existing.name = seed.name
);

WITH
store_rank AS (
    SELECT
        id,
        row_number() OVER (ORDER BY id) AS rn,
        count(*) OVER () AS total
    FROM stores
    WHERE id::text LIKE '30000000-0000-0000-0000-%'
),
book_rank AS (
    SELECT id, row_number() OVER (ORDER BY title, id) AS rn
    FROM books
    WHERE id::text LIKE '20000000-0000-0000-0000-%'
    ORDER BY title, id
    LIMIT 250
),
inventory_seed AS (
    SELECT stores.id AS store_id, books.id AS book_id
    FROM book_rank books
    JOIN store_rank stores
        ON stores.rn = ((books.rn - 1) % stores.total) + 1
)
INSERT INTO store_books (store_id, book_id, created_at)
SELECT store_id, book_id, NOW()
FROM inventory_seed
ON CONFLICT (store_id, book_id) DO NOTHING;

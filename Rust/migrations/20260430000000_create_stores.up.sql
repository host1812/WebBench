CREATE TABLE stores (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    description text NOT NULL,
    address text NOT NULL,
    phone_number text NOT NULL,
    web_site text NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE TABLE store_inventory (
    store_id uuid NOT NULL REFERENCES stores(id) ON DELETE CASCADE,
    book_id uuid NOT NULL REFERENCES books(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (store_id, book_id)
);

CREATE INDEX store_inventory_book_id_idx ON store_inventory(book_id);

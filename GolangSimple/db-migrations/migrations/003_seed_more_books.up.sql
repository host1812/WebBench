CREATE INDEX IF NOT EXISTS idx_books_isbn ON books(isbn);

DO $migration$
DECLARE
    batch_start integer;
    batch_end integer;
    inserted_count integer;
    adjectives text[] := ARRAY[
        'Silent', 'Hidden', 'Last', 'Bright', 'Lost', 'Secret',
        'Broken', 'Golden', 'Distant', 'Restless', 'Midnight',
        'Crimson', 'Glass', 'Winter', 'Summer', 'Autumn',
        'Burning', 'Wandering', 'Forgotten', 'Emerald',
        'Iron', 'Silver', 'Ancient', 'Modern', 'Blue',
        'Wild', 'Gentle', 'Fierce', 'Hollow', 'Endless'
    ];
    nouns text[] := ARRAY[
        'Library', 'Archive', 'Voyage', 'City', 'Garden',
        'Harbor', 'River', 'Empire', 'Kingdom', 'Machine',
        'Labyrinth', 'Theater', 'Mountain', 'Forest', 'Museum',
        'Island', 'Desert', 'Market', 'Observatory', 'Cathedral'
    ];
    places text[] := ARRAY[
        'Alexandria', 'Paris', 'London', 'Kyoto', 'Lisbon',
        'Prague', 'Istanbul', 'Cairo', 'Dublin', 'Florence',
        'Vienna', 'Madrid', 'Boston', 'Montreal', 'Calcutta',
        'Lagos', 'Buenos Aires', 'Melbourne', 'Stockholm', 'Athens'
    ];
    forms text[] := ARRAY[
        'Chronicles', 'Collected Papers', 'Letters', 'Fragments',
        'Stories', 'Notebooks', 'Fables', 'Essays', 'Memoirs',
        'Dispatches'
    ];
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM authors
        WHERE id::text LIKE '10000000-0000-0000-0000-%'
    ) THEN
        RAISE EXCEPTION 'seed authors are required before seeding more books';
    END IF;

    FOR batch_start IN 10001..100000 BY 5000 LOOP
        batch_end := LEAST(batch_start + 4999, 100000);
        RAISE NOTICE '003_seed_more_books inserting books %-%', batch_start, batch_end;

        WITH
        author_rank AS (
            SELECT
                id,
                row_number() OVER (ORDER BY id) AS rn,
                count(*) OVER () AS total
            FROM authors
            WHERE id::text LIKE '10000000-0000-0000-0000-%'
        ),
        book_seed AS (
            SELECT
                ('20000000-0000-0000-0000-' || lpad(series.n::text, 12, '0'))::uuid AS id,
                authors.id AS author_id,
                'The '
                    || adjectives[((series.n - 1) % array_length(adjectives, 1)) + 1]
                    || ' '
                    || nouns[((series.n - 1) % array_length(nouns, 1)) + 1]
                    || ' of '
                    || places[((series.n - 1) % array_length(places, 1)) + 1]
                    || ': '
                    || forms[((series.n - 1) % array_length(forms, 1)) + 1]
                    || ' '
                    || series.n AS title,
                '978' || lpad(series.n::text, 10, '0') AS isbn,
                1850 + (series.n % 175) AS published_year
            FROM generate_series(batch_start, batch_end) AS series(n)
            JOIN author_rank authors
                ON authors.rn = ((series.n - 1) % authors.total) + 1
        )
        INSERT INTO books (id, author_id, title, isbn, published_year, created_at, updated_at)
        SELECT id, author_id, title, isbn, published_year, NOW(), NOW()
        FROM book_seed seed
        WHERE NOT EXISTS (
            SELECT 1
            FROM books existing
            WHERE existing.id = seed.id
        )
        AND NOT EXISTS (
            SELECT 1
            FROM books existing
            WHERE existing.isbn = seed.isbn
        );

        GET DIAGNOSTICS inserted_count = ROW_COUNT;
        RAISE NOTICE '003_seed_more_books inserted % rows for books %-%', inserted_count, batch_start, batch_end;
    END LOOP;
END
$migration$;

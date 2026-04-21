package migrator

import "testing"

func TestSplitSQLHandlesDollarQuotedBlocks(t *testing.T) {
	sql := `
CREATE TABLE example (id int);
DO $migration$
BEGIN
  RAISE NOTICE 'hello; still same statement';
END
$migration$;
INSERT INTO example VALUES (1);
`

	statements := SplitSQL(sql)
	if len(statements) != 3 {
		t.Fatalf("expected 3 statements, got %d: %#v", len(statements), statements)
	}
}

func TestSplitSQLHandlesQuotedSemicolon(t *testing.T) {
	statements := SplitSQL(`INSERT INTO example VALUES ('a;b'); SELECT 1;`)
	if len(statements) != 2 {
		t.Fatalf("expected 2 statements, got %d", len(statements))
	}
}

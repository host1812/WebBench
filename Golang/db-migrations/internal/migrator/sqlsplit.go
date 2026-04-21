package migrator

import "strings"

func SplitSQL(sql string) []string {
	var statements []string
	var current strings.Builder

	inSingleQuote := false
	inDoubleQuote := false
	inLineComment := false
	inBlockComment := false
	dollarTag := ""

	for i := 0; i < len(sql); i++ {
		ch := sql[i]
		next := byte(0)
		if i+1 < len(sql) {
			next = sql[i+1]
		}

		current.WriteByte(ch)

		if inLineComment {
			if ch == '\n' {
				inLineComment = false
			}
			continue
		}
		if inBlockComment {
			if ch == '*' && next == '/' {
				current.WriteByte(next)
				i++
				inBlockComment = false
			}
			continue
		}
		if dollarTag != "" {
			if strings.HasPrefix(sql[i:], dollarTag) {
				for j := 1; j < len(dollarTag); j++ {
					current.WriteByte(sql[i+j])
				}
				i += len(dollarTag) - 1
				dollarTag = ""
			}
			continue
		}

		if inSingleQuote {
			if ch == '\'' && next == '\'' {
				current.WriteByte(next)
				i++
				continue
			}
			if ch == '\'' {
				inSingleQuote = false
			}
			continue
		}
		if inDoubleQuote {
			if ch == '"' && next == '"' {
				current.WriteByte(next)
				i++
				continue
			}
			if ch == '"' {
				inDoubleQuote = false
			}
			continue
		}

		switch {
		case ch == '-' && next == '-':
			current.WriteByte(next)
			i++
			inLineComment = true
		case ch == '/' && next == '*':
			current.WriteByte(next)
			i++
			inBlockComment = true
		case ch == '\'':
			inSingleQuote = true
		case ch == '"':
			inDoubleQuote = true
		case ch == '$':
			if tag, ok := readDollarTag(sql[i:]); ok {
				for j := 1; j < len(tag); j++ {
					current.WriteByte(sql[i+j])
				}
				i += len(tag) - 1
				dollarTag = tag
			}
		case ch == ';':
			statement := strings.TrimSpace(current.String())
			if statement != "" {
				statements = append(statements, statement)
			}
			current.Reset()
		}
	}

	if statement := strings.TrimSpace(current.String()); statement != "" {
		statements = append(statements, statement)
	}

	return statements
}

func readDollarTag(input string) (string, bool) {
	if len(input) < 2 || input[0] != '$' {
		return "", false
	}
	for i := 1; i < len(input); i++ {
		ch := input[i]
		if ch == '$' {
			return input[:i+1], true
		}
		if !(ch == '_' || ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z' || ch >= '0' && ch <= '9') {
			return "", false
		}
	}
	return "", false
}

func SummarizeSQL(statement string) string {
	lines := strings.Split(statement, "\n")
	for _, line := range lines {
		line = strings.TrimSpace(line)
		if line == "" || strings.HasPrefix(line, "--") {
			continue
		}
		fields := strings.Fields(line)
		summary := strings.Join(fields, " ")
		if len(summary) > 120 {
			return summary[:120] + "..."
		}
		return summary
	}
	return "sql statement"
}

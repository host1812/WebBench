package main

import (
	"errors"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestParseLimitDefaultsToTenThousand(t *testing.T) {
	request := httptest.NewRequest(http.MethodGet, "/api/v1/books", nil)

	limit, err := parseLimit(request)
	if err != nil {
		t.Fatalf("parseLimit returned error: %v", err)
	}
	if limit != defaultBookListLimit {
		t.Fatalf("expected %d, got %d", defaultBookListLimit, limit)
	}
}

func TestParseLimitRejectsInvalidValues(t *testing.T) {
	tests := []string{
		"/api/v1/books?limit=0",
		"/api/v1/books?limit=100001",
		"/api/v1/books?limit=abc",
	}

	for _, rawURL := range tests {
		request := httptest.NewRequest(http.MethodGet, rawURL, nil)
		_, err := parseLimit(request)
		if !errors.Is(err, errValidation) {
			t.Fatalf("expected validation error for %s, got %v", rawURL, err)
		}
	}
}

func TestValidateAuthorInputRejectsMissingName(t *testing.T) {
	_, _, err := validateAuthorInput("   ", "bio")
	if !errors.Is(err, errValidation) {
		t.Fatalf("expected validation error, got %v", err)
	}
}

func TestRoutesReturnBadRequestBeforeDatabaseWork(t *testing.T) {
	server := newApp(nil, "books-service-test").routes()

	request := httptest.NewRequest(http.MethodGet, "/api/v1/books?author_id=not-a-uuid", nil)
	recorder := httptest.NewRecorder()
	server.ServeHTTP(recorder, request)

	if recorder.Code != http.StatusBadRequest {
		t.Fatalf("expected status %d, got %d", http.StatusBadRequest, recorder.Code)
	}
}

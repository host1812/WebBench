package telemetry

import (
	"errors"

	"go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/trace"
)

func RecordSpanError(span trace.Span, err error) {
	if err == nil {
		return
	}
	span.RecordError(err)
	span.SetStatus(codes.Error, err.Error())
}

func RecordSpanErrorUnless(span trace.Span, err error, ignored ...error) {
	if err == nil {
		return
	}
	for _, ignoredErr := range ignored {
		if errors.Is(err, ignoredErr) {
			return
		}
	}
	RecordSpanError(span, err)
}

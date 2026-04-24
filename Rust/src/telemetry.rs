use std::time::Duration;

use opentelemetry::{KeyValue, trace::TracerProvider as _};
use opentelemetry_otlp::{Protocol, WithExportConfig};
use opentelemetry_sdk::{Resource, propagation::TraceContextPropagator, trace::SdkTracerProvider};
use tracing::info;
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt};

use crate::{config::AppConfig, error::AppError};

pub struct TelemetryGuard {
    tracer_provider: Option<SdkTracerProvider>,
}

impl TelemetryGuard {
    fn disabled() -> Self {
        Self {
            tracer_provider: None,
        }
    }

    fn enabled(tracer_provider: SdkTracerProvider) -> Self {
        Self {
            tracer_provider: Some(tracer_provider),
        }
    }

    fn is_enabled(&self) -> bool {
        self.tracer_provider.is_some()
    }
}

impl Drop for TelemetryGuard {
    fn drop(&mut self) {
        if let Some(provider) = self.tracer_provider.take() {
            if let Err(error) = provider.shutdown() {
                eprintln!("failed to shutdown OpenTelemetry tracer provider: {error}");
            }
        }
    }
}

pub fn init_tracing(config: &AppConfig) -> Result<TelemetryGuard, AppError> {
    opentelemetry::global::set_text_map_propagator(TraceContextPropagator::new());

    let filter = tracing_subscriber::EnvFilter::try_from_default_env().unwrap_or_else(|_| {
        tracing_subscriber::EnvFilter::new(config.observability.log_filter.clone())
    });
    let fmt_layer = tracing_subscriber::fmt::layer()
        .with_target(false)
        .compact();

    let telemetry = create_otlp_telemetry(config)?;
    let otel_layer = telemetry.tracer_provider.as_ref().map(|provider| {
        let tracer = provider.tracer(config.observability.service_name.clone());
        tracing_opentelemetry::layer().with_tracer(tracer)
    });

    tracing_subscriber::registry()
        .with(filter)
        .with(fmt_layer)
        .with(otel_layer)
        .init();

    if telemetry.is_enabled() {
        info!(
            service.name = %config.observability.service_name,
            deployment.environment = %config.observability.environment,
            telemetry.endpoint = %normalize_otlp_endpoint(
                config.observability.otlp_endpoint.as_deref().unwrap_or_default()
            )?,
            "OpenTelemetry collector telemetry enabled"
        );
    } else {
        info!("OpenTelemetry collector telemetry disabled");
    }

    Ok(telemetry)
}

fn create_otlp_telemetry(config: &AppConfig) -> Result<TelemetryGuard, AppError> {
    if config.observability.telemetry_enabled == Some(false) {
        return Ok(TelemetryGuard::disabled());
    }

    let endpoint = config
        .observability
        .otlp_endpoint
        .as_deref()
        .map(str::trim)
        .filter(|value| !value.is_empty());

    let Some(endpoint) = endpoint else {
        if config.observability.telemetry_enabled == Some(true) {
            return Err(AppError::Validation(
                "BOOKSVC_TELEMETRY_OTLP_ENDPOINT is required when telemetry is enabled".to_owned(),
            ));
        }

        return Ok(TelemetryGuard::disabled());
    };

    let exporter = opentelemetry_otlp::SpanExporter::builder()
        .with_http()
        .with_protocol(Protocol::HttpBinary)
        .with_endpoint(normalize_otlp_endpoint(endpoint)?)
        .with_timeout(Duration::from_secs(10))
        .build()
        .map_err(|error| AppError::Telemetry(error.to_string()))?;

    let resource = Resource::builder_empty()
        .with_service_name(config.observability.service_name.clone())
        .with_attribute(KeyValue::new(
            "deployment.environment.name",
            config.observability.environment.clone(),
        ))
        .with_attribute(KeyValue::new(
            "telemetry.destination",
            "azure-application-insights",
        ))
        .build();

    let tracer_provider = SdkTracerProvider::builder()
        .with_resource(resource)
        .with_batch_exporter(exporter)
        .build();

    Ok(TelemetryGuard::enabled(tracer_provider))
}

fn normalize_otlp_endpoint(endpoint: &str) -> Result<String, AppError> {
    let trimmed = endpoint.trim();
    if trimmed.is_empty() {
        return Err(AppError::Validation(
            "BOOKSVC_TELEMETRY_OTLP_ENDPOINT is required when telemetry is enabled".to_owned(),
        ));
    }

    let with_scheme = if trimmed.starts_with("http://") || trimmed.starts_with("https://") {
        trimmed.to_owned()
    } else {
        format!("http://{trimmed}")
    };

    let path = with_scheme.split_once("://").map(|(_, rest)| rest);
    if path.is_none() || path == Some("") {
        return Err(AppError::Validation(format!(
            "BOOKSVC_TELEMETRY_OTLP_ENDPOINT must be a valid collector address, got '{endpoint}'"
        )));
    }

    let normalized = with_scheme.trim_end_matches('/');
    if normalized.ends_with("/v1/traces") {
        Ok(normalized.to_owned())
    } else {
        Ok(format!("{normalized}/v1/traces"))
    }
}

#[cfg(test)]
mod tests {
    use crate::config::{AppConfig, DatabaseConfig, ObservabilityConfig, ServerConfig};

    use super::{create_otlp_telemetry, normalize_otlp_endpoint};

    #[test]
    fn telemetry_is_disabled_when_explicitly_disabled() {
        let mut config = test_config();
        config.observability.telemetry_enabled = Some(false);
        config.observability.otlp_endpoint = Some("otel-collector:4318".to_owned());

        let guard = create_otlp_telemetry(&config).unwrap();

        assert!(!guard.is_enabled());
    }

    #[test]
    fn telemetry_requires_otlp_endpoint_when_enabled() {
        let mut config = test_config();
        config.observability.telemetry_enabled = Some(true);

        let error = match create_otlp_telemetry(&config) {
            Ok(_) => panic!("telemetry should require an OTLP endpoint when enabled"),
            Err(error) => error,
        };

        assert!(
            error
                .to_string()
                .contains("BOOKSVC_TELEMETRY_OTLP_ENDPOINT")
        );
    }

    #[test]
    fn telemetry_is_enabled_when_otlp_endpoint_is_configured() {
        let mut config = test_config();
        config.observability.telemetry_enabled = Some(true);
        config.observability.otlp_endpoint = Some("otel-collector:4318".to_owned());

        let guard = create_otlp_telemetry(&config).unwrap();

        assert!(guard.is_enabled());
    }

    #[test]
    fn normalize_otlp_endpoint_adds_default_scheme_and_trace_path() {
        let endpoint = normalize_otlp_endpoint("otel-collector:4318").unwrap();

        assert_eq!(endpoint, "http://otel-collector:4318/v1/traces");
    }

    #[test]
    fn normalize_otlp_endpoint_preserves_existing_trace_path() {
        let endpoint = normalize_otlp_endpoint("https://collector.example/v1/traces").unwrap();

        assert_eq!(endpoint, "https://collector.example/v1/traces");
    }

    fn test_config() -> AppConfig {
        AppConfig {
            server: ServerConfig::default(),
            database: DatabaseConfig {
                connection_string: "postgres://postgres:postgres@postgres:5432/books".to_owned(),
                max_connections: 10,
            },
            observability: ObservabilityConfig::default(),
        }
    }
}

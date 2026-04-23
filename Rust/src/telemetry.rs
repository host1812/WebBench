use opentelemetry::{KeyValue, trace::TracerProvider as _};
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

    let telemetry = create_application_insights_telemetry(config)?;
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
            "application insights telemetry enabled"
        );
    } else {
        info!("application insights telemetry disabled");
    }

    Ok(telemetry)
}

fn create_application_insights_telemetry(config: &AppConfig) -> Result<TelemetryGuard, AppError> {
    if config.observability.telemetry_enabled == Some(false) {
        return Ok(TelemetryGuard::disabled());
    }

    let connection_string = config
        .observability
        .application_insights_connection_string
        .as_deref()
        .map(str::trim)
        .filter(|value| !value.is_empty());

    let Some(connection_string) = connection_string else {
        if config.observability.telemetry_enabled == Some(true) {
            return Err(AppError::Validation(
                "APPLICATIONINSIGHTS_CONNECTION_STRING is required when telemetry is enabled"
                    .to_owned(),
            ));
        }

        return Ok(TelemetryGuard::disabled());
    };

    // with_batch_exporter runs exports on a dedicated thread, so it must use a sync client.
    // Build the blocking client off the Tokio runtime to avoid reqwest blocking-client panics.
    let client = std::thread::spawn(reqwest::blocking::Client::new)
        .join()
        .map_err(|_| AppError::Telemetry("failed to create telemetry HTTP client".to_owned()))?;

    let exporter = opentelemetry_application_insights::Exporter::new_from_connection_string(
        connection_string.to_owned(),
        client,
    )
    .map_err(|error| AppError::Telemetry(error.to_string()))?;

    let resource = Resource::builder_empty()
        .with_service_name(config.observability.service_name.clone())
        .with_attribute(KeyValue::new(
            "deployment.environment.name",
            config.observability.environment.clone(),
        ))
        .build();

    let tracer_provider = SdkTracerProvider::builder()
        .with_resource(resource)
        .with_batch_exporter(exporter)
        .build();

    Ok(TelemetryGuard::enabled(tracer_provider))
}

impl TelemetryGuard {
    fn is_enabled(&self) -> bool {
        self.tracer_provider.is_some()
    }
}

#[cfg(test)]
mod tests {
    use crate::config::{AppConfig, DatabaseConfig, ObservabilityConfig, ServerConfig};

    use super::create_application_insights_telemetry;

    #[test]
    fn telemetry_is_disabled_when_explicitly_disabled() {
        let mut config = test_config();
        config.observability.telemetry_enabled = Some(false);
        config.observability.application_insights_connection_string =
            Some(connection_string().to_owned());

        let guard = create_application_insights_telemetry(&config).unwrap();

        assert!(!guard.is_enabled());
    }

    #[test]
    fn telemetry_requires_connection_string_when_enabled() {
        let mut config = test_config();
        config.observability.telemetry_enabled = Some(true);

        let error = match create_application_insights_telemetry(&config) {
            Ok(_) => panic!("telemetry should require a connection string when enabled"),
            Err(error) => error,
        };

        assert!(
            error
                .to_string()
                .contains("APPLICATIONINSIGHTS_CONNECTION_STRING")
        );
    }

    #[test]
    fn telemetry_is_enabled_when_connection_string_is_configured() {
        let mut config = test_config();
        config.observability.telemetry_enabled = Some(true);
        config.observability.application_insights_connection_string =
            Some(connection_string().to_owned());

        let guard = create_application_insights_telemetry(&config).unwrap();

        assert!(guard.is_enabled());
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

    fn connection_string() -> &'static str {
        "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://example.applicationinsights.azure.com/"
    }
}

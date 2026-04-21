use opentelemetry::{KeyValue, trace::TracerProvider as _};
use opentelemetry_sdk::{Resource, propagation::TraceContextPropagator, trace::SdkTracerProvider};
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

    Ok(telemetry)
}

fn create_application_insights_telemetry(config: &AppConfig) -> Result<TelemetryGuard, AppError> {
    if config.observability.telemetry_enabled == Some(false) {
        return Ok(TelemetryGuard::disabled());
    }

    let Some(connection_string) = config
        .observability
        .application_insights_connection_string
        .as_deref()
        .map(str::trim)
        .filter(|value| !value.is_empty())
    else {
        return Ok(TelemetryGuard::disabled());
    };

    let exporter = opentelemetry_application_insights::Exporter::new_from_connection_string(
        connection_string.to_owned(),
        reqwest::Client::new(),
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

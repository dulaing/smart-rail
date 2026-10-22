import {
  context,
  metrics,
  propagation,
  ROOT_CONTEXT,
  SpanKind,
  SpanStatusCode,
  trace,
  type Attributes,
  type Span,
} from "@opentelemetry/api";
import { AsyncLocalStorageContextManager } from "@opentelemetry/context-async-hooks";
import { W3CTraceContextPropagator } from "@opentelemetry/core";
import { OTLPMetricExporter } from "@opentelemetry/exporter-metrics-otlp-http";
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-http";
import { resourceFromAttributes } from "@opentelemetry/resources";
import { MeterProvider, PeriodicExportingMetricReader } from "@opentelemetry/sdk-metrics";
import { BatchSpanProcessor, type SpanProcessor } from "@opentelemetry/sdk-trace-base";
import { NodeTracerProvider } from "@opentelemetry/sdk-trace-node";

export interface TelemetryOptions {
  env?: NodeJS.ProcessEnv;
  /** Extra span processors, for tests that read the finished spans. */
  spanProcessors?: SpanProcessor[];
}

export interface Telemetry {
  shutdown(): Promise<void>;
}

// Always installs trace propagation, so a request's trace identity is carried into the callbacks it
// causes even when nothing is collecting spans. When OTEL_EXPORTER_OTLP_ENDPOINT is set (the Aspire
// AppHost sets it) spans and metrics are also exported there. Nothing recorded carries a pickup
// code, a credential, a signature or a body.
export function startTelemetry({ env = process.env, spanProcessors = [] }: TelemetryOptions = {}): Telemetry {
  const endpoint = env["OTEL_EXPORTER_OTLP_ENDPOINT"];
  // Grilled exports OTLP over HTTP. A gRPC-only endpoint would only produce errors on both sides, so
  // it is left alone (set the collector's HTTP endpoint instead).
  const exporting = Boolean(endpoint) && (env["OTEL_EXPORTER_OTLP_PROTOCOL"] ?? "http/protobuf") !== "grpc";
  if (endpoint) {
    console.log(exporting ? `Telemetry: exporting to ${endpoint}` : "Telemetry: the OTLP endpoint is gRPC-only, so spans and metrics are not exported");
  }
  const resource = resourceFromAttributes({ "service.name": env["OTEL_SERVICE_NAME"] || "grilled" });

  const processors = [...spanProcessors];
  if (exporting) processors.push(new BatchSpanProcessor(new OTLPTraceExporter()));
  const tracerProvider = new NodeTracerProvider({ resource, spanProcessors: processors });
  tracerProvider.register({ propagator: new W3CTraceContextPropagator(), contextManager: new AsyncLocalStorageContextManager().enable() });

  let meterProvider: MeterProvider | undefined;
  if (exporting) {
    meterProvider = new MeterProvider({
      resource,
      readers: [new PeriodicExportingMetricReader({ exporter: new OTLPMetricExporter(), exportIntervalMillis: 10_000 })],
    });
    metrics.setGlobalMeterProvider(meterProvider);
  }

  return {
    shutdown: async () => {
      await tracerProvider.shutdown();
      await meterProvider?.shutdown();
    },
  };
}

const tracer = trace.getTracer("grilled");
const meter = metrics.getMeter("grilled");

export const callbackAttempts = meter.createCounter("grilled.callbacks.attempts", {
  description: "Status callback delivery attempts, by result (delivered, transient, permanent).",
});
export const eventsReceived = meter.createCounter("grilled.events.received", {
  description: "Train-change events received from SmartRail, by result (APPLIED, STALE, DUPLICATE, conflict).",
});
export const ordersPlaced = meter.createCounter("grilled.orders.placed", {
  description: "Placement requests, by outcome (created, existing, refused).",
});

// The traceparent of the current span, to store with a change or send on a call.
export function currentTraceparent(): string | undefined {
  const carrier: Record<string, string> = {};
  propagation.inject(context.active(), carrier);
  return carrier["traceparent"];
}

export function injectTraceparent(headers: Record<string, string>): Record<string, string> {
  const traceparent = currentTraceparent();
  return traceparent ? { ...headers, traceparent } : headers;
}

// Runs `work` inside a new span. A given traceparent (from an incoming request, or one stored with an
// outbox row) is its parent; otherwise it continues the current trace.
export async function withSpan<T>(
  name: string,
  parent: string | undefined,
  attributes: Attributes,
  work: (span: Span) => Promise<T> | T,
  kind: SpanKind = SpanKind.INTERNAL,
): Promise<T> {
  const parentContext = parent ? propagation.extract(ROOT_CONTEXT, { traceparent: parent }) : context.active();
  const span = tracer.startSpan(name, { kind, attributes }, parentContext);
  try {
    return await context.with(trace.setSpan(parentContext, span), () => work(span));
  } catch (error) {
    span.setStatus({ code: SpanStatusCode.ERROR, message: error instanceof Error ? error.name : "error" });
    throw error;
  } finally {
    span.end();
  }
}

export { SpanKind, SpanStatusCode };

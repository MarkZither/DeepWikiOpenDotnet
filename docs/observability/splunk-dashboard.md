# Splunk dashboard — Generation service (TTF / tokens / errors)

This file contains example SPL queries and panel guidance you can import into Splunk (Dashboard Studio or Classic Simple XML).

Panels (recommended)

1) Time-to-first-token (TTF) — histogram / p50, p95
SPL (timechart):
```
| mstats avg(_value) WHERE metric_name="generation.ttf" earliest=-30m@h by provider span=1m
| timechart avg(_value) as "TTF (ms)" by provider
```

2) Tokens per second (throughput)
```
| mstats avg(_value) WHERE metric_name="generation.tokens_per_second" earliest=-30m@h by provider span=1m
| timechart avg(_value) as "tokens/s" by provider
```

3) Total tokens (counter)
```
| mstats sum(_value) WHERE metric_name="generation.tokens" earliest=-24h by provider
| timechart sum(_value) as "tokens_total" by provider
```

4) Error rate by type
```
| mstats sum(_value) WHERE metric_name="generation.errors" earliest=-6h by error_type,provider
| chart sum(_value) as errors by error_type, provider
```

5) Recent error events (log-style) — helpful for troubleshooting
```
index=main OR index=errors "provider failed" OR "provider_error" | table _time, host, source, sourcetype, _raw
```

6) Active sessions / in-flight prompts (optional)
- If you export a custom metric for active sessions, query `metric_name="deepwiki.sessions_active"`.
- Otherwise use a log-based panel that searches for `Session created` / `Prompt submitted` messages.

How to import

- In Splunk Dashboard Studio, create a new dashboard and add panels using the SPL queries above.
- For Classic dashboards, use Simple XML panels with the same SPL queries.

OTel → Splunk notes

- Configure the app to export OTLP to Splunk (collector or Splunk Observability endpoint):
  - Environment variable: `OTEL_EXPORTER_SPLUNK_ENDPOINT` (e.g. `https://ingest.YOUR_REGION.signalfx.com/v2/otlp`)
  - Optional headers: `OTEL_EXPORTER_SPLUNK_HEADERS` (e.g. `X-SF-TOKEN <token>` or `Authorization=Splunk <token>` depending on your collector)
- The service already supports `OTEL_EXPORTER_OTLP_ENDPOINT` (existing) and now supports `OTEL_EXPORTER_SPLUNK_ENDPOINT` for a second export target.

Podman / docker-compose

- The existing `docker-compose.yml` and `docker-compose.test.yml` are compatible with `podman-compose`.
- To test locally you can run an OTLP-capable Splunk Collector or point to your Splunk instance and set the environment variables listed above.

Example Splunk panel mapping (metric → field):
- Metric `generation.ttf` → `metric_name = "generation.ttf"`, dimension `provider`
- Metric `generation.tokens` → `metric_name = "generation.tokens"`
- Metric `generation.errors` → `metric_name = "generation.errors"`, labels `error_type`, `provider`

If you want, I can add a Simple XML dashboard file next and a short podman-compose test snippet — which would you prefer?
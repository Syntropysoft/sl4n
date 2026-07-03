![SyntropyLog](https://camo.githubusercontent.com/cbce90a3a43bbeda6494c1ddbfb309c0cb1e1d8c8b5abaa0f875b1d9dd8b5da7/68747470733a2f2f73796e74726f7079736f66742e636f6d2f73796e74726f70796c6f672d6c6f676f2e706e67)

# sl4n — Syntropy Log for .NET

**The declarative observability framework for .NET — built on Microsoft.Extensions.Logging.**
Correlation IDs, PII masking, per-level field control and retention — declared **once** and enforced on
every log, before it reaches any transport. Fail-safe by design (logging can never crash your app) and
NativeAOT-compatible.

[![NuGet](https://img.shields.io/nuget/v/sl4n.svg)](https://www.nuget.org/packages/sl4n) ![License: Apache 2.0](https://img.shields.io/badge/license-Apache%202.0-blue.svg) ![.NET 8+](https://img.shields.io/badge/.NET-8%2B-blue.svg) ![NativeAOT compatible](https://img.shields.io/badge/NativeAOT-compatible-brightgreen.svg)

> **sl4n is the .NET counterpart of [SyntropyLog](https://github.com/Syntropysoft/SyntropyLog)** — the
> Node.js / TypeScript observability framework (also on [npm as `syntropylog`](https://www.npmjs.com/package/syntropylog)).
> Same concepts and declarative model, adapted idiomatically to .NET: it rides on
> `Microsoft.Extensions.Logging` (you keep `ILogger<T>`) instead of a bespoke logger, is configured
> through DI (`AddSl4n`) instead of a global `init()`, and needs **no native addon** — .NET already
> compiles to native, so the whole pipeline is NativeAOT-safe.

---

## Quick start

```bash
dotnet add package sl4n
dotnet add package sl4n.AspNetCore
```

```csharp
// Program.cs — one line wires the whole pipeline into Microsoft.Extensions.Logging
builder.Services.AddSl4n(builder.Configuration.GetSection("sl4n"));
builder.Services.AddSl4nAspNetCore();
app.UseSl4n();
```

```csharp
// Any service — you keep using the standard ILogger<T>. Nothing bespoke.
public class PaymentService(ILogger<PaymentService> logger)
{
    public void Charge(decimal amount, string email) =>
        logger.LogInformation("Card charged {Amount} for {Email}", amount, email);
}
```

What lands on the console (structured JSON, one line):

```json
{"timestamp":"2026-06-20T13:11:48.0600000+00:00","level":"information","category":"PaymentService","message":"Card charged 299.9 for j**n@example.com","correlationId":"req-001","Amount":299.9,"Email":"j**n@example.com"}
```

`correlationId` was propagated automatically from the inbound HTTP header; `Email` was masked automatically — **by configuration, not magic**. Your business code is unchanged. From here you shape it: which fields each level emits, masking rules, where logs go, retention — each declared once. The sections below are how.

> **Masking is by field name.** A field whose *key* matches a rule is masked; free text you concatenate into the message is not. Pass sensitive data as **keyed structured fields** (`"… {Email}", email`) — not baked into the message string.

---

## What sl4n is

> **Not a logger — a log pipeline on top of MEL.** With Serilog, NLog, or raw `Microsoft.Extensions.Logging` you wire correlation IDs, PII redaction, and per-level field control yourself, in every service. sl4n does it for you: declare it **once** in `AddSl4n(...)`, and it runs on every `ILogger` call, in every async chain, across every service — before the entry reaches the **console, a database, an HTTP sink, or wherever your `ITransport` sends it.**

Every .NET team writes the same boilerplate: thread `correlationId` through every method signature, scrub `password`/`email` before logging, repeat the same `using (_logger.BeginScope(...))` on every controller action. sl4n solves that **declaratively** — you declare the rules once at startup and the framework enforces them on every log call.

It is scoped on purpose: sl4n owns the **log pipeline up to the moment of persistence** — matrix filtering, context propagation, masking, sanitization, serialization, retention metadata. **It does not manage any backend** (no Redis/HTTP/DB clients in the core — its only dependencies are `Microsoft.Extensions.*`). Where the entry goes is an `ITransport` you write.

Five pillars:

- **Logging Matrix** — a declarative whitelist of context fields per log level. If a field isn't in the matrix for that level, it never reaches a transport. Field control by config, not code review.
- **Masking** — PII redacted **by field name**, before any transport, in a never-throwing pipeline with a ReDoS guard. Non-strings under a sensitive key are redacted whole.
- **Retention-aware tagging** — `BeginRetentionScope("SOX_AUDIT_TRAIL")` stamps compliance metadata onto every log in scope so a downstream store routes it by policy.
- **Silent-observer safety** — sanitization, per-transport failure isolation, never-throw masking. Logging cannot crash your app; failures surface through hooks and `Sl4nStats` counters.
- **Durable buffer** — `DurableFileTransport` is a self-emptying disk spool that keeps audit entries safe across a backend outage — no rotation, no maintenance.

Built on `Microsoft.Extensions.Logging` (you keep `ILogger<T>`), DI-native, and **NativeAOT-compatible** (reflection-free pipeline).

---

## How it compares

Serilog and NLog are excellent logging frameworks. sl4n is a different layer — a **pipeline** that does *in the framework* what a logger leaves to you, running on top of MEL so you keep the standard `ILogger`:

| | Serilog / NLog / raw MEL | sl4n |
|---|---|---|
| **Category** | logger | log pipeline (matrix → masking → sanitization → serialization → routing) |
| **PII masking** | DIY (enrichers / destructuring policies) | built in, **by field name**, before any sink, never-throws |
| **Correlation IDs** | you thread them / manual scopes | automatic via `AsyncLocal` + drop-in middleware, declared once |
| **Per-level field control** | manual | declarative **Logging Matrix** |
| **Retention / audit tagging** | DIY | first-class `BeginRetentionScope` — travels with the entry |
| **If logging throws** | can bubble into your code | **silent observer** — logging never throws |
| **Durability across outages** | sink-specific | `DurableFileTransport` — self-emptying disk buffer |
| **NativeAOT** | varies | AOT-compatible — `[GeneratedRegex]`, `Utf8JsonWriter`, no reflection |

---

## The declarative shift

With Serilog/NLog/MEL you **write logging**. With sl4n you **declare observability**.

| Instead of… | You declare… | sl4n does automatically |
|---|---|---|
| Threading `correlationId` through every method | `app.UseSl4n()` | Extracts inbound headers, pushes context to every log in the request via `AsyncLocal` |
| Scrubbing sensitive fields before logging | `"masking": { "enableDefaultRules": true }` | Masks email, password, token, card, ssn, phone on every log |
| Repeating `BeginScope(new { correlationId })` everywhere | Configured once | Scope opened by the middleware — available to every `ILogger` in the call chain |
| Deciding which fields each level logs | `loggingMatrix` | Whitelists context fields per level; the rest never reach a transport |
| Building outbound headers per downstream target | `context.outbound` | `Sl4nDelegatingHandler` injects the right wire names per destination |
| Routing compliance logs manually | `BeginRetentionScope('SOX…')` | Stamps `retention`/`retentionClass`/`retentionDays` on every log in scope |

---

## A fuller example

The same start, now with a logging matrix (field control per level) and masking — all declarative:

```json
// appsettings.json
{
  "sl4n": {
    "masking": { "enableDefaultRules": true },
    "loggingMatrix": {
      "default":     ["correlationId"],
      "information": ["correlationId", "userId", "operation"],
      "error":       ["*"]
    },
    "context": {
      "source": "frontend",
      "autoGenerate": ["correlationId"],
      "inbound":  { "frontend": { "correlationId": "X-Correlation-ID", "traceId": "X-Trace-ID" } },
      "outbound": { "http":     { "correlationId": "X-Correlation-ID", "traceId": "X-Trace-ID" } }
    }
  }
}
```

```csharp
builder.Services.AddSl4n(builder.Configuration.GetSection("sl4n"));
builder.Services.AddSl4nAspNetCore();
app.UseSl4n();
// No transport configured ⇒ structured JSON to the console by default.
```

---

## Two pipelines, never mixed

```
HTTP request ──► Sl4nMiddleware
                   ├─► Sl4nContext.Push()   ──► propagation pipeline (AsyncLocal)
                   └─► logger.BeginScope() ──► log pipeline (MEL scope)
```

| Pipeline | Carrier | Responsibility |
|----------|---------|----------------|
| **Propagation** | `Sl4nContext` (`AsyncLocal`) | Translate wire names between services |
| **Log** | MEL scope (`BeginScope`) | Attach context fields, mask sensitive values |

`Sl4nLogger` has no knowledge of `Sl4nContext`. `Sl4nDelegatingHandler` has no knowledge of `ILogger`. The middleware is the only component that touches both — in two explicit, separate lines.

---

## Logging Matrix — the differentiator

A declarative contract for **context fields**: a per-level whitelist deciding which of the auto-propagating context values surface at each level. A field not whitelisted for a level never reaches a transport — reviewable as one config object instead of by grepping every log call.

> **Matrix governs context, not per-call metadata.** The fields you pass to `logger.LogInformation("… {Amount}", amount)` are always emitted (and masked) — if you don't want a field logged, don't pass it. The matrix exists for the auto-propagating *context* you can't trim at each call site.

```json
"loggingMatrix": {
  "default":     ["correlationId"],
  "information": ["correlationId", "userId", "operation"],
  "error":       ["*"]
}
```

```csharp
// AOT path — the typed builder keys off LogLevel, so a "not-a-level" typo is a compile error
cfg.LoggingMatrix = new MatrixBuilder()
    .Default("correlationId")
    .Level(LogLevel.Information, "correlationId", "userId", "operation")
    .All(LogLevel.Error)
    .Build();
```

| Key | Meaning |
|-----|---------|
| `default` | Applied to any level not listed explicitly |
| `Trace` … `Critical` | Per-level whitelist — MEL level names, case-insensitive |
| `["*"]` | Allow every context field at that level |

Always define `default`. When no matrix is configured, every context field passes through (backward compatible).

---

## Masking

Masking runs automatically on every entry before it reaches any transport, matched **by field name**.

| Field key (examples) | Strategy | Result |
|---|---|---|
| `email`, `mail` | Email — first + last char | `j**n@example.com` |
| `password`, `pass`, `pwd`, `secret` | Full mask | `**********` |
| `token`, `key`, `auth`, `jwt`, `bearer` | Full mask | `**********` |
| `credit_card`, `card_number` | Last four | `************1234` |
| `ssn`, `social_security` | Last four | `*****6789` |
| `phone`, `mobile`, `tel` | Last four | `******4567` |

Keep the defaults on and add your own rules **on top** — matched by field name. Reference `MaskKeys` instead of string literals to keep patterns out of secret scanners (Sonar S2068):

```json
"masking": {
  "enableDefaultRules": true,
  "regexTimeoutMs": 100,
  "rules": [ { "pattern": "^(cvv|cvc|securityCode)$", "strategy": "FullMask" } ]
}
```

```csharp
cfg.Masking.Rules.Add(new MaskingRuleConfig
{
    Pattern  = MaskKeys.Pattern(MaskKeys.Token),   // ^(token|key|auth|jwt|bearer)$
    Strategy = MaskingStrategy.FullMask,
});
```

Guarantees:

- **Non-string under a sensitive key → `[REDACTED]`.** An object, array, or number under a sensitive-named field is redacted whole, never stringified — nested PII can't leak through.
- **Masking never throws.** A custom-mask exception or a regex timeout redacts that one field fail-secure and fires `OnMaskingError`; logging keeps working.
- **ReDoS guard.** Custom-rule regexes run under `regexTimeoutMs` (default 100 ms). The built-in defaults are linear, source-generated `[GeneratedRegex]`.

> **Scope — by field name, never free text.** A value is masked because its *key* matches a rule, not because the string *looks* sensitive. Masking does **not** deep-walk nested object graphs — that would move a mountain of data on every log and penalize latency. Structure sensitive data as keyed fields; a nested object under a sensitive key is still redacted whole. This mirrors SyntropyLog's by-key spirit, kept allocation-light and AOT-safe for .NET.

---

## Context propagation

Conceptual field names (`correlationId`, `traceId`, `tenantId`) are internal keys. The name that travels **on the wire** is declared by you per source/target; the framework translates at the moment of sending. Application code only ever works with conceptual names.

```
inbound['frontend']   internal context    outbound['http']       outbound['kafka']
──────────────────    ────────────────    ─────────────────      ─────────────────
X-Correlation-ID  ->  correlationId   ->  X-Correlation-ID   /   correlationId
X-Trace-ID        ->  traceId         ->  X-Trace-ID         /   traceId
```

**Inbound** is handled by the ASP.NET Core middleware (`app.UseSl4n()`): it extracts the configured headers for `context.source`, auto-generates any field listed in `context.autoGenerate` (UUID), opens the propagation + log scopes for the request, and echoes response headers when `context.responseTarget` is set.

**Outbound** — inject the wire headers on an outgoing `HttpClient` via `Sl4nDelegatingHandler`:

```csharp
builder.Services.AddHttpClient("downstream")
    .AddHttpMessageHandler(sp =>
        new Sl4nDelegatingHandler(sp.GetRequiredService<IOptions<Sl4nConfig>>(), target: "http"));
```

**Manual context** — push fields outside a request (background jobs, workers):

```csharp
using Sl4nScope scope = Sl4nContext.Push(("correlationId", "req-001"), ("userId", "usr-42"));
Sl4nContext.Set("step", "payment");
```

---

## Retention policies

Declare named retention policies (compliance metadata), then tag the logs that must obey them — the .NET analog of SyntropyLog's `withRetention('SOX_AUDIT_TRAIL')`. The emitted entry carries `retention` / `retentionClass` / `retentionDays` so a downstream store applies the right retention.

```json
"retentionPolicies": {
  "SOX_AUDIT_TRAIL": { "days": 2555, "class": "SOX" },
  "GDPR_STANDARD":   { "days": 365,  "class": "GDPR" }
}
```

```csharp
using (logger.BeginRetentionScope("SOX_AUDIT_TRAIL"))
{
    logger.LogInformation("Payment approved {Amount}", amount);
    // → { …, "retention": "SOX_AUDIT_TRAIL", "retentionClass": "SOX", "retentionDays": 2555 }
}
```

The tag rides a MEL scope, so every log in the call chain inherits it. Retention metadata **bypasses the logging matrix** — it's a structural compliance stamp, not user context.

---

## Transports

Default output is structured JSON (no transport needed). Every entry carries an ISO-8601 `timestamp` captured **at log time** (not at async-worker time). Transports run on a background `Channel` worker, so logging never blocks the request.

| Transport | Output | Use case |
|---|---|---|
| *(default)* `ConsoleTransport` | Structured JSON, one line | Production, log aggregators |
| `ClassicConsoleTransport` | `ts [INF] category: message key=value` | Local development |
| `DurableFileTransport` | Wraps any inner transport, **buffered** | Surviving backend outages |
| `SpyTransport` (`sl4n.Testing`) | In-memory capture | Tests |

```csharp
builder.Services.AddSl4n(/* … */);
builder.Services.UseClassicConsole();          // swap JSON console for the human-readable one
// or: services.UseTransport(myTransport);     // replace all transports with your own ITransport
```

Any `ITransport` you write receives the already-masked, matrix-filtered, sanitized entry — the Universal Adapter role: connect it to a DB, HTTP API, or queue.

### Durable buffering

Wrap any transport in a `DurableFileTransport` to survive backend outages. It's a **buffer, not an archive**: in the happy path entries go straight to the inner transport and disk is untouched. When the inner transport throws, entries spool to a file and retry on later logs and on restart; once the backlog drains, the file is **deleted**. The spool only ever holds the undelivered backlog — no rotation, no rename, no cleanup to maintain.

```csharp
ITransport sink = new MyHttpTransport(/* … */);        // your real sink; may fail transiently
services.UseTransport(new DurableFileTransport(sink, "buffer/spool.jsonl"));
```

A crash mid-outage is recovered on the next startup: the leftover spool is replayed when a fresh `DurableFileTransport` is constructed. Cost is O(1) per log while the sink is down, O(n) once on recovery.

---

## Reliability & observability

Three safety properties hold on every log:

- **Sanitization.** Control characters and ANSI escape sequences are stripped from the message and field values before any transport sees them — a value can't inject a fake log line. (The exception blob is left intact so stack-trace newlines survive.)
- **Transport isolation.** If one transport throws, the others still receive the entry and the worker keeps running. The failure is counted and surfaced via `OnLogFailure`.
- **Masking never throws.** A custom-mask error or a regex timeout redacts that field fail-secure and fires `OnMaskingError`.

Runtime counters are exposed via `Sl4nStats` — the `getStats()` equivalent — resolvable from DI:

```csharp
Sl4nStatsSnapshot s = provider.GetRequiredService<Sl4nStats>().Snapshot();
// s.LogsProcessed · s.TransportFailures · s.DroppedEntries · s.MaskingFailures

services.AddSl4n(cfg =>
{
    cfg.OnLogFailure           = (ex, transport) => Console.Error.WriteLine($"{transport}: {ex.Message}");
    cfg.Masking.OnMaskingError = (ex, field)     => Console.Error.WriteLine($"mask {field}: {ex.Message}");
});
```

---

## Testing

`sl4n.Testing` ships a `SpyTransport` that captures emitted entries — so you assert on exactly what would have shipped: levels, messages, context fields, and **masked** values.

```csharp
var spy = new SpyTransport();
var services = new ServiceCollection();
services.AddSl4n(cfg => cfg.Masking.EnableDefaultRules = true);
services.UseSpyTransport(spy);           // capture entries instead of writing to the console

// … exercise the code under test, then assert on what was emitted:
spy.Entries[0]["Email"].Should().Be("j**n@example.com");   // masking really ran
spy.AtLevel("error").Should().BeEmpty();
spy.AnyMessageContains("information", "Order created").Should().BeTrue();
```

`SpyTransport` surface: `Entries`, `Count`, `Clear()`, `AtLevel(level)`, `WithField(key, value)`, `AnyMessageContains(level, substring)`.

---

## AOT compatibility

sl4n is `IsAotCompatible=true`. Every code path is reflection-free:

- `[GeneratedRegex]` — compile-time regex for the default rules
- `Utf8JsonWriter` / `JsonDocument` — no `JsonSerializer` reflection
- `Action<T>` configuration overload — no `IConfiguration` binding reflection

```csharp
// AOT-safe — the Action<Sl4nConfig> overload
services.AddSl4n(cfg =>
{
    cfg.Masking.EnableDefaultRules = true;
    cfg.Context.Source = "frontend";
});

// Reflection-based (annotated [RequiresUnreferencedCode]) — fine for non-AOT apps
services.AddSl4n(configuration.GetSection("sl4n"));
```

---

## Performance

**Allocations don't vary by machine and ratios travel well — read the story off those, not the raw
nanoseconds.** The ns below are one BenchmarkDotNet run on x86/Windows (.NET 8); an Apple M2, where
much of the work was done, posts materially lower absolute times and a tighter gap to plain MEL. One
`LogInformation` call, active scope (`correlationId` + `traceId`), masked `Email` field:

| Method | Allocated | Mean (x86/Win — indicative) | Ratio |
|--------|----------:|----------------------------:|------:|
| MEL no-op (NullLogger) | 72 B | 39 ns | 1.00 |
| NLog (scope, NullTarget, via MEL) | 496 B | 328 ns | 8.3 |
| MEL working (scope + dict, no masking) | 792 B | 335 ns | 8.5 |
| **sl4n (scope + masking, null transport)** | **494 B** | **490 ns** | **12.4** |
| Serilog (scope, no sinks, via MEL) | 712 B | 1,434 ns | 36.4 |

- **Allocation — the portable number: sl4n 494 B.** Less than a plain MEL scope (792 B), about the
  same as NLog (496 B), ~30% below Serilog (712 B) — *while doing PII masking that none of them do*.
- **Comfortably faster than Serilog** on every machine tested (~3× here).
- Slower in raw ns than a no-masking MEL/NLog — and that gap narrows on ARM/M2. Either way it's the
  **asynchronous handoff**, not masking: `LogInformation` only snapshots scope + enqueues; masking,
  matrix filtering and sanitization run on a background worker, off the request's hot path. The number
  is caller-observed latency, *not* the cost of masking.

> Under this tight loop the `sl4n` row is bimodal (fastest ~330 ns, on par with NLog): firing logs
> back-to-back saturates the async channel, so queued entries survive to Gen1 and add GC jitter. Real
> apps log sparsely — the channel drains between calls and this doesn't appear.

---

## What sl4n is not

It is a structured-logging and context-propagation framework. It is **not** a log aggregation backend (use Elasticsearch / Loki / CloudWatch), a distributed-tracing system (use OpenTelemetry), or a metrics collector (use Prometheus / Datadog). It is the component that makes every log line **correct, consistent, and safe before it reaches any of those systems**.

---

## Security

- **No network I/O at runtime.** sl4n contacts no external URLs; the only output is what your transports produce.
- **No extra runtime dependencies.** The core depends only on `Microsoft.Extensions.*` — already present in any ASP.NET Core app.
- **Hardened pipeline.** Masking never throws, custom regexes run under a ReDoS timeout, control characters are sanitized out, and a failing transport is isolated from the rest.

---

## What's in the box

| Feature | One-liner | API |
|---|---|---|
| **Logging Matrix** | Per-level whitelist of context fields; typed `MatrixBuilder` | `Sl4nConfig.LoggingMatrix` |
| **Masking** | Redact PII by field name before any transport; custom rules, ReDoS guard, never-throws | `MaskingConfig`, `MaskKeys`, `MaskingStrategy` |
| **Context propagation** | Conceptual↔wire header translation via `AsyncLocal` | `Sl4nContext`, `ContextConfig` |
| **ASP.NET Core middleware** | Extract inbound context + open MEL scope per request | `sl4n.AspNetCore`, `app.UseSl4n()` |
| **Outbound propagation** | Inject wire headers on outgoing `HttpClient` calls | `Sl4nDelegatingHandler` |
| **Retention** | Compliance tags stamped on entries, bypassing the matrix | `Sl4nRetention.BeginRetentionScope`, `RetentionPolicy` |
| **Transports** | JSON + classic console; write your own `ITransport` | `ConsoleTransport`, `ClassicConsoleTransport` |
| **Durable buffer** | Self-emptying disk spool that survives outages | `DurableFileTransport` |
| **Silent-observer safety** | Sanitization + transport isolation + never-throw masking | (pipeline) |
| **Self-observability** | Runtime counters — logs, transport/masking failures, drops | `Sl4nStats.Snapshot()` |
| **Testing toolkit** | Capture emitted entries and assert on them | `sl4n.Testing`, `SpyTransport` |
| **NativeAOT** | Reflection-free pipeline (`GeneratedRegex`, `Utf8JsonWriter`) | `IsAotCompatible=true` |

---

## Running the tests & benchmarks

```bash
dotnet test tests/sl4n.Tests/sl4n.Tests.csproj
dotnet run --project benchmarks/sl4n.Benchmarks --configuration Release -- --filter '*ComparativeBenchmark*'
```

---

## License

Apache-2.0 — see [LICENSE](./LICENSE).

<p align="center">
  <img src="https://syntropysoft.com/syntropylog-logo.png" alt="SyntropyLog Logo" width="170"/>
</p>

<h1 align="center">sl4n</h1>
<h2  align="center">Syntropy Log for .Net</h2>

<p align="center">
  <strong>The Declarative Observability Framework for .NET.</strong>
  <br />
  You declare what each log should carry. sl4n handles the rest.
</p>

<p align="center">
  <a href="#"><img src="https://img.shields.io/badge/status-alpha-orange.svg" alt="Alpha"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="License"></a>
  <a href="#"><img src="https://img.shields.io/badge/.NET-8%2B-blue.svg" alt=".NET 8+"></a>
  <a href="#"><img src="https://img.shields.io/badge/NativeAOT-compatible-brightgreen.svg" alt="NativeAOT compatible"></a>
</p>

---

## What is sl4n?

Every .NET team writes the same boilerplate: thread `correlationId` through every method signature, scrub `password` fields before logging, repeat the same `using (_logger.BeginScope(...))` on every controller action.

sl4n solves the boilerplate problem declaratively. You declare the rules once at startup. The framework applies them consistently on every log call, across every service — without you thinking about it again.

```csharp
public class PaymentService
{
    private readonly ILogger<PaymentService> _logger;

    public void Charge(decimal amount, string email)
    {
        _logger.LogInformation("Card charged {Amount} for {Email}", amount, email);
        // → {"level":"information","correlationId":"req-001","amount":299.9,"email":"j**n@example.com"}
        //    ^^^^^^^^^^^^^^^^^^^^^ scope from Sl4nMiddleware    ^^^^^^^^^^^^^ masking by sl4n
    }
}
```

The `correlationId` propagated automatically from the inbound HTTP header. The `email` was masked automatically. Your business code is unchanged.

---

## The declarative shift

| Instead of... | You declare... | sl4n does automatically |
|---------------|----------------|------------------------|
| Threading `correlationId` through every method | `app.UseSl4n()` | Extracts inbound headers, pushes context to every log in the request via `AsyncLocal` |
| Scrubbing sensitive fields before logging | `"masking": { "enableDefaultRules": true }` | Masks email, password, token, credit card on every log |
| Repeating `BeginScope(new { correlationId })` everywhere | Configured once | Scope is opened by the middleware — available to every `ILogger` in the call chain |
| Manually building outbound headers per service | `context.outbound` config | `Sl4nDelegatingHandler` injects the right wire names per destination automatically |

---

## Packages

| Package | Description |
|---------|-------------|
| `sl4n` | Core — masking, context propagation, async transport |
| `sl4n.AspNetCore` | Middleware — extracts inbound context, opens MEL scope per request |

---

## Quick start

```csharp
// Program.cs
builder.Services.AddSl4n(builder.Configuration.GetSection("sl4n"));
builder.Services.AddSl4nAspNetCore();

app.UseSl4n();
```

```json
// appsettings.json
{
  "sl4n": {
    "masking": { "enableDefaultRules": true },
    "context": {
      "source": "frontend",
      "inbound": {
        "frontend": { "correlationId": "X-Correlation-ID", "traceId": "X-Trace-ID" }
      },
      "outbound": {
        "http": { "correlationId": "X-Correlation-ID", "traceId": "X-Trace-ID" }
      }
    }
  }
}
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

## Console output

Every entry carries an ISO-8601 `timestamp` captured at log time. Two console transports ship:

- **`ConsoleTransport`** (default) — one JSON object per line, for machine ingestion.
- **`ClassicConsoleTransport`** — human-readable `2026-06-20T… [INF] category: message key=value` for
  local development. Swap it in after `AddSl4n`:

```csharp
builder.Services.AddSl4n(builder.Configuration.GetSection("sl4n"));
builder.Services.UseClassicConsole();   // replaces the JSON console transport
```

Both implement `ITransport` — write your own for any backend (the Universal Adapter role).

---

## Default masking rules

| Field pattern | Strategy | Example |
|---------------|----------|---------|
| `email`, `mail` | Email — first + last char | `j**n@example.com` |
| `password`, `pass`, `pwd`, `secret` | Full mask | `**********` |
| `token`, `key`, `auth`, `jwt`, `bearer` | Full mask | `**********` |
| `credit_card`, `card_number` | Last four | `************1234` |
| `ssn`, `social_security` | Last four | `*****6789` |
| `phone`, `mobile`, `tel` | Last four | `******4567` |

### Custom rules & safety

Custom rules are **appended** to the defaults (keep `enableDefaultRules` on) — matched by field name:

```json
// appsettings.json — inside "sl4n"
"masking": {
  "enableDefaultRules": true,
  "regexTimeoutMs": 100,
  "rules": [
    { "pattern": "^(cvv|cvc|securityCode)$", "strategy": "FullMask" }
  ]
}
```

Reference `MaskKeys` instead of string literals to keep patterns out of secret scanners (Sonar S2068):

```csharp
cfg.Masking.Rules.Add(new MaskingRuleConfig
{
    Pattern  = MaskKeys.Pattern(MaskKeys.Token),  // ^(token|key|auth|jwt|bearer)$
    Strategy = MaskingStrategy.FullMask,
});
```

Guarantees:

- **Non-string under a sensitive key → `[REDACTED]`.** An object, array, or number under a
  sensitive-named field is redacted whole, never stringified — nested PII can't leak through.
- **Masking never throws.** A custom-mask exception or a regex timeout redacts that one field
  fail-secure and fires `OnMaskingError`; logging keeps working.
- **ReDoS guard.** Custom-rule regexes run under `regexTimeoutMs` (default 100 ms). The built-in
  defaults are linear, source-generated `[GeneratedRegex]` — not subject to the timeout.

**Scope — by field name, never free text.** A value is masked because its *key* matches a rule, not
because the string *looks* sensitive. Masking does **not** deep-walk nested object graphs — that
would move a mountain of data on every log and penalize latency. Structure sensitive data as keyed
fields; a nested object under a sensitive key is still redacted whole. This mirrors SyntropyLog's
by-key spirit, kept allocation-light and AOT-safe for .NET.

---

## Logging matrix

*"You declare what each log should carry"* — this is where. The **logging matrix** is a per-level
whitelist of **context fields**: a field not whitelisted for a level never reaches a transport. The
same request context emits different subsets at different levels — narrow at `information`, wide at
`error` — reviewable as one config object instead of by grepping every log call.

```json
// appsettings.json — inside the "sl4n" section
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

**It filters context only.** The per-call fields you pass to
`logger.LogInformation("Charged {Amount}", amount)` are always emitted (and masked) — the matrix
governs only the auto-propagating scope you can't trim at each call site. Leave a field off a level's
list to keep it out of that level; always define `default`. When no matrix is configured, every
context field passes through (backward compatible).

---

## Propagation headers

`correlationId`, `traceId` are **conceptual names internal to the framework** — not the names that travel on the wire. The wire name per destination is declared by you in configuration.

```
inbound['frontend']   internal context    outbound['http']       outbound['kafka']
──────────────────    ────────────────    ─────────────────      ─────────────────
X-Correlation-ID  ->  correlationId   ->  X-Correlation-ID   /   correlationId
X-Trace-ID        ->  traceId         ->  X-Trace-ID         /   traceId
```

Application code never sees wire names. It only works with conceptual names.

---

## Outbound propagation

```csharp
// Register for a named HttpClient
builder.Services.AddHttpClient("downstream")
    .AddHttpMessageHandler(sp =>
        new Sl4nDelegatingHandler(sp.GetRequiredService<IOptions<Sl4nConfig>>(), target: "http"));
```

Fields present in `Sl4nContext.Current` are automatically injected as outbound headers using the wire names configured under `context.outbound`.

---

## Manual context

```csharp
// Push fields for the duration of a scope
using Sl4nScope scope = Sl4nContext.Push(
    ("correlationId", "req-001"),
    ("userId",        "usr-42")
);

// Or set individual fields
Sl4nContext.Set("step", "payment");
```

---

## Reliability & observability

Three safety properties hold on every log:

- **Sanitization.** Control characters and ANSI escape sequences are stripped from the message and
  field values before any transport sees them — a value can't inject a fake log line. (The exception
  blob is left intact so stack-trace newlines survive.)
- **Transport isolation.** If one transport throws, the others still receive the entry and the worker
  keeps running. The failure is counted and surfaced via `OnLogFailure`.
- **Masking never throws.** A custom-mask error or a regex timeout redacts that field fail-secure and
  fires `OnMaskingError`.

Runtime counters are exposed via `Sl4nStats` — the `getStats()` equivalent — resolvable from DI:

```csharp
Sl4nStatsSnapshot s = provider.GetRequiredService<Sl4nStats>().Snapshot();
// s.LogsProcessed · s.TransportFailures · s.DroppedEntries · s.MaskingFailures
```

```csharp
// wire the failure hooks on the AOT config path
services.AddSl4n(cfg =>
{
    cfg.OnLogFailure           = (ex, transport) => Console.Error.WriteLine($"{transport}: {ex.Message}");
    cfg.Masking.OnMaskingError = (ex, field)     => Console.Error.WriteLine($"mask {field}: {ex.Message}");
});
```

---

## Retention policies

Declare named retention policies (compliance metadata), then tag the logs that must obey them — the
.NET analog of SyntropyLog's `withRetention('SOX_AUDIT_TRAIL')`. The emitted entry carries
`retention` / `retentionClass` / `retentionDays` so a downstream store can apply the right retention.

```json
// appsettings.json — inside "sl4n"
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

The tag rides a MEL scope, so every log in the call chain inherits it. Retention metadata **bypasses
the logging matrix** — it's a structural compliance stamp, not user context.

---

## AOT compatibility

sl4n is `IsAotCompatible=true`. Every code path uses:
- `[GeneratedRegex]` — compile-time regex, no reflection
- `Utf8JsonWriter` — no `JsonSerializer` reflection
- `Action<T>` configuration overload — no `IConfiguration` binding reflection

```csharp
// AOT-safe
services.AddSl4n(cfg =>
{
    cfg.Masking.EnableDefaultRules = true;
    cfg.Context.Source = "frontend";
});

// Not AOT-safe (annotated with [RequiresUnreferencedCode])
services.AddSl4n(configuration.GetSection("sl4n"));
```

---

## Performance

Benchmarks run with BenchmarkDotNet, `DefaultJob` (N≈99, CI ~3%), .NET 8, Linux/WSL — AMD Ryzen 7 7735HS.

| Method | Mean | Allocated |
|--------|-----:|----------:|
| MEL no-op (NullLogger) | 73 ns | 72 B |
| **sl4n (scope + masking, null transport)** | **607 ns** | **482 B** |
| MEL working (scope + dict, no masking) | 618 ns | 792 B |
| Serilog (scope, no sinks, via MEL) | 684 ns | 712 B |
| NLog (scope, NullTarget, via MEL) | 547 ns | 496 B |

**sl4n with masking active allocates less than MEL without masking** (482 B vs 792 B). It is faster than Serilog and within 11 ns of MEL — doing more work.

---

## What sl4n is not

sl4n is a structured logging and context propagation framework. It is not:

- A log aggregation backend (use Elasticsearch, Loki, CloudWatch)
- A distributed tracing system (use OpenTelemetry)
- A metrics collector (use Prometheus, Datadog)

It is the component that makes every log line correct, consistent, and safe before it reaches any of those systems.

---

## Security

**No network I/O at runtime.** sl4n does not contact any external URLs. The only output is what your transports produce.

**No extra runtime dependencies.** The core package depends only on `Microsoft.Extensions.*` — already present in any ASP.NET Core application.

---

## Running the tests

```bash
dotnet test tests/sl4n.Tests/sl4n.Tests.csproj
```

## Running the benchmarks

```bash
dotnet run --project benchmarks/sl4n.Benchmarks --configuration Release -- --filter '*ComparativeBenchmark*'
```

---

## License

Apache 2.0 — see [LICENSE](./LICENSE).

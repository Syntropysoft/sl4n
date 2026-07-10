# sl4n → SyntropyLog parity roadmap

**Goal:** bring sl4n to conceptual parity with SyntropyLog (TS) as it stands today, respecting
.NET idioms (no 1:1 port). Working branch: **`develop`** (sl4n had only `main`; created `develop`
per the library convention — commits go to develop, not main).

Baseline before this work: 81 tests green, `dotnet 10.0.300`, target `net8.0`, AOT-compatible.

---

## Where sl4n already has parity (do NOT re-port)

- Context propagation — conceptual↔wire names, inbound/outbound maps, `AsyncLocal`, UUID auto-gen,
  response headers (`Sl4nContext`, `Sl4nMiddleware`, `Sl4nDelegatingHandler`).
- Masking **by field name** with default rules (email/password/token/card/ssn/phone).
- Async transport pipeline (`Channel` + `Sl4nTransportWorker` + `ITransport` + JSON `ConsoleTransport`).
- MEL integration (`Sl4nLogger`, `Sl4nLoggerProvider`) + ASP.NET Core middleware.

## .NET salvedades (intentional differences — keep them)

- Rides on **MEL `ILogger`** instead of a bespoke metadata-first API.
- **DI (`AddSl4n`)** instead of `init()` / global singleton.
- `ITransport` **is** the "Universal Adapter".
- **No native addon** — .NET already compiles native; the TS native/JS masking-parity bug does not apply.

---

## Phased plan

- [x] **Phase 1 — Logging Matrix** ✅ (engine + typed builder + worker filtering + DI + 11 tests + README) — 92 tests green, AOT-clean
- [x] **Phase 2 — Masking to parity** ✅ (custom rules via config, non-string redaction, `MaskKeys`
      = `maskEnum`, ReDoS timeout, `OnMaskingError` never-throw) — 99 tests green, AOT-clean.
      **Deferred:** deep **nested** walk of dictionary values — coupled to `ConsoleTransport`, which
      today serializes flat (a nested dict value would `ToString()` to garbage). Fold nested masking +
      nested JSON serialization together into **Phase 4**. Func-based custom masks are supported by
      constructing `MaskingRule` directly (config path is declarative pattern+strategy only).
- [x] **Phase 3 — Safety boundary** ✅ (sanitization of control chars/ANSI in the worker, per-transport
      failure isolation + `OnLogFailure`, `Sl4nStats`/`Snapshot` = `getStats()`, masking-failure counter
      wired through DI) — 110 tests green, AOT-clean.
- [x] **Phase 4 — Transports** ✅ log-time ISO-8601 `timestamp`, `ClassicConsoleTransport` +
      `UseClassicConsole()`, and `DurableFileTransport` — a self-emptying disk **buffer** (not an
      archive): happy path never touches disk, spools only the undelivered backlog during an outage,
      deletes the file on drain, recovers leftover spool on restart. No rotation/rename/cleanup
      (Gabriel's constraint). `UseTransport`/`AddTransport` helpers. 137 tests green, AOT-clean.
      DECIDED (Gabriel): nested/deep masking = **documented salvedad, not built**. Respect the JS
      spirit — mask by FIELD NAME, never free text — but do NOT deep-walk nested object graphs (would
      "move a mountain of data" and penalize latency). Non-string under a sensitive key already
      redacts whole. Documented in README masking "Scope" note.

- [x] **Phase 5 — Retention policies** ✅ `RetentionPolicy` + `RetentionRegistry` +
      `Sl4nRetention.BeginRetentionScope` (MEL scope carrying `__retention`); worker resolves the
      policy and stamps `retention`/`retentionClass`/`retentionDays`, bypassing the matrix; the raw
      `__retention` field is consumed, never emitted. Config `Sl4nConfig.RetentionPolicies` + DI +
      README section — 127 tests green, AOT-clean.
- [ ] **Phase 5 — Distributed** (Kafka/Redis instrumentation helpers — adapter package)
- [~] **Phase 6 — Testing package + docs parity** — DONE: `sl4n.Testing` project with `SpyTransport`
      (capture + `AtLevel`/`WithField`/`AnyMessageContains`) + `UseSpyTransport` DI helper; added to
      solution + test project; README "Testing your code" section — 132 tests, full solution builds.
      README rewritten in SyntropyLog's "AI-readable" structure (pitch → quick start with exact I/O →
      what-it-is + pillars → comparison table → declarative-shift → feature sections → what's-in-the-box →
      security). REMAINING: dedicated `docs/` pages mirroring SyntropyLog's `docs/` (optional).

All six phases delivered. Optional follow-ups: dedicated `docs/` pages mirroring SyntropyLog;
a `MaxBufferedEntries` cap on `DurableFileTransport` for very long outages (currently unbounded).

## JS-parity backlog (source audit 2026-07-10, done while planning the JVM port)

Verified against the JS README "What's in the box" inventory — these are real gaps in sl4n's code,
listed here so they live in the roadmap like everything else:

- [ ] **PackageTags honesty (priority)** — `sl4n.csproj` lists `opentelemetry` in `PackageTags`
      but no OTel integration exists anywhere in the code. Honest-positioning rule: remove the tag,
      or build the feature (an `ITransport` emitting to an OTLP logger, per the JS README's
      "OpenTelemetry" pattern). Do not ship a keyword without the capability.
- [ ] **W3C `traceparent`** — context middleware supports inbound/outbound maps + UUID autogen but
      does not parse `traceparent`; JS `correlationIdMiddleware` does. (The JVM port plans it in
      its Phase 6 — .NET shouldn't lag its younger sibling.)
- [ ] **Always-on `audit` level** — JS has an audit level that bypasses level thresholds; sl4n only
      has retention tagging. Evaluate an MEL-idiomatic equivalent (EventId- or scope-based).
- [ ] **Hot reconfiguration** — no `IOptionsMonitor` wiring; MEL supports it natively. Evaluate
      hot-changing level/matrix (JS has runtime reconfiguration; Logback gives the JVM port `scan`
      for free).
- [ ] **Masking fixture (stretch)** — sl4n's strategy-enum model predates the canonical `MaskSpec`;
      it does NOT run the shared 17-case `mask-parity-cases.json` that JS/Python/Rust/JVM assert.
      Migrating to `MaskSpec` would put the whole family on one correctness contract.

Minor cleanups:
- [x] License — set to **Apache-2.0** (Gabriel's call) across `sl4n.csproj` + `sl4n.Testing.csproj`.
- [x] Repo URL fixed to `github.com/Syntropysoft/sl4n`.

---

## Phase 1 — Logging Matrix (design)

**Semantics (from SyntropyLog `docs/logging-matrix.md`):** a declarative per-level whitelist of
**context/scope fields**. Fields not whitelisted for a level never reach a transport. Per-call
structured state (MEL message-template args) is **always** emitted (and masked) — the matrix filters
only the auto-propagating context. `"*"` = allow every context field. `default` = fallback for any
level not listed explicitly.

**.NET mapping:** matrix keys are **MEL level names** (`Trace|Debug|Information|Warning|Error|Critical`)
plus `default`, matched case-insensitively. (SyntropyLog's `info/warn/fatal` aliases are intentionally
NOT supported — sl4n emits MEL names; documenting the mapping instead.)

**Config surface** (`Sl4nConfig.LoggingMatrix : Dictionary<string,string[]>`), binds flat from appsettings:
```json
"loggingMatrix": {
  "default":     ["correlationId"],
  "information": ["correlationId", "userId", "operation"],
  "error":       ["*"]
}
```
AOT path uses a typed builder (LogLevel-keyed → catches "not-a-level" typos):
```csharp
cfg.LoggingMatrix = new MatrixBuilder()
    .Default("correlationId")
    .Level(LogLevel.Information, "correlationId", "userId", "operation")
    .All(LogLevel.Error)
    .Build();
```

**Resolution** (`LoggingMatrix.AllowedFields(levelName)`):
- not configured (empty) → `null` ⇒ allow all (backward compatible).
- level found (or `default`) & contains `"*"` → `null` ⇒ allow all.
- level found (or `default`) → that set.
- configured but level unlisted AND no `default` → empty set ⇒ **drop all context** (strict whitelist;
  docs tell users to always define `default`).

**Files:**
- `src/sl4n/Config/Sl4nConfig.cs` — add `LoggingMatrix` dict.
- `src/sl4n/Matrix/LoggingMatrix.cs` — engine (`Create`, `AllowedFields`, `Empty`).
- `src/sl4n/Matrix/MatrixBuilder.cs` — typed builder.
- `src/sl4n/Transport/Sl4nTransportWorker.cs` — filter scope fields by matrix in `Build()`.
- `src/sl4n/Sl4nServiceCollectionExtensions.cs` — register `LoggingMatrix`, inject into worker.
- `tests/sl4n.Tests/Matrix/LoggingMatrixTests.cs` + worker filtering tests.
- `README.md` — Logging Matrix section.

**Invariant:** `level`/`category`/`message`/`exception` and per-call structured state are never
filtered by the matrix — only scope/context fields are.

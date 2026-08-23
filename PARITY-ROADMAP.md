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
- **`RegexTimeoutMs` actually works here.** In JS the field is accepted and **inert** — V8 cannot
  interrupt a running regex, so the reference rejects explosive patterns statically at `init()` and
  says so rather than implying a guarantee it cannot keep. .NET's `Regex` honours a real timeout
  (`RegexMatchTimeoutException`, see `MaskingEngine.cs`), so sl4n has a runtime backstop the
  reference cannot have. A difference in our favour — keep it, and do not "align" it away.

---

## Backport analysis — SyntropyLog 1.4.1 / 1.4.2 (native + masking fixes)

Four fixes that shipped in SyntropyLog (JS) 1.4.1/1.4.2 were analysed against sl4n (2026-08-08).
**All four are N/A** — sl4n's architecture avoids each bug class by design, so there is nothing to
port. Recorded here so the next person doesn't wonder whether they were missed.

| SyntropyLog (JS) fix | sl4n | Why |
| :-- | :-- | :-- |
| **UTF-8 panic** in the native `truncate` (a byte-index slice split a multi-byte char → process `SIGABRT`, not catchable from JS) | N/A | No Rust addon. C# does no byte-index truncation of values; no `SIGABRT` path exists. |
| **Masking mutated the caller's object** (the JS engine wrote redactions back in place, corrupting nested caller objects) | N/A | `MaskingEngine.Apply` is a non-mutating LINQ projection (`state.Select(...)` → a new sequence); masking is flat, with no deep-walk. |
| **Cross-tenant PII leak** from a process-global native config (`OnceCell`) | N/A | DI, not a global singleton; `MaskingEngine.Create` is a per-instance factory — no shared native config. |
| **Native path fed a serialized string to object-consuming transports** (durable audit / OTLP / adapters silently got a string they couldn't route or persist) | N/A | Object-based pipeline end to end: `ITransport.Log(IReadOnlyDictionary)` — the worker hands the dict to every transport, which serializes itself. There is no string fast-path to break. |

Note the irony of #4: it makes the JS **native path** deliver each transport the shape it needs
(string for console, object for adapters) — which is exactly what sl4n's worker already does. sl4n was
born with that seam in the right place.

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

- [ ] **Retention shape diverged from the reference — decision pending (2026-08-22).** Not an
      incomplete port: a different design, and it is not recorded under *.NET salvedades*, so it is
      either a divergence to close or a salvedad to declare. Three axes:

      | | SyntropyLog 2.1.0 | sl4n |
      |---|---|---|
      | unit | `years` XOR `months` | `Days` (int) |
      | emitted | `retention` + `retentionUntil` (materialised date) | `retention` + `retentionClass` + `retentionDays` (duration) |
      | field names | `retention`, `retentionUntil` | `retention`, `retentionClass`, `retentionDays` |

      The second axis is the one that carries weight. The reference materialises the date **at write
      time** so a sweep is a plain range scan — `WHERE retention_until < now()` — correct across
      records filed under different revisions of the same policy, without the sweeper knowing
      anything about policies. `retentionDays` is also stamped at write time, so it survives
      revisions equally, but the sweep becomes `written_at + retentionDays`, a computed expression
      rather than an indexable column. See `docs/compliance.md` § *Where this framework's job ends*
      in the JS repo for the full argument.

      **Platform trap — verified on .NET 10, and it inverts the invariant.** The reference rounds
      *long* on every edge case, on purpose: ending a window early is the failure an auditor
      punishes. .NET's built-in arithmetic rounds *short*.

      | | JS (`setUTCMonth` / `setUTCFullYear`) | .NET (`AddMonths` / `AddYears`) |
      |---|---|---|
      | 31-Jan + 1 month | **3-Mar** — longer | **28-Feb** — shorter |
      | 29-Feb + 7 years | **1-Mar** — longer | **28-Feb** — shorter |
      | 29-Feb + 12 months | **1-Mar** — longer | **28-Feb** — shorter |

      So materialising a date here is not "port the JS function". A naive `AddMonths` ends every
      edge-case window one to three days early — code that looks correct and quietly produces the
      exact failure the design exists to prevent. Whatever shape is chosen, the rounding has to be
      overridden explicitly and pinned by a test, the way the JS side pins the rollover.

      **What .NET brings that JS cannot**, and which may make the right answer here *better* than
      the reference rather than merely different:

      - `DateOnly` — a compliance window ends on a **date**, not an instant. JS has only `Date`.
      - The unit can be years-XOR-months **at compile time** (required-one-of), instead of a union
        the caller can still violate at runtime as in JS.
      - `TimeProvider` makes write-time computation deterministic in tests without injecting clocks
        by hand.

      Options, with their cost — **not decided, this is a maintainer call** because sl4n publishes
      three NuGet packages and the emitted shape is part of their contract:

      1. **Declare it a salvedad.** Move it to *.NET salvedades* and stop treating it as a gap. Free,
         and defensible if the consumers here sweep on a duration.
      2. **Add a materialised date alongside** `retentionDays`, keeping both. Additive, minor, nobody
         breaks, and consumers that want the range scan get it. Needs the rounding override.
      3. **Converge on the reference** — one unit, a materialised date, deprecate `retentionDays`.
         What a strict reading of the parity contract asks for, and **breaking** across all three
         packages.

      Option 2 or 3 should use `DateOnly` and compile-time unit exclusivity rather than mirroring the
      JS signature: parity is about the guarantee, not about the shape of the API.

- [ ] **Masking exemption per sink — the audit trail needs the truth.** JS 1.5.0 added
      `masking.exemptTransports`: masking runs once before the transport loop, so every sink gets
      the same obfuscated entry, which is right for consoles and APMs and wrong for exactly one —
      the audit ledger, where `2*****9` proves nothing. sl4n has no equivalent: `MaskingConfig` is
      `EnableDefaultRules` / `Rules` / `RegexTimeoutMs`, and nothing can opt a sink out.

      **.NET can do this better than the reference.** JS matches exempt sinks by **name**, which is
      why it needs `UnknownExemptTransportError` — a typo would silently mask the one sink that had
      to hold evidence. Here `ITransport` is already the adapter and registration goes through DI, so
      the exemption can be a typed marker on the registration instead of a string list. The whole
      typo failure class disappears rather than being caught at startup.

      Keep the rest of the reference's rule: the exemption is declared by the **application**, never
      by a transport about itself — a dependency must not be able to ship a sink that exempts
      itself — and everything else still applies to the exempt output (truncation, depth caps).

- [ ] **Retention policy versioning — provenance, not computation.** JS 2.0.0 added
      `retention: { version, emitRules }`: with `emitRules` on, the full rules ride on the entry
      under `retentionRules`, stamped `policyVersion`. sl4n's `RetentionPolicy` is `{ Days, Class }`
      — no version anywhere, and no way to say which revision a record was filed under.

      Worth being precise about what this is *not* for. The non-linear purge already works here:
      because `retentionDays` is stamped at write time, records filed under a policy later revised
      from 36 to 42 to 12 months each keep the window in force when they were written, and no sweep
      has to reconstruct which revision applied. Versioning does not fix that — it is **provenance**,
      for the auditor asking under which revision a given record was filed. Registries get re-seeded;
      without the stamp a persisted rule cannot say which one it came from.

      Scope the emission the same way: off by default, because an in-process consumer resolves the
      class at write time (see the next item) and only an **out-of-process** reader — a shipper
      parsing JSON with no registry — needs the rules on the entry.

- [ ] **Resolve a policy without a logger.** JS 2.0.0 added `getRetentionPolicy(name)`,
      `getRetentionPolicies()` and `getRetentionUntil(name, at)` so a domain write path that
      persists the retention class in its own column gets the same answer the logger got, against
      the same frozen registry, at write time. sl4n has `RetentionRegistry` but nothing public that
      resolves against it outside the logging path.

      **DI makes this cleaner here than in JS.** The reference bolted accessors onto a global facade;
      sl4n can expose the registry as an injectable read-only service, which is the idiomatic answer
      and needs no singleton. Same guarantee — one registry, one answer, whichever way it is asked —
      with a loud failure on an unregistered name rather than a silent null.

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

- [x] **Perf: per-key-name decision cache in masking** — DONE 2026-07-11 (family fix, found by
      the Java port's JMH suite; Java 4,497→1,187 ns/op, JS 442→183, Python 5,576→1,697, all
      same-day). sl4n was the least affected — `[GeneratedRegex]` defaults and no wide catch-all —
      but custom config rules are runtime-interpreted regexes and their cost scaled linearly:
      measured 235 ns/op (defaults) vs 642 ns/op (defaults + 8 custom). With the bounded
      `ConcurrentDictionary` cache (cap 4096; `RegexMatchTimeoutException` is transient and NEVER
      cached; rule set is immutable post-construction so no invalidation is needed): **75 / 79
      ns/op** — masking cost no longer scales with the number of custom rules, which is exactly
      the regulated-industry configuration (cuit/dni/iban/… stacked on top of the defaults).
      140 tests green.

---

## Phase 7 — hardening backport (from SyntropyLog JS 1.4.0/1.4.1, 2026-07-14)

The JS sibling shipped a hardening wave; these are the pieces that apply to .NET
(ReDoS does NOT — sl4n already enforces a REAL 100 ms regex timeout by default,
something V8 cannot do; and there is no native-addon fallback to observe).

- [x] **7.1 Masking decision cache** — RESOLVED IN MERGE: the same family fix was implemented
      independently upstream (2026-07-11, benchmarked 642→79 ns/op — see the backlog entry
      above) while this branch built its own. The merge kept the upstream implementation
      (`FindMatchingRule`/`ScanRules`, internal `DecisionCacheMax`, its test suite) and
      grafted this branch's unique addition on top: public **`HasRuleFor(key)`** — a cached
      decision lookup the worker needs for 7.5's message re-render (timeout ⇒ `true`,
      fail-secure).
- [x] **7.2 Durable outage observability** — `DurableFileTransport.TryForward` swallows the
      inner failure (`catch { return false; }`): buffering IS the handling, but the operator
      never learns an outage started, why, or that it recovered — and the worker can't see it
      either (the durable eats the exception before per-transport isolation). Add optional
      ctor callbacks, default null ⇒ behavior byte-identical: `onOutageStarted(Exception)`
      fired ONCE per false→true backlog transition (JS 1.4.1 "report once, cached" lesson),
      `onBacklogDrained(int delivered)` on full drain.
- [x] **7.3 Poison spool line** — a crash mid-`Append` leaves a truncated JSON line;
      `Deserialize` then throws inside `Drain()` on every subsequent `Log()` → the spool is
      wedged forever. Skip unparseable lines (they were never a complete entry), report via
      an optional `onCorruptLine(Exception, string)` callback.
- [x] **7.4 CI executes the AOT claim** — ci.yml only builds+tests JIT, and only on `main`
      (develop never runs CI!). Add develop to triggers, and an `aot-smoke` job: publish a
      tiny console app with `PublishAot=true`, RUN the binary, assert masked output
      (password `[REDACTED]`, no cleartext PII). Compiling proves it links; this proves it
      runs. Mirrors the JS `alpine-smoke` job.
- [x] **7.5 ★ PII LEAK: template-interpolated values land in `message` unmasked** — found
      while building 7.4. `Sl4nLogger` stores `formatter(state, exception)` (MEL's
      pre-formatted message, RAW values); the worker masks only `StructuredState`. So the
      README's own quick start (`"Card charged {Amount} for {Email}"`) emits the cleartext
      email inside `message` next to a masked `Email` field — looks masked, isn't. The .NET
      twin of JS 1.2.0's message-first routing fix. The README output IS the contract; fix
      the code to match it: when any state key has a masking rule (a cached decision — 7.1
      synergy), re-render `message` from `{OriginalFormat}` using the MASKED values
      (AOT-safe token substitution; `{{`/`}}` literals honored; format specifiers lose
      fidelity only on re-rendered = masked entries). No state key with a rule ⇒ message is
      byte-identical to MEL's (common case, zero cost beyond the cached lookups).
- [x] **7.6 ★ Double-dispose crash on clean shutdown** — found by RUNNING the 7.4 smoke
      (before AOT even entered the picture): the worker is registered both as a singleton
      and as its own IHostedService — two DI descriptors, one instance — so the
      ServiceProvider disposes it twice, and the second `DisposeAsync()` hit the disposed
      CTS → `ObjectDisposedException` on every clean host shutdown. `DisposeAsync` is now
      idempotent. Exactly why the smoke exists: executed, not claimed.

All of Phase 7 delivered — AOT smoke passes (JIT-verified locally; the CI `aot-smoke`
job publishes with PublishAot on linux-x64 and executes the binary).

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

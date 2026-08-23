# Changelog

All notable changes to **sl4n** are documented here. This project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) and
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **Masking exemption per sink — the audit trail gets the truth (1.1.0).** Masking runs once before
  the transport loop, so until now every sink received the same redacted entry. That is right for
  consoles and APMs and wrong for exactly one: the audit ledger, where `j**n@example.com` proves
  nothing. A sink registered under `Sl4nTransportKeys.Unmasked` now receives the values as they
  arrived, plus MEL's own message instead of the re-rendered one.

  The exemption is **keyed DI**, not an sl4n API: `AddKeyedSingleton<ITransport>(Sl4nTransportKeys.Unmasked, sink)`.
  The JS reference matches exempt sinks by name and therefore needs an `UnknownExemptTransportError`
  to catch a typo that would silently mask the one sink holding the evidence. Here the framework
  keeps the two groups apart on its own — keyed services do not come back from `GetServices<T>()` —
  so that failure cannot be expressed. Forgetting the key masks the sink, which errs safe.

  It skips masking and **nothing else**: the logging matrix still filters context fields and the
  sanitizer still strips control characters. Both are pinned by test.

  Cost when unused is zero: the second projection is allocated only if a sink is registered under
  the key. The state is enumerated **exactly once** either way — it is a lazy reference that can
  hang off a disposed request scope, so a re-enumeration would drop the entry for every sink, not
  just the exempt one. Pinned by `LazyState_IsEnumeratedExactlyOnce_WithExemptSink`.

  Verified under Native AOT by publishing and **executing** the smoke binary, which now asserts both
  sides: no cleartext on the console, cleartext in the keyed sink.

  **Measured, not assumed.** The change unified the masking loop (per-key `MaskOne` instead of the
  lazy `Apply` projection) so the state is read once. On an Apple M2 that made the masked path
  **8% faster and 88 B lighter per entry** — the LINQ iterator and its capturing closure are gone.
  The exempt sink costs 65 ns and **zero extra allocation** on top. Numbers come from the new
  `WorkerBuildBenchmark`; the pre-existing benchmarks could not see any of this (see below).

- **`retentionUntil` — the compliance window materialised at write time (1.1.0).** An entry tagged
  with a retention policy now also carries the date its window ends, so a purge is an indexable
  range scan (`WHERE retention_until < CURRENT_DATE`) instead of a computed expression, and records
  filed under an older revision of a policy keep the window that was in force when they were
  written. `retentionDays` stays; nothing that reads it breaks.

  `RetentionPolicy` gained **`Months`** and **`Years`** alongside `Days`. Declare exactly one:
  declaring two throws the new `Sl4nConfigurationException` **at startup**, never from the logging
  path. An ambiguous compliance window has no safe default, and a host that refuses to boot is
  better than records swept on a date nobody chose. This is the library's first `throw` anywhere,
  and it is deliberately confined to service construction.

  **The arithmetic rounds long, on purpose.** .NET's `AddMonths`/`AddYears` clamp to the last day of
  a short target month — 31-Jan + 1 month gives 28-Feb — which ends a window up to three days early,
  the exact failure retention exists to prevent. sl4n rolls forward (31-Jan + 1 month → 3-Mar),
  matching the JS sibling. Pinned by test across every short month and both leap-day cases.

  Prefer the calendar unit: `days` is exact but drifts. `2555` — the value this README used to
  show for SOX — is 7 × 365 and misses two leap days, ending two days before seven actual years.

  Anchored to the **event's own timestamp**, not to the clock the worker reads: the worker can be
  seconds behind under backlog, and a window that moved with queue depth would not be reproducible
  from the entry. No timestamp and no declared unit both mean the field is omitted, never guessed.

  Emitted as an ISO `yyyy-MM-dd` string rather than a `DateOnly`, because the JSON transport would
  have formatted a bare `DateOnly` with the server's culture — `8/23/2033` in `en-US`, `23/8/2033`
  in `es-AR`. Pinned by test under three cultures.

  Cost, measured with `WorkerBuildBenchmark`: +93 ns and +72 B on entries whose policy resolves;
  every other path is unchanged within noise.

- **Resolve a retention policy without going through a logger (1.1.0).** A domain write path that
  persists the retention class in its own column now gets the same answer the logger stamps, from
  the same registry and the same arithmetic. `RetentionRegistry` — already public and already
  registered in DI — gained:

  - `Policies` — the frozen registry, enumerable.
  - `Resolve(name)` — throws `RetentionPolicyNotFoundException` (carrying the name and the sorted
    available ones) instead of returning null. The caller is deciding how long to keep a record; a
    null there persists it with no retention at all, discovered at audit time rather than deploy
    time. `TryResolve` stays for callers where a miss is a branch, not a bug.
  - `Until(name, at)` — the same `retentionUntil` the logging path stamps.

  The registry is now genuinely immutable. `IReadOnlyDictionary` does not make a map read-only: a
  downcast to `IDictionary` brings the mutators back, and a caller who kept the dictionary it passed
  in could edit it afterwards. Either route could redefine a compliance window for records already
  written under the old one. It copies on construction and exposes a real `ReadOnlyDictionary`;
  both routes have a test.

- **`llms.txt`** — the compact API contract for code-generating agents, mirroring the Node
  sibling's. Every fact in it was checked against the source or its test, including the two that
  are easy to get wrong from memory: a configured matrix with no `default` drops all context on an
  unlisted level, and the dictionary handed to `ITransport.Log` is reused across entries. Ships
  inside the `sl4n` package, so an agent resolving it from NuGet gets the contract without cloning.

### Changed — internal

- **The published projects build with zero warnings, and stay that way.** The 68 outstanding
  `CS1591` warnings (public members with no XML doc) are gone — documented, not suppressed. The
  three published projects now carry `TreatWarningsAsErrors`, so a missing doc on a public member or
  an AOT/trim warning fails the build instead of joining a pile nobody reads. That pile was already
  hiding things: a warning introduced earlier in this same release went unnoticed until the totals
  were compared by hand. Tests and benchmarks are deliberately not strict.

- **The gate ships as git hooks** (`.githooks/`, enabled with `git config core.hooksPath .githooks`).
  pre-commit builds and runs the suite; pre-push adds the NativeAOT smoke — publish and *execute*,
  because compiling only proves it links. Both were verified to fail on a broken tree, not just to
  pass on a clean one.

### Added — internal

- **`WorkerBuildBenchmark` — the pipeline is finally measurable.** Every existing benchmark timed
  `logger.LogInformation(...)`, which snapshots the scope and writes to the channel; the worker
  drains on another thread, so masking, matrix filtering, sanitization and the message re-render
  never entered a number — including under the `*ComparativeBenchmark*` filter CI runs on `main`.
  It also read backwards: a slower worker fills the channel sooner, `DropOldest` starts discarding,
  and the logger call gets *faster*, so a pipeline regression could show up as a benchmark win.
  The new benchmark times `Build()` over one event with no channel and no transports.


## [1.0.6] — 2026-08-08

Documentation only — **no API or behavior changes**. Ship the 1.0.5 fixes with a NuGet-safe README.

### Fixed

- **README no longer renders broken on NuGet.** A centered HTML header (`<p align="center">` + a
  `syntropysoft.com` logo `<img width>`) had crept back onto `develop` and shipped in 1.0.5 — NuGet
  blocks that image host and escapes `<img width>` to raw text (the exact breakage 1.0.3 removed the
  logo for). The header is now plain Markdown (heading + shields.io badges), rendering correctly on
  both NuGet and GitHub.

[1.0.6]: https://github.com/Syntropysoft/sl4n/releases/tag/v1.0.6

## [1.0.5] — 2026-08-08

Phase 7 hardening: two security/correctness fixes (message masking + clean shutdown), durable-transport
observability, and masking performance. Backport from the SyntropyLog JS 1.4.0/1.4.1 family — all of it
landed after v1.0.4 was cut, so **v1.0.4 shipped without these fixes**.

### Fixed

- **PII leak inside the log message (7.5).** MEL pre-formats the message with RAW values, so
  `log.Info("Card charged {Amount} for {Email}", …)` emitted the cleartext email inside `message`
  right next to a masked `Email` field — it *looked* masked but wasn't. The worker now **re-renders
  the message from the masked values** whenever a state key has a masking rule (AOT-safe,
  culture-invariant token substitution: `{{ }}` escapes honored, unknown tokens verbatim,
  `null → "(null)"`). Entries with no maskable key keep MEL's formatting byte-for-byte. The .NET
  twin of JS 1.2.0's message-first routing fix.
- **Double-dispose crash on host shutdown (7.6).** `Sl4nTransportWorker` is registered as a singleton
  AND as its own `IHostedService` (two DI descriptors, one instance), so the provider disposed it
  twice and every clean host shutdown threw `ObjectDisposedException` on the CTS. `DisposeAsync` is
  now idempotent. Found by running the new AOT smoke.
- **Poison spool line wedged the durable drain (7.3).** A crash mid-append left a truncated JSON line
  that made `DurableFileTransport.Drain()` fail on every subsequent `Log()` (same parse error each
  time). Unparseable lines are now skipped and reported once per episode via `onCorruptLine`.

### Added

- **Durable-outage observability (7.2).** `DurableFileTransport` swallowed the inner transport failure
  invisibly. Optional constructor callbacks (default `null` = behavior identical): `onOutageStarted(ex)`
  once per outage transition, `onBacklogDrained(count)` on full drain.
- **AOT smoke in CI (7.4).** `ci.yml` now runs on `develop` and publishes `tests/sl4n.AotSmoke` with
  `PublishAot` on linux-x64, **executes the binary**, and asserts the emitted JSON (message included)
  is masked. Compiling proved it links; this proves it runs.

### Performance

- **Masking cost no longer scales with the number of custom rules.** Every key paid the full
  rule scan per log — cheap for the `[GeneratedRegex]` defaults (235 ns/op for a 3-field entry),
  but custom config rules are runtime-interpreted regexes and each one added linear cost
  (642 ns/op with 8 custom rules — the typical regulated-industry setup: cuit, dni, iban, …).
  The engine now caches the *decision* per key name (matched rule or "no rule"), never the
  value: **75 ns/op with defaults, 79 ns/op with defaults + 8 custom** — 3.1x and 8.1x.
  Safety properties: bounded at 4096 entries (hostile payloads generating unique key names
  cannot grow memory — past the cap, new keys still mask correctly, uncached); a
  `RegexMatchTimeoutException` during the scan is transient and is **never cached** (the
  fail-secure `[REDACTED]` behavior is unchanged); the rule set is immutable after
  construction, so the cache never needs invalidation. Masked output is byte-for-byte
  identical — 140 tests green. Family fix: found by the Java port's JMH suite
  (4,497→1,187 ns/op there), landed in SyntropyLog (JS, 442→183) and slpy (5,576→1,697)
  the same day.

[1.0.5]: https://github.com/Syntropysoft/sl4n/releases/tag/v1.0.5

## [1.0.4] — 2026-07-09

Documentation only — **no API or behavior changes**.

### Changed

- README family line: sl4n is presented as the .NET member of the SyntropyLog family, sibling links
  now point at the published packages only (npm / PyPI — not the repos), and the Python member
  ([slpy](https://pypi.org/project/slpy-log/)) is referenced for the first time.

[1.0.4]: https://github.com/Syntropysoft/sl4n/releases/tag/v1.0.4

## [1.0.3] — 2026-06-20

Documentation only — **no API or behavior changes**.

### Removed

- README logo. NuGet renders a markdown image at full resolution (oversized) and escapes an HTML
  `<img width="…">` to raw text, so there is no way to show it at a sensible size — the `sl4n` heading
  is enough. (Still fine on GitHub without it.)

[1.0.3]: https://github.com/Syntropysoft/sl4n/releases/tag/v1.0.3

## [1.0.2] — 2026-06-20

Documentation only — **no API or behavior changes**.

### Fixed

- README logo is now sized (`<img width="150">`) instead of rendering at full resolution on NuGet —
  markdown image syntax can't constrain the size, so it shipped huge in 1.0.1.

[1.0.2]: https://github.com/Syntropysoft/sl4n/releases/tag/v1.0.2

## [1.0.1] — 2026-06-20

Documentation and packaging only — **no API or behavior changes**. (NuGet packages are immutable, so
these README fixes ship as a patch rather than replacing 1.0.0.)

### Fixed

- README logo now renders on NuGet — uses the GitHub camo-proxied image URL; the raw
  `syntropysoft.com` URL is blocked by NuGet's image allowlist.
- Performance section reworked to be machine-independent: leads with allocations (identical across
  machines) and ratios, marks absolute nanoseconds as indicative (measured on x86/Windows), and notes
  that an Apple M2 posts materially lower absolute times.

[1.0.1]: https://github.com/Syntropysoft/sl4n/releases/tag/v1.0.1

## [1.0.0] — 2026-06-20

First stable release. sl4n is the .NET counterpart of
[SyntropyLog](https://github.com/Syntropysoft/SyntropyLog) (Node.js / TypeScript) — a declarative log
pipeline on top of `Microsoft.Extensions.Logging`, NativeAOT-compatible. Supersedes `1.0.0-beta.1`.

### Added

- **Logging Matrix** — per-level whitelist of context fields (`Sl4nConfig.LoggingMatrix`) with a
  typed, LogLevel-keyed `MatrixBuilder`. A field not whitelisted for a level never reaches a transport.
- **Masking** — PII redaction **by field name**, before any transport:
  - default rules (email, password, token, credit card, ssn, phone);
  - custom rules via configuration (`MaskingConfig.Rules`) appended on top of the defaults;
  - `MaskKeys` sensitive-key aliases (Sonar-safe, no string literals);
  - a non-string value under a sensitive key is redacted whole to `[REDACTED]`;
  - never-throw pipeline with `OnMaskingError` and a `RegexTimeoutMs` ReDoS guard.
- **Context propagation** — conceptual↔wire header translation via `AsyncLocal` (`Sl4nContext`),
  inbound/outbound maps, UUID auto-generation, and response-header echo.
- **ASP.NET Core** (`sl4n.AspNetCore`) — `app.UseSl4n()` extracts inbound context and opens the
  propagation + MEL log scopes per request; `Sl4nDelegatingHandler` injects outbound wire headers.
- **Retention policies** — `logger.BeginRetentionScope("SOX_AUDIT_TRAIL")` stamps
  `retention` / `retentionClass` / `retentionDays` on every log in scope (bypassing the matrix);
  policies declared in `Sl4nConfig.RetentionPolicies`.
- **Transports** — JSON `ConsoleTransport` (default) and human-readable `ClassicConsoleTransport`;
  log-time ISO-8601 timestamps; `UseClassicConsole` / `UseTransport` / `AddTransport` helpers.
- **DurableFileTransport** — a self-emptying disk **buffer** in front of an inner transport: the happy
  path never touches disk, only the undelivered backlog is spooled during an outage, the file is
  deleted on drain, and a leftover spool is recovered on restart. No rotation or cleanup to maintain.
- **Reliability & observability** — control-character/ANSI sanitization, per-transport failure
  isolation with `OnLogFailure`, and `Sl4nStats.Snapshot()` runtime counters (logs processed,
  transport failures, dropped entries, masking failures).
- **Testing** (`sl4n.Testing`) — `SpyTransport` captures emitted entries for assertions
  (`AtLevel`, `WithField`, `AnyMessageContains`); `UseSpyTransport` DI helper.
- **NativeAOT** — the whole pipeline is reflection-free (`[GeneratedRegex]`, `Utf8JsonWriter`,
  `Action<Sl4nConfig>` configuration path).

### Notes

- Masking matches **field names, never free text**, and does **not** deep-walk nested object graphs —
  a deliberate performance and .NET/AOT salvedad. Structure sensitive data as keyed fields; a nested
  object under a sensitive key is still redacted whole.

[1.0.0]: https://github.com/Syntropysoft/sl4n/releases/tag/v1.0.0

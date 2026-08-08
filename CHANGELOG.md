# Changelog

All notable changes to **sl4n** are documented here. This project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) and
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

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

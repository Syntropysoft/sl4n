# Changelog

All notable changes to **sl4n** are documented here. This project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) and
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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

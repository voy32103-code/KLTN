# Changelog

All notable changes to ReqSimulator are documented in this file.

## [0.1.1] - 2026-08-11

### Fixed

- Restore .NET dependencies from the official NuGet source in GitHub Actions instead of local-only developer package folders.

## [0.1.0] - 2026-08-11

### Added

- Reviewed scenario lifecycle with persona templates and scenario review audits.
- AAOC extraction, glossary normalization, one-to-one matching, and feedback safeguards.
- Public URL ingestion with SSRF protections and a Playwright fallback for JavaScript-rendered pages.
- Private R2 upload flow and audio-only video ingestion through FFmpeg and Gemini Files API.
- PostgreSQL-backed ingestion queue processed by a GitHub Actions run-once worker.
- Frontend accessibility and ingestion upload regression tests.
- CI, issue forms, environment examples, and an English contributor-facing README.

### Security

- Admin-only source ingestion and publishing.
- Internal API-to-AI-service authentication and short-lived R2 presigned URLs.

[0.1.1]: https://github.com/voy32103-code/KLTN/releases/tag/v0.1.1
[0.1.0]: https://github.com/voy32103-code/KLTN/releases/tag/v0.1.0

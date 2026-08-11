# MediaCrawler assessment and business-video ingestion test

## Decision

Do **not** integrate MediaCrawler directly into the deployed ReqSimulator
backend or call it synchronously from a controller.  It is a useful project to
study and a possible **local, manual research tool**, but it relies on logged-in
browser sessions, platform-specific signature handling, and (optionally)
proxies.  Those characteristics do not fit a free Vercel + Render + GitHub
Actions deployment, and can violate a platform's terms or its license when
used outside its stated learning/research purpose.

The recommended production path remains:

```text
Admin-authorized public source or permitted local media
  -> validation, limits, provenance and prompt-injection guardrails
  -> ingestion_jobs + private R2 artifact
  -> GitHub Actions run-once worker
  -> Gemini creates a DraftReady scenario
  -> admin review and publish
```

Only Admin users may create ingestion jobs.  A draft must never be published
automatically.

## What to learn from MediaCrawler

| Reuse as a design idea | Do not copy into the deployed service |
| --- | --- |
| A source adapter per platform, producing one normalized record shape | Login accounts, cookies, saved browser profiles, CDP access, or remote Chrome |
| A cheap HTTP fetch first, with Playwright as a bounded fallback for public JavaScript pages | Signature extraction, anti-bot evasion, CAPTCHA bypass, or proxy pools |
| Explicit retries, rate limits, job status and resumable work | A synchronous controller request that waits for crawling and Gemini |
| Provenance: source URL, time, author/display name only when authorized, and extraction result | Sending raw, unfiltered comments or personal data directly to Gemini |

If social-media research is later approved, implement a separate
`SocialSourceAdapter` that accepts only platform-approved, public data.  It
should sanitize PII, de-duplicate records, record provenance, impose a small
per-source quota, and send the resulting evidence through the existing
`ingestion_jobs` review flow.  It is not a reason to give the cloud service a
user's social-media login or to use automated bypass techniques.

## Recommended video for the first acceptance test

Use the public Wavetec appointment-booking explainer as the **business
reference**: [Online appointment booking and scheduling — how it works](https://www.wavetec.com/solutions/smart-online-appointment-booking-and-scheduling-software/how-it-works/).
Its video is also available at [YouTube](https://www.youtube.com/watch?v=knZzXf0zbEE).
The demonstrated flow is a good match for a requirements-engineering scenario:

1. A customer enters from a landing page or social link.
2. The customer selects a branch and an available time slot.
3. The customer supplies booking details and confirms.
4. The customer can manage, reschedule, or cancel the booking.

Expected draft evidence after ingestion: at least the Customer and Branch
Staff/Administrator roles; branch and slot selection; availability validation;
booking confirmation; and reschedule/cancel rules.  The model may phrase these
differently, so review the meaning rather than exact wording.

The current UI uploads a **local video/audio file**; it does not ingest a
YouTube URL as a video file.  Public playback is not permission to download,
re-upload, or send a video to Gemini.  Use a file supplied with permission by
the owner/provider, a video under a suitable licence, or (best for a
repeatable demo) make a short MP4 yourself that narrates the four steps above.
Do not use downloader or copyright-bypass tools.

## Alternative business references

These are useful when a permitted local copy is available:

| Domain | Reference | What the scenario should discover |
| --- | --- | --- |
| Clinic/service appointments | [Square Appointments dashboard walkthrough](https://squareup.com/us/en/square-university/appointments-services/square-appointments-dashboard-walkthrough) | Services, staff members, booking policies, online booking, dashboard operations |
| Procurement | [Purchase requisition to purchase order workflow](https://www.youtube.com/watch?v=5nmBGs_aieA) | Request, approval, purchase order, status/exception handling and audit trail |

The Square page labels its walkthrough as four minutes, so it is a compact
candidate for a manual demo.  Check the right to obtain and upload the media
before using it.

## Repeatable audio-only acceptance test

1. Prepare a permitted MP4/MOV (ideally 2–8 minutes, clear spoken narration,
   no sensitive personal data) or record an original short explainer using the
   appointment steps above.
2. Sign in as an Admin and upload it in the ingestion screen.  Wait until the
   private-R2 upload has completed and note the job ID.
3. In GitHub, run the [Ingestion worker workflow](https://github.com/voy32103-code/KLTN/actions/workflows/ingestion-worker.yml) on `main`.
   The worker extracts **audio only** with FFmpeg, so visual-only information
   is intentionally not a test assertion.
4. Refresh the job history.  The expected terminal state is `DraftReady`
   (or a specific, actionable failure state instead of an indefinitely
   processing job).
5. Review the generated draft against the five expected appointment concepts
   above.  Verify that it has not invented a payment rule, a personal name, or
   a fact not present in the narration.
6. Publish only if the admin review is correct.  If it is not, keep the job
   as a draft and capture the job ID, worker log, and source timestamp for a
   regression case.

For the first real-Gemini run, use a newly recorded demo video.  This makes
the test legal, deterministic enough for a report, and avoids exposing a
third party's content or personal data to the provider.

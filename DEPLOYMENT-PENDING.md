# Pending deployment dependencies

The application code for asynchronous ingestion is ready, but the following
external actions are intentionally deferred:

1. **Cloudflare R2**: enable R2 for the Cloudflare account. Then create the
   private `reqsimulator-ingestion` bucket, apply CORS for
   `https://kltn-chi.vercel.app`, and create an Object Read/Write S3 API token
   for the backend's `R2__*` settings.
2. **GitHub Actions worker**: add these repository secrets before manually
   running the `Ingestion worker` workflow: `INGESTION_BACKEND_URL`,
   `INGESTION_WORKER_KEY`, and `GEMINI_API_KEY`. No Render background worker
   or payment method is required.

The ingestion UI accepts video and audio. The worker extracts an MP3 audio
track from video before sending it to Gemini.

No credentials belong in this file or in source control.

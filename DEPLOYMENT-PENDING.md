# Pending deployment dependencies

The application code for asynchronous ingestion is ready, but the following
external actions are intentionally deferred:

1. **Cloudflare R2**: enable R2 for the Cloudflare account. Then create the
   private `reqsimulator-ingestion` bucket, apply CORS for
   `https://kltn-chi.vercel.app`, and create an Object Read/Write S3 API token
   for the backend's `R2__*` settings.
2. **Render worker**: add payment information before creating the Starter
   `reqsimulator-ingestion-worker` background worker. It needs the shared
   worker key, the existing Gemini key, and
   `INGESTION_BACKEND_URL=https://req-simulator-backend.onrender.com`.

No credentials belong in this file or in source control.

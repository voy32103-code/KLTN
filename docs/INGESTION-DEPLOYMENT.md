# Ingestion deployment and demo guide

## Architecture

The ingestion queue is stored in Neon PostgreSQL. Admin users create URL or
video/audio jobs through the Vercel frontend. The GitHub Actions workflow
claims exactly one queued job, runs Playwright or FFmpeg/Gemini, and stores a
scenario draft back in the backend.

This replaces the paid Render Background Worker. No continuous worker service
is required.

## Required setup

### 1. Cloudflare R2

Enable R2 in the Cloudflare Dashboard, then create a **private** bucket named
`reqsimulator-ingestion-private`.

Apply this CORS rule to the bucket:

```text
Origin: https://kltn-chi.vercel.app
Methods: PUT, HEAD
Allowed headers: Content-Type
Expose headers: ETag
```

Create an R2 S3 API token with Object Read and Write permission. Set these
secrets on the Render backend service:

```text
R2__ServiceUrl=https://ACCOUNT_ID.r2.cloudflarestorage.com
R2__AccessKeyId=<R2 access key>
R2__SecretAccessKey=<R2 secret key>
R2__Bucket=reqsimulator-ingestion-private
Ingestion__WorkerKey=<random secret of at least 32 bytes>
```

Do not add any of these values to Git.

### 2. GitHub repository secrets

Open **GitHub repository → Settings → Secrets and variables → Actions** and
add:

```text
INGESTION_BACKEND_URL=https://req-simulator-backend.onrender.com
INGESTION_WORKER_KEY=<same value as Ingestion__WorkerKey>
GEMINI_API_KEY=<Gemini API key>
```

The workflow is [`.github/workflows/ingestion-worker.yml`](../.github/workflows/ingestion-worker.yml).
It installs Chromium, Playwright and FFmpeg, then processes one job. A daily
scheduled run also cleans expired R2 artifacts and recovers expired queue
leases. It does not need Render billing or a Render worker service.

The backend records the ingestion schema migration as
`20260811_ingestion_queue_v1` in `application_schema_migrations`, guarded by
a PostgreSQL advisory lock. This prevents the ingestion DDL from running on
every application startup.

## How to demo ingestion

1. Sign in as an Admin.
2. Queue either a public URL or a video/audio file (maximum 250 MB).
3. The UI displays: `Job queued — chạy GitHub Action.`
4. Open **GitHub → Actions → Ingestion worker → Run workflow** on `main`.
5. Wait for the workflow to finish, then refresh the Admin page to review and
   publish the generated draft.

Useful public fixtures:

- SPA crawler: <https://quotes.toscrape.com/js/>
- MP4 upload: <https://samplelib.com/lib/preview/mp4/sample-5s.mp4>

## Troubleshooting

| Message | Cause | Fix |
| --- | --- | --- |
| `Job queued — chạy GitHub Action.` | The queue is intentionally waiting for a manual free runner. | Run the **Ingestion worker** GitHub workflow. |
| `Waiting for the ingestion worker.` | Old frontend build or no worker was running. | Redeploy/refresh the frontend; use the GitHub workflow. |
| `Đã xảy ra lỗi hệ thống nội bộ` while selecting a media file | R2 is disabled or the backend `R2__*` values are missing. | Enable R2 and configure the five backend secrets above. |
| `provider_unavailable` | The GitHub `GEMINI_API_KEY` is absent, invalid, or quota-limited. | Set/replace the GitHub secret and rerun the workflow. |

## Local checks

From the repository root, run:

```powershell
./tools/smoke_ingestion.ps1
```

Or run the focused checks manually:

```powershell
cd frontend; npm.cmd run build
cd ../backend/ReqSimulator.API; dotnet build -c Release --no-restore
cd ../../ai-service; ./.venv/Scripts/python.exe -m unittest tests.test_debug_regressions tests.test_ingestion_worker
```

The local test fixture at `tools/ingestion-spa-fixture/` inserts its content
only after JavaScript executes. It is only for local E2E tests; production
SSRF rules must continue to reject local/private URLs.

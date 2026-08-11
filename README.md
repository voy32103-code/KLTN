# ReqSimulator

ReqSimulator là hệ thống web hỗ trợ giảng dạy và thực hành **khai thác yêu
cầu phần mềm** (*Requirements Elicitation*). Sinh viên phỏng vấn stakeholder
ảo; hệ thống áp dụng progressive disclosure/gating, trích xuất yêu cầu và đánh
giá coverage. Giảng viên/Admin có thể review kết quả, điều chỉnh đánh giá và
tạo scenario mới từ nguồn được kiểm soát.

Trạng thái tài liệu: **11/08/2026**. Hệ thống phù hợp cho demo, pilot học phần
và dữ liệu do Admin kiểm duyệt; chưa được thiết kế cho tải lớn hoặc tự động
publish nội dung AI.

## Kiến trúc đang chạy

```text
Vercel frontend (Vite + TypeScript)
  -> Render backend (ASP.NET Core 9, JWT, API, PostgreSQL queue)
     -> Neon PostgreSQL (sessions, scenarios, jobs, artifacts metadata)
     -> Render AI service (FastAPI, persona/evaluation/provider adapters)
     -> private Cloudflare R2 (video/audio artifacts)
  -> GitHub Actions run-once worker (Playwright, FFmpeg, Gemini)
```

Backend là ranh giới công khai: xác thực, phân quyền và dữ liệu nằm ở đây. AI
service chỉ nhận request có `X-AI-Service-Key`. GitHub Action có worker key
riêng; không có Render Background Worker trả phí.

## Chức năng chính

- JWT và role `Student`, `Lecturer`, `Admin`.
- Scenario versioning, stakeholder/persona, hidden requirements và gating
  chống lộ ground truth cho sinh viên.
- Chat mô phỏng stakeholder, extraction/normalization Actor–Action–Object–
  Condition, matching một-một, coverage và learning feedback.
- Lecturer review/override có audit trail; Admin tạo, review và publish
  scenario.
- Nạp tri thức từ URL công khai: HTTP trước, Playwright fallback cho SPA,
  giới hạn SSRF/redirect/kích thước/thời gian.
- Nạp tri thức từ video/audio do Admin upload: private R2, PostgreSQL queue,
  GitHub Actions, FFmpeg **audio-only**, Gemini structured output, rồi Admin
  review trước khi publish.

## Video/audio ingestion

Đây là **trích xuất tri thức nghiệp vụ qua audio**, không phải fine-tuning và
không phải hiểu toàn bộ hình ảnh trong video.

```text
Admin upload -> presigned R2 PUT -> Queued in Neon
-> GitHub Action claims one job -> FFmpeg extracts MP3 audio
-> Gemini Files API + JSON schema -> AwaitingReview -> Admin publish
```

Video/audio tối đa 250 MB; worker chuẩn hóa audio thành MP3 128 kbps/44.1 kHz,
giới hạn FFmpeg 180 giây và giới hạn audio 128 MB. Video ingestion chỉ hỗ trợ
model Gemini vì dùng Gemini Files API. Workflow xử lý một job mỗi run; Admin
có thể chạy thủ công hoặc chờ lịch hằng ngày lúc 10:17 (giờ Việt Nam).

Tài liệu giải thích để demo/bảo vệ: [luồng video và Q&A phản biện](docs/VIDEO-KNOWLEDGE-INGESTION-DEFENSE-GUIDE.md).

## Triển khai và secrets

| Thành phần | Nền tảng | Cấu hình quan trọng |
| --- | --- | --- |
| Frontend | Vercel | `VITE_API_BASE_URL` |
| Backend | Render | Neon connection string, JWT, AI internal key, `R2__*`, `Ingestion__WorkerKey` |
| AI service | Render | `AI_SERVICE_INTERNAL_KEY`, Gemini/provider keys |
| Database | Neon PostgreSQL | `ConnectionStrings__DefaultConnection` |
| Worker | GitHub Actions | `INGESTION_BACKEND_URL`, `INGESTION_WORKER_KEY`, `GEMINI_API_KEY` |

R2 bucket phải là private. Không commit access key, secret key, JWT key, worker
key, Gemini key hay database URL. Xem [deployment status](docs/INGESTION-DEPLOYMENT.md)
để biết cách chạy ingestion thủ công và xử lý `Queued`.

## Chạy local

Yêu cầu: .NET SDK 9, Node 20+, pnpm, Python **3.12** và PostgreSQL test riêng.
Sao chép `.env.example` tương ứng cho backend, AI service và frontend; không
dùng secret production trên máy local.

```powershell
# Backend (mặc định http://localhost:5206)
dotnet restore .\KLTN.sln
dotnet run --project .\backend\ReqSimulator.API

# Frontend (terminal khác)
Set-Location .\frontend
pnpm.cmd install --frozen-lockfile
pnpm.cmd run dev

# AI service (terminal khác)
Set-Location ..\ai-service
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe -m uvicorn app.main:app --reload --host 127.0.0.1 --port 8000
```

## Kiểm tra local

```powershell
# .NET build và unit runner
dotnet build .\KLTN.sln -c Release --no-restore
dotnet run --project .\backend\ReqSimulator.API.UnitTests\ReqSimulator.API.UnitTests.csproj -c Release --no-build

# Frontend
Set-Location .\frontend
pnpm.cmd run build
pnpm.cmd test
pnpm.cmd exec playwright install chromium  # chỉ cần lần đầu cho E2E/a11y
pnpm.cmd exec playwright test

# AI service, sau khi đã cài requirements bằng Python 3.12
Set-Location ..\ai-service
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
```

Backend integration suite ghi vào PostgreSQL, nên chỉ chạy khi đã cấu hình một
database riêng có tên `test` hoặc `integration`; tuyệt đối không dùng Neon
production.

## Tài liệu hiện hành

- [Kết quả audit kiến trúc và rủi ro còn mở](docs/AUDIT-2026-08-11.md)
- [Trạng thái deployment/ingestion](docs/INGESTION-DEPLOYMENT.md)
- [Nạp tri thức từ video và câu trả lời phản biện](docs/VIDEO-KNOWLEDGE-INGESTION-DEFENSE-GUIDE.md)
- [Đánh giá MediaCrawler và video test](docs/MEDIACRAWLER-ADOPTION-AND-VIDEO-TESTING.md)
- [Trạng thái implementation ngày 08/08](docs/IMPLEMENTATION-80-PERCENT-STATUS.md)

Các tài liệu status cũ là bằng chứng lịch sử theo ngày ghi trên tài liệu; audit
mới nhất và code hiện tại có ưu tiên cao hơn nếu có mâu thuẫn.

# Deployment status — 11/08/2026

Tên file được giữ lại để không làm hỏng liên kết cũ, nhưng nội dung “pending”
ban đầu đã lỗi thời.

## Trạng thái đã xác nhận bởi người vận hành

- Frontend đang deploy trên Vercel.
- Backend và AI service đang deploy trên Render; database là Neon PostgreSQL.
- Bucket R2 private `reqsimulator-ingestion-private` đã tạo; CORS cho
  `https://kltn-chi.vercel.app` đã được cấu hình.
- Backend Render đã có `R2__ServiceUrl`, `R2__AccessKeyId`,
  `R2__SecretAccessKey`, `R2__Bucket`, `Ingestion__WorkerKey`.
- GitHub repository secrets đã có `INGESTION_BACKEND_URL`,
  `INGESTION_WORKER_KEY`, `GEMINI_API_KEY`.
- Workflow `Ingestion worker` đã chạy thành công với job video/audio thực tế.

Không ghi giá trị secret vào file này hoặc Git.

## Vận hành ingestion miễn phí

1. Admin upload file video/audio hoặc tạo job URL công khai.
2. UI báo `Job queued — chạy GitHub Action.` là trạng thái bình thường: job
   đã ở Neon và chờ runner.
3. Vào GitHub Actions, mở **Ingestion worker**, chọn `main` rồi bấm
   **Run workflow**. Workflow cũng chạy theo lịch 03:17 UTC (10:17 Việt Nam).
4. Làm mới lịch sử ingestion: `Queued` -> `Processing` -> `AwaitingReview`
   hoặc `Failed` có `ErrorCode`.
5. Chỉ mở, kiểm tra và publish bản nháp nếu nội dung nghiệp vụ đúng nguồn.

URL workflow: <https://github.com/voy32103-code/KLTN/actions/workflows/ingestion-worker.yml>

## Kiểm tra sau deploy

- Vercel build phải dùng `pnpm-lock.yaml` cập nhật (`pnpm install --frozen-lockfile`).
- Backend phải cho phép origin Vercel trong `Cors__AllowedOrigins` nếu override
  cấu hình mặc định.
- AI service và backend phải dùng cùng giá trị internal key; worker key trên
  GitHub phải trùng `Ingestion__WorkerKey` trên backend.
- R2 tiếp tục private; không bật public bucket/public domain cho ingestion.
- Với video, dùng file có quyền sử dụng và có lời thuyết minh. Hệ thống chỉ
  gửi audio đã trích xuất cho Gemini.

## Rủi ro vận hành còn lại

GitHub Actions run-once là lựa chọn phù hợp gói miễn phí nhưng có độ trễ và
xử lý một job mỗi lần chạy. Xem audit nội bộ trước khi mở rộng thời lượng video,
số người dùng hoặc mức độ tự động hóa.

# Mốc hoàn thiện 80% — pipeline phỏng vấn yêu cầu

Tài liệu này ghi nhận mức sẵn sàng theo **luồng chạy thực tế**. “80%” ở đây
nghĩa là đã có tích hợp end-to-end, dữ liệu được lưu/kiểm tra và có kiểm thử
cho happy path cùng các lỗi quan trọng; không đồng nghĩa đã thay thế đánh giá
nghiên cứu với người dùng thật.

| Hạng mục pipeline | Trạng thái runtime | Bằng chứng chính |
| --- | --- | --- |
| 1. Quản lý kịch bản | Đạt mốc | Preview → chỉnh sửa → publish version bất biến; ground truth và luật gate được lưu theo version. |
| 2–4. Stakeholder và hội thoại nhiều lượt | Đạt mốc | Persona được chọn trước phiên; history, mood/patience và requirement đã mở khóa được lưu server-side. Nội dung ground truth mới mở khóa không trả về client. |
| 5–6. Trích xuất và chuẩn hóa | Đạt mốc | AI trả contract Actor–Action–Object–Condition, được Pydantic kiểm tra, chuẩn hóa/deduplicate có thể tái lập, và lưu cả raw + normalized JSONB. |
| 7. Ground truth | Đạt mốc | Hidden requirements có category, gate, reveal condition, version và chỉ lecturer/admin xem được. |
| 8–9. Matching và coverage | Đạt mốc | Similarity embedding (có fallback), matching một-một có trọng số, ngưỡng scoring công khai và coverage không đếm một extraction cho nhiều ground truth. |
| 10. Feedback học tập | Đạt mốc | Hiển thị strengths, weaknesses, bước hỏi tiếp theo và danh sách extraction chưa gán để giảng viên rà soát. |
| 12. Mô hình hóa trực quan | Đạt mốc | Gợi ý Use Case/ERD Mermaid được sinh từ yêu cầu trích xuất và hiển thị trong trang kết quả. |
| 13. Đánh giá | Đạt mốc kỹ thuật | Match detail, điểm, policy và override của giảng viên có thể xem lại; bộ unit test che phủ contract/fallback/gating/matching. |

## Ranh giới còn lại trước khi tuyên bố hoàn tất nghiên cứu

- Chạy study với người dùng thật và bộ transcript/annotation đã chốt; báo cáo
  precision/recall, agreement giữa annotator và phản hồi UX.
- Đo độ ổn định/latency với khóa Gemini hoặc Groq thật; không dùng fallback
  để kết luận chất lượng model.
- Mở rộng crawler cho trang SPA/video phức tạp và bổ sung idempotency key cho
  retry mạng phía client nếu triển khai ở môi trường có traffic thật.

## Kiểm chứng tại thời điểm cập nhật

- `ai-service`: `python -m unittest discover -s tests -v` — 54 tests pass.
- `backend`: `dotnet build .\\backend\\ReqSimulator.API -c Release --no-restore` — 0 errors, 0 warnings.
- `frontend`: `npm.cmd test` và `npm.cmd run build` — pass.

Integration test backend không chạy trong mốc này vì nó có ghi vào PostgreSQL
và cần một database test cô lập được cấu hình rõ ràng.

## Chi tiết những phần đã triển khai

### AI service

- Mở rộng contract extraction từ `text + confidence` sang cấu trúc:
  `id`, `actor`, `action`, `object`, `condition`, `type`, `priority`,
  `confidence`, `raw_text`.
- Dùng Pydantic để giới hạn độ dài, confidence trong khoảng `0..1`, chỉ chấp
  nhận loại `FR/NFR/BR`, priority `high/medium/low` và từ chối field lạ.
- Kết nối structured prompt vào endpoint `/api/extract` và sửa lỗi template
  JSON bị `str.format` hiểu nhầm dấu ngoặc thành placeholder.
- Thêm chuẩn hóa deterministic cho Actor–Action–Object–Condition, xử lý dấu
  tiếng Việt, ánh xạ alias và tạo `canonicalKey`/`canonicalText`.
- Deduplicate theo canonical key và giữ phiên bản có confidence cao nhất.
- Giữ contract extraction cũ để backend hiện tại vẫn tương thích, đồng thời
  trả thêm `structuredRequirements` và `normalizedRequirements`.
- Thay matching độc lập từng ground truth bằng weighted one-to-one assignment;
  một extraction không thể được tính cho nhiều hidden requirement.
- Đánh dấu extraction không được gán vào `extractionsToReview` và trả
  `extraExtractedCount` trong kết quả đánh giá.
- Bổ sung nhận diện loại câu hỏi tiếng Việt và phát hiện câu hỏi gần trùng để
  điều chỉnh mood/patience của stakeholder hợp lý hơn.

Các file chính:

- `ai-service/app/models/schemas.py`
- `ai-service/app/prompts/structured_extraction_prompt.py`
- `ai-service/app/services/extract_service.py`
- `ai-service/app/services/normalization_service.py`
- `ai-service/app/services/matching_service.py`
- `ai-service/app/services/evaluate_service.py`
- `ai-service/app/services/gating_service.py`

### Backend ASP.NET

- Mở rộng DTO giao tiếp AI để nhận normalized requirement, extraction cần rà
  soát và số lượng extraction dư.
- Lưu raw structured extraction và normalized extraction trong hai cột JSONB
  của `extracted_requirements`.
- Bổ sung hai cột qua schema bootstrap:
  `raw_requirement_data` và `normalized_requirement_data`.
- API review của giảng viên trả cả dữ liệu raw/normalized để phục vụ truy vết.
- Evaluation response xuyên suốt thêm `extraExtractedCount`.
- Không trả `newlyRevealed` về trình duyệt sinh viên. Nội dung này vẫn được lưu
  trong persona state phía server để duy trì gating nhưng không làm lộ ground
  truth qua response chat.
- Cập nhật integration contract test để kiểm tra ground truth không xuất hiện
  trong chat response.

Các file chính:

- `backend/ReqSimulator.API/Models/Entities.cs`
- `backend/ReqSimulator.API/Data/SchemaBootstrapper.cs`
- `backend/ReqSimulator.API/Services/AiServiceClient.cs`
- `backend/ReqSimulator.API/Controllers/SessionsController.cs`
- `backend/ReqSimulator.API.IntegrationTests/Program.cs`

### Frontend

- Mở rộng kiểu dữ liệu evaluation với `extraExtractedCount` và
  `extractionsToReview`.
- Hiển thị riêng danh sách requirement trích xuất chưa match để giảng viên rà
  soát, thay vì âm thầm bỏ qua hoặc đưa vào coverage.
- Mở rộng contract review để nhận raw/normalized extraction từ backend.

Các file chính:

- `frontend/src/types.ts`
- `frontend/src/views.ts`

## Kiểm thử được bổ sung

- Structured extraction trả đúng contract và canonical requirement.
- Structured parser từ chối requirement có type không hợp lệ.
- Chuẩn hóa tiếng Việt/alias và deduplicate giữ confidence cao hơn.
- Matching một-một không tái sử dụng extraction và bỏ ứng viên dưới ngưỡng.
- Evaluation endpoint trả đúng extraction dư cần rà soát.
- Phân loại câu hỏi tiếng Việt: ngoại lệ, làm rõ và ràng buộc.
- Phát hiện câu hỏi gần trùng trong hội thoại nhiều lượt.
- Chat response không làm lộ `NewlyRevealed`.

Các test mới/cập nhật:

- `ai-service/tests/test_extract_service.py`
- `ai-service/tests/test_normalization_and_matching.py`
- `ai-service/tests/test_evaluate_service.py`
- `ai-service/tests/test_scenario_gating.py`
- `backend/ReqSimulator.API.IntegrationTests/Program.cs`

## Lưu ý khi tiếp tục hoặc triển khai

1. Dùng Python trong `ai-service/.venv`; Python hệ thống có thể thiếu package.
2. Khi backend khởi động, cần cho phép schema bootstrap tạo hai cột JSONB mới.
3. Không dùng kết quả fallback để kết luận chất lượng extraction/evaluation;
   backend hiện trả `503` nếu extraction/evaluation là fallback để tránh khóa
   điểm sai.
4. Trước khi chạy integration suite, cấu hình PostgreSQL test có tên chứa
   `test` hoặc `integration`. Suite có thao tác ghi và dọn dữ liệu do nó tạo.
5. Chưa commit hoặc stage các thay đổi. Worktree ban đầu đã có thay đổi riêng ở
   README, prompt/schema/retry handler và một số file local; các phần đó được
   giữ nguyên, không reset.

## Mốc ghi nhận

- Ngày cập nhật: 2026-08-08.
- Mục tiêu của lượt triển khai: đưa từng phần chính của pipeline tới mức có thể
  chạy end-to-end khoảng 80%, ưu tiên tính đúng của extraction, normalization,
  matching, coverage và bảo vệ ground truth.

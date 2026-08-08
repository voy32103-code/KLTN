# Tài liệu ReqSimulator

Tài liệu này phản ánh codebase đến ngày **08/08/2026**. Đây là điểm bắt đầu để phân biệt nội dung đã triển khai, nội dung đang làm và tài liệu lịch sử.

## Mục đích hệ thống

ReqSimulator được xây dựng từ một vấn đề quan sát được ở sinh viên: biết định nghĩa “requirement” nhưng khó chủ động khai thác nhu cầu thật, khó hỏi tiếp khi stakeholder trả lời mơ hồ, và chưa hình dung rõ công việc của Business Analyst. Hệ thống cho phép luyện phỏng vấn nhiều lần trong một môi trường có ground truth, transcript và phản hồi định lượng.

## Ba thành phần chạy thực tế

| Thành phần | Công nghệ | Trách nhiệm |
|---|---|---|
| Frontend | Vite, Vanilla TypeScript, Chart.js | Giao diện student/lecturer/admin; chat; kết quả; quản trị và preview kịch bản |
| Backend | ASP.NET Core 9, EF Core, Npgsql, PostgreSQL | Auth/role, phiên làm bài, versioning, gọi AI, lưu đánh giá, lecturer override |
| AI service | FastAPI, Google GenAI, NumPy, HTTPX, Pydantic | Persona response, gating, consistency check, extraction, embedding/evaluation, crawler/video |

Phiên bản hiện tại **không dùng PyTorch hoặc SentenceTransformers**. `EMBEDDING_MODEL` mặc định là một Gemini embedding model; embedding được lấy qua API và cosine similarity được tính bằng NumPy.

## Luồng dữ liệu

```text
Browser
  -> ASP.NET API + JWT
     -> PostgreSQL
     -> FastAPI qua X-AI-Service-Key
        -> Gemini/Groq/DeepSeek/Mimo/OpenRouter tùy cấu hình
```

Backend là ranh giới công khai và nguồn lưu dữ liệu. AI service được bảo vệ bằng khóa nội bộ. Kịch bản đã publish được lưu thành phiên bản bất biến; session giữ khóa ngoại đến đúng phiên bản đã bắt đầu.

## Chức năng đã có trong code

- Đăng ký, đăng nhập JWT và phân quyền Student/Lecturer/Admin.
- Danh sách scenario/persona, tạo session, gửi tin nhắn và kết thúc phiên.
- Gating theo loại câu hỏi và hidden requirement, consistency checker fail-closed.
- Trích xuất yêu cầu và đánh giá mức độ bao phủ.
- Lease finalization và unique evaluation để giảm kết quả trùng khi kết thúc phiên.
- Lecturer review, lecturer override và audit trail.
- Crawler URL tĩnh có SSRF guard, giới hạn redirect/kích thước/thời gian.
- Xử lý video bằng FFmpeg/Gemini theo điều kiện môi trường.
- Admin preview, chỉnh sửa, kiểm tra và publish scenario.
- Scenario versioning; bản cũ vẫn phục vụ lịch sử của session.
- Global rate limit và policy chặt hơn cho auth, AI chat và ingestion.
- Global exception handler và các security header cơ bản.

## Cấu hình tối thiểu

Backend:

```env
ConnectionStrings__DefaultConnection=Host=...;Database=...;Username=...;Password=...
Jwt__Key=it-nhat-32-byte
AiService__BaseUrl=http://localhost:8000
AiService__InternalKey=it-nhat-32-byte
```

AI service:

```env
AI_SERVICE_INTERNAL_KEY=cung-gia-tri-voi-backend-va-it-nhat-32-byte
GEMINI_API_KEY=...
MODEL_NAME=gemini-2.5-flash
EMBEDDING_MODEL=models/text-embedding-004
```

Frontend:

```env
VITE_API_BASE_URL=http://localhost:5206
```

Xem `.env.example` của từng dịch vụ trước khi chạy. Không đưa secret thật vào Git.

## Lệnh phát triển và kiểm tra

```powershell
# Backend
dotnet restore .\backend\ReqSimulator.API
dotnet build .\backend\ReqSimulator.API -c Release
dotnet run --project .\backend\ReqSimulator.API
dotnet run --project .\backend\ReqSimulator.API.UnitTests\ReqSimulator.API.UnitTests.csproj

# Frontend
Set-Location .\frontend
npm install
npm run build
npm test
npm run dev

# AI service
Set-Location ..\ai-service
pip install -r requirements.txt
python -m unittest discover -s tests -v
uvicorn app.main:app --reload --host 127.0.0.1 --port 8000
```

Integration test backend phải dùng PostgreSQL test biệt lập. Build project test không thay đổi dữ liệu; chạy test có thể ghi dữ liệu test nên không dùng database demo/production.

## Kết quả baseline ngày 01/08/2026

| Kiểm tra | Kết quả |
|---|---|
| Backend Release build | Thành công, 0 warning, 0 error |
| Backend unit runner | 5/5 thành công |
| Backend integration project build | Thành công; chưa chạy runtime vì chưa có PostgreSQL test biệt lập |
| Frontend production build | Thành công |
| Frontend test | 1/1 thành công |
| AI service unittest | 46/46 thành công |
| Python compile check | Thành công |

Các con số này là baseline tại thời điểm kiểm tra, không phải lời cam kết coverage. Frontend chỉ có một contract test nên chưa đủ chứng minh UI ổn định toàn diện.

## Tài liệu nên đọc

1. [Trạng thái triển khai mục tiêu 80%](IMPLEMENTATION-80-PERCENT-STATUS.md): các hạng mục vừa hoàn thiện, cách chạy evaluation và phần còn lại.
2. [Báo cáo tự nghiên cứu](research_academic/BAO_CAO_TU_NGHIEN_CUU_REQSIMULATOR.md): lý do chọn đề tài, so sánh sản phẩm, quyết định công nghệ và các vấn đề thật.
3. [Bản đồ dự án](project_map.md): module, entry point và trách nhiệm hiện tại.
4. [Tóm tắt dự án đã kiểm chứng](research_academic/master_project_summary.md): tóm tắt ngắn dùng cho báo cáo/thuyết trình.
5. [Giới hạn kỹ thuật](architecture_specs/unresolved_issues_and_limitations.md): vấn đề còn mở và khó khăn đã giải quyết.
6. [Đặc tả đánh giá](architecture_specs/evaluation_spec.md) và [đặc tả kịch bản](architecture_specs/scenario_config_spec.md): ý định thiết kế; cần đối chiếu code khi dùng làm nguồn triển khai.
7. [Phiếu khảo sát người dùng](research_academic/user_survey_template.md): biểu mẫu đánh giá sau phiên mô phỏng.
8. [Runbook calibration/A-B/Mermaid](research_academic/calibration_ab_mermaid_runbook.md): schema dataset, tiêu chí kết luận và cơ chế repair.
7. [Lộ trình triển khai](plans_progress/IMPLEMENTATION_ROADMAP.md): kế hoạch và tiến độ phát triển chi tiết của dự án.

## Quy hoạch thư mục tài liệu (`docs/`)

Thư mục tài liệu được tổ chức phân cấp khoa học như sau:
- `/architecture_specs`: Chứa các đặc tả kiến trúc, luồng hệ thống, tài liệu API và thiết kế thích ứng.
- `/research_academic`: Các báo cáo nghiên cứu học thuật, de cuong và tài liệu phục vụ bảo vệ Khóa luận tốt nghiệp.
- `/audits_evaluation`: Báo cáo đánh giá chất lượng, kiểm định bảo mật, kiểm thử mã nguồn.
- `/guides_checklists`: Hướng dẫn vận hành, checklists triển khai và chạy thử (smoke validation).
- `/plans_progress`: Lộ trình phát triển (roadmaps), so sánh đặc tả pipeline và các báo cáo tiến độ theo ngày/tuần.
- `/scenarios`: Các tệp định nghĩa kịch bản mô phỏng phỏng vấn.
- `/references`: Tài liệu định hướng và đặc tả chương trình gốc từ giảng viên.
- `/assets`: Chứa sơ đồ, wireframe và tài nguyên đồ họa hỗ trợ.
- `/archive`: Lưu trữ các tài liệu nháp, kế hoạch cũ và báo cáo walkthrough lịch sử của các đợt sửa lỗi trước.

## Quy ước đọc tài liệu

- **Current/canonical**: bốn tài liệu ở danh sách trên và code hiện tại.
- **Specification/target**: mô tả thiết kế mong muốn; không đồng nghĩa đã triển khai đủ.
- **Audit/report lịch sử**: bằng chứng tại ngày tạo, có thể không còn đúng sau các lần sửa (được phân loại vào `/audits_evaluation` hoặc `/archive`).
- File `# Software Requirements Specification.txt` là mẫu SRS tham khảo, không phải đặc tả đã hoàn thiện của ReqSimulator.

Không sử dụng các con số thực nghiệm như “tăng 28,4%” hoặc “Cohen's kappa 0,81” nếu không kèm dataset, cách lấy mẫu và script tái lập. Codebase hiện chưa có bằng chứng đủ để công bố các kết quả đó.

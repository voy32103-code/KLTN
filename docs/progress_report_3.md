# BÁO CÁO TIẾN ĐỘ KHÓA LUẬN TỐT NGHIỆP (LẦN 3)

**Đề tài:** Ứng dụng AI trong hỗ trợ đào tạo nghiệp vụ phân tích và thiết kế phần mềm: Thiết kế hệ thống mô phỏng tương tác  
**Tên hệ thống:** ReqSimulator  
**Ngày báo cáo:** 17/07/2026  

---

## 1. Các Nâng cấp & Điều chỉnh Quan trọng trong Giai đoạn 3

Trong giai đoạn này, nhóm đã thực hiện refactor kiến trúc toàn diện nhằm giải quyết các bài toán về hiệu năng, chi phí vận hành API, tính bền bỉ của hệ thống khi chạy thực nghiệm đông người, và rà soát bảo mật DevSecOps:

1.  **Hỗ trợ Đa mô hình AI (Multi-LLM Engine):**
    *   *Mục tiêu:* Cho phép linh hoạt chuyển đổi giữa các mô hình AI trực tiếp từ Frontend để đánh giá hiệu quả phản hồi của từng dòng mô hình đối với sinh viên.
    *   *Giải pháp:* Tích hợp song song 2 nhà cung cấp dịch vụ AI lớn: **Google Gemini** (Gemini 2.5 Flash, Gemini 1.5 Flash, Gemini 2.5 Pro) và **Groq Cloud** (Llama 3.3 70B, Llama 3.1 8B). Mô hình được lựa chọn (`SelectedModel`) được lưu trữ động trong thuộc tính trạng thái JSONB `PersonaState` của bảng `simulation_sessions` ở Backend mà không cần thay đổi cấu trúc bảng cơ sở dữ liệu (Database Migration).

2.  **Cơ chế Xoay vòng & Fallback API Keys thông minh:**
    *   *Mục tiêu:* Tránh việc hệ thống bị gián đoạn (Rate Limit / Quota Exceeded) khi nhiều sinh viên cùng thực hiện phỏng vấn đồng thời bằng tài khoản API miễn phí.
    *   *Giải pháp:* Xây dựng lớp quản lý tập trung **`ApiClientManager`** tại Python AI Service:
        *   *Xoay vòng (Rotation):* Đọc danh sách nhiều API Keys Gemini từ biến môi trường (`GEMINI_API_KEYS=key1,key2,key3`). Khi một Key gặp lỗi hạn mức (HTTP 429/403), hệ thống sẽ tự động khóa key đó trong 30 phút và chuyển sang key tiếp theo trong danh sách để thực hiện lại request.
        *   *Dự phòng (Fallback):* Nếu tất cả API Keys Gemini đều bị lỗi hoặc hết hạn mức, hệ thống tự động chuyển tiếp request sang Groq API để gọi mô hình Llama tương ứng nhằm đảm bảo phiên học tập của sinh viên không bị đứt gãy.

3.  **Rà soát Bảo mật & Loại bỏ Hardcode (DevSecOps Audit):**
    *   *Mục tiêu:* Đảm bảo không rò rỉ API keys, JWT Secrets hoặc cấu hình tĩnh lên Git khi bàn giao mã nguồn.
    *   *Giải pháp:*
        *   Quét toàn bộ mã nguồn, chuyển các dữ liệu nhạy cảm (Connection String, JWT Key, API Keys) vào các tệp cấu hình môi trường `.env` và thiết lập `.gitignore` chặt chẽ.
        *   Refactor cấu hình CORS tĩnh (`https://kltn-chi.vercel.app`) trong Backend .NET (`Program.cs` và `appsettings.json`) thành cấu hình động đọc từ biến môi trường `Cors:AllowedOrigins` nhằm tăng tính linh hoạt khi triển khai Frontend trên các tên miền khác nhau.
        *   Hỗ trợ HTTP method `HEAD` cho các endpoint kiểm tra `/` và `/health` tại FastAPI giúp Uptime Robot ping giữ ấm máy chủ Render ổn định, tránh hiện tượng máy chủ ngủ đông (Cold Start).

4.  **Bản địa hóa & Tinh chỉnh Kịch bản Nghiên cứu (HUFLIT Context):**
    *   *Giải pháp:* Theo yêu cầu thực tiễn của đợt thực nghiệm, nhóm đã cập nhật toàn bộ thông tin của kịch bản mẫu hệ thống đăng ký học phần: Thay thế tên trường giả định "TechEd University" thành trường đại học **HUFLIT** trong cả database seeder C# và file cấu hình kịch bản JSON của AI Service.

---

## 2. Các Hạng mục Đã Hoàn thành chi tiết

### 2.1 Lớp 1: AI Service (FastAPI - Python)
*   Hoàn thành file **`api_client_manager.py`** quản lý danh sách client Gemini SDK động và gọi API Groq bằng `httpx` không đồng bộ.
*   Chuyển đổi các tác vụ gọi generate content và embeddings sang xử lý bất đồng bộ (`asyncio.to_thread`) để tránh gây nghẽn Event Loop của FastAPI.
*   Cập nhật `main.py` hỗ trợ method `HEAD` và bỏ qua xác thực API key cho các route ping sức khỏe hệ thống.
*   Vá lỗi cú pháp thụt lề thụ động trong thuật toán ma trận tương đồng tại `evaluate_service.py` và giải quyết triệt để các cảnh báo kiểm tra kiểu tĩnh của Pyright.

### 2.2 Lớp 2: Backend (ASP.NET Core .NET 9)
*   Cập nhật `AiServiceClient.cs` để truyền thuộc tính `SelectedModel` sang AI Service trong tất cả các API nghiệp vụ (Chat, Trích xuất, Đánh giá).
*   Bổ sung trường `SelectedModel` vào lớp Snapshot trạng thái Persona (`SessionsController.cs`) và gán giá trị mặc định là `"gemini-2.5-flash"` nếu không có yêu cầu đặc biệt từ client.
*   Đăng ký đọc cấu hình CORS từ biến môi trường để cấp quyền truy cập động cho client.

### 2.3 Lớp 3: Frontend (Vite + TypeScript)
*   Tích hợp Dropdown lựa chọn mô hình AI với hiệu ứng kính mờ (Liquid Glass) bắt mắt bên cạnh nút khởi đầu cuộc phỏng vấn.
*   Lưu trữ lựa chọn mô hình của người dùng vào `localStorage` để đồng bộ giữa các phiên làm việc.
*   Hiển thị tag trạng thái mô hình AI đang hoạt động trực quan trên thanh tiêu đề của giao diện chat.

---

## 3. Thống kê Codebase Hiện tại

| Thành phần | LOC ước tính | Số lượng file | Trạng thái |
|---|---|---|---|
| **Backend (ASP.NET Core)** | ~1,680 LOC | 11 files | Build thành công 100% (0 lỗi, 0 cảnh báo) |
| **AI Service (FastAPI)** | ~1,550 LOC | 10 files | Hoạt động ổn định, 30 unit tests pass 100% |
| **Frontend (Vite + TS)** | ~1,250 LOC | 8 files | Đóng gói (production build) thành công |
| **Tài liệu học thuật (docs/)** | 42 files | 42 files | Cập nhật đầy đủ |

---

## 4. Kế hoạch Cho Giai đoạn Tiếp theo

Hệ thống đã hoàn tất toàn bộ các tính năng kỹ thuật và sẵn sàng đưa vào vận hành thực nghiệm. Nhóm sẽ tập trung thực hiện các công việc sau:

1.  **Triển khai thực nghiệm trên 30 sinh viên:**
    *   Tổ chức buổi thực nghiệm chia làm nhóm đối chứng (Control Group) và nhóm thực nghiệm (Treatment Group).
    *   Thu thập dữ liệu transcript tương tác của sinh viên và kết quả đánh giá tự động từ hệ thống.
2.  **Thu thập phản hồi & Đo lường:**
    *   Phát phiếu khảo sát trải nghiệm người dùng (SUS Score) và bảng hỏi đánh giá tính hữu ích đối với việc học RE.
    *   Thống kê định lượng các chỉ số Coverage và sự suy giảm Patience của sinh viên khi phỏng vấn.
3.  **Hoàn thiện Luận văn:**
    *   Tiến hành phân tích số liệu thực nghiệm để đưa vào Chương 4 (Thực nghiệm và Thảo luận).
    *   Chỉnh sửa định dạng luận văn theo quy chuẩn của nhà trường để chuẩn bị cho hội đồng bảo vệ.

---

*Sinh viên thực hiện kính trình giảng viên hướng dẫn xem xét và cho ý kiến chỉ đạo.*

# ReqSimulator

**ReqSimulator** là hệ thống web mô phỏng phỏng vấn stakeholder ảo hỗ trợ giảng dạy và thực hành kỹ thuật khai thác yêu cầu phần mềm (Requirement Elicitation) trong học phần Kỹ nghệ yêu cầu (Requirement Engineering). 

Hệ thống cho phép sinh viên trực tiếp phỏng vấn stakeholder được điều khiển bởi AI (có tính cách, cảm xúc, độ kiên nhẫn riêng), tự động trích xuất các yêu cầu sinh viên đã khai thác và so khớp với tập yêu cầu ẩn (ground truth) để chấm điểm độ bao phủ (Coverage Score) chi tiết.

---

## 1. Kiến trúc Hệ thống

Hệ thống được thiết kế theo kiến trúc 3 thành phần tách biệt (Decoupled 3-Service Architecture):

*   **Frontend (TypeScript / Vite / Vanilla CSS):** Giao diện tương tác gọn nhẹ của Sinh viên (Chat, Dashboard báo cáo) và Giảng viên (Bảng điều khiển review session, xem transcript).
*   **Backend (ASP.NET Core 9 / PostgreSQL / EF Core):** Quản lý xác thực JWT, phân quyền (Student/Lecturer), lưu trữ phiên phỏng vấn, tin nhắn, và điều phối quy trình chấm điểm (Lease-based finalization).
*   **AI Service (FastAPI / Python / PyTorch / Gemini):** Trực tiếp điều phối máy trạng thái của stakeholder, kiểm soát việc tiết lộ thông tin qua các cổng (Information Gating), hậu kiểm rò rỉ thông tin (Consistency Checker) và thực hiện pipeline chấm điểm ngữ nghĩa (Sentence-Transformers).

---

## 2. Hướng dẫn Khởi chạy Hệ thống (Local Development)

### Yêu cầu hệ thống:
*   Windows / macOS / Linux
*   PostgreSQL 15+
*   .NET SDK 9.0+
*   Python 3.12+ (kèm pip)
*   Node.js 20+ (kèm npm hoặc pnpm)

---

### Bước 1: Cấu hình Cơ sở dữ liệu (PostgreSQL)
1. Khởi tạo một cơ sở dữ liệu trống trên PostgreSQL (ví dụ đặt tên là `req_simulator`).
2. Mở file cấu hình environment của Backend tại thư mục `backend/ReqSimulator.API/.env` (hoặc tạo từ `.env.example`) và cập nhật chuỗi kết nối:
   ```env
   ConnectionStrings__DefaultConnection="Host=localhost;Database=req_simulator;Username=your_user;Password=your_password"
   Jwt__Secret="YourSuperSecretSecurityKeyThatIsAtLeast32BytesLong"
   Jwt__Issuer="ReqSimulator"
   Jwt__Audience="ReqSimulator"
   ```

---

### Bước 2: Chạy AI Service (FastAPI)
AI Service chịu trách nhiệm xử lý logic mô phỏng stakeholder và chấm điểm ngữ nghĩa.

1. Di chuyển vào thư mục `ai-service`:
   ```bash
   cd ai-service
   ```
2. Tạo môi trường ảo Python và kích hoạt:
   ```bash
   # Windows
   python -m venv .venv
   .venv\Scripts\activate

   # Linux/macOS
   python3 -m venv .venv
   source .venv/bin/activate
   ```
3. Cài đặt các thư viện dependencies:
   ```bash
   pip install -r requirements.txt
   ```
4. Tạo tệp `.env` dựa trên `.env.example` và cấu hình khóa API Gemini:
   ```env
   GEMINI_API_KEY="AIzaSyYourGeminiApiKeyHere"
   EMBEDDING_MODEL="all-MiniLM-L6-v2"
   PORT=8000
   ```
5. Khởi động AI Service:
   ```bash
   uvicorn app.main:app --host 127.0.0.1 --port 8000 --reload
   ```

---

### Bước 3: Chạy Backend Web API (.NET 9)
Backend sẽ tự động kiểm tra cấu trúc cơ sở dữ liệu và tự vá schema (seed data) khi khởi chạy lần đầu.

1. Di chuyển vào thư mục `backend/ReqSimulator.API`:
   ```bash
   cd backend/ReqSimulator.API
   ```
2. Khôi phục gói tin và chạy ứng dụng:
   ```bash
   dotnet restore
   dotnet run
   ```
   *Mặc định API sẽ lắng nghe tại cổng `http://localhost:5242` hoặc `https://localhost:7142`.*

---

### Bước 4: Chạy Frontend (Vite + TS)
1. Di chuyển vào thư mục `frontend`:
   ```bash
   cd frontend
   ```
2. Cài đặt các node packages:
   ```bash
   npm install
   ```
3. Tạo tệp `.env` cấu hình API URL trỏ về cổng của Backend:
   ```env
   VITE_API_BASE_URL="http://localhost:5242"
   ```
4. Khởi chạy ở chế độ phát triển (dev):
   ```bash
   npm run dev
   ```
   *Mở trình duyệt truy cập vào địa chỉ hiển thị trên terminal (thông thường là `http://localhost:5173`).*

---

## 3. Các kịch bản Baseline sẵn có (Scenarios)

Hệ thống cung cấp sẵn 3 kịch bản phỏng vấn mẫu đã được seed tự động vào database:
1.  **Đăng ký học phần (University Course Registration System):** Stakeholder là bà Nguyễn (Quản lý đào tạo) - Độ khó: Trung bình (10 yêu cầu ẩn).
2.  **Đặt lịch khám bệnh (Hospital Appointment System):** Stakeholder là bà Trần (Điều phối viên phòng khám) - Độ khó: Khó (12 yêu cầu ẩn).
3.  **Quản lý kho hàng tạp hóa (Small Business Inventory Management):** Stakeholder là ông Lâm (Chủ cửa hàng) - Độ khó: Dễ (9 yêu cầu ẩn).

*Lưu ý quan trọng:* Để hệ thống phân loại câu hỏi và kích hoạt mở khóa thông tin nghiệp vụ chính xác, quy trình phỏng vấn (chat) cần được thực hiện bằng **tiếng Anh (English-only)**.

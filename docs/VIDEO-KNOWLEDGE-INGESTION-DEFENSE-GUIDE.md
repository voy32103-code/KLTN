# Nạp tri thức nghiệp vụ từ video: luồng xử lý và ghi chú phản biện

## Cách gọi chính xác trong báo cáo

Hệ thống **không huấn luyện lại Gemini** và cũng không đọc hình ảnh trong video.
Cách mô tả chính xác là:

> Nạp tri thức nghiệp vụ từ video qua kênh âm thanh: hệ thống tách audio,
> dùng Gemini trích xuất yêu cầu nghiệp vụ có cấu trúc, rồi lưu thành bản nháp
> scenario để Admin kiểm duyệt trước khi công bố.

Đây là một pipeline *information extraction + scenario synthesis*, không phải
fine-tuning và không phải RAG toàn cục.  “Tri thức” đầu ra là Scenario Config
(bối cảnh, stakeholder, yêu cầu ẩn, điều kiện mở khóa và câu hỏi gợi mở), được
lưu cho scenario cụ thể. Mô hình không tự động "học vĩnh viễn" từ video đó.

## Sơ đồ luồng end-to-end

```mermaid
sequenceDiagram
    actor A as "Admin/Giảng viên"
    participant F as "Vercel frontend"
    participant B as ".NET backend + Neon"
    participant R as "Private Cloudflare R2"
    participant G as "GitHub Actions worker"
    participant X as "FFmpeg"
    participant M as "Gemini Files + model"

    A->>F: "Chọn MP4/MOV/audio và model"
    F->>B: "Tạo upload intent (Admin only)"
    B->>B: "Tạo SourceArtifact + IngestionJob(AwaitingUpload)"
    B-->>F: "Presigned R2 PUT URL, có hạn 10 phút"
    F->>R: "Upload thẳng file; backend không nhận file lớn"
    F->>B: "Xác nhận upload"
    B->>B: "Kiểm tra kích thước, chuyển job thành Queued"
    A->>G: "Run workflow (hoặc lịch hằng ngày)"
    G->>B: "Claim một job bằng Worker Key"
    B->>B: "Transaction + SKIP LOCKED; job = Processing"
    B-->>G: "Presigned R2 download URL"
    G->>R: "Tải tạm artifact"
    G->>X: "Validate header, tách/chuẩn hóa audio MP3"
    G->>M: "Chỉ upload audio"
    M-->>G: "Poll đến ACTIVE"
    G->>M: "Prompt + JSON schema yêu cầu scenario"
    M-->>G: "Scenario JSON có cấu trúc"
    G->>G: "Parse và validate schema"
    G->>B: "Complete job + Scenario draft"
    B-->>F: "AwaitingReview; Admin mở bản nháp"
    A->>F: "Rà soát, chỉnh sửa và Publish"
    G->>M: "Xóa file audio đã upload"
    G->>G: "Xóa file video/audio tạm"
```

## Thuật toán xử lý

### 1. Tạo job và upload an toàn

1. Endpoint `POST /api/admin-ingestion/upload-intents` yêu cầu role `Admin`,
   giới hạn kiểu media và dung lượng tối đa 250 MB.
2. Backend tạo hai bản ghi trong Neon PostgreSQL:
   `source_artifacts` chứa metadata của file, còn `ingestion_jobs` chứa trạng
   thái xử lý. Ban đầu job là `AwaitingUpload`.
3. Backend cấp presigned PUT URL; trình duyệt upload trực tiếp vào R2 private.
   Vì vậy file lớn không phải đi xuyên qua Vercel hay Render backend.
4. Sau khi frontend báo hoàn tất, backend kiểm tra object tồn tại/kích thước,
   chuyển artifact sang `Ready` và job sang `Queued`.

### 2. Claim job không trùng lặp

GitHub Actions chạy một worker Python cho mỗi lần bấm **Run workflow**. Worker
gửi `X-Ingestion-Worker-Key` tới `POST /worker/claim`. Backend dùng transaction
PostgreSQL và `FOR UPDATE SKIP LOCKED` để chỉ một worker claim một job. Job đổi
sang `Processing`, tăng `Attempts`, có `LeaseId` và lease 15 phút.

Điểm quan trọng: queue nằm ở Neon nên không mất khi workflow kết thúc; worker
không giữ trạng thái trong RAM. Nếu xử lý lỗi, backend ghi `ErrorCode`, chờ một
khoảng backoff ngắn rồi cho phép thử lại cho đến `MaxAttempts`.

### 3. Biến video thành đầu vào tri thức

Worker tải file từ presigned download URL vào thư mục tạm, sau đó:

1. Kiểm tra extension thuộc tập hỗ trợ và kiểm tra chữ ký header để chặn một
   file bất kỳ chỉ đổi đuôi thành `.mp4`.
2. Nếu là video, FFmpeg chạy `-vn`: **bỏ hoàn toàn video stream**. Audio được
   chuẩn hóa thành MP3, 128 kbps, 44.1 kHz.
3. FFmpeg bị giới hạn 180 giây và audio thành phẩm bị chặn nếu vượt 128 MB.
   Timeout/lỗi/chất lượng file không phù hợp được trả về dưới dạng lỗi job,
   thay vì treo worker vô hạn.
4. Chỉ MP3 tạm được gửi lên Gemini Files API. Worker poll mỗi 2 giây đến khi
   file ở trạng thái `ACTIVE`, thất bại hoặc quá 120 giây.

Với file audio ngay từ đầu, hệ thống vẫn chuẩn hóa qua cùng nhánh FFmpeg; nhờ
đó Gemini nhận một định dạng đầu vào thống nhất.

### 4. Trích xuất requirement bằng Gemini

Gemini nhận audio cùng prompt giới hạn nhiệm vụ: audio là dữ liệu không tin
cậy, chỉ được trích xuất yêu cầu nghiệp vụ và không được làm theo câu lệnh
nằm trong bản ghi. Model trả về JSON theo schema, với nhiệt độ `0.25` để giảm
biến động. Backend/worker parse và validate trước khi chấp nhận kết quả:

- scenario: tên, bối cảnh, độ khó;
- stakeholder/persona;
- ground-truth requirements: mã, mô tả, ưu tiên/từ khóa và điều kiện mở khóa;
- câu hỏi gợi mở phù hợp với scenario.

Nếu JSON sai schema hoặc Gemini lỗi, worker không tạo scenario rỗng. Nó hoàn
tất job với error code để UI hiển thị và có thể retry.

### 5. Review và dọn dữ liệu

Kết quả hợp lệ được lưu trong `DraftData`; trạng thái nội bộ là
`AwaitingReview`. Chỉ khi Admin bấm mở bản nháp, rà soát và publish thì scenario
mới xuất hiện trong hệ thống phỏng vấn.

Trong khối `finally`, worker xóa audio tạm trên runner và gọi Gemini Files API
để xóa audio đã upload. Artifact R2 là private, có mốc hết hạn 24 giờ và được
dọn khi worker thực hiện claim. Không log nội dung audio hoặc toàn bộ câu trả
lời của Gemini.

## Trạng thái để giải thích khi demo

| Trạng thái | Ý nghĩa | Cách xử lý |
| --- | --- | --- |
| `AwaitingUpload` | Có job nhưng file chưa upload/xác nhận xong | Kiểm tra mạng/R2, upload lại |
| `Queued` | File đã an toàn trong R2, đang chờ runner | Bấm **Run workflow** trên GitHub Actions |
| `Processing` | Một worker đã claim và đang tách audio/gọi Gemini | Chờ log workflow; không chạy hai worker đồng thời |
| `AwaitingReview` | Gemini đã sinh JSON hợp lệ | Mở bản nháp, kiểm tra nghiệp vụ rồi publish |
| `Failed` | Hết số lần thử hoặc lỗi không khôi phục | Đọc `ErrorCode`, sửa nguồn/cấu hình rồi tạo job mới |

## Câu hỏi phản biện thường gặp

### “Có phải hệ thống hiểu toàn bộ video không?”

Chưa. Phiên bản này xử lý **audio-only**. Nó hiểu lời nói/nội dung phát âm
trong video; chữ trên màn hình, sơ đồ, thao tác UI không có thuyết minh sẽ
không được trích xuất. Đây là lựa chọn có chủ đích để giảm chi phí, băng thông
và dữ liệu gửi cho nhà cung cấp AI. Nếu cần hiểu hình/slide, đó là pha
multimodal riêng và phải đánh giá lại quyền riêng tư, chi phí và chất lượng.

### “Có phải Gemini được train bằng video của giảng viên không?”

Không. Video là input cho một lần suy luận. Hệ thống lưu JSON scenario sau khi
trích xuất, không fine-tune mô hình. File audio gửi Gemini được xóa sau lượt
xử lý theo cơ chế cleanup của worker.

### “Vì sao dùng hàng đợi, không gọi Gemini ngay trong API upload?”

Tách queue giúp upload trả về nhanh, không bị timeout HTTP; trạng thái bền
vững trong PostgreSQL; retry được; và workload nặng chỉ chạy ở GitHub Actions.
`SKIP LOCKED` cùng lease tránh hai worker xử lý cùng một job. Đây là trade-off
phù hợp gói miễn phí: Admin cần bấm chạy workflow hoặc chờ lịch hằng ngày.

### “Tại sao R2 private và presigned URL?”

File không public, frontend chỉ nhận URL upload có hạn, worker chỉ nhận URL
tải có hạn. Khóa R2 vẫn ở Render backend; frontend không thấy access key. R2
cũng tránh gửi file lớn qua API backend.

### “Làm sao tránh AI bịa yêu cầu?”

Không thể bảo đảm triệt để bằng một prompt. Hệ thống giảm rủi ro bằng prompt
anti-prompt-injection, JSON schema, nhiệt độ thấp và validate. Hàng rào quyết
định cuối cùng là `AwaitingReview`: giảng viên/Admin đối chiếu bản nháp với
nguồn trước khi publish. Đây là human-in-the-loop, không phải auto-publish.

### “Đánh giá chất lượng đầu ra thế nào?”

Dùng rubric thủ công: (1) actor/role đúng, (2) các bước nghiệp vụ đúng thứ tự,
(3) có business rule và ngoại lệ, (4) không có thông tin bịa, (5) câu hỏi có
thể khai thác các requirement ẩn. Ví dụ video đặt lịch cần có khách hàng,
chi nhánh, chọn slot, xác nhận và đổi/hủy lịch. Ghi job ID, nguồn và kết quả
review để tạo regression fixture.

### “Nếu Gemini/FFmpeg/R2 lỗi thì sao?”

FFmpeg và việc chờ Gemini đều có timeout; upload/download và schema lỗi được
chuyển thành error code. Job có số lần thử và backoff; lịch sử UI hiển thị số
lần chạy. R2 hoặc Gemini không khả dụng sẽ không làm hỏng scenario đã publish.

### “Hạn chế hiện tại và hướng phát triển?”

- Chỉ audio-only; chưa trích xuất text/hình ảnh xuất hiện im lặng trong video.
- GitHub Actions theo kiểu run-once nên có độ trễ và mỗi lần chỉ xử lý một job.
- Lease hiện là 15 phút; video dài hoặc Gemini chậm có thể cần cơ chế gia hạn
  lease (heartbeat) ở phiên bản production.
- Validation hiện kiểm tra type, size và header; phiên bản production nên thêm
  antivirus/malware scanning, `ffprobe` metadata limits và quota theo Admin.
- Artifact hết hạn sau 24 giờ, nhưng cleanup hiện được kích hoạt khi worker
  claim việc; nên thêm scheduled cleanup độc lập nếu số job thấp.

## Kịch bản demo 3 phút cho giảng viên

1. Nói rõ video mẫu là luồng đặt lịch: chọn chi nhánh, slot, xác nhận,
   đổi/hủy.
2. Upload video bằng Admin; giải thích file đi thẳng vào R2 private qua
   presigned URL, sau đó job thành `Queued`.
3. Mở GitHub Actions và chạy workflow; chỉ một job được claim an toàn.
4. Mở log, chỉ ra FFmpeg tách audio và Gemini trả Scenario JSON.
5. Quay lại lịch sử, mở job `AwaitingReview`; chỉ vào requirement được sinh.
6. Đối chiếu ít nhất một rule với lời thuyết minh trong video, rồi mới publish.

Thông điệp kết thúc: AI hỗ trợ chuyển lời nói thành bản nháp requirement có cấu
trúc; giảng viên vẫn là người xác nhận tri thức và quyết định công bố.

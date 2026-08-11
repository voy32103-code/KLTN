# Tài liệu ReqSimulator

Chỉ mục này phản ánh codebase và hạ tầng đã được rà soát ngày **11/08/2026**.
Khi tài liệu lịch sử mâu thuẫn với code hoặc audit mới nhất, ưu tiên audit mới
nhất và code hiện tại.

## Tài liệu vận hành hiện hành

1. [Audit hệ thống 11/08/2026](AUDIT-2026-08-11.md) — phạm vi review, bằng
   chứng test, rủi ro, giới hạn và thứ tự khắc phục.
2. [Deployment và ingestion runbook](INGESTION-DEPLOYMENT.md) — Vercel,
   Render, Neon, private R2, GitHub Actions và cách xử lý job `Queued`.
3. [Video knowledge-ingestion defense guide](VIDEO-KNOWLEDGE-INGESTION-DEFENSE-GUIDE.md)
   — thuật toán audio-only, Mermaid flow, trạng thái job và Q&A bảo vệ.
4. [MediaCrawler assessment and video testing](MEDIACRAWLER-ADOPTION-AND-VIDEO-TESTING.md)
   — phạm vi được học/áp dụng, giới hạn pháp lý và video nghiệp vụ test.

## Tài liệu lịch sử hoặc nghiên cứu

- [Implementation status 08/08/2026](IMPLEMENTATION-80-PERCENT-STATUS.md):
  snapshot trước khi hoàn thiện luồng R2 + GitHub Actions; không dùng số lượng
  test trong tài liệu này làm kết quả kiểm tra hiện tại.
- [Implementation readiness](IMPLEMENTATION-READINESS.md): checklist lịch sử.
- [AAOC calibration runbook](research_academic/calibration_ab_mermaid_runbook.md)
  và [user survey template](research_academic/user_survey_template.md): tài
  liệu nghiên cứu/evaluation, không phải runbook production.

## Quy ước

- Không đặt secret, URL database có password, R2 access key hay Gemini key vào
  `docs/` hoặc Git.
- Chỉ Admin được tạo ingestion source và chỉ Admin publish draft do AI tạo.
- Video công khai không đồng nghĩa với có quyền tải lại hoặc gửi cho Gemini;
  chỉ dùng file do người vận hành sở hữu hoặc có quyền sử dụng.
- Các runbook/audit được liệt kê ở trên là tài liệu versioned. Nội dung nghiên
  cứu, artifact lớn hoặc tài liệu nháp vẫn bị ignore mặc định; kiểm tra không
  có secret trước khi thêm bất kỳ tài liệu mới nào vào Git.

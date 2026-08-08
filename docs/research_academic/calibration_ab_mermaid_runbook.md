# Calibration, A/B Feedback và Mermaid Validation Runbook

Ngày cập nhật: 2026-08-08

## 1. AAOC calibration trên dataset lớn

Công cụ: ai-service/tools/calibrate_aaoc.py

Annotation queue đã tạo:

    docs/research_academic/pilot_dataset/aaoc_annotation_queue_200.jsonl

File này có đúng 200 cặp, có provenance và candidate tổng hợp, nhưng label cuối để null.
Hai người gán nhãn độc lập điền annotator1Label và annotator2Label. Nếu bất đồng,
người thứ ba điền adjudicatedLabel.

Hoàn tất và kiểm tra quota:

    python ai-service/tools/finalize_aaoc_annotations.py --input docs/research_academic/pilot_dataset/aaoc_annotation_queue_200.jsonl --output docs/research_academic/pilot_dataset/aaoc_calibration_ready.jsonl --report docs/research_academic/pilot_dataset/aaoc_annotation_report.json --minimum 200

Lệnh sẽ thất bại nếu chưa đủ 200 nhãn thống nhất/adjudicated. Không được sao chép
proposedLabel vào nhãn người đánh giá mà không đọc từng cặp.

Mỗi dòng JSONL là một cặp candidate–ground truth đã được gán nhãn:

    {"id":"P001","label":true,"typeMatch":true,"actionMatch":true,"objectMatch":true,"actorScore":1.0,"conditionScore":0.8,"requirementType":"FR"}

Chạy:

    python ai-service/tools/calibrate_aaoc.py --dataset data/aaoc-labelled.jsonl --output reports/aaoc-calibration.json --bootstrap-rounds 2000

Quy trình cố định:

- Chia train/holdout 80/20 theo hash ID để có thể tái lập.
- Giữ trọng số Actor/Action/Object/Condition = 20/30/30/20.
- Grid search threshold từ 0.60 đến 0.95 trên train.
- Báo cáo Precision/Recall/F1 trên holdout, theo từng FR/NFR/BR.
- Bootstrap 95% CI; cảnh báo nếu tổng mẫu dưới 200 hoặc holdout dưới 40.
- Không thay threshold production nếu CI chưa ổn định hoặc lỗi tập trung vào một requirement type.

## 2. A/B test learning feedback

- Variant A: feedback coaching theo luật xác định.
- Variant B: AI learning feedback.
- Phân nhóm ổn định theo session ID; variant được lưu cùng evaluation.
- Cả hai variant không nhận nội dung hidden requirement trong dữ liệu sinh feedback.
- Sinh viên đánh giá Helpfulness, Actionability, No-answer-leak từ 1 đến 5.
- Mỗi session chỉ có một bản ghi survey.
- Dashboard luôn hiển thị n/30 và số còn thiếu cho cả A lẫn B, kể cả khi chưa có survey.

Admin đọc kết quả tại:

    GET /api/Admin/stats/feedback-experiment

Không kết luận winner trước khi mỗi variant có ít nhất 30 phản hồi. Chỉ chọn variant mới
khi Helpfulness và Actionability tăng mà No-answer-leak không giảm.

## 3. Mermaid validation

Mọi diagram sinh bởi AI được kiểm tra:

- Header đúng graph/flowchart TD/LR hoặc erDiagram.
- Cân bằng ngoặc, giới hạn 50.000 ký tự.
- Chặn init directive, script, javascript URL và click directive.
- Nếu lỗi, tự dựng lại Mermaid xác định từ structured FR và ghi trạng thái repaired.

UI hiển thị trạng thái Mermaid valid/repaired để giảng viên biết diagram đã qua repair.

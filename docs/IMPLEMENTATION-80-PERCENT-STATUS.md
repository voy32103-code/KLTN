# Trạng thái triển khai mục tiêu 80%

Ngày cập nhật: 2026-08-08

## Đã triển khai

- Matching AAOC one-to-one: Actor/Action/Object/Condition = 20/30/30/20; lọc Type, Action, Object; ngưỡng matched 0.80.
- Phân loại câu hỏi vague, on-topic, specific, conditional; progressive disclosure theo mức chất lượng.
- Scenario lưu source URL; preview nhận tối đa 10 nguồn, gộp và loại trùng trước manual review/publish.
- Ground truth lưu actor/action/object/condition/type/priority và JSON chuẩn hóa.
- Tách Stakeholder–Persona; scenario publish sinh 3 stakeholder x 2 persona.
- Conversation log lưu topic và question quality của câu hỏi sinh viên.
- AI learning feedback chỉ nhận ID/category/match score/AAOC, không nhận nội dung hidden requirement; provider lỗi sẽ dùng fallback xác định.
- Diagram nhận requirement có cấu trúc, chỉ biến FR thành use case và dùng scenario làm ngữ cảnh ERD.
- Evaluation runner sinh Precision/Recall/F1 micro/macro; có phiếu user survey chuẩn.
- Calibration runner cho dataset JSONL lớn: split tái lập, grid search threshold, bootstrap 95% CI và breakdown FR/NFR/BR.
- Đã tạo annotation queue đúng 200 cặp AAOC; calibration bị khóa cho đến khi đủ dual-review/adjudication.
- UI preview cho phép sửa và validate Actor/Action/Object/Condition/Type/Priority trước publish.
- A/B feedback tích hợp end-to-end: assignment theo session, persistence, student survey và admin report.
- Admin A/B report hiển thị quota n/30, remaining và chỉ báo ready-for-analysis cho cả hai variant.
- Mermaid được validate tự động; output lỗi được dựng lại xác định và đánh dấu repaired.

## Cách chạy evaluation

    python ai-service/tools/evaluate_pilot.py --annotations docs/research_academic/pilot_dataset/annotations --predictions docs/research_academic/pilot_dataset/predictions --output docs/research_academic/pilot_dataset/evaluation-report.json

Mỗi file prediction có tên transcript_id.prediction.json, chứa matches với hiddenId và matchType,
hoặc cùng schema hidden_requirement_labels của annotation.

## Còn lại để đạt mức production

- Thu thập đủ ít nhất 200 cặp AAOC đã gán nhãn và kiểm định liên giám khảo.
- Thu thập tối thiểu 30 survey cho mỗi A/B variant trước khi kết luận.
- Bổ sung kiểm thử render Mermaid bằng parser chính thức trong pipeline browser.

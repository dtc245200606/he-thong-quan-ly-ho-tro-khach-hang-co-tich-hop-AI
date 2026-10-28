# Báo cáo hoàn thiện CustomerSupport

Đã đọc nguồn ứng dụng MVC, entity/schema, service Gemini, Razor/CSS/JS, cấu hình/chạy và verification. Tài liệu dựa trên chức năng hiện hữu. Không sửa controller, model, service, view, DB schema, provider/model đang lưu hoặc nghiệp vụ đã hoạt động. Không xóa dữ liệu local. Trạng thái xóa test_crm.html đã có từ trước được giữ nguyên; không commit/push hoặc đưa DB vào Git.

## File tạo mới

- docs/01-Phan-tich-bai-toan.md
- docs/02-Yeu-cau-chuc-nang.md
- docs/03-Yeu-cau-phi-chuc-nang.md
- docs/04-Use-Case.md
- docs/05-Thiet-ke-CSDL.md
- docs/06-Kien-truc-he-thong.md
- docs/07-Thiet-ke-AI.md
- docs/08-Prompt-AI.md
- docs/09-Nhat-ky-su-dung-AI.md
- docs/10-Ke-hoach-trien-khai.md
- docs/11-Test-Cases.md
- docs/12-Deployment.md
- docs/ai-evidence/README.md
- docs/ai-evidence/01-Vong-1.md
- docs/ai-evidence/02-Vong-2.md
- docs/ai-evidence/03-Vong-3.md
- docs/verification-results.md
- docs/hoan-thien-project.md (báo cáo này)
- .env.example
- scripts/backup-db.ps1
- scripts/restore-db.ps1
- verification/DatabaseChecks/DatabaseChecks.csproj
- verification/DatabaseChecks/Program.cs

Tiện ích DatabaseChecks chỉ phục vụ backup/restore và tự kiểm SQLite, không thêm chức năng nghiệp vụ hoặc bảng vào ứng dụng. Package dùng lại các phiên bản SQLite của project.

## File sửa

- README.md: môi trường/cài/chạy/role/Gemini/test/backup/restore/cấu trúc và link tài liệu.
- .gitignore: bổ sung env/backup/log/profile/publish/cấu hình local và các artifact; giữ .env.example. Ignore không xóa file trên máy. Repo hiện phần lớn source còn untracked từ trước, chưa tự stage/commit.

## Verification thực chạy

Build ứng dụng và browser thành công, 0 warning/error. 24 service, 37 HTTP, 8 browser, 7 database, 4 live Gemini và 4 kiểm tra wrapper restore thành công. Backup thật được tạo và kiểm integrity; restore thật chỉ thực hiện trên target tạm, DB đang dùng chỉ chạy -WhatIf. Xem [báo cáo chi tiết](verification-results.md), gồm cả sự cố môi trường ban đầu và giới hạn coverage.

## Việc người dùng cần làm

1. Chạy và chụp kết quả 3 vòng prompt theo ai-evidence, điền model/thời gian/input/context/output/nhận xét; không gọi đây là lịch sử cũ.
2. Chụp demo các role, ticket/nguồn AI/history/handoff/fallback; che key, mật khẩu, cookie và dữ liệu cá nhân. Ảnh browser local hiện có là ảnh kiểm UI, không thay minh chứng 3 vòng prompt.
3. Chạy các test thủ công M01–M09 chưa được bao phủ (đặc biệt Staff login và input biên); cập nhật Actual sau khi thực sự chạy.
4. Đối chiếu rubric giảng viên, yêu cầu hình thức/ảnh/video/báo cáo nộp. Nếu yêu cầu public URL, thực hiện deployment theo 12; localhost hiện chưa là public hosting.

## Nhóm tiêu chí đã có căn cứ

| Tiêu chí theo yêu cầu người dùng | Minh chứng hiện có |
|---|---|
| Phân tích bài toán/phạm vi | 01, liệt kê đúng chức năng/giới hạn |
| Yêu cầu chức năng/phi chức năng | 02–03, validation, quyền, ổn định và giới hạn rõ |
| Actor/use case | 04, Customer/Staff/Admin/Guest và Mermaid |
| Thiết kế CSDL | 05, đúng 5 bảng, PK/FK/unique/delete behavior, ERD |
| Kiến trúc và dữ liệu | 06, Razor/CSS/JS, MVC, EF SQLite, Gemini, diagram |
| Tích hợp AI/prompt có trách nhiệm | 07–09, context/history/key/fallback/timeout/rate, cảnh báo AI, live smoke thành công |
| Cải tiến prompt | Có protocol/phiếu 3 vòng trung thực; **output/ảnh đánh giá thực tế còn thiếu** |
| Kiểm thử đúng/sai/biên | 11 và kết quả tự động; **test thủ công biên còn thiếu**, không tự ghi Pass |
| Kế hoạch/triển khai/vận hành | 10, 12, README, env mẫu, backup/restore được kiểm an toàn |
| Vệ sinh repo/bảo toàn dữ liệu | ignore được xác minh; không secret thật trong file tạo/sửa, không xóa dữ liệu |

Chưa có đề rubric đầy đủ để xác nhận điểm hoặc mức xếp loại chính thức. Project đã có nền tảng tài liệu/kỹ thuật cho các nhóm trên; các tiêu chí đòi hỏi ảnh, lịch sử thử nghiệm hoặc public deployment vẫn cần người nộp bài hoàn tất.

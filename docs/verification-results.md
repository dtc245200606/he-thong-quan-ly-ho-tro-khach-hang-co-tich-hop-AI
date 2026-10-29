# Kết quả verification thực tế

## Lần chạy 29/10/2026 (sau khi thêm chatbot, Groq, SLA, xác nhận email, phân công, trợ lý CRM, ràng buộc DB)

.NET SDK 10.0.401 trên Windows 11. Mọi bộ chạy trên DB/settings tạm; DB thật không bị sửa.

| Lệnh / kiểm tra | Kết quả thực tế | Phạm vi |
|---|---|---|
| `dotnet build CustomerSupportAI.csproj` | **Thành công, 0 warning, 0 error** | |
| ServiceChecks | **33 PASS**, 0 fail | Gemini (giả lập HTTP), mã hóa key, key không giải mã được không làm sập |
| ChatbotChecks | **74 PASS**, 0 fail | 20 tiêu chí chatbot, xác nhận email, SLA, Groq/Gemini (giả lập HTTP), trợ lý CRM (quyền, số liệu khớp DB, không ghi được, audit), trigger ràng buộc, xóa nhân viên → bỏ phân công, dữ liệu mẫu + backfill, nâng cấp DB cũ |
| HttpChecks (server thật, port 5181) | **110 PASS**, 0 fail | Toàn bộ kiểm thử cũ + chatbot API, phân quyền admin API 401/403, rate limit, xác nhận email qua outbox, SLA, đổi ưu tiên, phân công, trợ lý CRM không có key |
| BrowserChecks (Edge headless) | **28 PASS**, 0 fail | Desktop 1440px và mobile 390px: không tràn ngang, widget chat mở/trả lời/nằm gọn, console chat, công cụ AI ticket |
| DatabaseChecks | **8 PASS** | Backup/restore trên DB tạm |
| Publish Release + chạy Production | **Đạt** | Không có App_Data/DB/settings trong bản publish; key ring trong thư mục dữ liệu; sau khởi động lại key vẫn giải mã được; ticket không tồn tại → 404; trang lỗi không lộ stack trace |
| Docker image | **Chưa kiểm thử** | Docker Desktop không chạy trên máy phát triển |
| Groq/Gemini với key thật cho chatbot và trợ lý CRM | **Chưa kiểm thử tự động** | Cần key của người dùng; dùng nút "Kiểm tra kết nối chatbot" ở /Settings |

Lỗi phát hiện nhờ kiểm thử trong lần này: truy vấn danh sách nhân viên phụ trách không dịch được sang SQL (500 ở trang ticket); `dotnet publish` lỗi do quét thư mục artifacts và có thể chép `App_Data/*.json` (key đã mã hóa) vào bản publish; key Data Protection mất sau khi khởi động lại container. Cả ba đã sửa và có kiểm thử.

## Lần chạy 08/10/2026

Ngày chạy **08/10/2026, Asia/Saigon (UTC+07)**, khoảng 00:15–00:20. .NET SDK 10.0.102 trên Windows. Đây là kết quả lần chạy trong phiên hoàn thiện tài liệu, không phải kết quả giả định và không phải minh chứng đã chạy ba vòng prompt.

| Lệnh / kiểm tra | Actual result | Phạm vi / giới hạn |
|---|---|---|
| dotnet build CustomerSupportAI.csproj | **Thành công**, 0 warning, 0 error | Ứng dụng hiện tại, không sửa nghiệp vụ |
| ServiceChecks | **24 PASS**, exit 0 | Mock Gemini; key mã hóa, câu thông thường, history/context, parsing và fallback |
| dotnet build BrowserChecks.csproj | **Thành công**, 0 warning, 0 error | Tiện ích Edge |
| HttpChecks.ps1 -CaptureBrowser: HTTP | **37 PASS**, exit 0 cuối script | DB/settings test riêng, port 5181; UpdateStatus được gọi bằng Admin |
| HttpChecks.ps1 -CaptureBrowser: Edge | **8 PASS**, cùng exit 0 | 6 trang desktop/mobile, một chat-chip/source, landing mobile |
| DatabaseChecks | **7 PASS**, exit 0 | DB tạm: online WAL backup, không ghi đè, từ chối target mở/WAL và nguồn hỏng, giữ safety copy/target bytes |
| scripts/backup-db.ps1 | **Thành công**, integrity_check ok | Tạo backups/customer-support-20261008-001711-311.db từ DB local; không sửa dữ liệu nghiệp vụ |
| scripts/restore-db.ps1 -WhatIf trên DB local | **Thành công**, chỉ preview | Không phục hồi/ghi đè DB đang dùng |
| 4 kiểm tra wrapper restore trên target tạm | **4 PASS** | Mock Read-Host trả CANCEL/RESTORE để kiểm: hủy không tạo file, WhatIf không tạo file, xác nhận restore target tạm, replacement giữ safety copy |
| LiveGemini --verify-web | **4 PASS**, exit 0 | 3 câu hỏi thật mode ai + lưu/nhớ history; model local gemini-3.5-flash-lite, không đổi model/key |
| Git ignore | **Đã xác minh** | bin/obj, App_Data, DB/WAL/SHM, backup, .env, log, artifacts/profile bị ignore; .env.example vẫn có thể version |

## Gemini thật

Câu “xin chào” nhận mode=ai, sources rỗng; phản hồi thực tế:

> Xin chào! Tôi là trợ lý chăm sóc khách hàng của CustomerSupport. Tôi có thể giúp gì cho bạn hôm nay?

Sau “Tôi tên là Linh.”, hỏi “Tôi vừa cho biết tên tôi là gì?” nhận câu trả lời xác định tên Linh. Kiểm tra DB có đúng 3 lượt thử, rồi cleanup user và lượt chat thử bằng code LiveGemini. Không lưu key, password hoặc cookie trong báo cáo. Site hiện có tại localhost:5180 đã được dùng cho lần kiểm tra; đây không phải deployment public.

## Sự cố lần đầu và cách xử lý

- Tiện ích DB ban đầu tham chiếu Microsoft.Data.Sqlite metapackage không có trong nguồn NuGet offline. Đổi sang Microsoft.Data.Sqlite.Core 10.0.12 + SQLitePCLRaw.bundle_e_sqlite3 2.1.12 đã được app dùng; chạy lại thành công.
- HTTP/server chạy trong sandbox bị giới hạn quyền Windows Event Log/Data Protection, gây đóng kết nối. Chạy lại với quyền Windows bình thường thành công toàn bộ; không sửa controller/service để né lỗi môi trường.
- Thử khởi động thêm 5180 báo port đã dùng; sử dụng instance CustomerSupport hiện có để kiểm tra LiveGemini, không dừng process của người dùng.

## Artifacts local

Browser tạo dashboard-desktop/mobile.png, assistant-desktop/mobile.png, knowledge-desktop/mobile.png và landing-mobile.png trong verification-artifacts. HTTP logs/DB/profile được giữ local và bỏ Git. Các lần test thất bại ban đầu vẫn có artifact riêng, không bị xóa để làm đẹp kết quả. Target thử restore nằm dưới verification-artifacts/script-checks-*; không restore vào DB thật.

## Chưa được coi là Pass

Các M01–M09 trong 11 chưa chạy thủ công, đặc biệt đăng nhập Staff riêng, các ngưỡng độ dài và history >6. M10 đã kiểm cancellation/WhatIf bằng mock input và target tạm, chưa là video người dùng thao tác Read-Host tương tác. Ba vòng ai-evidence chưa chạy/chụp ảnh, chưa đo độ chính xác AI/hiệu năng và chưa deploy public. Smoke Gemini hiện tại không thay thế ba vòng prompt.

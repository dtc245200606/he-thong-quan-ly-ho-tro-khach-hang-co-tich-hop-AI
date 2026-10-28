# 11. Test cases và kết quả

## Quy ước

**Expected** = hành vi mong đợi theo code/yêu cầu. **Automated coverage** = bài nào có assertion, với giới hạn gì. **Actual result** = kết quả của lần chạy được ghi trong [verification-results.md](verification-results.md). Các ô “Chưa chạy thủ công” không phải Pass. Các verification là console/PowerShell fail-fast, không phải bộ xUnit; `dotnet test` không thay thế lệnh chạy chúng.

## Test đúng, sai và biên theo bộ hiện có

| ID / loại | Input / thao tác | Expected | Automated coverage | Actual result |
|---|---|---|---|---|
| T01 Đúng | Đọc landing, kho hướng dẫn không login | 200 / có giao diện | HttpChecks | Xem báo cáo chạy HTTP |
| T02 Đúng | Đăng ký thông tin hợp lệ | Customer được tạo và vào Dashboard | HttpChecks | Xem báo cáo chạy HTTP |
| T03 Đúng | 7 trang Customer sau login | 200 | HttpChecks | Xem báo cáo chạy HTTP |
| T04 Đúng | Tạo ticket, đọc, trả lời, search + Open | Ticket/tin hiển thị đúng | HttpChecks | Xem báo cáo chạy HTTP |
| T05 Sai quyền | Customer khác đọc ticket ID | 404 | HttpChecks | Xem báo cáo chạy HTTP |
| T06 Sai quyền | Customer mở Settings | AccessDenied | HttpChecks | Xem báo cáo chạy HTTP |
| T07 Đúng | Không key, hỏi đổi mật khẩu | mode knowledge, nguồn phù hợp | ServiceChecks + HttpChecks | Xem báo cáo hai bộ |
| T08 Sai/ngữ cảnh rỗng | Tìm unrelated xyz | Không bịa nguồn | ServiceChecks | Xem báo cáo service |
| T09 Đúng | Có key giả/mock, xin chào / bạn là ai / bạn có thể giúp gì / thủ đô Pháp, không bài | Mỗi câu gọi provider và mode ai | ServiceChecks mô phỏng, không chứng minh key thật | Xem báo cáo service |
| T10 Đúng | Hỗ trợ rồi xin chào | Chào không kéo nguồn cũ; câu hỗ trợ lấy bài | ServiceChecks | Xem báo cáo service |
| T11 Đúng | Reload chat / tài khoản khác / Clear | Lưu riêng, không lộ, Clear xóa riêng | HttpChecks | Xem báo cáo HTTP |
| T12 Đúng | Không key, hỏi trạng thái | Chỉ ticket riêng gần nhất | HttpChecks | Xem báo cáo HTTP |
| T13 Sai | Message chỉ khoảng trắng | HTTP 400 | HttpChecks | Xem báo cáo HTTP |
| T14 Sai | CSRF token invalid | HTTP 400 | HttpChecks | Xem báo cáo HTTP |
| T15 Đúng | Handoff với chat có sẵn | Ticket chứa transcript | HttpChecks | Xem báo cáo HTTP |
| T16 Đúng | Admin login, danh sách, tạo bài/Staff | Trang hoạt động và dữ liệu xuất hiện | HttpChecks | Xem báo cáo HTTP |
| T17 Đúng/bảo mật | Lưu key, sau đó key trống / RemoveKey | Ciphertext; form không lộ; giữ/ngắt đúng | ServiceChecks + HttpChecks | Xem báo cáo hai bộ |
| T18 Đúng | UpdateStatus bằng Admin | Resolved rồi Closed; Closed không có composer | HttpChecks; label cũ nói staff nhưng session thực tế là Admin | Xem báo cáo HTTP; Staff riêng chưa chạy |
| T19 Sai nghiệp vụ | Reply ticket Closed | Không thêm tin | HttpChecks | Xem báo cáo HTTP |
| T20 Đúng | Đổi password và đăng nhập mới | Password mới xác thực được | HttpChecks | Xem báo cáo HTTP |
| T21 Biên/rate | Gửi lặp tới vượt cửa sổ 12/phút | Có HTTP 429 | HttpChecks (loop tối đa 14) | Xem báo cáo HTTP |
| T22 Biên/provider | MAX_TOKENS có text | Giữ mode ai và notice | ServiceChecks | Xem báo cáo service |
| T23 Sai/provider | JSON lỗi, HTTP 401, 429, timeout giả | Fallback rõ nguyên nhân | ServiceChecks; timeout mô phỏng exception, không chờ 45 giây | Xem báo cáo service |
| T24 Hạn chế/provider | SAFETY | mode ai, Success=false, không giả mất API | ServiceChecks | Xem báo cáo service |
| T25 Đúng/bảo mật | Payload provider | Header key, endpoint/schema, history + context, bỏ thought | ServiceChecks; chỉ kiểm một lượt cũ, chưa khẳng định test đủ ngưỡng 6 | Xem báo cáo service |
| T26 Biên/UI | Viewport 1440x1050 và 390x1000 | Dashboard/Assistant/Knowledge không tràn ngang; landing mobile | BrowserChecks qua HttpChecks -CaptureBrowser | Xem báo cáo browser |
| T27 Đúng/UI | Nhấn prompt chip | fetch chạy và render sources, send bật lại | BrowserChecks; đang test fallback không key | Xem báo cáo browser |
| T28 AI thật | xin chào, nói tên Linh, hỏi lại tên | mode ai, chào không nguồn, nhớ tên, lưu 3 lượt | LiveGemini --verify-web, cần app/key/quota thật; tài khoản tạm được dọn | Xem báo cáo live riêng |
| T29 Đúng/backup | DB đang mở WAL có dữ liệu committed | Backup đọc được dữ liệu | DatabaseChecks (bổ sung cho script) | Xem báo cáo database |
| T30 Sai/backup | Đích backup đã có | Không ghi đè | DatabaseChecks | Xem báo cáo database |
| T31 Sai/restore | Target đang mở/WAL hoặc nguồn hỏng | Từ chối; target giữ nguyên | DatabaseChecks | Xem báo cáo database |
| T32 Đúng/restore | Restore offline vào target mới/cũ | Dữ liệu phục hồi, giữ bản trước restore | DatabaseChecks; xác nhận Read-Host của PS kiểm riêng | Xem báo cáo database |

## Test thủ công cần bổ sung (chưa có automated assertion tương ứng)

| ID | Input đúng/sai/biên | Expected | Automated coverage | Actual result |
|---|---|---|---|---|
| M01 | Đăng nhập sai password / email đã đăng ký | Báo lỗi, không tạo phiên/user mới | Chưa có | Chưa chạy thủ công |
| M02 | Mật khẩu đăng ký/đổi 9 và 10 ký tự; confirm lệch | 9 từ chối, 10 nhận nếu các trường khác đúng; lệch từ chối | Chưa có test ngưỡng | Chưa chạy thủ công |
| M03 | Subject 120/121, FirstMessage 9/10/4000/4001, Category giả | Chỉ ngưỡng/danh mục hợp lệ tạo ticket | Chưa có test ngưỡng | Chưa chạy thủ công |
| M04 | Reply 1/4000/4001, Message 1/2000/2001 | Chấp nhận trong ngưỡng; ngoài ngưỡng từ chối | Chỉ blank Message đã có HTTP | Chưa chạy thủ công |
| M05 | History hơn 6 lượt; câu cũ ngoài cửa sổ | Request chỉ chứa 6 lượt cũ và câu mới; lưu DB vẫn nhiều hơn | Chưa có assertion ngưỡng 6 | Chưa chạy thủ công |
| M06 | Staff login, reply Open và UpdateStatus; Customer giả POST UpdateStatus | Staff làm được/đổi In progress; Customer bị chặn | HTTP hiện dùng Admin cho UpdateStatus | Chưa chạy thủ công |
| M07 | Title bài 160/161, Category 60/61, Body 6000/6001 | Validation giới hạn | Chưa có | Chưa chạy thủ công |
| M08 | Handoff không history / transcript dài | Chuyển Create / cắt ≤4000 | HTTP mới test history có sẵn | Chưa chạy thủ công |
| M09 | Prompt injection, yêu cầu refund/chính sách chưa có | Không lộ dữ liệu, không bịa/xác nhận thao tác | Chưa đánh giá Gemini thật | Chưa chạy; dùng ai-evidence |
| M10 | Restore nhập khác RESTORE, hoặc -WhatIf | Không ghi DB; -WhatIf không yêu cầu xác nhận | Kiểm PS dry-run riêng nếu có báo cáo | Xem báo cáo hoặc chưa chạy |

## Lệnh chạy

```powershell
dotnet build .\CustomerSupportAI.csproj
dotnet run --project .\verification\ServiceChecks\ServiceChecks.csproj
dotnet build .\verification\BrowserChecks\BrowserChecks.csproj
.\verification\HttpChecks.ps1 -CaptureBrowser
dotnet run --project .\verification\DatabaseChecks\DatabaseChecks.csproj
# Sau khi Start-Web đang chạy 5180 và Gemini có key:
dotnet run --project .\verification\LiveGemini\LiveGemini.csproj -- --verify-web
```

HTTP dùng test.db/settings riêng trong verification-artifacts, server 5181; Edge dùng profile riêng và cổng debug 9224. Các artifacts bỏ Git nhưng giữ local. Service dùng thư mục tạm, key giả và handler mô phỏng. Live dùng DB hiện tại, tạo rồi xóa chỉ user/lượt chat thử; không sử dụng --use-model nếu muốn giữ cấu hình hiện có. Không coi Pass test mô phỏng là bằng chứng Gemini thực sự đang kết nối.

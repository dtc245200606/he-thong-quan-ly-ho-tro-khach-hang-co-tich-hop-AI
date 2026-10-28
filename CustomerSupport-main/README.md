# CustomerSupport

Cổng hỗ trợ khách hàng tiếng Việt dùng ASP.NET Core MVC (.NET 10), Razor/CSS/JavaScript, SQLite/EF Core và Google Gemini. Tên assembly vẫn là `CustomerSupportAI.csproj`, giữ cấu hình build hiện có.

## Môi trường

- .NET **SDK 10**, target net10.0; EF Core SQLite 10.0.12.
- Windows PowerShell 5.1+ cho script chạy, HTTP verification và backup/restore.
- Internet để restore NuGet lần đầu và gọi Gemini.
- Microsoft Edge cho BrowserChecks, đường dẫn hiện tại `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`.
- Quyền ghi DB/App_Data; cổng 5180 (demo), 5181 (HTTP test), 9224 (Edge debug) còn trống.

## Cài và chạy

Mở PowerShell trong thư mục CustomerSupport:

```powershell
dotnet restore .\CustomerSupportAI.csproj
dotnet build .\CustomerSupportAI.csproj
.\Start-Web.ps1
```

Mở **http://localhost:5180**; Ctrl+C dừng app. Start-Web bật Development/LocalSetup; nếu chưa có Admin, sinh mật khẩu random trong `App_Data/local-admin.txt`. Đọc file local, đăng nhập rồi đổi mật khẩu; không chiếu/commit file này. Admin đã tồn tại không bị reset. Dữ liệu giữ qua các lần chạy.

Chạy trực tiếp với môi trường tự cấu hình:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project .\CustomerSupportAI.csproj --no-launch-profile --urls http://localhost:5180
```

Lệnh trực tiếp không tự bật LocalSetup. [.env.example](.env.example) chỉ là mẫu biến; **.NET không tự đọc .env**. Đặt biến trong PowerShell hoặc secret configuration của hosting. Không có mật khẩu demo cố định trong source.

## Tài khoản và role

| Role | Quyền thực tế |
|---|---|
| Guest | Landing, đăng ký/đăng nhập, kho hướng dẫn công khai |
| Customer | Tạo/tìm/xem/phản hồi ticket riêng; tổng quan/hoạt động riêng; trợ lý và chuyển chat thành ticket |
| Staff | Đăng nhập tại màn hình chung `/Account/Login` (email hoặc số điện thoại), hệ thống tự nhận diện vai trò; xử lý mọi ticket/cập nhật trạng thái và danh mục; lọc hộp thư theo trạng thái, "chờ nhân viên phản hồi", danh mục, khách hàng, ngày tạo; dùng **công cụ AI trong ticket**; **xem, thêm, sửa, khóa/mở khóa, xóa khách hàng** tại `/Customers`; không quản lý Staff/Admin |
| Admin | Đăng nhập tại màn hình chung `/Account/Login`; xử lý ticket như Staff; **thêm/xem/sửa/khóa/xóa mọi tài khoản** (Admin, Staff, Customer) và đổi vai trò tại `/Admin/Users`; xem **nhật ký hoạt động** `/Admin/AuditLog`; tạo/sửa hướng dẫn; cấu hình/test Gemini |

Mọi tài khoản đăng nhập có profile/đổi mật khẩu và lịch sử trợ lý riêng. Mọi vai trò đăng nhập tại một màn hình chung `/Account/Login` bằng email hoặc số điện thoại (khai báo khi đăng ký); máy chủ đọc vai trò trong DB rồi chuyển Khách hàng tới Tổng quan, Staff/Admin tới danh sách ticket. `/Account/StaffLogin` cũ chuyển hướng về trang này.

### Quản lý tài khoản

- **Đặt lại mật khẩu**: Admin (mọi tài khoản) và Staff (khách hàng) nhập mật khẩu mới trong trang Sửa; người dùng tự đặt lại qua **Quên mật khẩu?** (xem mục bên dưới).
- **Khóa/mở khóa**: tài khoản bị khóa không đăng nhập được và phiên đang mở bị đăng xuất ngay (cookie được kiểm tra với DB ở mỗi request, nên đổi vai trò/xóa user cũng có hiệu lực ngay).
- **Xóa**: xóa vĩnh viễn tài khoản cùng ticket và lịch sử trợ lý của chính nó. Tài khoản đã phản hồi trong ticket của người khác (thường là Staff) không xóa được, hãy khóa thay vì xóa.
- **Bảo vệ**: không tự khóa/xóa/đổi vai trò chính mình; luôn còn ít nhất một Admin đang hoạt động.
- **Nhật ký**: mọi thao tác tạo/sửa/khóa/mở khóa/xóa tài khoản được ghi vào bảng `AuditLogs` (không có khóa ngoại nên không mất khi xóa user).
- **Chống dò mật khẩu**: 5 lần sai liên tiếp cho cùng IP + email sẽ bị chặn 15 phút (bộ nhớ tiến trình, mất khi khởi động lại; sau reverse proxy cần cấu hình ForwardedHeaders để lấy đúng IP).
- **Nâng cấp DB cũ**: khi khởi động, app tự thêm cột `Users.IsActive` và bảng `AuditLogs` (additive, không mất dữ liệu).

Chưa có phân công ticket, mức ưu tiên lưu trong DB, SLA, nhóm hỗ trợ, tệp đính kèm, ghi chú nội bộ hay thanh toán (schema hiện tại không có các cột/bảng này). Sidebar nhóm theo Tổng quan · Hỗ trợ khách hàng · Trí tuệ nhân tạo, Admin có thêm Quản lý vận hành (danh sách nhân viên) và Hệ thống; dashboard dùng số liệu thật, Staff thấy thêm số ticket chờ phản hồi và thời gian phản hồi đầu tiên trung bình (từ lúc tạo ticket đến tin nhắn đầu tiên của nhân viên, với ticket tạo trong kỳ).


### Quên mật khẩu

`/Account/ForgotPassword` (liên kết "Quên mật khẩu?" ở trang đăng nhập): nhập email hoặc số điện thoại → hệ thống gửi liên kết `/Account/ResetPassword?token=…` tới email của tài khoản.

- Token ký bằng ASP.NET Data Protection, **hết hạn sau 30 phút**, chứa dấu của mật khẩu hiện tại nên **chỉ dùng được một lần** và tự vô hiệu khi mật khẩu đổi. Không cần bảng DB mới.
- Phản hồi giống nhau dù tài khoản có tồn tại hay không; email gửi nền nên thời gian phản hồi cũng không lộ thông tin. Tài khoản bị khóa không nhận liên kết.
- Mỗi IP tối đa 5 yêu cầu / 15 phút. Nhật ký ghi `user.password-reset-request` và `user.password-reset`.
- Sau khi đặt lại hoặc đổi mật khẩu, **mọi phiên đăng nhập khác bị đăng xuất** (cookie mang dấu mật khẩu, kiểm tra mỗi request). Phiên đang đổi mật khẩu được cấp lại cookie.
- **Gửi email**: đặt `Smtp__Host`, `Smtp__Port` (mặc định 587), `Smtp__User`, `Smtp__Password`, `Smtp__From`, `Smtp__EnableSsl` (mặc định true). Nên đặt `App__BaseUrl` (ví dụ `https://support.congty.vn`) để liên kết không phụ thuộc Host header.
- Chưa cấu hình SMTP: ở **Development** email được lưu thành tệp `.txt` trong `App_Data/mail-outbox` (đổi bằng `Mail__OutboxDirectory`; thư mục bị Git bỏ qua, chứa liên kết hợp lệ nên không chia sẻ); ở môi trường khác trang báo chưa cấu hình email và hướng dẫn liên hệ quản trị viên.
- Bài hướng dẫn mẫu "Đăng nhập và bảo vệ tài khoản" chỉ được cập nhật cho DB mới; với DB đang có, Admin sửa bài này trong Kho hướng dẫn để trợ lý không trả lời rằng chưa có chức năng đặt lại.
## Gemini

Admin mở /Settings, nhập key từ [Google AI Studio](https://aistudio.google.com/apikey), chọn model được cấp quyền, lưu và **Kiểm tra kết nối thực tế**. Default code `gemini-3.8-flash`; model đã lưu local có thể khác. Có key không đồng nghĩa API hoạt động.

Key mã hóa bằng Data Protection ngoài wwwroot, gửi bằng header; không trả key xuống trình duyệt. Hỗ trợ `Gemini__ApiKey` hoặc `GEMINI_API_KEY`/`GOOGLE_API_KEY`, `Gemini__Model`; saved settings ưu tiên. Key trống giữ key; ngắt kết nối chặn cả key môi trường. Không commit secret/ciphertext local/key ring.

Gemini nhận câu hỏi, tối đa **6 lượt hỏi/đáp cũ**, tối đa 3 bài liên quan và 8 tóm tắt ticket của chính người hỏi cho câu hỗ trợ. “xin chào” và kiến thức thường vẫn gọi Gemini dù không có tài liệu. DB lưu nhiều lượt; trang hiển thị 50; Hội thoại mới xóa lịch sử riêng. Customer chủ động chuyển 3 lượt gần nhất thành ticket.

Thiếu key/lỗi API/quota/timeout dùng **Tra cứu kho hướng dẫn** với lý do. Không có bài thì báo chưa có hướng dẫn thay thế. Gemini hạn chế nội dung vẫn nhận diện mode ai; MAX_TOKENS giữ text kèm notice. Timeout server 45s/UI 55s; Ask/Handoff chung 12 request/phút/người dùng. **AI có thể sai; không gửi mật khẩu/OTP/CVV.** Xem [thiết kế AI](docs/07-Thiet-ke-AI.md) và [prompt](docs/08-Prompt-AI.md).

### Công cụ AI trong ticket (Staff/Admin)

Bảng "Trợ lý AI cho nhân viên" trong trang chi tiết ticket gọi `POST /Tickets/Ai/{id}` (`task` = `classify` | `draft` | `summary`, chung giới hạn 12 request/phút):

- **Phân loại**: gợi ý danh mục (một trong 5 danh mục có sẵn), mức ưu tiên và lý do. Chỉ đổi danh mục khi nhân viên bấm *Áp dụng* và xác nhận; thay đổi ghi vào nhật ký `ticket.category`. Mức ưu tiên chỉ để tham khảo vì DB chưa có cột ưu tiên.
- **Soạn nháp phản hồi**: dựa trên lịch sử và tối đa 3 bài hướng dẫn liên quan; cấm hứa hoàn tiền/bồi thường/thời hạn không có trong hướng dẫn. Nhân viên sửa, sao chép, tạo lại hoặc chèn vào ô phản hồi; sau khi chèn phải tick xác nhận đã xem lại mới gửi được.
- **Tóm tắt lịch sử**: vấn đề, các bước đã thử, phản hồi quan trọng, hiện trạng, việc cần làm, thông tin còn thiếu.

Dữ liệu gửi Gemini: tiêu đề, danh mục, trạng thái, tin nhắn đầu và 19 tin mới nhất (tối đa 600 ký tự/tin), vai trò người gửi; email, số điện thoại và dãy số ≥12 chữ số bị thay bằng `[đã ẩn]`; không gửi tên/email khách hàng. Không lưu kết quả AI vào ticket. Thiếu key hoặc lỗi API trả thông báo lỗi, **không** tạo kết quả thay thế. Mỗi lần dùng ghi nhật ký `ai.ticket`.

## Dữ liệu mẫu (demo)

```powershell
# Dừng web (Ctrl+C) trước. Script tự sao lưu DB vào backups\ rồi thêm dữ liệu.
.\scripts\seed-demo.ps1
# Gỡ toàn bộ dữ liệu mẫu:
.\scripts\seed-demo.ps1 -Remove
```

Tạo 3 nhân viên, 14 khách hàng, 48 yêu cầu (~150 tin nhắn) trải trong 75 ngày gần nhất với đủ trạng thái, 5 danh mục, một số ticket đang chờ nhân viên phản hồi và vài lượt hỏi trợ lý. Nội dung là hư cấu; mọi tài khoản mẫu dùng email `@demo.customersupport.vn` và số điện thoại dạng `09990000xx`. Mật khẩu chung được sinh ngẫu nhiên và ghi vào `App_Data/demo-accounts.txt` (bị Git bỏ qua). Chạy lại khi đã có dữ liệu mẫu sẽ không tạo trùng. `-Remove` chỉ xóa tài khoản `@demo.customersupport.vn` cùng ticket/tin nhắn/lịch sử trợ lý của chúng; tài khoản mẫu nào đã phản hồi trong ticket thật sẽ được giữ lại và khóa.

## Verification

```powershell
dotnet build .\CustomerSupportAI.csproj
dotnet run --project .\verification\ServiceChecks\ServiceChecks.csproj
dotnet build .\verification\BrowserChecks\BrowserChecks.csproj
.\verification\HttpChecks.ps1 -CaptureBrowser
dotnet run --project .\verification\DatabaseChecks\DatabaseChecks.csproj
```

Chỉ HTTP: `.\verification\HttpChecks.ps1`. HTTP dùng DB/settings riêng; Browser dùng profile riêng, chụp ảnh trong verification-artifacts bỏ Git. Service mô phỏng Google, không chứng minh key thật. DatabaseChecks kiểm backup/restore trên DB tạm. Đây là console/PowerShell tests; `dotnet test` không thay lệnh chạy chúng.

Khi app chạy 5180 và Gemini có key:

```powershell
dotnet run --project .\verification\LiveGemini\LiveGemini.csproj -- --verify-web
```

Live test gọi API thật cho lời chào/nhớ tên, tạo rồi dọn user/chat thử trong DB hiện tại, dùng quota. Không cần --use-model. [Test Cases](docs/11-Test-Cases.md) phân biệt Expected/Coverage/Actual; [kết quả chạy](docs/verification-results.md) ghi lần thực sự chạy.

## Backup / restore

```powershell
.\scripts\backup-db.ps1
.\scripts\backup-db.ps1 -BackupPath 'D:\SafeBackups\demo-01.db'
# Dừng web bằng Ctrl+C trước restore:
.\scripts\restore-db.ps1 -BackupPath 'D:\SafeBackups\demo-01.db' -WhatIf
.\scripts\restore-db.ps1 -BackupPath 'D:\SafeBackups\demo-01.db'
```

Thêm -DatabasePath cho DB tùy chỉnh. Backup dùng API SQLite nhất quán với WAL và integrity_check, không ghi đè backup. Restore cảnh báo/yêu cầu gõ RESTORE, từ chối DB mở/còn recovery files, giữ bản trước restore. Không xóa dữ liệu local/sidecar để ép phục hồi. Tiện ích verification/DatabaseChecks cần SDK/source repo. [12-Deployment](docs/12-Deployment.md) hướng dẫn dữ liệu persistent, key ring, publish và giới hạn.

## Cấu trúc

```text
Controllers/          MVC endpoints và phân quyền
Models/, ViewModels/  Entity, input/validation và UI models
Data/                 DbContext, khởi tạo và seed
Services/             Gemini, cấu hình mã hóa, tìm hướng dẫn
Views/, wwwroot/      Razor, CSS, JavaScript
verification/         Service, HTTP, Browser, LiveGemini, DatabaseChecks
scripts/              Backup/restore PowerShell
docs/                 12 tài liệu, ai-evidence và báo cáo verification
App_Data/, *.db       Dữ liệu local, bỏ Git
```

bin/, obj/, App_Data/, DB/backup, key, log, verification-artifacts/browser profile bỏ Git, vẫn giữ trên máy. Startup EnsureCreated và SQL bổ sung 2 bảng; chưa dùng EF migrations.

## Tài liệu môn học

| Tài liệu | Nội dung |
|---|---|
| [01](docs/01-Phan-tich-bai-toan.md) | Phân tích/phạm vi |
| [02](docs/02-Yeu-cau-chuc-nang.md), [03](docs/03-Yeu-cau-phi-chuc-nang.md) | Yêu cầu chức năng/phi chức năng |
| [04](docs/04-Use-Case.md) | Actor/use case/Mermaid |
| [05](docs/05-Thiet-ke-CSDL.md) | 5 bảng, PK/FK/ràng buộc/ERD |
| [06](docs/06-Kien-truc-he-thong.md) | Kiến trúc MVC/luồng dữ liệu |
| [07](docs/07-Thiet-ke-AI.md), [08](docs/08-Prompt-AI.md), [09](docs/09-Nhat-ky-su-dung-AI.md) | AI/prompt/nhật ký trung thực |
| [10](docs/10-Ke-hoach-trien-khai.md) | Kế hoạch/demo |
| [11](docs/11-Test-Cases.md) | Test đúng/sai/biên và kết quả |
| [12](docs/12-Deployment.md) | Deployment/backup/restore |
| [ai-evidence](docs/ai-evidence/README.md) | 3 vòng prompt chưa chạy, có phiếu điền output/ảnh |

Bạn cần bổ sung ảnh demo, chạy 3 vòng prompt, các test thủ công còn thiếu và đối chiếu rubric giảng viên. Project chưa deploy public, không tuyên bố điểm khi chưa có đề chấm đầy đủ.

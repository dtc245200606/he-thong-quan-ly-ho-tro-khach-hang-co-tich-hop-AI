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

| Role     | Quyền thực tế                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Guest    | Landing, đăng ký/đăng nhập, kho hướng dẫn công khai                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| Customer | Tạo/tìm/xem/phản hồi ticket riêng; tổng quan/hoạt động riêng; trợ lý và chuyển chat thành ticket                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| Staff    | Đăng nhập tại màn hình chung `/Account/Login` (email hoặc số điện thoại), hệ thống tự nhận diện vai trò; xử lý mọi ticket, cập nhật trạng thái, danh mục, **mức ưu tiên** và **người phụ trách**; lọc hộp thư theo trạng thái, "chờ nhân viên phản hồi", danh mục, khách hàng, người phụ trách ("Giao cho tôi", "Chưa phân công"), ngày tạo; dùng **công cụ AI trong ticket** và **trợ lý CRM** (/Assistant); trả lời khách trong **Hội thoại chatbot**; **xem, thêm, sửa, khóa/mở khóa, xóa khách hàng** tại `/Customers`; không quản lý Staff/Admin |
| Admin    | Đăng nhập tại màn hình chung `/Account/Login`; xử lý ticket như Staff; **thêm/xem/sửa/khóa/xóa mọi tài khoản** (Admin, Staff, Customer) và đổi vai trò tại `/Admin/Users`; xem **nhật ký hoạt động** `/Admin/AuditLog`; tạo/sửa hướng dẫn; cấu hình/test Gemini                                                                                                                                                                                                                                                                                       |

Mọi tài khoản đăng nhập có profile/đổi mật khẩu và lịch sử trợ lý riêng. Mọi vai trò đăng nhập tại một màn hình chung `/Account/Login` bằng email hoặc số điện thoại (khai báo khi đăng ký); máy chủ đọc vai trò trong DB rồi chuyển Khách hàng tới Tổng quan, Staff/Admin tới danh sách ticket. `/Account/StaffLogin` cũ chuyển hướng về trang này.

### Quản lý tài khoản

- **Đặt lại mật khẩu**: Admin (mọi tài khoản) và Staff (khách hàng) nhập mật khẩu mới trong trang Sửa; người dùng tự đặt lại qua **Quên mật khẩu?** (xem mục bên dưới).
- **Khóa/mở khóa**: tài khoản bị khóa không đăng nhập được và phiên đang mở bị đăng xuất ngay (cookie được kiểm tra với DB ở mỗi request, nên đổi vai trò/xóa user cũng có hiệu lực ngay).
- **Xóa**: xóa vĩnh viễn tài khoản cùng ticket và lịch sử trợ lý của chính nó. Tài khoản đã phản hồi trong ticket của người khác (thường là Staff) không xóa được, hãy khóa thay vì xóa.
- **Bảo vệ**: không tự khóa/xóa/đổi vai trò chính mình; luôn còn ít nhất một Admin đang hoạt động.
- **Nhật ký**: mọi thao tác tạo/sửa/khóa/mở khóa/xóa tài khoản được ghi vào bảng `AuditLogs` (không có khóa ngoại nên không mất khi xóa user).
- **Chống dò mật khẩu**: 5 lần sai liên tiếp cho cùng IP + email sẽ bị chặn 15 phút (bộ nhớ tiến trình, mất khi khởi động lại; sau reverse proxy cần cấu hình ForwardedHeaders để lấy đúng IP).
- **Nâng cấp DB cũ**: khi khởi động, app tự thêm cột `Users.IsActive` và bảng `AuditLogs` (additive, không mất dữ liệu).

**Phân công ticket**: nhân viên chọn người phụ trách (nhân viên/quản trị đang hoạt động, kèm số yêu cầu đang mở) trong trang ticket; ghi nhật ký `ticket.assign`. Xóa tài khoản nhân viên → ticket về "Chưa phân công" (FK SET NULL), đổi nhân viên thành khách hàng → bỏ phân công các ticket của họ. Chưa có nhóm hỗ trợ, tệp đính kèm, ghi chú nội bộ trong ticket (ghi chú nội bộ chỉ có ở hội thoại chatbot) hay thanh toán. Sidebar nhóm theo Tổng quan · Hỗ trợ khách hàng · Trí tuệ nhân tạo, Admin có thêm Quản lý vận hành (danh sách nhân viên) và Hệ thống; dashboard dùng số liệu thật, Staff thấy thêm số ticket chờ phản hồi và thời gian phản hồi đầu tiên trung bình (từ lúc tạo ticket đến tin nhắn đầu tiên của nhân viên, với ticket tạo trong kỳ).

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

- **Phân loại**: gợi ý danh mục (một trong 5 danh mục có sẵn), mức ưu tiên, lý do, **cảm xúc** (Tích cực/Trung lập/Lo lắng/Không hài lòng/Bức xúc) và **ý định** của khách; kèm **gợi ý người phụ trách** tính từ dữ liệu thật (nhân viên đang có ít yêu cầu mở nhất, không do AI đoán). Danh mục, mức ưu tiên và người phụ trách chỉ thay đổi khi nhân viên bấm _Áp dụng_ và xác nhận trong hộp thoại; ghi nhật ký `ticket.category`, `ticket.priority`, `ticket.assign`.
- **Soạn nháp phản hồi**: dựa trên lịch sử và tối đa 3 bài hướng dẫn liên quan; cấm hứa hoàn tiền/bồi thường/thời hạn không có trong hướng dẫn. Nhân viên sửa, sao chép, tạo lại hoặc chèn vào ô phản hồi; sau khi chèn phải tick xác nhận đã xem lại mới gửi được.
- **Tóm tắt lịch sử**: vấn đề, các bước đã thử, phản hồi quan trọng, hiện trạng, việc cần làm, thông tin còn thiếu.

### Trợ lý CRM cho nhân viên (/Assistant)

Với Staff/Admin, trang Trợ lý trở thành trợ lý CRM **chỉ đọc**, chạy bằng Groq hoặc Gemini (cấu hình chatbot). AI chỉ gọi được 6 công cụ truy vấn dữ liệu thật: `crm_overview` (thống kê ticket/khách hàng N ngày, quá hạn SLA, chưa phân công, thời gian phản hồi đầu), `list_tickets`, `ticket_details`, `search_customers`, `customer_profile`, `staff_workload`. Không có công cụ thêm/sửa/xóa; AI chỉ đề xuất, nhân viên tự thực hiện trong màn hình ticket. Quyền kiểm tra ở controller và trong service (khách hàng bị từ chối trước khi gọi AI). Kết quả công cụ được ẩn email/số điện thoại trước khi gửi AI; khung bên phải liệt kê các bản ghi CRM đã tra cứu (có liên kết) để phân biệt dữ liệu với phần suy luận. Mỗi lần dùng ghi `AuditLogs` (`ai.crm`, chỉ tên công cụ) và `AiLogs` (task `crm`). Không có key thì báo cần cấu hình, **không** dùng mock để tránh số liệu giả. Prompt: `Prompts/crm_assistant_system.txt`.

Dữ liệu gửi Gemini: tiêu đề, danh mục, trạng thái, tin nhắn đầu và 19 tin mới nhất (tối đa 600 ký tự/tin), vai trò người gửi; email, số điện thoại và dãy số ≥12 chữ số bị thay bằng `[đã ẩn]`; không gửi tên/email khách hàng. Không lưu kết quả AI vào ticket. Thiếu key hoặc lỗi API trả thông báo lỗi, **không** tạo kết quả thay thế. Mỗi lần dùng ghi nhật ký `ai.ticket`.

## Chatbot hỗ trợ trên website

Khách vãng lai và khách hàng đã đăng nhập có nút **Hỗ trợ trực tuyến** ở góc phải mỗi trang (`Views/Shared/_ChatWidget.cshtml`, `wwwroot/js/chat-widget.js`). Hỏi FAQ không cần đăng nhập. Luồng xử lý: widget → `ChatController` (`/api/chat/sessions`) → `ChatbotService` → nhà cung cấp AI (`Services/ChatProviders.cs`) → công cụ backend → SQLite.

**Nhà cung cấp (chạy bằng API key)**: Admin chọn tại /Settings → _Chatbot hỗ trợ trên website_: `auto` (Groq nếu có key, rồi Gemini, cuối cùng Mock), `groq` (API key Groq tại console.groq.com/keys, gọi Chat Completions chuẩn OpenAI có function calling, model mặc định `openai/gpt-oss-120b`; có thể đổi sang model Groq khác hỗ trợ gọi công cụ như `llama-3.3-70b-versatile`), `gemini` (dùng chung key Gemini ở trên), `mock` (không gọi AI, dùng cho phát triển/kiểm thử). Key Groq được mã hóa như key Gemini; cũng nhận `Groq__ApiKey`/`GROQ_API_KEY`. Trang Trợ lý AI (/Assistant) và công cụ AI trong ticket vẫn dùng Gemini. Khi AI thật lỗi và bật dự phòng, chatbot dùng Mock và gắn nhãn "Chế độ dự phòng · không phải AI"; nhật ký ghi rõ việc dự phòng.

**Công cụ AI được phép** (AI không truy cập DB trực tiếp, tối đa 4 vòng công cụ/tin nhắn):

| Công cụ                                                 | Quy tắc backend                                                                                                                                                     |
| ------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `search_faq(query)`                                     | Tìm trong Kho hướng dẫn bằng `KnowledgeSearch` hiện có                                                                                                              |
| `get_ticket_status(ticket_id)`                          | Cần email phiên (hoặc tài khoản đăng nhập); chỉ trả ticket thuộc email đó; mã không tồn tại và mã của người khác nhận cùng một thông báo                            |
| `create_ticket(title, description, category, priority)` | Kiểm tra độ dài, danh mục (5 danh mục hiện có) và ưu tiên (Thấp/Trung bình/Cao/Khẩn cấp); kênh `chat`; một phiên chỉ một ticket đang mở (gọi lặp lại trả ticket cũ) |
| `handoff_to_human(reason, urgent)`                      | Tạo/dùng lại ticket, ghi lý do và hội thoại gần đây, `urgent` → ưu tiên Khẩn cấp; phiên chuyển sang chế độ nhân viên, bot ngừng trả lời                             |

**Xác nhận email (khách vãng lai)**: email nhập ở widget hoặc trong tin nhắn được lưu vào phiên ở trạng thái _chưa xác nhận_, hệ thống gửi liên kết `/Chat/Verify?token=…` (ký bằng Data Protection, hiệu lực 24 giờ, chỉ đúng với email hiện tại của phiên). Trước khi khách bấm liên kết, `get_ticket_status`, `create_ticket` và `handoff_to_human` đều trả `email_unverified`, không đọc hay tạo dữ liệu nào. Tối đa 3 email xác nhận/phiên và 5 email/15 phút/IP. Dùng chung cấu hình gửi mail với _Quên mật khẩu_ (Development không có SMTP thì ghi vào `App_Data/mail-outbox`); môi trường khác chưa cấu hình SMTP thì khách cần đăng nhập. Khách hàng đã đăng nhập được xác minh bằng tài khoản. Sau khi xác nhận, email chưa có tài khoản sẽ được tạo tài khoản khách hàng (mật khẩu ngẫu nhiên, nhận lại bằng _Quên mật khẩu_, ghi nhật ký `chat.customer-create`). Email của nhân viên/quản trị hoặc tài khoản bị khóa bị từ chối.

**SLA phản hồi đầu**: hạn = thời điểm tạo + số giờ theo mức ưu tiên (mục `Sla:FirstResponseHours` trong appsettings.json, mặc định Khẩn cấp 4h, Cao 8h, Trung bình 24h, Thấp 72h; tính theo giờ lịch, chưa trừ ngoài giờ làm việc). Ticket tạo từ form web mặc định _Trung bình_; ticket từ chatbot theo mức AI chọn, chuyển nhân viên khẩn cấp → _Khẩn cấp_. Nhân viên đổi mức ưu tiên trong trang ticket (ghi nhật ký `ticket.priority`), hạn được tính lại. Trang ticket và console chat hiện _Hạn phản hồi đầu / Quá hạn / Đạt / Trễ SLA_; danh sách ticket đánh dấu _Quá hạn SLA_. Ticket cũ (trước khi có cột) không có ưu tiên nên không có SLA cho tới khi nhân viên đặt.

**Bảo vệ dữ liệu**: trước khi gửi AI, email/số điện thoại/số thẻ ≥12 chữ số/CMND-CCCD/mật khẩu/OTP được thay bằng `[ĐÃ_ẨN_…]` (cả tin nhắn, lịch sử và kết quả công cụ). Phản hồi AI được kiểm tra: xin mật khẩu/OTP/CVV → thay bằng thông báo an toàn; hứa hoàn tiền/bồi thường/giảm giá → thêm cảnh báo cần nhân viên xem xét; tự nhận "đã tạo yêu cầu" khi backend chưa lưu → bị thay. System prompt ở `Prompts/chatbot_system.txt`. Bảng `AiLogs` lưu nhà cung cấp, dự phòng, thời gian, số mục đã ẩn, danh sách công cụ — không lưu nội dung.

**Giới hạn** (mục `Chatbot` trong appsettings.json): 10 tin/phút/phiên, 20 phiên/phút/IP, 1.000 ký tự/tin, 4 vòng công cụ. Phiên được xác thực bằng token ngẫu nhiên (DB chỉ lưu SHA-256) và gắn với tài khoản đang đăng nhập.

**Nhân viên**: menu _Hội thoại chatbot_ (`/ChatAdmin`, Staff/Admin) lọc theo trạng thái, xem hội thoại tự cập nhật 4 giây/lần, mở ticket liên quan, gửi **phản hồi công khai** (hiện trong khung chat của khách và lưu vào ticket) hoặc **ghi chú nội bộ** (không bao giờ trả về API khách). Phản hồi viết trong màn hình ticket cũng tự hiện trong khung chat. Admin xem thêm nhật ký công cụ AI. API: `GET /api/chat/admin/sessions?status=`, `GET /api/chat/admin/sessions/{id}`, `POST …/{id}/replies`, `POST …/{id}/status`; chưa đăng nhập → 401, khách hàng → 403.

**CSDL**: thêm bảng `ChatSessions`, `ChatMessages`, `AiLogs` và cột `Tickets.Priority` (null cho ticket cũ), `Tickets.Channel` (mặc định `web`), `Tickets.SlaDueAt` theo kiểu bổ sung khi khởi động; dữ liệu cũ giữ nguyên. Ticket hiện ưu tiên (cho nhân viên) và nhãn _Tiếp nhận qua chatbot_.

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
dotnet run --project .\verification\ChatbotChecks\ChatbotChecks.csproj
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
Controllers/          MVC + API endpoints, phân quyền [Authorize] theo vai trò
  TicketsController     ticket: danh sách/lọc, tạo, trả lời, trạng thái, danh mục, ưu tiên, phân công, công cụ AI
  CustomersController   CRUD khách hàng (Staff/Admin); AdminController: mọi tài khoản, nhật ký
  ChatController        API chatbot công khai (/api/chat/sessions), trang xác nhận email /Chat/Verify
  ChatAdminController   console hội thoại chatbot + API quản trị (/api/chat/admin)
  AssistantController   trợ lý khách hàng (Gemini) và trợ lý CRM (nhân viên)
Models/, ViewModels/  Entity, validation, UI models, DisplayText (nhãn tiếng Việt)
Data/                 AppDbContext, nâng cấp schema bổ sung (SupportSchema), ràng buộc (IntegrityRules), dữ liệu mẫu
Services/             Nghiệp vụ và tích hợp AI:
  ChatbotService        chatbot: phiên, 4 công cụ, chuyển nhân viên, xác nhận email, đồng bộ ticket
  CrmAssistantService   trợ lý CRM chỉ đọc (6 công cụ truy vấn)
  ChatToolLoop          vòng gọi công cụ dùng chung (tối đa 4 vòng)
  ChatProviders         IChatProvider: Groq, Gemini, Mock — đổi nhà cung cấp không đụng nghiệp vụ
  ChatSafety            ẩn dữ liệu cá nhân + kiểm tra câu trả lời AI
  TicketAiService, AiAssistantService  công cụ AI trong ticket, trợ lý khách (Gemini)
  SlaPolicy, UserAdministrationService, *SettingsStore, EmailSender, LoginThrottle
Prompts/              System prompt của chatbot và trợ lý CRM
Views/, wwwroot/      Razor, CSS (tokens.css + site.css), JavaScript (chat-widget, chat-console, ticket, assistant)
verification/         ServiceChecks, ChatbotChecks, HttpChecks + BrowserChecks, DatabaseChecks, LiveGemini
scripts/              Backup/restore/seed PowerShell
Dockerfile, docker-compose.yml  Chạy bằng container, dữ liệu trong volume /data
docs/                 12 tài liệu, ai-evidence và báo cáo verification
App_Data/, *.db       Dữ liệu local, bỏ Git và không đưa vào bản publish
```

bin/, obj/, App_Data/, DB/backup, key, log, verification-artifacts/browser profile bỏ Git, vẫn giữ trên máy. Startup dùng EnsureCreated cho DB mới và SQL bổ sung (bảng, cột, chỉ mục, trigger) cho DB cũ; không dùng EF migrations. Chi tiết: [docs/05](docs/05-Thiet-ke-CSDL.md).

## Chạy bằng Docker

```powershell
Copy-Item .env.example .env   # điền BootstrapAdmin__Email/Password; tùy chọn Groq__ApiKey, Gemini__ApiKey, SMTP
docker compose up -d --build  # http://localhost:8080 (Production, dữ liệu trong volume /data)
```

Xem [docs/12-Deployment](docs/12-Deployment.md) (sao lưu volume, HTTPS qua reverse proxy). Image chưa được build thử trên máy phát triển (Docker Desktop không chạy); bản publish Release đã được chạy thử ở Production với cùng cấu hình.

## Tài liệu môn học

| Tài liệu                                                                                      | Nội dung                                          |
| --------------------------------------------------------------------------------------------- | ------------------------------------------------- |
| [01](docs/01-Phan-tich-bai-toan.md)                                                           | Phân tích/phạm vi                                 |
| [02](docs/02-Yeu-cau-chuc-nang.md), [03](docs/03-Yeu-cau-phi-chuc-nang.md)                    | Yêu cầu chức năng/phi chức năng                   |
| [04](docs/04-Use-Case.md)                                                                     | Actor/use case/Mermaid                            |
| [05](docs/05-Thiet-ke-CSDL.md)                                                                | 5 bảng, PK/FK/ràng buộc/ERD                       |
| [06](docs/06-Kien-truc-he-thong.md)                                                           | Kiến trúc MVC/luồng dữ liệu                       |
| [07](docs/07-Thiet-ke-AI.md), [08](docs/08-Prompt-AI.md), [09](docs/09-Nhat-ky-su-dung-AI.md) | AI/prompt/nhật ký trung thực                      |
| [10](docs/10-Ke-hoach-trien-khai.md)                                                          | Kế hoạch/demo                                     |
| [11](docs/11-Test-Cases.md)                                                                   | Test đúng/sai/biên và kết quả                     |
| [12](docs/12-Deployment.md)                                                                   | Deployment/backup/restore                         |
| [ai-evidence](docs/ai-evidence/README.md)                                                     | 3 vòng prompt chưa chạy, có phiếu điền output/ảnh |

Bạn cần bổ sung ảnh demo, chạy 3 vòng prompt, các test thủ công còn thiếu và đối chiếu rubric giảng viên. Project chưa deploy public, không tuyên bố điểm khi chưa có đề chấm đầy đủ.

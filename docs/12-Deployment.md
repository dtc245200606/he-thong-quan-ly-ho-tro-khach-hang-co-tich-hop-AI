# 12. Deployment, backup và phục hồi

## Môi trường demo

.NET SDK 10 (project net10.0, EF/SQLite 10.0.12), Windows PowerShell 5.1 hoặc PowerShell mới hơn. Restore packages cần NuGet/mạng lần đầu; Google API cần Internet khi dùng AI. BrowserChecks cần Microsoft Edge ở đường dẫn trong test và port 9224 rảnh. App mặc định dùng file SQLite dưới content root; identity chạy phải có quyền ghi DB, sidecar và App_Data.

```powershell
Set-Location 'C:\duong-dan\CustomerSupport'
dotnet restore .\CustomerSupportAI.csproj
dotnet build .\CustomerSupportAI.csproj
.\Start-Web.ps1
```

Mở http://localhost:5180. Start-Web bật Development/LocalSetup, nếu chưa có Admin sinh mật khẩu random và lưu App_Data/local-admin.txt. Đọc file local, đăng nhập và đổi mật khẩu; không chiếu file lên báo cáo/Git. Admin đã có không bị reset. Không dùng script Development để public Internet.

## Cấu hình

.env.example là danh sách biến tham khảo, ASP.NET **không tự đọc .env**. Đặt biến trong PowerShell hoặc nền tảng hosting; ví dụ không chứa key thật:

```powershell
$env:Gemini__Model = 'model-duoc-tai-khoan-cap-quyen'
$env:ConnectionStrings__DefaultConnection = 'Data Source=C:\CustomerSupportData\customer-support.db'
$env:AiSettings__StorageDirectory = 'C:\CustomerSupportData\settings'
```

Nhập key qua trang Admin /Settings hoặc secret environment của máy chạy; không dán vào source/README. Saved settings ưu tiên môi trường; RemoveKey chặn key môi trường. Tránh đặt nhiều alias key hoặc biến rỗng che alias sau. Backup DB không tự bao gồm cấu hình AI hay key ring.

**Data Protection key ring** (dùng để mã hóa API key, cookie đăng nhập, link đặt lại mật khẩu và link xác nhận email): ứng dụng dùng tên cố định `CustomerSupportAI`, nên đổi thư mục dự án không làm mất key. Trên server hoặc container hãy đặt `DataProtection__KeysDirectory` tới thư mục bền vững (ví dụ `C:\CustomerSupportData\keys` hoặc `/data/keys`) và sao lưu cùng DB; mất key ring thì phải nhập lại API key. Key không giải mã được không làm sập ứng dụng: /Settings hiện cảnh báo để nhập lại.

**Nhà cung cấp AI:**

| Chức năng | Nhà cung cấp | Cấu hình |
|---|---|---|
| Trợ lý khách hàng (/Assistant), công cụ AI trong ticket | Gemini | /Settings → Kết nối Gemini, hoặc `Gemini__ApiKey`, `Gemini__Model` |
| Chatbot website, trợ lý CRM của nhân viên | Groq (ưu tiên) hoặc Gemini | /Settings → Chatbot, hoặc `Groq__ApiKey`/`GROQ_API_KEY`, `Groq__Model`, `Chatbot__Provider` (auto, groq, gemini, mock) |

Sau khi lưu, bấm **Kiểm tra kết nối thực tế** (Gemini) và **Kiểm tra kết nối chatbot**. Các nút này gửi một yêu cầu thật và báo lỗi cụ thể (key sai, hết hạn mức, model sai, timeout). Chế độ `mock` không gọi AI, chỉ dùng cho phát triển/kiểm thử; trợ lý CRM không dùng mock để tránh số liệu giả.

## Backup SQLite an toàn

```powershell
.\scripts\backup-db.ps1
.\scripts\backup-db.ps1 -DatabasePath 'C:\CustomerSupportData\customer-support.db' -BackupPath 'D:\SafeBackups\demo-01.db'
.\scripts\backup-db.ps1 -WhatIf
```

Mặc định tạo backups/customer-support-timestamp.db, không ghi đè file có sẵn. Script gọi tiện ích .NET `verification/DatabaseChecks`, dùng Microsoft.Data.Sqlite.BackupDatabase và integrity_check; không copy riêng file DB đang có WAL. Có thể backup khi app chạy, phản ánh snapshot committed nhất quán tại lúc backup; không có lịch chạy tự động. Thư mục backup bỏ Git. Dữ liệu chứa tài khoản/chat: lưu ở nơi có quyền truy cập phù hợp; copy ra thiết bị khác để tránh hỏng cùng ổ đĩa.

## Restore có cảnh báo và xác nhận

1. Dừng CustomerSupport bằng Ctrl+C tại terminal của nó. Không kill các process .NET khác. Bảo đảm không có chương trình khác giữ DB.
2. Giữ một backup hiện tại, chọn đúng đường dẫn (script không tự đọc custom connection string).
3. Xem trước rồi chạy:

```powershell
.\scripts\restore-db.ps1 -BackupPath '.\backups\demo-01.db' -WhatIf
.\scripts\restore-db.ps1 -BackupPath '.\backups\demo-01.db'
# DB tùy chỉnh: thêm -DatabasePath 'C:\CustomerSupportData\customer-support.db'
```

4. Script cảnh báo dữ liệu bị thay rồi yêu cầu gõ chính xác **RESTORE**; nhập khác hủy. Không có -Force để bỏ qua bước này.
5. Tiện ích kiểm tra backup trước; từ chối khi target có -wal/-shm/-journal hoặc file đang được mở trên Windows. Không xóa sidecar để ép restore; dừng app đúng cách/để SQLite recovery và checkpoint hoàn tất. Nếu vẫn còn sidecar, cần kiểm tra nguyên nhân với công cụ SQLite trước khi thử lại.
6. Target hiện có được giữ trong `*.before-restore-*.db`; thay file dưới khóa FileShare.None, lỗi copy sẽ cố khôi phục từ bản này. Mất điện/hết đĩa vẫn cần phục hồi thủ công bằng safety copy; không cam kết thao tác file nguyên tử. Target mới dùng CreateNew; file staging do công cụ tạo được dọn sau thao tác.
7. Khởi động app, kiểm tra login/ticket/hướng dẫn/chat. Không chạy restore thử trên dữ liệu thật chỉ để lấy minh chứng.

Các script yêu cầu source repo, SDK và project DatabaseChecks; nếu chỉ copy output publish thì cần mang theo script/tiện ích hoặc build tiện ích riêng trước. Kiểm thử DatabaseChecks chỉ thao tác DB tạm, không restore DB local của người dùng.

## Docker / Docker Compose

`Dockerfile` (build bằng `mcr.microsoft.com/dotnet/sdk:10.0`, chạy bằng `aspnet:10.0` dưới user không phải root) và `docker-compose.yml` lưu mọi trạng thái vào volume `/data`: DB, cấu hình AI đã mã hóa, key ring, outbox mail. `.dockerignore` loại `App_Data`, DB, backup, `.env`, `verification*` khỏi image.

```powershell
Copy-Item .env.example .env   # điền BootstrapAdmin__Email/Password, tùy chọn Groq__ApiKey, Gemini__ApiKey, SMTP
docker compose up -d --build  # http://localhost:8080
docker compose logs -f web
# Sao lưu volume dữ liệu (DB + settings + keys) ra thư mục hiện tại:
docker run --rm -v customer-support_customer-support-data:/data -v ${PWD}:/backup alpine tar czf /backup/cs-data.tgz -C /data .
```

Container chạy `ASPNETCORE_ENVIRONMENT=Production` trên HTTP 8080; đặt reverse proxy có HTTPS phía trước khi public. **Trạng thái kiểm thử:** chưa build được image trên máy phát triển vì Docker Desktop không chạy. Đã kiểm thử tương đương: `dotnet publish -c Release` rồi chạy bản publish ở Production với đúng các biến môi trường của image (DB, settings, key ring trong thư mục dữ liệu riêng), khởi động lại và xác nhận API key vẫn giải mã được (xem verification-results).

## Publish khi cần triển khai thật (chưa thực hiện public)

```powershell
dotnet publish .\CustomerSupportAI.csproj -c Release -o .\publish
```

Publish không mang verification/artifacts, App_Data, DB hay backup (DefaultItemExcludes trong csproj), nên không làm lộ cấu hình key local. Chạy DLL từ working directory publish, đặt ASPNETCORE_ENVIRONMENT=Production và URL/HTTPS phù hợp nền tảng. Nếu dùng reverse proxy cần cấu hình HTTPS/forwarded headers đúng môi trường; project chưa cung cấp cấu hình proxy chuyên biệt. Không tự coi localhost là deployment public.

Checklist người vận hành: HTTPS và certificate; DB/App_Data/key ring persistent ngoài wwwroot; AllowedHosts theo domain; bootstrap admin secret qua môi trường chỉ khi cần, LocalSetup=false; kiểm tra quyền thư mục; backup/restore thử với DB sao chép; quota và model Gemini; không log key/cookie/chat nhạy cảm; policy lưu dữ liệu; smoke ba role và theo dõi lỗi. Khôi phục mật khẩu/email xác minh, backup theo lịch và HA chưa có trong code.

# 03. Yêu cầu phi chức năng

Phân biệt cơ chế đã có với mục tiêu vận hành cần kiểm chứng.

| Nhóm | Hiện thực hiện có | Cách xác minh / giới hạn |
|---|---|---|
| Bảo mật tài khoản | PasswordHasher; cookie HttpOnly, SameSite Strict, Secure theo request, hết hạn 8 giờ có gia hạn; authorize trên server | HTTP phân quyền/đổi mật khẩu; HTTPS bắt buộc khi public. Chưa có MFA, rate limit đăng nhập hay thu hồi mọi phiên khi đổi mật khẩu |
| Cách ly dữ liệu | CustomerId lọc ticket; UserId lọc chat; Gemini chỉ nhận ticket của người hỏi | HTTP customer thứ hai nhận 404 và không thấy chat người khác |
| Chống CSRF/XSS | AutoValidateAntiforgeryToken toàn cục; fetch gửi token; Razor encode, JS textContent | HTTP token sai 400; chưa có kiểm thử thâm nhập đầy đủ |
| Bảo vệ API key | Header x-goog-api-key; Data Protection; không trả key trên form; cấu hình local bỏ Git | ServiceChecks + HTTP kiểm tra mã hóa; bảo vệ cả key ring và quyền file |
| Khả dụng | Fallback khi thiếu key/lỗi/quota/timeout; báo rõ mode | Mô phỏng lỗi nhà cung cấp; không có HA, retry tự động hay cache AI |
| Giới hạn tài nguyên | Ask/Handoff chung policy 12 request/phút/người dùng, hàng đợi 0; timeout Gemini 45s, JS 55s | HTTP nhận 429; giới hạn trong bộ nhớ một tiến trình, quota Google riêng |
| Bền vững | SQLite/EF Core, lưu ticket/chat; backup SQLite nhất quán, restore xác nhận/khóa file | DatabaseChecks trên DB tạm; chưa có backup tự động theo lịch |
| Dễ dùng | Tiếng Việt, 6 mục chính, khu vực Admin riêng, layout desktop/mobile, labels và skip-link | BrowserChecks chống tràn ngang; chưa đánh giá WCAG đầy đủ hay khảo sát người dùng |
| Dễ bảo trì | MVC + ViewModels + 3 service; docs bám code; tests dạng console/PowerShell | dotnet build và verification; không thêm tầng/phụ thuộc nghiệp vụ mới |
| Triển khai | .NET 10, thư mục dữ liệu ghi được, DB và key ring tồn tại qua khởi động | Deployment checklist; mặc định chỉ demo localhost |

Mục tiêu thực hành: chạy demo ổn định với dữ liệu nhỏ, thông báo lỗi có thể hiểu và thao tác không mất dữ liệu local. Chưa đo throughput, độ trễ p95, số người dùng đồng thời, độ chính xác AI hoặc SLA; không coi các mục tiêu này là kết quả đã đạt. Không có chính sách tự động xóa dữ liệu; người vận hành cần xác định thời gian lưu, quyền truy cập backup và cơ chế xóa theo nhu cầu triển khai thực tế.

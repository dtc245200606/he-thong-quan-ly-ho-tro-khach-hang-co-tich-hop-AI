# 01. Phân tích bài toán

CustomerSupport là cổng hỗ trợ khách hàng bằng tiếng Việt, xây dựng trên ASP.NET Core MVC (.NET 10), SQLite/EF Core và Google Gemini. Tài liệu mô tả phiên bản code hiện có, không giả định chức năng ngoài project. Đối chiếu `Controllers/`, `Data/`, `Models/`, `Services/`, `ViewModels/`, `Views/`, `wwwroot/`, cấu hình và `verification/`.

## Vấn đề và mục tiêu

Khách hàng cần một nơi gửi vấn đề, theo dõi trạng thái và đọc lại trao đổi. Nhân viên cần hàng đợi chung để phản hồi; quản trị viên cần quản lý nhân viên, nội dung hướng dẫn và kết nối AI. Hệ thống kết hợp tự tra cứu → hỏi trợ lý → chuyển nhân viên, dùng dữ liệu thật thay vì số liệu minh họa trên dashboard.

Mục tiêu môn học: nghiệp vụ dễ demo; phân quyền rõ; dữ liệu bền vững; AI có ngữ cảnh và chế độ dự phòng minh bạch; có tài liệu thiết kế, kiểm thử và hướng dẫn vận hành có thể lặp lại.

## Đối tượng và phạm vi

| Đối tượng | Nhu cầu được hỗ trợ |
|---|---|
| Khách chưa đăng nhập | Trang giới thiệu, đọc/tìm hướng dẫn, đăng ký/đăng nhập |
| Customer | Yêu cầu và tin nhắn của mình, trợ lý riêng, chuyển nhân viên, tài khoản |
| Staff | Hàng đợi mọi khách hàng, phản hồi/cập nhật trạng thái, tổng quan/hoạt động |
| Admin | Quyền xử lý như Staff; danh sách người dùng, tạo Staff, biên soạn hướng dẫn, cấu hình Gemini |

Nghiệp vụ gồm 5 nhóm dữ liệu: Users, Tickets, TicketMessages, KnowledgeArticles, AssistantTurns. Không có bảng đơn hàng, thanh toán, phân công nhân viên hoặc phiên chat riêng. Các danh mục thanh toán/bảo hành chỉ dùng để phân loại yêu cầu; ứng dụng không thực hiện giao dịch hay xác nhận chính sách thương mại.

## Luồng giải quyết

1. Customer đăng ký và được đăng nhập; đọc hướng dẫn hoặc tạo yêu cầu.
2. Yêu cầu mới có mã CS, trạng thái Open và tin nhắn đầu tiên.
3. Staff/Admin đọc hàng đợi, phản hồi và cập nhật trạng thái.
4. Customer theo dõi, phản hồi khi yêu cầu chưa Closed.
5. Trợ lý gọi Gemini nếu đã có key, với lịch sử và ngữ cảnh phù hợp; thiếu tài liệu không ngăn AI trả lời.
6. Customer chủ động chuyển tối đa 3 lượt chat gần đây thành yêu cầu hỗ trợ.

## Giới hạn được chấp nhận

Triển khai mặc định trên một máy, chưa có tên miền công khai, email xác minh/khôi phục mật khẩu, tệp đính kèm, SLA, thông báo thời gian thực, đánh giá hài lòng hay thanh toán. Danh sách ticket lấy tối đa 100 bản ghi gần nhất sau lọc, chưa có phân trang. AI có thể sai; người dùng kiểm chứng hoặc chuyển nhân viên. Không hứa hẹn hiệu năng hay điểm rubric khi chưa có kết quả đo/đề chấm cụ thể.

## Tiêu chí đánh giá và nguồn minh chứng

Phân tích/yêu cầu: tài liệu 01–03; use case: 04; CSDL: 05; kiến trúc: 06; AI/prompt: 07–09; kế hoạch: 10; kiểm thử: 11; triển khai: 12. Bộ prompt 3 vòng trong `ai-evidence/` là thiết kế thử nghiệm, chưa phải lịch sử sử dụng thực tế. Kết quả chạy tự động được ghi riêng trong 11 và `verification-results.md`.

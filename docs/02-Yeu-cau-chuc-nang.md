# 02. Yêu cầu chức năng theo code hiện có

| ID | Chức năng / tiêu chí chấp nhận | Nguồn |
|---|---|---|
| FR01 | Đăng ký Customer: tên 2–100 ký tự, email hợp lệ ≤320, mật khẩu ≥10, xác nhận khớp, email chuẩn hóa không trùng; đăng nhập sau khi tạo | AccountController, AccountViewModels |
| FR02 | Đăng nhập bằng email/mật khẩu; RememberMe; ReturnUrl chỉ nhận URL nội bộ; đăng xuất bằng POST | AccountController |
| FR03 | Xem thông tin tài khoản của mình; đổi mật khẩu sau khi xác minh mật khẩu hiện tại, mới ≥10 ký tự | AccountController |
| FR04 | Customer tạo ticket: tiêu đề bắt buộc ≤120, 5 danh mục hợp lệ, tin đầu 10–4000 ký tự; sinh mã CS duy nhất, Open | TicketsController, TicketViewModels |
| FR05 | Tìm ticket theo tiêu đề/mã; lọc 4 trạng thái; Customer chỉ thấy của mình, Staff/Admin thấy tất cả; tối đa 100 kết quả | TicketsController.Index/VisibleTickets |
| FR06 | Xem chi tiết/trao đổi; phản hồi 1–4000 ký tự; ticket Closed không nhận phản hồi; Staff/Admin trả lời ticket Open đưa sang In progress | TicketsController.Details/Reply |
| FR07 | Staff/Admin cập nhật Open, In progress, Resolved, Closed; trạng thái ngoài danh sách trả 400 | TicketsController.UpdateStatus |
| FR08 | Tổng quan đếm ticket, cần xử lý, hoàn tất, lượt chat của chính người dùng, 5 ticket gần nhất và phân bố danh mục | DashboardController.Index |
| FR09 | Xem 50 tin nhắn ticket gần nhất trong phạm vi quyền truy cập | DashboardController.Activity |
| FR10 | Kho hướng dẫn công khai: tìm từ khóa không dấu, lọc danh mục, xem chi tiết; Admin tạo/sửa bài | KnowledgeController, KnowledgeSearch |
| FR11 | Trợ lý dành cho người đăng nhập; câu hỏi 1–2000 ký tự không trắng; gọi Gemini khi có key kể cả lời chào, dùng ngữ cảnh có liên quan | AssistantController.Ask, AiAssistantService |
| FR12 | Lưu lượt hỏi/đáp riêng theo UserId; trang hiển thị 50 lượt gần nhất, API dùng tối đa 6 lượt; Hội thoại mới xóa toàn bộ lịch sử của mình | AssistantController.Index/Ask/Clear |
| FR13 | Customer chuyển tối đa 3 lượt gần nhất thành ticket Other/Open, nội dung ≤4000; chưa có chat chuyển đến form tạo ticket | AssistantController.Handoff |
| FR14 | API không khả dụng → hiển thị tra cứu hướng dẫn và thông báo; không có key vẫn tra trạng thái tối đa 8 ticket của mình | AiAssistantService, AssistantController |
| FR15 | Admin xem danh sách tài khoản và số ticket, tạo Staff với email không trùng và mật khẩu băm | AdminController |
| FR16 | Admin lưu model/key Gemini, để trống giữ key, ngắt kết nối chặn cả key môi trường; kiểm tra gọi API thật | SettingsController, AiSettingsStore |

Danh mục ticket lưu bằng tiếng Anh: Product support, Billing, Warranty & repair, Account access, Other; `DisplayText` dịch khi hiển thị. Mỗi cập nhật trạng thái chọn trực tiếp từ danh sách, chưa có sơ đồ chuyển trạng thái bắt buộc. Dashboard tính hoàn tất = Resolved + Closed; trang Tickets đếm đã giải quyết = Resolved. Đây là hai chỉ số khác nhau trong code hiện tại.

Không có giao diện sửa role, xóa tài khoản, xóa ticket/bài hướng dẫn, phân công ticket, sửa hồ sơ hay khôi phục mật khẩu qua email. Admin không tạo ticket qua Create/Handoff vì hai chức năng này chỉ cho Customer. Trợ lý không tự thay đổi dữ liệu nghiệp vụ.

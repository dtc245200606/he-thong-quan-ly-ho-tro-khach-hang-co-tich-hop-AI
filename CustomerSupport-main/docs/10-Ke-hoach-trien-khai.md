# 10. Kế hoạch triển khai và demo

## Các giai đoạn

| Giai đoạn | Công việc | Điều kiện hoàn thành / minh chứng |
|---|---|---|
| 1. Khảo sát | Đọc controller/service/model/view/test; đối chiếu phạm vi | 01–06 đúng chức năng hiện có |
| 2. AI | Tài liệu prompt/context/bảo mật/fallback, không thay provider hoặc nghiệp vụ | 07–09; ServiceChecks; kết nối thật được kiểm tra riêng |
| 3. Vận hành | .env.example, README, ignore, backup/restore | Script không ghi đè backup; restore xác nhận và từ chối DB mở; DatabaseChecks |
| 4. Verification | Build, service, HTTP, browser; ghi Actual | 11, verification-results; không đánh Pass cho bài thủ công chưa chạy |
| 5. Minh chứng môn học | Chạy 3 vòng prompt, chụp demo, đối chiếu rubric giảng viên | Người dùng bổ sung ai-evidence và ảnh đã che dữ liệu |
| 6. Nếu cần public | Publish, HTTPS, quyền thư mục/backup/key ring, smoke và theo dõi | 12 checklist; hiện chưa được coi là đã deploy public |

## Kịch bản demo ngắn

1. Mở localhost:5180, xem landing và kho hướng dẫn.
2. Đăng ký Customer thử; tạo yêu cầu và phản hồi, lọc mã CS.
3. Đăng nhập Staff do Admin tạo; xem ticket, phản hồi và đổi trạng thái.
4. Customer hỏi “xin chào” khi Gemini hoạt động; chỉ rõ nhãn AI · Gemini. Hỏi cách tạo ticket để xem context, thử ghi nhớ tên.
5. Customer chuyển nhân viên, mở ticket chứa transcript; tài khoản Customer khác không xem được ticket đó.
6. Admin biên soạn bài, xem danh sách người dùng; giới thiệu cấu hình AI mà không chiếu key/mật khẩu.
7. Dùng môi trường test riêng để minh họa fallback/429, tránh ngắt cấu hình đang dùng chỉ để chụp ảnh.

## Rủi ro và xử lý

Quota/model Gemini lỗi: có fallback, đổi model được cấp quyền trong Settings sau kiểm tra; không khẳng định AI thật khi nhãn tra cứu. Port xung đột: dừng đúng tiến trình hoặc chọn port khác; LiveGemini --verify-web dùng cố định 5180. SQLite lỗi/đầy đĩa: giữ backup, không xóa WAL/SHM tùy tiện; dừng app và kiểm tra trước restore. Di chuyển máy: key mã hóa cần key ring/identity tương thích hoặc nhập lại. Không thay schema tùy ý khi chưa backup vì project chưa dùng migrations.

Không có ngày hoàn thành/điểm rubric được tự đặt thay giảng viên. Ưu tiên ổn định chức năng hiện có; những tính năng ngoài phạm vi ghi là hướng phát triển, không là phần đã làm.

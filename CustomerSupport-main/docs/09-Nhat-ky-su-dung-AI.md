# 09. Nhật ký sử dụng AI và tính trung thực của minh chứng

## Phân biệt hai loại AI

Gemini là AI **của sản phẩm**, gọi qua AiAssistantService để trả khách hàng. Codex là trợ lý **hỗ trợ phát triển** trong phiên làm việc này: đọc code, soạn tài liệu, viết tiện ích backup/restore và chạy kiểm tra. Đây không phải hai provider thay thế nhau trong ứng dụng.

## Nhật ký phiên hoàn thiện tài liệu

Ngày thực hiện sẽ được đối chiếu với timestamp trong `verification-results.md`. Yêu cầu của chủ project: đọc chức năng hiện có, không viết lại nghiệp vụ, không bịa tính năng/test Pass, tạo 12 tài liệu và bộ đánh giá prompt 3 vòng; bổ sung backup/restore, README và ignore dữ liệu local.

| Công việc dùng AI hỗ trợ | Sản phẩm / kiểm chứng |
|---|---|
| Phân tích code ứng dụng và test | Bảng quyền/use case/ERD đối chiếu controller, entity, AppDbContext |
| Soạn tài liệu | docs 01–12, Mermaid, prompt và giới hạn đúng code |
| Thiết kế thử nghiệm prompt | ai-evidence/ có ô output/ảnh để người dùng bổ sung |
| Soạn script vận hành | backup-db.ps1, restore-db.ps1; kiểm tra SQLite trên DB tạm |
| Chạy verification | Kết quả thực tế tách riêng khỏi Expected; ghi trong 11 và verification-results |

Không có dữ liệu đủ để xác nhận lịch sử mọi lần dùng AI trước phiên này, số giờ tiết kiệm hay chất lượng prompt cũ. Không suy diễn các vòng thử nghiệm thành nhật ký thật. Người nộp bài cần tự đọc, chạy demo và chịu trách nhiệm xác nhận nội dung; có thể bổ sung log trao đổi phát triển được phép chia sẻ và đã che secret.

## Nhật ký Gemini để bổ sung

| Thời gian | Model | Câu hỏi / context đã che dữ liệu | Kết quả thực tế | Đánh giá / ảnh |
|---|---|---|---|---|
| Chưa điền | Chưa điền | Chưa điền | Chưa chạy bộ đánh giá 3 vòng | Chưa có |

Bài LiveGemini smoke kiểm tra chào hỏi/lịch sử hiện tại nếu chạy thành công chỉ chứng minh phiên bản hiện tại; không chứng minh V1/V2/V3 đã từng được sử dụng theo lịch sử. Không đăng API key, mật khẩu bootstrap, cookie đăng nhập, DB hoặc screenshot chứa secret vào tài liệu.

# 07. Thiết kế AI

## Vị trí và nhiệm vụ

Gemini là nhà cung cấp sinh câu trả lời trong `Services/AiAssistantService.cs`; `AssistantController` xử lý quyền, ngữ cảnh và persistence. `SettingsController` chỉ Admin được cấu hình/test. AI hỗ trợ giải đáp, không quyết định quyền truy cập và không tự thao tác DB.

## Input / output

| Dữ liệu | Giới hạn / nguồn |
|---|---|
| Câu hỏi hiện tại | Request Message 1–2000 ký tự, không trắng; trim trước gửi |
| Lịch sử | Tối đa **6 lượt hỏi/đáp cũ** của UserId; mỗi lượt gồm user + model. Thêm câu hiện tại = tối đa 13 entries trong contents |
| Knowledge | Tối đa 3 bài liên quan: Title + Body. Chuẩn hóa không dấu, tìm từ khóa, title trọng số 4/body 1, không dùng embeddings |
| Ticket context | Tối đa 8 ticket gần nhất của **chính người hỏi**, gồm mã/tiêu đề/danh mục/trạng thái; không gửi toàn bộ tin nhắn hoặc email khách hàng |
| Service output | AssistantReply(Reply, Mode, Success, Notice) |
| Web output | JSON reply, mode, notice, sources(title,url); Success là trạng thái nội bộ, không phải trường JSON endpoint |

Giới hạn 6 là số lượt **được đưa vào prompt**, không phải giới hạn DB. Trang hiển thị 50 lượt; Clear xóa toàn bộ lượt riêng. Handoff dùng 3 lượt, cắt transcript còn 4000 ký tự.

## Lựa chọn ngữ cảnh

Lời chào/giới thiệu/khả năng của trợ lý trả qua Gemini mà không cần bài hướng dẫn. Câu hỗ trợ được xác định bằng từ khóa trong KnowledgeSearch.IsSupportQuestion; ForAssistant lấy bài phù hợp. Câu tiếp nối có một số tiền tố và câu trước là hỗ trợ có thể lấy bài từ câu trước. Ticket context chỉ thêm khi **câu hiện tại** được nhận diện là hỗ trợ. Đây là heuristic nên có thể bỏ sót cách diễn đạt; lịch sử vẫn được gửi.

Staff/Admin cũng chỉ có ticket context mang CustomerId của chính tài khoản, không đưa toàn bộ hàng đợi vào Gemini. Không có tài liệu thì Gemini vẫn trả lời kiến thức thường; thông tin doanh nghiệp chưa được cung cấp phải hỏi thêm/chuyển nhân viên, không bịa.

## Request và xử lý phản hồi

HTTPS POST `/v1beta/models/{model}:generateContent`, key ở header `x-goog-api-key`; body gồm systemInstruction, contents và generationConfig. Temperature 0.25; maxOutputTokens 4096; model bắt đầu `gemini-3` thêm thinkingLevel low. Tên model cấu hình được; default code `gemini-3.8-flash` không đảm bảo model/quota khả dụng ở mọi tài khoản.

Đọc candidate đầu tiên, bỏ phần thought=true, ghép text. Text hợp lệ dù không có finishReason vẫn giữ mode ai. MAX_TOKENS giữ câu trả lời kèm notice đạt giới hạn. SAFETY/RECITATION/BLOCKLIST/PROHIBITED_CONTENT/SPII hoặc promptFeedback.blockReason → thông báo hạn chế, mode ai, Success=false; không giả thành mất kết nối.

## Fallback, timeout và rate limit

Thiếu key; HTTP 400/404 model/config, 401 key, 403 quyền, 429 quota, 503 quá tải hoặc lỗi HTTP khác; lỗi mạng; timeout; JSON sai; 200 không có text dùng mode knowledge/Success=false và thông báo. Fallback hiển thị tối đa 2 bài trong danh sách đã truy xuất; không có bài thì nói chưa có hướng dẫn thay thế và đề nghị thử lại/chuyển nhân viên. Riêng chưa có key, controller tra trạng thái từ các ticket riêng gần nhất. Không chạy fallback chỉ vì không tìm thấy tài liệu.

HttpClient timeout 45 giây; trình duyệt abort sau 55 giây. Ask và Handoff dùng chung hạn mức 12 request/phút/tài khoản, không xếp hàng; vượt trả HTTP 429, UI đề nghị chờ. Settings/Test không gắn policy này. Rate limiter trong bộ nhớ một instance; quota của Gemini là giới hạn riêng. Không có retry tự động.

## Bảo mật và giới hạn

Key lưu qua Data Protection ở App_Data/gemini-settings.json, không hiển thị lại trên form, không log raw key/body lỗi nhà cung cấp. Key mã hóa vẫn là dữ liệu nhạy cảm; bảo vệ quyền thư mục và key ring. Key môi trường hỗ trợ Gemini__ApiKey, GEMINI_API_KEY, GOOGLE_API_KEY; cấu hình lưu có ưu tiên, RemoveKey chặn fallback môi trường. .env.example chỉ tham khảo, không tự nạp .env.

Prompt coi ticket/chat là dữ liệu, không phải lệnh thay đổi quy tắc; không hỏi password/OTP/CVV, không tự xác nhận refund. Lọc dữ liệu trên server là lớp bảo vệ chính; prompt không đảm bảo chống mọi prompt injection. Lịch sử đã lưu có thể chứa dữ liệu nhạy cảm do người dùng tự nhập; chưa có bộ che PII tự động.

UI hiển thị **AI có thể sai** và thông báo dữ liệu gửi Gemini. Người dùng cần đối chiếu nguồn/chuyển Staff với vấn đề chính sách, tiền hoặc dữ liệu tài khoản. Chưa có đo lường độ chính xác hoặc bảo đảm câu trả lời tất định; bài nguồn là ngữ cảnh truy xuất, không phải chứng nhận độ đúng.

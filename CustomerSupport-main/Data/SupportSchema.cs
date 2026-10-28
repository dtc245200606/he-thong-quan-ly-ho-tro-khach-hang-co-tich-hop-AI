using CustomerSupportAI.Models;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportAI.Data;

public static class SupportSchema
{
    // Additive upgrade: preserve existing accounts, tickets and messages.
    public static async Task InitializeAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS KnowledgeArticles (
              Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL,
              Category TEXT NOT NULL, Body TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS AssistantTurns (
              Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, UserId INTEGER NOT NULL,
              Question TEXT NOT NULL, Reply TEXT NOT NULL, Mode TEXT NOT NULL, CreatedAt TEXT NOT NULL,
              FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE);
            CREATE INDEX IF NOT EXISTS IX_AssistantTurns_UserId_Id ON AssistantTurns(UserId, Id);
            CREATE TABLE IF NOT EXISTS AuditLogs (
              Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, ActorId INTEGER NULL,
              ActorName TEXT NOT NULL, "Action" TEXT NOT NULL, Target TEXT NOT NULL,
              Detail TEXT NOT NULL, CreatedAt TEXT NOT NULL);
            """);
        // Additive column for databases created before account locking existed.
        var hasIsActive = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('Users') WHERE name = 'IsActive'")
            .SingleAsync();
        if (hasIsActive == 0)
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1;");
        }
        // Additive column for phone-number sign-in.
        var hasPhone = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('Users') WHERE name = 'PhoneNumber'")
            .SingleAsync();
        if (hasPhone == 0)
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users ADD COLUMN PhoneNumber TEXT NULL;");
        }
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_PhoneNumber ON Users(PhoneNumber) WHERE PhoneNumber IS NOT NULL;");
        if (await db.KnowledgeArticles.AnyAsync()) return;
        db.KnowledgeArticles.AddRange(
            Article("Cách tạo yêu cầu hỗ trợ", "Bắt đầu", "Vào Yêu cầu hỗ trợ → Tạo yêu cầu. Nhập tiêu đề, chọn danh mục và mô tả vấn đề (ít nhất 10 ký tự). Cung cấp mã đơn hàng hoặc mã sản phẩm nếu có, các bước đã thử và lỗi gặp phải. Sau khi gửi, hệ thống cấp mã CS để bạn theo dõi. Không gửi mật khẩu, mã OTP hoặc số thẻ."),
            Article("Theo dõi và phản hồi yêu cầu", "Yêu cầu hỗ trợ", "Trong Yêu cầu hỗ trợ, tìm bằng tiêu đề hoặc mã CS, lọc theo trạng thái rồi chọn Mở. Nhập nội dung tại Gửi phản hồi để tiếp tục trao đổi với nhân viên. Yêu cầu đã đóng không nhận phản hồi mới; bạn có thể tạo yêu cầu mới và ghi mã yêu cầu cũ."),
            Article("Ý nghĩa các trạng thái hỗ trợ", "Yêu cầu hỗ trợ", "Mới tiếp nhận: yêu cầu đã được ghi nhận. Đang xử lý: nhân viên đang hỗ trợ. Đã giải quyết: nhân viên đã đưa ra hướng giải quyết. Đã đóng: cuộc trao đổi kết thúc. Chỉ nhân viên và quản trị viên được cập nhật trạng thái. Hệ thống chưa cam kết thời gian phản hồi cụ thể."),
            Article("Đăng nhập và bảo vệ tài khoản", "Tài khoản", "Đăng nhập bằng email đã đăng ký. Khi quên mật khẩu, bấm Quên mật khẩu? ở trang đăng nhập, nhập email hoặc số điện thoại đã đăng ký; hệ thống gửi liên kết đặt lại tới email của tài khoản, hiệu lực 30 phút và chỉ dùng một lần. Nếu không nhận được email, liên hệ nhân viên hỗ trợ. Để đổi mật khẩu khi đã đăng nhập, vào Tài khoản → Đổi mật khẩu, nhập mật khẩu hiện tại và mật khẩu mới có ít nhất 10 ký tự. Không chia sẻ mật khẩu hoặc OTP."),
            Article("Hỗ trợ thanh toán và hóa đơn", "Thanh toán", "Khi gặp vấn đề thanh toán, tạo yêu cầu với danh mục Thanh toán, cung cấp mã giao dịch, thời điểm và mô tả lỗi. Không gửi số thẻ đầy đủ, CVV hay OTP. Nhân viên sẽ kiểm tra yêu cầu; hệ thống chưa có chính sách hoàn tiền hoặc thời gian xử lý được công bố, nên trợ lý không thể xác nhận khoản hoàn tiền."),
            Article("Bảo hành và xử lý lỗi sản phẩm", "Sản phẩm", "Tạo yêu cầu với danh mục Bảo hành và sửa chữa hoặc Hỗ trợ sản phẩm. Cung cấp tên sản phẩm, mã đơn, ngày mua nếu biết, mô tả lỗi và các bước đã thử. Điều kiện bảo hành cần được nhân viên xác nhận; không có thời hạn bảo hành chung được công bố trong hệ thống."),
            Article("Trợ lý thông minh và chuyển nhân viên", "Trợ lý", "Trợ lý tra cứu kho hướng dẫn và các yêu cầu của chính bạn. Khi được cấu hình nhà cung cấp AI, trợ lý có thể trả lời dựa trên ngữ cảnh cuộc hội thoại. Nếu chưa giải quyết được, chọn Chuyển nhân viên ở trang Trợ lý để tạo một yêu cầu từ cuộc trao đổi gần đây. AI không tự thay đổi trạng thái hoặc thực hiện thanh toán."));
        await db.SaveChangesAsync();
    }
    private static KnowledgeArticle Article(string title, string category, string body) => new() { Title = title, Category = category, Body = body };
}

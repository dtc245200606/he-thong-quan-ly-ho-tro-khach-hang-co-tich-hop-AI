using System.Security.Cryptography;
using System.Text;
using CustomerSupportAI.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportAI.Data;

/// <summary>
/// Fictional demo data for presentations: staff, customers, tickets with realistic threads spread over
/// the last 75 days, and a few assistant turns. Every demo account uses the <see cref="Domain"/> email
/// domain, so <see cref="RemoveAsync"/> deletes exactly what <see cref="SeedAsync"/> created.
/// Run with <c>dotnet run -- --seed-demo</c> or <c>-- --remove-demo</c> (see scripts/seed-demo.ps1).
/// </summary>
public static class DemoData
{
    public const string Domain = "demo.customersupport.vn";
    private static readonly string NormalizedSuffix = "@" + Domain.ToUpperInvariant();

    private static readonly (string Name, string Email)[] Staff =
    [
        ("Trần Minh Anh", "minhanh.tran"),
        ("Lê Quốc Huy", "quochuy.le"),
        ("Phạm Thu Hà", "thuha.pham"),
    ];

    private static readonly (string Name, string Email)[] Customers =
    [
        ("Nguyễn Văn An", "an.nguyen"), ("Trần Thị Bình", "binh.tran"), ("Lê Hoàng Cường", "cuong.le"),
        ("Phạm Ngọc Diệp", "diep.pham"), ("Hoàng Đức Duy", "duy.hoang"), ("Vũ Thị Hạnh", "hanh.vu"),
        ("Đặng Minh Khoa", "khoa.dang"), ("Bùi Thanh Lan", "lan.bui"), ("Đỗ Quang Minh", "minh.do"),
        ("Ngô Thị Ngọc", "ngoc.ngo"), ("Dương Văn Phúc", "phuc.duong"), ("Lý Thu Quỳnh", "quynh.ly"),
        ("Mai Anh Tuấn", "tuan.mai"), ("Trịnh Bảo Vy", "vy.trinh"),
    ];

    /// <summary>Thread templates: subject, category, customer request, staff reply, customer follow-up, staff closing. {0} = order code.</summary>
    private static readonly (string Subject, string Category, string Ask, string Reply, string FollowUp, string Close)[] Scenarios =
    [
        ("Bị trừ tiền hai lần cho đơn {0}", "Billing",
            "Chào shop, tôi thanh toán đơn {0} bằng thẻ ngân hàng nhưng tài khoản bị trừ tiền hai lần. Nhờ kiểm tra giúp tôi.",
            "Chào anh/chị, chúng tôi đã ghi nhận. Anh/chị vui lòng gửi giúp mã giao dịch của hai lần trừ tiền (không gửi số thẻ) để bộ phận kế toán đối soát.",
            "Mã giao dịch là FT2410{1} và FT2410{2}, cả hai cùng thời điểm.",
            "Kế toán đã xác nhận một giao dịch trùng và gửi yêu cầu hoàn về ngân hàng. Thời gian tiền về tài khoản phụ thuộc ngân hàng phát hành thẻ."),
        ("Không nhận được hóa đơn VAT đơn {0}", "Billing",
            "Công ty tôi cần hóa đơn VAT cho đơn {0} nhưng chưa nhận được email hóa đơn.",
            "Chào anh/chị, anh/chị vui lòng xác nhận lại tên công ty, mã số thuế và email nhận hóa đơn giúp chúng tôi.",
            "Tôi đã cập nhật đủ thông tin trong phần ghi chú đơn hàng rồi.",
            "Hóa đơn điện tử đã được xuất và gửi lại tới email anh/chị cung cấp. Anh/chị kiểm tra cả thư mục Spam giúp chúng tôi."),
        ("Mã giảm giá không áp dụng được", "Billing",
            "Tôi nhập mã khuyến mãi tháng này nhưng hệ thống báo mã không hợp lệ khi thanh toán đơn {0}.",
            "Chào anh/chị, mã này chỉ áp dụng cho đơn từ một giá trị tối thiểu. Anh/chị cho chúng tôi biết tổng giá trị giỏ hàng để kiểm tra nhé.",
            "Giỏ hàng của tôi khoảng 450.000đ.",
            "Đơn của anh/chị chưa đạt giá trị tối thiểu của chương trình nên mã chưa áp dụng được. Chúng tôi đã gửi hướng dẫn các mã phù hợp qua email."),
        ("Không đăng nhập được sau khi đổi điện thoại", "Account access",
            "Tôi mới đổi điện thoại và giờ không đăng nhập được tài khoản, báo sai mật khẩu dù tôi nhớ đúng.",
            "Chào anh/chị, anh/chị thử dùng chức năng Quên mật khẩu ở trang đăng nhập để nhận liên kết đặt lại qua email đã đăng ký nhé.",
            "Tôi đã đặt lại được mật khẩu nhưng muốn kiểm tra xem có ai khác đăng nhập không.",
            "Sau khi đặt lại mật khẩu, mọi phiên đăng nhập cũ đã bị đăng xuất. Anh/chị nên dùng mật khẩu riêng cho tài khoản này."),
        ("Muốn đổi email đăng nhập", "Account access",
            "Email cũ của tôi không còn dùng nữa, tôi muốn đổi sang email mới thì làm thế nào?",
            "Chào anh/chị, để bảo mật, anh/chị vui lòng gửi yêu cầu từ chính tài khoản này và cho biết email mới. Nhân viên sẽ cập nhật sau khi xác minh.",
            "Email mới của tôi đã ghi trong hồ sơ, nhờ bạn cập nhật giúp.",
            "Chúng tôi đã cập nhật email đăng nhập. Từ lần sau anh/chị đăng nhập bằng email mới."),
        ("Tài khoản bị khóa không rõ lý do", "Account access",
            "Sáng nay tôi đăng nhập thì báo tài khoản bị khóa, tôi không làm gì sai cả.",
            "Chào anh/chị, chúng tôi đang kiểm tra lịch sử tài khoản. Anh/chị có đăng nhập sai mật khẩu nhiều lần trước đó không?",
            "Có thể con tôi đã thử đăng nhập vài lần trên máy tính ở nhà.",
            "Chúng tôi đã mở khóa tài khoản. Nếu bị chặn vì nhập sai nhiều lần, hệ thống sẽ tự cho thử lại sau 15 phút."),
        ("Máy lọc không khí báo lỗi E3", "Product support",
            "Máy lọc không khí mua theo đơn {0} hiển thị lỗi E3 và tự tắt sau vài phút.",
            "Chào anh/chị, lỗi E3 thường liên quan đến cảm biến bụi. Anh/chị thử tháo nắp sau, vệ sinh cảm biến bằng tăm bông khô rồi khởi động lại giúp chúng tôi.",
            "Tôi đã vệ sinh nhưng máy vẫn báo E3.",
            "Cảm ơn anh/chị đã thử. Chúng tôi đã chuyển yêu cầu sang bộ phận bảo hành để hẹn lịch kiểm tra trực tiếp."),
        ("Không kết nối được ứng dụng với thiết bị", "Product support",
            "Ứng dụng trên điện thoại không tìm thấy thiết bị dù đã bật Bluetooth.",
            "Chào anh/chị, anh/chị cho biết dòng điện thoại và phiên bản ứng dụng đang dùng nhé. Đồng thời thử cấp quyền Vị trí cho ứng dụng.",
            "Điện thoại Android 14, ứng dụng bản mới nhất. Cấp quyền Vị trí xong thì đã thấy thiết bị.",
            "Rất vui vì đã kết nối được. Android yêu cầu quyền Vị trí để quét Bluetooth; chúng tôi sẽ bổ sung hướng dẫn này vào kho hướng dẫn."),
        ("Hướng dẫn cài đặt lần đầu", "Product support",
            "Tôi mới nhận hàng đơn {0}, không thấy sách hướng dẫn tiếng Việt trong hộp.",
            "Chào anh/chị, chúng tôi gửi kèm liên kết hướng dẫn tiếng Việt qua email. Anh/chị cần hỗ trợ bước nào cứ phản hồi tại đây.",
            "Cảm ơn, tôi làm theo được rồi.",
            "Cảm ơn anh/chị đã phản hồi. Chúng tôi đóng yêu cầu này, anh/chị có thể tạo yêu cầu mới khi cần."),
        ("Màn hình bị sọc sau 2 tháng sử dụng", "Warranty & repair",
            "Màn hình sản phẩm đơn {0} xuất hiện sọc dọc sau 2 tháng, tôi muốn bảo hành.",
            "Chào anh/chị, anh/chị gửi giúp ảnh chụp màn hình lỗi và tem bảo hành trên sản phẩm. Điều kiện bảo hành cần được kỹ thuật viên xác nhận.",
            "Tôi đã gửi ảnh qua email theo hướng dẫn.",
            "Kỹ thuật viên đã xác nhận lỗi thuộc diện bảo hành. Đơn vị vận chuyển sẽ liên hệ anh/chị để nhận sản phẩm."),
        ("Hỏi tiến độ sửa chữa", "Warranty & repair",
            "Tôi gửi máy bảo hành tuần trước theo đơn {0}, giờ muốn biết tình trạng sửa chữa.",
            "Chào anh/chị, trung tâm bảo hành đang chờ linh kiện thay thế. Chúng tôi sẽ cập nhật ngay khi có lịch trả máy.",
            "Khoảng bao lâu thì có linh kiện vậy?",
            "Linh kiện đã về và máy đã sửa xong. Đơn vị vận chuyển sẽ giao lại trong 2-3 ngày làm việc."),
        ("Pin sạc nhanh hết", "Warranty & repair",
            "Pin của thiết bị đơn {0} giờ chỉ dùng được khoảng 2 tiếng, trước đây dùng cả ngày.",
            "Chào anh/chị, anh/chị cho biết thiết bị đã dùng bao lâu và có cập nhật phần mềm gần đây không?",
            "Dùng được 8 tháng, có cập nhật phần mềm tuần trước.",
            "Bản cập nhật mới có lỗi tiêu hao pin đã được sửa ở bản vá hôm nay. Anh/chị cập nhật lại và theo dõi thêm giúp chúng tôi."),
        ("Góp ý về giao diện website", "Other",
            "Trang thanh toán trên điện thoại hơi khó bấm nút Xác nhận vì bị che bởi bàn phím.",
            "Cảm ơn anh/chị đã góp ý chi tiết. Chúng tôi đã chuyển cho đội phát triển; anh/chị đang dùng trình duyệt nào ạ?",
            "Tôi dùng Safari trên iPhone.",
            "Đội phát triển đã ghi nhận lỗi trên Safari. Cảm ơn anh/chị đã giúp chúng tôi cải thiện sản phẩm."),
        ("Muốn hủy đơn hàng {0}", "Other",
            "Tôi đặt nhầm màu, muốn hủy đơn {0} để đặt lại.",
            "Chào anh/chị, đơn hàng chưa được giao cho đơn vị vận chuyển nên có thể hủy. Anh/chị xác nhận lại giúp chúng tôi là muốn hủy toàn bộ đơn nhé.",
            "Đúng rồi, hủy giúp tôi toàn bộ đơn.",
            "Chúng tôi đã hủy đơn {0}. Anh/chị có thể đặt lại đơn mới với màu mong muốn."),
    ];

    private static readonly (string Question, string Reply)[] AssistantSamples =
    [
        ("Làm sao để theo dõi yêu cầu hỗ trợ?", "Vào Yêu cầu hỗ trợ, tìm theo tiêu đề hoặc mã CS rồi chọn Mở để xem trao đổi và phản hồi tiếp."),
        ("Tôi quên mật khẩu thì làm gì?", "Bấm Quên mật khẩu? ở trang đăng nhập và nhập email hoặc số điện thoại đã đăng ký; liên kết đặt lại có hiệu lực 30 phút."),
        ("Trạng thái Đã giải quyết nghĩa là gì?", "Đã giải quyết nghĩa là nhân viên đã đưa ra hướng giải quyết. Nếu chưa ổn, bạn có thể phản hồi tiếp trong yêu cầu."),
    ];

    public static async Task<string> SeedAsync(AppDbContext db, IPasswordHasher<SupportUser> hasher, string appDataDirectory)
    {
        if (await db.Users.AnyAsync(user => user.NormalizedEmail.EndsWith(NormalizedSuffix)))
        {
            return $"Dữ liệu mẫu đã tồn tại (tài khoản @{Domain}). Chạy --remove-demo trước nếu muốn tạo lại.";
        }

        var password = "Demo-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant() + "!";
        var random = new Random(2026);
        var now = DateTimeOffset.UtcNow;
        var takenPhones = (await db.Users.Where(u => u.PhoneNumber != null).Select(u => u.PhoneNumber!).ToListAsync()).ToHashSet();

        SupportUser NewUser(string name, string local, string role, DateTimeOffset created, string? phone)
        {
            var email = $"{local}@{Domain}";
            var user = new SupportUser
            {
                DisplayName = name, Email = email, NormalizedEmail = email.ToUpperInvariant(), Role = role, CreatedAt = created,
                PhoneNumber = phone is not null && takenPhones.Add(phone) ? phone : null
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            return user;
        }

        var staff = Staff.Select((s, i) => NewUser(s.Name, s.Email, UserRoles.Staff, now.AddDays(-120 + i), null)).ToList();
        var customers = Customers.Select((c, i) => NewUser(c.Name, c.Email, UserRoles.Customer,
            now.AddDays(-110 + i * 6).AddHours(random.Next(0, 23)), $"09990000{i + 10:00}")).ToList();
        db.Users.AddRange(staff);
        db.Users.AddRange(customers);
        await db.SaveChangesAsync();

        // Ticket creation times, oldest first so ids follow time (lists sort by id).
        var created = Enumerable.Range(0, 48)
            .Select(_ => now.AddDays(-Math.Pow(random.NextDouble(), 1.6) * 75).AddMinutes(-random.Next(0, 600)))
            .Order()
            .ToList();
        var tickets = new List<SupportTicket>();
        foreach (var start in created)
        {
            var scenario = Scenarios[random.Next(Scenarios.Length)];
            var customer = customers[random.Next(customers.Count)];
            var agent = staff[random.Next(staff.Count)];
            var order = $"DH{random.Next(100000, 999999)}";
            string Fill(string text) => string.Format(text, order, random.Next(1000, 9999), random.Next(1000, 9999));
            var age = (now - start).TotalDays;

            // How far the thread got: 1 = only the request … 4 = closed out by staff.
            var roll = random.NextDouble();
            var (steps, status) = age > 14
                ? (4, roll < 0.7 ? "Closed" : "Resolved")
                : age > 4
                    ? roll < 0.4 ? (4, "Resolved") : roll < 0.8 ? (2, "In progress") : (3, "In progress")
                    : roll < 0.35 ? (1, "Open") : roll < 0.65 ? (3, "In progress") : (2, "In progress");

            var ticket = new SupportTicket
            {
                TicketNumber = $"CS-{start.ToOffset(TimeSpan.FromHours(7)):yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}",
                Subject = Fill(scenario.Subject), Category = scenario.Category, CustomerId = customer.Id, CreatedAt = start
            };
            var texts = new[] { scenario.Ask, scenario.Reply, scenario.FollowUp, scenario.Close };
            var at = start;
            for (var step = 0; step < steps; step++)
            {
                if (step > 0)
                {
                    // Staff answer within 20 min-18 h; customers reply within 1-24 h.
                    at = at.AddMinutes(step % 2 == 1 ? random.Next(20, 18 * 60) : random.Next(60, 24 * 60));
                    if (at > now) break;
                }
                ticket.Messages.Add(new TicketMessage { AuthorId = step % 2 == 0 ? customer.Id : agent.Id, Body = Fill(texts[step]), CreatedAt = at });
            }
            // A thread cut short by "now" stays open with whoever wrote last.
            ticket.Status = ticket.Messages.Count < steps ? (ticket.Messages.Count == 1 ? "Open" : "In progress") : status;
            ticket.UpdatedAt = at > now ? ticket.Messages.Last().CreatedAt : at;
            tickets.Add(ticket);
        }
        db.Tickets.AddRange(tickets);

        foreach (var customer in customers.Take(5))
        {
            var at = now.AddDays(-random.Next(1, 20));
            foreach (var (question, reply) in AssistantSamples.Take(random.Next(1, AssistantSamples.Length + 1)))
            {
                db.AssistantTurns.Add(new AssistantTurn { UserId = customer.Id, Question = question, Reply = reply, Mode = "knowledge", CreatedAt = at });
                at = at.AddMinutes(2);
            }
        }
        await db.SaveChangesAsync();

        Directory.CreateDirectory(appDataDirectory);
        var file = Path.Combine(appDataDirectory, "demo-accounts.txt");
        var sb = new StringBuilder("Tài khoản dữ liệu mẫu (chỉ dùng để demo; tệp này bị Git bỏ qua)\n");
        sb.AppendLine($"Mật khẩu chung: {password}\n");
        sb.AppendLine("Nhân viên hỗ trợ:");
        foreach (var user in staff) sb.AppendLine($"  {user.Email}  ({user.DisplayName})");
        sb.AppendLine("Khách hàng:");
        foreach (var user in customers) sb.AppendLine($"  {user.Email}  {user.PhoneNumber}  ({user.DisplayName})");
        await File.WriteAllTextAsync(file, sb.ToString(), Encoding.UTF8);

        return $"Đã tạo {staff.Count} nhân viên, {customers.Count} khách hàng, {tickets.Count} yêu cầu, {tickets.Sum(t => t.Messages.Count)} tin nhắn. Thông tin đăng nhập: {file}";
    }

    public static async Task<string> RemoveAsync(AppDbContext db, string appDataDirectory)
    {
        var ids = await db.Users.Where(user => user.NormalizedEmail.EndsWith(NormalizedSuffix)).Select(user => user.Id).ToListAsync();
        if (ids.Count == 0) return "Không có dữ liệu mẫu để xóa.";

        var ticketIds = await db.Tickets.Where(t => ids.Contains(t.CustomerId)).Select(t => t.Id).ToListAsync();
        var messages = await db.TicketMessages.Where(m => ticketIds.Contains(m.TicketId)).ExecuteDeleteAsync();
        var tickets = await db.Tickets.Where(t => ticketIds.Contains(t.Id)).ExecuteDeleteAsync();
        await db.AssistantTurns.Where(t => ids.Contains(t.UserId)).ExecuteDeleteAsync();

        // A demo staff account that later replied in a real ticket is kept (and locked) so that history survives.
        var keep = await db.TicketMessages.Where(m => ids.Contains(m.AuthorId)).Select(m => m.AuthorId).Distinct().ToListAsync();
        await db.Users.Where(u => keep.Contains(u.Id)).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
        var users = await db.Users.Where(u => ids.Contains(u.Id) && !keep.Contains(u.Id)).ExecuteDeleteAsync();

        var file = Path.Combine(appDataDirectory, "demo-accounts.txt");
        if (File.Exists(file)) File.Delete(file);
        return $"Đã xóa {users} tài khoản, {tickets} yêu cầu, {messages} tin nhắn mẫu." +
            (keep.Count > 0 ? $" Giữ lại và khóa {keep.Count} tài khoản mẫu vì đã phản hồi trong yêu cầu thật." : "");
    }
}

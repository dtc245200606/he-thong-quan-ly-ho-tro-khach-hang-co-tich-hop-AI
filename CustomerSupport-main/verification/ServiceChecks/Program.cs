using System.Net;
using System.Text;
using CustomerSupportAI.Models;
using CustomerSupportAI.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

var folder = Path.Combine(Path.GetTempPath(), "CustomerSupportChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try {
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["AiSettings:StorageDirectory"] = folder }).Build();
    var settings = new AiSettingsStore(config, new TestEnvironment { ContentRootPath = folder }, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder,"keys"))));
    var articles = new List<KnowledgeArticle> { new() { Id=1, Title="Đổi mật khẩu", Body="Vào tài khoản để đổi mật khẩu.", Category="Tài khoản" } };
    Check(KnowledgeSearch.Find(articles,"doi mat khau").Count == 1,"accent-insensitive knowledge retrieval");
    Check(KnowledgeSearch.Find(articles,"unrelated xyz").Count == 0,"unknown question has no fabricated source");
    var handler = new ProviderHandler(); var client = new HttpClient(handler);
    var service = new AiAssistantService(client,settings,NullLogger<AiAssistantService>.Instance);
    var local = await service.GetReplyAsync("mật khẩu","",articles,[],default);
    Check(local.Mode=="knowledge" && !local.Success && handler.RequestCount==0,"no key uses disclosed knowledge mode");
    settings.Save(new AiSettingsForm { Model="test-model",ApiKey="test-secret-key" });
    Check(settings.Read().Key=="test-secret-key","encrypted key round trip");
    Check(!File.ReadAllText(Path.Combine(folder,"gemini-settings.json")).Contains("test-secret-key"),"key never persisted as plaintext");
    settings.Save(new AiSettingsForm { Model="test-model" });
    Check(settings.Read().Key=="test-secret-key","blank key preserves configuration");
    var ai = await service.GetReplyAsync("Câu hỏi mới","CS-001: đang xử lý",articles,[new AssistantTurn { Question="Câu trước",Reply="Phản hồi trước" }],default);
    Check(ai.Success && ai.Mode=="ai" && ai.Reply=="Câu trả lời đã kiểm tra.","provider response parsing");
    Check(handler.LastBody.Contains("CS-001") && handler.LastBody.Contains("Câu trước",StringComparison.Ordinal) && handler.LastBody.Contains("Đổi mật khẩu",StringComparison.Ordinal),"provider receives history, scoped ticket context and knowledge");
    Check(handler.HadHeader && !handler.LastUrl.Contains("test-secret-key"),"key sent in header rather than URL");
    Check(handler.LastUrl=="https://generativelanguage.googleapis.com/v1beta/models/test-model:generateContent" && handler.HasContext,"Gemini endpoint receives correct request schema");
    handler.Body="""{"candidates":[{"finishReason":"MAX_TOKENS","content":{"parts":[{"text":"partial response"}]}}]}""";
    var partial = await service.GetReplyAsync("test","",articles,[],default);
    Check(partial.Success && partial.Mode=="ai" && partial.Notice!=null,"usable truncated AI answer is retained with notice");
    handler.Body="""{"candidates":[{"content":{"parts":[{"text":"Xin chào! Tôi có thể giúp gì cho bạn?"}]}}]}""";
    foreach(var question in new[]{"xin chào","bạn là ai","bạn có thể giúp gì?","Thủ đô nước Pháp là gì?"}) {
        var before=handler.RequestCount;
        var greeting=await service.GetReplyAsync(question,"",[],[],default);
        Check(greeting.Mode=="ai" && greeting.Success && handler.RequestCount==before+1,"general question reaches Gemini without documents: "+question);
    }
    Check(KnowledgeSearch.ForAssistant(articles,"xin chào","đổi mật khẩu").Count==0,"greeting does not inherit irrelevant documents from history");
    Check(KnowledgeSearch.ForAssistant(articles,"đổi mật khẩu",null).Count==1,"support question retrieves relevant context");
    handler.Body="""{"candidates":[{"finishReason":"SAFETY"}]}""";
    var blocked=await service.GetReplyAsync("test","",[],[],default);
    Check(blocked.Mode=="ai" && !blocked.Success,"provider safety response does not pretend the provider is unavailable");
    handler.Body="invalid json";
    var malformed=await service.GetReplyAsync("test","",articles,[],default);
    Check(!malformed.Success && malformed.Mode=="knowledge","malformed provider JSON handled");
    handler.Status=HttpStatusCode.Unauthorized;
    var invalidKey=await service.GetReplyAsync("test","",articles,[],default);
    Check(invalidKey.Notice?.Contains("API key") == true && !invalidKey.Success,"invalid key reports actionable connection error");
    handler.Status=HttpStatusCode.TooManyRequests;
    var fallback = await service.GetReplyAsync("test","",articles,[],default);
    Check(fallback.Mode=="knowledge" && fallback.Notice!=null && !fallback.Success,"quota failures disclose fallback");
    handler.Timeout=true;
    var timeout = await service.GetReplyAsync("test","",articles,[],default);
    Check(timeout.Mode=="knowledge" && timeout.Notice!=null,"provider timeout handled");
    // Staff ticket AI tools.
    handler.Timeout=false; handler.Status=HttpStatusCode.OK;
    var ticketAi = new TicketAiService(service);
    var customer = new SupportUser { DisplayName="Nguyễn Văn Bí Mật", Email="khach@example.com", Role=UserRoles.Customer };
    var agent = new SupportUser { DisplayName="Nhân viên A", Role=UserRoles.Staff };
    var ticket = new SupportTicket { TicketNumber="CS-TEST", Subject="Không thanh toán được", Category="Other", Status="Open", Customer=customer };
    ticket.Messages.Add(new TicketMessage { Id=1, Author=customer, Body="Thẻ 4111 1111 1111 1111 bị trừ tiền, gọi tôi 0912 345 678 hoặc mail khach@example.com" });
    ticket.Messages.Add(new TicketMessage { Id=2, Author=agent, Body="Anh/chị gửi mã giao dịch giúp chúng tôi." });
    var context = TicketAiService.Context(ticket);
    Check(!context.Contains("4111") && !context.Contains("0912") && !context.Contains("khach@example.com") && context.Contains("[đã ẩn]"),"ticket AI redacts card, phone and email");
    Check(!context.Contains("Bí Mật") && context.Contains("Khách hàng:") && context.Contains("Nhân viên:"),"ticket AI omits customer name but keeps speaker roles");
    var categories = new[] { "Product support","Billing","Other" };
    handler.Body="""{"candidates":[{"content":{"parts":[{"text":"{\"category\":\"Billing\",\"priority\":\"Cao\",\"reason\":\"Khách bị trừ tiền.\"}"}]}}]}""";
    var classified = await ticketAi.ClassifyAsync(ticket,categories,default);
    Check(classified.Value is { Category:"Billing", Priority:"Cao" } && handler.LastBody.Contains("application/json"),"ticket AI classification parsed from JSON response");
    handler.Body="""{"candidates":[{"content":{"parts":[{"text":"{\"category\":\"Refunds\",\"priority\":\"Cao\",\"reason\":\"x\"}"}]}}]}""";
    Check((await ticketAi.ClassifyAsync(ticket,categories,default)) is { Value:null, Error:not null },"ticket AI rejects a category outside the allowed list");
    handler.Body="""{"candidates":[{"content":{"parts":[{"text":"```json\n{\"issue\":\"Bị trừ tiền\",\"tried\":[],\"keyPoints\":[\"Đã hỏi mã giao dịch\"],\"current\":\"Chờ khách\",\"nextSteps\":[\"Đối soát\"],\"missing\":[\"Mã giao dịch\"]}\n```"}]}}]}""";
    var summarised = await ticketAi.SummarizeAsync(ticket,default);
    Check(summarised.Value is { Issue:"Bị trừ tiền" } s && s.Missing.Single()=="Mã giao dịch" && s.Tried.Count==0,"ticket AI summary parsed, code fence tolerated");
    handler.Body="""{"candidates":[{"content":{"parts":[{"text":"Chào anh/chị, ..."}]}}]}""";
    var drafted = await ticketAi.DraftReplyAsync(ticket,articles,"Nhân viên A",default);
    Check(drafted.Value=="Chào anh/chị, ..." && handler.LastBody.Contains("Đổi mật khẩu") && handler.LastBody.Contains("không hứa hoàn tiền"),"ticket AI draft uses knowledge and forbids unsupported promises");
    handler.Status=HttpStatusCode.TooManyRequests;
    Check((await ticketAi.DraftReplyAsync(ticket,[],"A",default)) is { Value:null, Error:not null },"ticket AI provider failure returns an error, not invented content");
    handler.Status=HttpStatusCode.OK;
    settings.Save(new AiSettingsForm { Model="test-model",RemoveKey=true });
    Check(!service.IsConfigured,"disconnect removes stored key");
    var countBefore=handler.RequestCount;
    Check((await ticketAi.SummarizeAsync(ticket,default)) is { Value:null, Error:not null } && handler.RequestCount==countBefore,"ticket AI without key reports setup step and makes no request");
    config["Gemini:ApiKey"]="environment-key";
    Check(!service.IsConfigured,"explicit disconnect blocks environment key");
    Console.WriteLine("All service checks passed.");
} finally { Directory.Delete(folder,true); }
static void Check(bool condition,string name) { if(!condition) throw new Exception("FAILED: "+name); Console.WriteLine("PASS: "+name); }
sealed class ProviderHandler : HttpMessageHandler
{
    public int RequestCount; public string LastBody="",LastUrl=""; public bool HadHeader,Timeout,HasContext; public HttpStatusCode Status=HttpStatusCode.OK;
    public string Body="""{"candidates":[{"finishReason":"STOP","content":{"parts":[{"thought":true,"text":"Hidden thought"},{"text":"Câu trả lời đã kiểm tra."}]}}]}""";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
        RequestCount++;var raw=await request.Content!.ReadAsStringAsync(token);using var payload=System.Text.Json.JsonDocument.Parse(raw);HasContext=payload.RootElement.TryGetProperty("systemInstruction",out _) && payload.RootElement.GetProperty("contents").GetArrayLength()==3;LastBody=System.Text.RegularExpressions.Regex.Unescape(raw);LastUrl=request.RequestUri!.ToString();HadHeader=request.Headers.TryGetValues("x-goog-api-key",out var keys) && keys.Single()=="test-secret-key";
        if(Timeout)throw new TaskCanceledException();
        return new HttpResponseMessage(Status){Content=new StringContent(Body,Encoding.UTF8,"application/json")};
    }
}
sealed class TestEnvironment : IWebHostEnvironment {
    public string ApplicationName{get;set;}="Checks";public string EnvironmentName{get;set;}="Development";public string ContentRootPath{get;set;}="";
    public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public string WebRootPath{get;set;}="";public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
}

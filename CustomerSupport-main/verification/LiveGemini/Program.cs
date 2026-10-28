using System.Text.Json;
using CustomerSupportAI.Services;
using CustomerSupportAI.Data;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=root,ApplicationName="CustomerSupportAI",EnvironmentName="Development"});
builder.Logging.ClearProviders();builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);builder.Services.AddDataProtection();builder.Services.AddSingleton<AiSettingsStore>();builder.Services.AddHttpClient<AiAssistantService>(c=>c.Timeout=TimeSpan.FromSeconds(45));
builder.Services.AddDbContext<AppDbContext>(options=>options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source="+Path.Combine(root,"customer-support.db")));
await using var app=builder.Build();var settings=app.Services.GetRequiredService<AiSettingsStore>();var (model,key)=settings.Read();
var selected=args.FirstOrDefault(a=>a.StartsWith("--use-model="));
if(selected is not null){settings.Save(new AiSettingsForm{Model=selected["--use-model=".Length..]});(model,key)=settings.Read();}
Console.WriteLine($"Configured: {!string.IsNullOrWhiteSpace(key)}; Model: {model}");
if(string.IsNullOrWhiteSpace(key))return;
if(args.Contains("--verify-web")) {
    using var handler=new HttpClientHandler{CookieContainer=new CookieContainer()};using var web=new HttpClient(handler){BaseAddress=new Uri("http://localhost:5180"),Timeout=TimeSpan.FromSeconds(55)};
    var email="assistant-check-"+Guid.NewGuid().ToString("N")+"@example.invalid";
    using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try{
        string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var register=await web.GetStringAsync("/Account/Register");var password=Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))+"aA1!";
        using var signed=await web.PostAsync("/Account/Register",new FormUrlEncodedContent(new Dictionary<string,string>{["DisplayName"]="Assistant verification",["Email"]=email,["Password"]=password,["ConfirmPassword"]=password,["__RequestVerificationToken"]=Token(register)}));signed.EnsureSuccessStatusCode();
        var page=await web.GetStringAsync("/Assistant");var token=Token(page);
        foreach(var question in new[]{"xin chào","Tôi tên là Linh.","Tôi vừa cho biết tên tôi là gì?"}) {
            using var ask=new HttpRequestMessage(HttpMethod.Post,"/Assistant/Ask");ask.Headers.Add("RequestVerificationToken",token);ask.Content=JsonContent.Create(new{message=question});using var reply=await web.SendAsync(ask);reply.EnsureSuccessStatusCode();using var body=JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
            var mode=body.RootElement.GetProperty("mode").GetString();var text=body.RootElement.GetProperty("reply").GetString()!;
            if(mode!="ai")throw new Exception("Expected real Gemini response, got "+mode+": "+text);
            if(question=="xin chào"&&body.RootElement.GetProperty("sources").GetArrayLength()!=0)throw new Exception("Greeting incorrectly received article sources");
            if(question.Contains("vừa")&&!text.Contains("Linh",StringComparison.OrdinalIgnoreCase))throw new Exception("Conversation history was not used");
            Console.WriteLine("PASS real web: "+question+" -> mode=ai; "+text);
        }
        var saved=await db.AssistantTurns.CountAsync(t=>t.UserId==db.Users.Where(u=>u.Email==email).Select(u=>u.Id).First());
        if(saved!=3)throw new Exception("Conversation turns not persisted");Console.WriteLine("PASS: real AI history persisted and recalled.");
    }finally{
        var user=await db.Users.SingleOrDefaultAsync(u=>u.Email==email);if(user is not null){await db.AssistantTurns.Where(t=>t.UserId==user.Id).ExecuteDeleteAsync();db.Users.Remove(user);await db.SaveChangesAsync();}
    }
    return;
}
if(args.Contains("--probe")) {
    using var probeClient=new HttpClient{Timeout=TimeSpan.FromSeconds(45)};
    await Task.WhenAll(new[]{model,"gemini-3.7-flash","gemini-3.5-flash-lite"}.Distinct().Select(async name=>{
        using var probe=new HttpRequestMessage(HttpMethod.Post,$"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(name)}:generateContent");probe.Headers.Add("x-goog-api-key",key);
        probe.Content=System.Net.Http.Json.JsonContent.Create(new {contents=new[]{new{role="user",parts=new[]{new{text="xin chào"}}}},generationConfig=new{maxOutputTokens=256}});
        try{using var reply=await probeClient.SendAsync(probe);using var body=JsonDocument.Parse(await reply.Content.ReadAsStringAsync());Console.WriteLine(name+": HTTP "+(int)reply.StatusCode);if(body.RootElement.TryGetProperty("error",out var error))Console.WriteLine(error.GetProperty("message").GetString()!.Replace(key,"[redacted]"));else Console.WriteLine("Generation received.");}catch(TaskCanceledException){Console.WriteLine(name+": TIMEOUT");}
    }));return;
}
var service=app.Services.GetRequiredService<AiAssistantService>();var result=await service.GetReplyAsync("xin chào","",[],[],default);
Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
using var client=new HttpClient();using var request=new HttpRequestMessage(HttpMethod.Get,"https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000");request.Headers.Add("x-goog-api-key",key);using var response=await client.SendAsync(request);Console.WriteLine("Model list HTTP: "+(int)response.StatusCode);
if(response.IsSuccessStatusCode){using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());foreach(var item in doc.RootElement.GetProperty("models").EnumerateArray())if(item.GetProperty("name").GetString()!.Contains("flash")&&item.TryGetProperty("supportedGenerationMethods",out var methods)&&methods.EnumerateArray().Any(m=>m.GetString()=="generateContent"))Console.WriteLine(item.GetProperty("name").GetString());}

param([switch]$CaptureBrowser)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $projectRoot 'verification-artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$run = Join-Path $artifacts ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$base = 'http://localhost:5181'
$randomBytes = New-Object byte[] 24
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
$random.GetBytes($randomBytes)
$random.Dispose()
$password = [Convert]::ToBase64String($randomBytes) + 'aA1!'
$environmentNames = @('ConnectionStrings__DefaultConnection','ASPNETCORE_ENVIRONMENT','BootstrapAdmin__Email','BootstrapAdmin__Password','Gemini__ApiKey','AiSettings__StorageDirectory','Logging__LogLevel__Default','Mail__OutboxDirectory','Smtp__Host','Chatbot__Provider','Groq__ApiKey')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
function Check($condition, $name) { if (!$condition) { throw "FAILED: $name" }; Write-Output "PASS: $name" }
function Page($path, $session) { Invoke-WebRequest -Uri ($base + $path) -WebSession $session -UseBasicParsing }
function Token($response) { $match = [regex]::Match($response.Content, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"'); if (!$match.Success) { throw 'CSRF token missing' }; [Net.WebUtility]::HtmlDecode($match.Groups[1].Value) }
function PostForm($path, $session, $fields, $tokenPage) { $fields['__RequestVerificationToken'] = Token $tokenPage; Invoke-WebRequest -Uri ($base + $path) -WebSession $session -Method Post -Headers @{RequestVerificationToken=$fields['__RequestVerificationToken']} -Body $fields -UseBasicParsing }
function JsonPost($path, $session, $payload, $token) { Invoke-WebRequest -Uri ($base + $path) -WebSession $session -Method Post -ContentType 'application/json; charset=utf-8' -Headers @{RequestVerificationToken=$token} -Body ([Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json))) -UseBasicParsing }
function StatusOf($script) { try { (& $script).StatusCode } catch { if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { throw } } }
$server = $null
try {
    $env:ConnectionStrings__DefaultConnection = 'Data Source=' + (Join-Path $run 'test.db')
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:BootstrapAdmin__Email = 'admin@test.local'
    $env:BootstrapAdmin__Password = $password
    $env:Gemini__ApiKey = ''
    $env:AiSettings__StorageDirectory = Join-Path $run 'settings'
    $outbox = Join-Path $run 'mail-outbox'
    $env:Mail__OutboxDirectory = $outbox
    $env:Smtp__Host = ''
    $env:Chatbot__Provider = 'mock'
    $env:Groq__ApiKey = ''
    $env:Logging__LogLevel__Default = 'Warning'
    $dll = Join-Path $projectRoot 'bin\Debug\net10.0\CustomerSupportAI.dll'
    $server = Start-Process -FilePath 'dotnet' -ArgumentList @('"' + $dll + '"', '--urls', $base) -WorkingDirectory $projectRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $run 'server.log') -RedirectStandardError (Join-Path $run 'server-error.log') -PassThru
    $ready = $false
    for ($attempt=0; $attempt -lt 30; $attempt++) { try { $null = Invoke-WebRequest -Uri $base -UseBasicParsing; $ready=$true; break } catch { Start-Sleep -Milliseconds 500 } }
    Check $ready 'isolated server starts without modifying live data'
    $one = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $two = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $admin = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $landingPage = Page '/' $one
    Check ($landingPage.Content -match 'landing-hero') 'public landing page renders'
    Check ((Page '/Knowledge' $one).StatusCode -eq 200) 'knowledge library available without signing in'
    $registration = Page '/Account/Register' $one
    $registered = PostForm '/Account/Register' $one @{DisplayName='HTTP Test Customer';Email='one@test.local';Password=$password;ConfirmPassword=$password} $registration
    Check ($registered.Content -match 'stats-grid') 'registration signs customer in to dashboard'
    foreach ($path in @('/Dashboard','/Tickets','/Assistant','/Knowledge','/Dashboard/Activity','/Account/Profile','/Account/ChangePassword')) { Check ((Page $path $one).StatusCode -eq 200) "customer page $path" }
    $ticketPage = Page '/Tickets/Create' $one
    $ticket = PostForm '/Tickets/Create' $one @{Subject='HTTP test ticket';Category='Account access';FirstMessage='I cannot access my account, please help.'} $ticketPage
    Check ($ticket.Content -match 'HTTP test ticket') 'customer creates support ticket'
    $ticketId = [regex]::Match($ticket.Content, 'name="TicketId" value="(\d+)"').Groups[1].Value
    Check ($ticketId.Length -gt 0) 'ticket detail includes correct reply target'
    $reply = PostForm '/Tickets/Reply' $one @{TicketId=$ticketId;Body='Additional details from the customer'} $ticket
    Check ($reply.Content -match 'Additional details from the customer') 'customer replies to own ticket'
    $filtered = Page '/Tickets?search=HTTP&status=Open' $one
    Check ($filtered.Content -match 'HTTP test ticket') 'ticket search and status filter'
    $otherRegistration = Page '/Account/Register' $two
    $null = PostForm '/Account/Register' $two @{DisplayName='Second Customer';Email='two@test.local';Password=$password;ConfirmPassword=$password} $otherRegistration
    Check ((StatusOf { Page "/Tickets/Details/$ticketId" $two }) -eq 404) 'another customer cannot read private ticket'
    $denied = Page '/Settings' $one
    Check ($denied.BaseResponse.ResponseUri.AbsolutePath -eq '/Account/AccessDenied') 'customer cannot access AI settings'
    $assistantPage = Page '/Assistant' $one
    $token = Token $assistantPage
    $answer = JsonPost '/Assistant/Ask' $one @{message='Làm thế nào để đổi mật khẩu tài khoản?'} $token
    $data = $answer.Content | ConvertFrom-Json
    Check ($data.mode -eq 'knowledge' -and $data.sources.Count -gt 0) 'assistant returns disclosed knowledge mode with sources'
    Check (([Net.WebUtility]::HtmlDecode((Page '/Assistant' $one).Content)) -match 'Làm thế nào để đổi mật khẩu') 'assistant history persists on reload'
    Check (!(([Net.WebUtility]::HtmlDecode((Page '/Assistant' $two).Content)) -match 'Làm thế nào để đổi mật khẩu')) 'assistant history isolated by user'
    $status = (JsonPost '/Assistant/Ask' $one @{message='Trạng thái yêu cầu của tôi là gì?'} $token).Content | ConvertFrom-Json
    Check ($status.reply -match 'HTTP test ticket') 'assistant retrieves own ticket status without provider key'
    Check ((StatusOf { JsonPost '/Assistant/Ask' $one @{message='  '} $token }) -eq 400) 'blank assistant question rejected'
    Check ((StatusOf { JsonPost '/Assistant/Ask' $one @{message='hello'} 'invalid' }) -eq 400) 'assistant rejects invalid CSRF token'
    $handoff = PostForm '/Assistant/Handoff' $one @{} $assistantPage
    Check (([Net.WebUtility]::HtmlDecode($handoff.Content)) -match 'Cuộc trao đổi gần đây') 'handoff creates ticket with recent conversation'
    $null = PostForm '/Assistant/Clear' $one @{} (Page '/Assistant' $one)
    Check (!(([Net.WebUtility]::HtmlDecode((Page '/Assistant' $one).Content)) -match 'Làm thế nào để đổi mật khẩu')) 'new conversation clears own history'
    $login = Page '/Account/Login' $admin
    $signedAdmin = PostForm '/Account/Login' $admin @{Email='admin@test.local';Password=$password} $login
    Check ($signedAdmin.BaseResponse.ResponseUri.AbsolutePath -eq '/Tickets') 'administrator signs in on the shared login and lands on tickets'
    Check ((Page '/Admin/Users' $admin).StatusCode -eq 200) 'administrator account directory'
    $settingsPage = Page '/Settings' $admin
    $saved = PostForm '/Settings' $admin @{Model='test-model';ApiKey='test-key-for-encryption'} $settingsPage
    Check ($saved.Content -match 'value="test-model"' -and $saved.Content -notmatch 'test-key-for-encryption') 'admin saves model and key is never redisplayed'
    $settingsFile = Join-Path $run 'settings\gemini-settings.json'
    Check ((Get-Content -Raw $settingsFile) -notmatch 'test-key-for-encryption') 'admin key encrypted on disk'
    $null = PostForm '/Settings' $admin @{Model='gemini-3.8-flash';RemoveKey='true'} $saved
    $edit = Page '/Knowledge/Edit' $admin
    $created = PostForm '/Knowledge/Edit' $admin @{Id=0;Title='Verified policy';Category='Tài khoản';Body='Verified content for assistant knowledge retrieval.'} $edit
    Check ($created.Content -match 'Verified content') 'admin publishes knowledge article'
    $staffPage = Page '/Admin/Users' $admin
    $staffCreated = PostForm '/Admin/CreateStaff' $admin @{'NewStaff.DisplayName'='Test Staff';'NewStaff.Email'='staff@test.local';'NewStaff.TemporaryPassword'=$password} $staffPage
    Check ($staffCreated.Content -match 'staff@test.local') 'admin creates support staff account'
    $newSession = { New-Object Microsoft.PowerShell.Commands.WebRequestSession }
    $staffSession = & $newSession
    Check ((Page '/Account/StaffLogin' $staffSession).BaseResponse.ResponseUri.AbsolutePath -eq '/Account/Login') 'old staff portal redirects to the shared login'
    $staffLogin = PostForm '/Account/Login' $staffSession @{Email='staff@test.local';Password=$password} (Page '/Account/Login' $staffSession)
    Check ($staffLogin.BaseResponse.ResponseUri.AbsolutePath -eq '/Tickets') 'staff signs in on the shared login and lands on tickets'
    $deniedAdmin = Page '/Admin/Users' $staffSession
    Check ($deniedAdmin.BaseResponse.ResponseUri.AbsolutePath -eq '/Account/AccessDenied') 'staff cannot open administrator account management'
    $customerList = Page '/Customers?search=one@test.local' $staffSession
    Check ($customerList.Content -match 'one@test.local') 'staff views the customer directory'
    $customerId = [regex]::Match($customerList.Content, '/Customers/Details/(\d+)').Groups[1].Value
    Check ($customerId.Length -gt 0) 'customer directory links to customer details'
    $customerEdit = Page "/Customers/Edit/$customerId" $staffSession
    $customerSaved = PostForm "/Customers/Edit/$customerId" $staffSession @{DisplayName='Renamed Customer';Email='one@test.local';IsActive='true'} $customerEdit
    Check (([Net.WebUtility]::HtmlDecode($customerSaved.Content)) -match 'Renamed Customer') 'staff edits a customer profile'
    $three = & $newSession
    $null = PostForm '/Account/Register' $three @{DisplayName='Third Customer';Email='three@test.local';Password=$password;ConfirmPassword=$password} (Page '/Account/Register' $three)
    $threeList = Page '/Customers?search=three@test.local' $staffSession
    $threeId = [regex]::Match($threeList.Content, '/Customers/Details/(\d+)').Groups[1].Value
    Check ($threeId.Length -gt 0) 'staff finds the third customer'
    $null = PostForm "/Customers/SetActive/$threeId" $staffSession @{active='false'} (Page "/Customers/Details/$threeId" $staffSession)
    Check ((Page '/Tickets' $three).BaseResponse.ResponseUri.AbsolutePath -eq '/Account/Login') 'locked customer is signed out immediately'
    $lockedLogin = PostForm '/Account/Login' $three @{Email='three@test.local';Password=$password} (Page '/Account/Login' $three)
    Check (([Net.WebUtility]::HtmlDecode($lockedLogin.Content)) -match 'đã bị khóa') 'locked customer cannot sign in'
    $null = PostForm "/Customers/Delete/$threeId" $staffSession @{} (Page "/Customers/Details/$threeId" $staffSession)
    Check ((StatusOf { Page "/Customers/Details/$threeId" $staffSession }) -eq 404) 'staff deletes a customer account'
    Check ((Page '/Tickets?status=Awaiting' $staffSession).Content -match 'HTTP test ticket') 'staff filters tickets awaiting a team reply'
    $byCustomer = [Net.WebUtility]::HtmlDecode((Page "/Tickets?customerId=$customerId" $staffSession).Content)
    Check ($byCustomer -match 'HTTP test ticket' -and $byCustomer -match 'Khách hàng: Renamed Customer') 'staff filters tickets by customer'
    # The header notification menu can quote ticket subjects, so assert on the list's empty state instead.
    Check (([Net.WebUtility]::HtmlDecode((Page '/Tickets?from=2000-01-01&to=2000-01-02' $staffSession).Content)) -match 'Không tìm thấy yêu cầu phù hợp') 'ticket date range filter excludes other dates'
    Check (([Net.WebUtility]::HtmlDecode((Page '/Tickets?category=Billing' $one).Content)) -match 'Không tìm thấy yêu cầu phù hợp') 'ticket category filter applies for customers'
    $staffTicket = Page "/Tickets/Details/$ticketId" $staffSession
    Check ($staffTicket.Content -match 'data-ai-endpoint' -and (Page "/Tickets/Details/$ticketId" $one).Content -notmatch 'data-ai-endpoint') 'AI tools shown to staff only'
    Check ((PostForm "/Tickets/Ai/$ticketId" $one @{task='summary'} (Page "/Tickets/Details/$ticketId" $one)).BaseResponse.ResponseUri.AbsolutePath -eq '/Account/AccessDenied') 'customer cannot call ticket AI tools'
    Check ((StatusOf { PostForm "/Tickets/Ai/$ticketId" $staffSession @{task='summary'} $staffTicket }) -eq 502) 'ticket AI without provider key returns an error, not a fabricated result'
    Check ((StatusOf { PostForm "/Tickets/Ai/$ticketId" $staffSession @{task='bogus'} $staffTicket }) -eq 400) 'ticket AI rejects unknown task'
    Check ((PostForm '/Tickets/UpdateCategory' $one @{id=$ticketId;category='Billing'} (Page "/Tickets/Details/$ticketId" $one)).BaseResponse.ResponseUri.AbsolutePath -eq '/Account/AccessDenied') 'customer cannot change ticket category'
    $recategorised = PostForm '/Tickets/UpdateCategory' $staffSession @{id=$ticketId;category='Billing'} $staffTicket
    Check (([Net.WebUtility]::HtmlDecode($recategorised.Content)) -match 'Đã cập nhật danh mục') 'staff changes ticket category'
    Check (([Net.WebUtility]::HtmlDecode((Page '/Admin/AuditLog?kind=ticket.category' $admin).Content)) -match 'Truy cập tài khoản → Hóa đơn và thanh toán') 'category change recorded in audit log'
    $adminCreate = PostForm '/Admin/Create' $admin @{DisplayName='Second Admin';Email='admin2@test.local';Role='Admin';Password=$password} (Page '/Admin/Create' $admin)
    Check ($adminCreate.Content -match 'admin2@test.local') 'administrator creates another administrator'
    Check ((Page '/Admin/AuditLog' $admin).Content -match 'admin2@test.local') 'administrative actions appear in the audit log'
    $brute = & $newSession
    for ($i=0; $i -lt 6; $i++) { $attempt = PostForm '/Account/Login' $brute @{Email='nobody@test.local';Password='wrong-password-1'} (Page '/Account/Login' $brute) }
    Check (([Net.WebUtility]::HtmlDecode($attempt.Content)) -match 'quá nhiều lần') 'repeated failed sign-ins are throttled'
    # Forgot password: link delivered to the development outbox, single use, ends other sessions.
    $resetUser = & $newSession
    $null = PostForm '/Account/Register' $resetUser @{DisplayName='Reset Customer';Email='reset@test.local';Password=$password;ConfirmPassword=$password} (Page '/Account/Register' $resetUser)
    Check ((Page '/Account/Login' (& $newSession)).Content -match '/Account/ForgotPassword') 'login page links to forgot password'
    $anon = & $newSession
    $unknown = PostForm '/Account/ForgotPassword' $anon @{Email='nobody-here@test.local'} (Page '/Account/ForgotPassword' $anon)
    Check ($unknown.BaseResponse.ResponseUri.AbsolutePath -eq '/Account/ForgotPasswordSent') 'unknown account gets the same neutral confirmation'
    $sent = PostForm '/Account/ForgotPassword' $anon @{Email='RESET@test.local'} (Page '/Account/ForgotPassword' $anon)
    Check ($sent.BaseResponse.ResponseUri.AbsolutePath -eq '/Account/ForgotPasswordSent') 'known account gets the same neutral confirmation'
    $mail = $null
    for ($i=0; $i -lt 20 -and !$mail; $i++) { Start-Sleep -Milliseconds 250; $mail = Get-ChildItem $outbox -Filter '*reset_test_local*' -ErrorAction SilentlyContinue | Select-Object -First 1 }
    Check ($mail -and @(Get-ChildItem $outbox).Count -eq 1) 'reset email written only for the existing account'
    $mailText = [IO.File]::ReadAllText($mail.FullName)
    $resetPath = [regex]::Match($mailText, '/Account/ResetPassword\?token=[^\s]+').Value
    Check ($resetPath.Length -gt 0 -and $mailText -notmatch [regex]::Escape($password)) 'reset email contains a tokenised link and no password'
    Check (([Net.WebUtility]::HtmlDecode((Page ($resetPath + 'x') $anon).Content)) -match 'không còn hiệu lực') 'tampered reset token rejected'
    $resetForm = Page $resetPath $anon
    $mismatch = PostForm '/Account/ResetPassword' $anon @{Token=[uri]::UnescapeDataString($resetPath.Split('=',2)[1]);NewPassword='new-reset-pass-1';ConfirmPassword='other-pass-123'} $resetForm
    Check (([Net.WebUtility]::HtmlDecode($mismatch.Content)) -match 'không khớp') 'reset rejects mismatched confirmation'
    $resetPassword = $password + 'reset'
    $done = PostForm '/Account/ResetPassword' $anon @{Token=[uri]::UnescapeDataString($resetPath.Split('=',2)[1]);NewPassword=$resetPassword;ConfirmPassword=$resetPassword} $resetForm
    Check ($done.BaseResponse.ResponseUri.AbsolutePath -eq '/Account/Login' -and ([Net.WebUtility]::HtmlDecode($done.Content)) -match 'Đã đặt lại mật khẩu') 'password reset succeeds'
    Check (([Net.WebUtility]::HtmlDecode((Page $resetPath $anon).Content)) -match 'không còn hiệu lực') 'reset link cannot be reused'
    Check ((Page '/Tickets' $resetUser).BaseResponse.ResponseUri.AbsolutePath -eq '/Account/Login') 'existing sessions signed out after reset'
    $stale = & $newSession
    $oldLogin = PostForm '/Account/Login' $stale @{Email='reset@test.local';Password=$password} (Page '/Account/Login' $stale)
    Check ($oldLogin.BaseResponse.ResponseUri.AbsolutePath -eq '/Account/Login') 'old password no longer works'
    $fresh = & $newSession
    $newLogin = PostForm '/Account/Login' $fresh @{Email='reset@test.local';Password=$resetPassword} (Page '/Account/Login' $fresh)
    Check ($newLogin.BaseResponse.ResponseUri.AbsolutePath -eq '/Dashboard') 'new password signs in'
    Check ((Page '/Admin/AuditLog?kind=user.password-reset' $admin).Content -match 'reset@test.local') 'password reset recorded in audit log'
    $updated = PostForm '/Tickets/UpdateStatus' $admin @{id=$ticketId;status='Resolved'} (Page "/Tickets/Details/$ticketId" $admin)
    Check (([Net.WebUtility]::HtmlDecode($updated.Content)) -match 'Đã giải quyết') 'staff role can update ticket status'
    $closed = PostForm '/Tickets/UpdateStatus' $admin @{id=$ticketId;status='Closed'} $updated
    Check ($closed.Content -notmatch 'id="reply-body"') 'closed ticket disables reply composer'
    $attemptedReply = PostForm '/Tickets/Reply' $one @{TicketId=$ticketId;Body='This must not be accepted'} (Page "/Tickets/Details/$ticketId" $one)
    Check ($attemptedReply.Content -notmatch 'This must not be accepted') 'server rejects replies to closed ticket'
    $newPassword = $password + 'new'
    $changed = PostForm '/Account/ChangePassword' $one @{CurrentPassword=$password;NewPassword=$newPassword;ConfirmPassword=$newPassword} (Page '/Account/ChangePassword' $one)
    Check ($changed.StatusCode -eq 200) 'customer changes password'
    $null = PostForm '/Account/Logout' $one @{} (Page '/Account/Profile' $one)
    $relogin = PostForm '/Account/Login' $one @{Email='one@test.local';Password=$newPassword} (Page '/Account/Login' $one)
    Check ($relogin.Content -match 'stats-grid') 'new password authenticates'
    $token = Token (Page '/Assistant' $two)
    $limited = $false
    for ($i=0; $i -lt 14; $i++) { if ((StatusOf { JsonPost '/Assistant/Ask' $two @{message='Tạo yêu cầu hỗ trợ'} $token }) -eq 429) { $limited=$true;break } }
    Check $limited 'assistant request limit enforced'
    # Website chatbot (mock provider, isolated DB): public API, permissions, handoff, staff console, rate limits.
    function ChatApi($method, $path, $session, $csrf, $chatToken, $payload) {
        $headers = @{RequestVerificationToken=$csrf}
        if ($chatToken) { $headers['X-Chat-Token'] = $chatToken }
        $params = @{Uri=($base + $path); WebSession=$session; Method=$method; Headers=$headers; UseBasicParsing=$true}
        if ($payload) { $params['ContentType'] = 'application/json; charset=utf-8'; $params['Body'] = [Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json)) }
        Invoke-WebRequest @params
    }
    function NewChat($session, $csrf) { (ChatApi 'POST' '/api/chat/sessions' $session $csrf).Content | ConvertFrom-Json }
    function Say($chat, $session, $csrf, $text) { (ChatApi 'POST' "/api/chat/sessions/$($chat.id)/messages" $session $csrf $chat.token @{message=$text}).Content | ConvertFrom-Json }
    $guest = & $newSession
    $landing = Page '/' $guest
    Check ($landing.Content -match 'id="chat-widget"') 'chat widget rendered for guests'
    Check ((Page '/Tickets' $staffSession).Content -notmatch 'id="chat-widget"') 'chat widget hidden for staff'
    $guestCsrf = Token $landing
    Check ((StatusOf { ChatApi 'POST' '/api/chat/sessions' $guest 'invalid' }) -eq 400) 'chat API requires the CSRF token'
    $chat = NewChat $guest $guestCsrf
    Check ((StatusOf { ChatApi 'GET' "/api/chat/sessions/$($chat.id)" $guest $guestCsrf 'wrong-token' }) -eq 404) 'chat session refuses a wrong session token'
    $faq = Say $chat $guest $guestCsrf 'Làm thế nào để đổi mật khẩu tài khoản?'
    Check ((@($faq.messages | Where-Object sender -eq 'bot')[0].body) -match 'Đăng nhập và bảo vệ tài khoản') 'chatbot answers FAQ without sign-in'
    Check ((StatusOf { Say $chat $guest $guestCsrf ('x' * 1001) }) -eq 400) 'chat message over 1000 characters rejected'
    $ticketNumber = [regex]::Match((Page "/Tickets/Details/$ticketId" $one).Content, '<title>(CS-[0-9A-Z-]+)').Groups[1].Value
    $lookup = Say $chat $guest $guestCsrf "Trạng thái yêu cầu $ticketNumber"
    Check ($lookup.needsEmail -eq $true) 'ticket lookup asks a guest for an email first'
    # Guest emails must be confirmed through the emailed link before private data is used.
    function ConfirmChat($address, $session) {
        $pattern = '*' + ($address -replace '[^a-zA-Z0-9]', '_') + '*'
        $link = $null
        for ($i=0; $i -lt 30 -and !$link; $i++) {
            Start-Sleep -Milliseconds 200
            foreach ($file in @(Get-ChildItem $outbox -Filter $pattern -ErrorAction SilentlyContinue)) {
                $found = [regex]::Match([IO.File]::ReadAllText($file.FullName), '/Chat/Verify\?token=\S+').Value
                if ($found) { $link = $found }
            }
        }
        if (!$link) { throw "FAILED: no chat confirmation email for $address" }
        Page $link $session
    }
    $null = ChatApi 'PUT' "/api/chat/sessions/$($chat.id)/contact" $guest $guestCsrf $chat.token @{email='one@test.local'}
    $pending = Say $chat $guest $guestCsrf "Trạng thái yêu cầu $ticketNumber"
    $pendingReply = @($pending.messages | Where-Object sender -eq 'bot')[0].body
    Check ($pendingReply -match 'xác nhận' -and $pendingReply -notmatch 'Đã đóng' -and $pending.state.emailVerified -eq $false) 'unconfirmed email cannot read ticket status'
    Check (([Net.WebUtility]::HtmlDecode((Page '/Chat/Verify?token=forged' $guest).Content)) -match 'Liên kết không hợp lệ') 'forged confirmation link rejected'
    $confirmed = ConfirmChat 'one@test.local' $guest
    Check (([Net.WebUtility]::HtmlDecode($confirmed.Content)) -match 'Đã xác nhận email') 'confirmation link from the email verifies the chat'
    $owned = Say $chat $guest $guestCsrf "Trạng thái yêu cầu $ticketNumber"
    Check ((@($owned.messages | Where-Object sender -eq 'bot')[0].body) -match 'Đã đóng') 'owner email sees the ticket status'
    $stranger = & $newSession
    $strangerCsrf = Token (Page '/' $stranger)
    $strangerChat = NewChat $stranger $strangerCsrf
    $null = ChatApi 'PUT' "/api/chat/sessions/$($strangerChat.id)/contact" $stranger $strangerCsrf $strangerChat.token @{email='two@test.local'}
    $null = ConfirmChat 'two@test.local' $stranger
    $foreign = Say $strangerChat $stranger $strangerCsrf "Trạng thái yêu cầu $ticketNumber"
    $foreignReply = @($foreign.messages | Where-Object sender -eq 'bot')[0].body
    Check ($foreignReply -match 'Không tìm thấy' -and $foreignReply -notmatch 'HTTP test ticket') 'another email cannot read the ticket'
    Check ((StatusOf { ChatApi 'GET' "/api/chat/sessions/$($chat.id)" $stranger $strangerCsrf $strangerChat.token }) -eq 404) 'chat session cannot be opened with another session token'
    $handed = Say $strangerChat $stranger $strangerCsrf 'Cho tôi gặp nhân viên'
    Check ($handed.state.mode -eq 'human' -and $handed.state.ticketNumber -match '^CS-') 'chatbot hands the conversation to staff with a ticket'
    $silent = Say $strangerChat $stranger $strangerCsrf 'Tôi vẫn đang chờ'
    Check (@($silent.messages | Where-Object sender -eq 'bot').Count -eq 0) 'bot does not reply while staff handle the chat'
    Check ((StatusOf { Invoke-WebRequest -Uri ($base + '/api/chat/admin/sessions') -UseBasicParsing }) -eq 401) 'chat admin API rejects anonymous callers'
    Check ((StatusOf { Invoke-WebRequest -Uri ($base + '/api/chat/admin/sessions') -WebSession $one -UseBasicParsing }) -eq 403) 'chat admin API rejects customers'
    Check ((Page '/ChatAdmin' $one).BaseResponse.ResponseUri.AbsolutePath -eq '/Account/AccessDenied') 'customer cannot open the chat console'
    $consolePage = Page '/ChatAdmin?status=waiting_staff' $staffSession
    $sessionId = [regex]::Match($consolePage.Content, '/ChatAdmin/Details/(\d+)').Groups[1].Value
    Check ($sessionId.Length -gt 0) 'staff sees the waiting conversation in the chat console'
    $staffCsrf = Token $consolePage
    $null = ChatApi 'POST' "/api/chat/admin/sessions/$sessionId/replies" $staffSession $staffCsrf $null @{body='Chào bạn, nhân viên đã tiếp nhận.'; internal=$false}
    $null = ChatApi 'POST' "/api/chat/admin/sessions/$sessionId/replies" $staffSession $staffCsrf $null @{body='Ghi chú: khách VIP.'; internal=$true}
    $seen = (ChatApi 'GET' "/api/chat/sessions/$($strangerChat.id)?after=0" $stranger $strangerCsrf $strangerChat.token).Content | ConvertFrom-Json
    Check ((@($seen.messages | Where-Object sender -eq 'staff').body -join ' ') -match 'đã tiếp nhận' -and (($seen.messages.body -join ' ') -notmatch 'khách VIP')) 'customer sees public staff reply, never the internal note'
    $detail = (Invoke-WebRequest -Uri ($base + "/api/chat/admin/sessions/$sessionId") -WebSession $staffSession -UseBasicParsing).Content | ConvertFrom-Json
    Check (@($detail.messages | Where-Object isInternal).Count -eq 1 -and $detail.ticket.number -eq $handed.state.ticketNumber -and $null -eq $detail.logs) 'staff console shows notes and ticket; AI logs reserved for admins'
    $adminDetail = (Invoke-WebRequest -Uri ($base + "/api/chat/admin/sessions/$sessionId") -WebSession $admin -UseBasicParsing).Content | ConvertFrom-Json
    Check (@($adminDetail.logs).Count -ge 1 -and (@($adminDetail.logs).toolCalls -join ' ') -match 'handoff_to_human:ok') 'admin sees AI tool-call log'
    $chatTicketPage = Page "/Tickets/Details/$($detail.ticket.id)" $staffSession
    $chatTicketHtml = [Net.WebUtility]::HtmlDecode($chatTicketPage.Content)
    Check ($chatTicketHtml -match 'Tiếp nhận qua chatbot' -and $chatTicketHtml -match 'Đạt SLA phản hồi đầu') 'chat ticket visible in the normal ticket screen with its SLA state'
    $reprioritised = [Net.WebUtility]::HtmlDecode((PostForm '/Tickets/UpdatePriority' $staffSession @{id=$detail.ticket.id;priority='Cao'} $chatTicketPage).Content)
    Check ($reprioritised -match 'Ưu tiên: Cao' -and $reprioritised -match 'Đã cập nhật mức ưu tiên') 'staff changes ticket priority (SLA recomputed)'
    Check ((PostForm '/Tickets/UpdatePriority' $one @{id=$detail.ticket.id;priority='Thấp'} (Page '/Tickets' $one)).BaseResponse.ResponseUri.AbsolutePath -eq '/Account/AccessDenied') 'customer cannot change ticket priority'
    # Ticket assignment.
    $staffPick = [regex]::Match($reprioritised, '<option value="(\d+)"[^>]*>Test Staff').Groups[1].Value
    Check ($staffPick.Length -gt 0) 'assignee list offers active staff'
    $assigned = [Net.WebUtility]::HtmlDecode((PostForm '/Tickets/UpdateAssignee' $staffSession @{id=$detail.ticket.id;assigneeId=$staffPick} (Page "/Tickets/Details/$($detail.ticket.id)" $staffSession)).Content)
    Check ($assigned -match 'Phụ trách: Test Staff' -and $assigned -match 'Đã giao yêu cầu') 'staff assigns a ticket'
    Check (([Net.WebUtility]::HtmlDecode((Page '/Tickets?assignee=me' $staffSession).Content)) -match [regex]::Escape($handed.state.ticketNumber)) 'assigned ticket appears under "Giao cho tôi"'
    Check ((PostForm '/Tickets/UpdateAssignee' $one @{id=$detail.ticket.id;assigneeId=''} (Page '/Tickets' $one)).BaseResponse.ResponseUri.AbsolutePath -eq '/Account/AccessDenied') 'customer cannot assign tickets'
    $customerAsAssignee = [regex]::Match((Page "/Customers?search=one@test.local" $staffSession).Content, '/Customers/Details/(\d+)').Groups[1].Value
    $badAssign = [Net.WebUtility]::HtmlDecode((PostForm '/Tickets/UpdateAssignee' $staffSession @{id=$detail.ticket.id;assigneeId=$customerAsAssignee} (Page "/Tickets/Details/$($detail.ticket.id)" $staffSession)).Content)
    Check ($badAssign -match 'phải là nhân viên' -and $badAssign -match 'Phụ trách: Test Staff') 'a customer cannot be made the assignee'
    Check (([Net.WebUtility]::HtmlDecode((Page '/Admin/AuditLog?kind=ticket.assign' $admin).Content)) -match 'Test Staff') 'assignment recorded in the audit log'
    # Staff CRM assistant without an AI key answers honestly instead of inventing figures.
    $staffAssistant = Page '/Assistant' $staffSession
    Check (([Net.WebUtility]::HtmlDecode($staffAssistant.Content)) -match 'Trợ lý CRM') 'staff assistant page shows the CRM mode'
    $crmAnswer = (JsonPost '/Assistant/Ask' $staffSession @{message='Tổng quan yêu cầu 30 ngày qua'} (Token $staffAssistant)).Content | ConvertFrom-Json
    Check ($crmAnswer.mode -eq 'crm-error' -and $crmAnswer.reply -match 'Groq hoặc Gemini') 'CRM assistant without AI key reports the setup step, no fabricated statistics'
    $portalTicket = PostForm '/Tickets/Create' $two @{Subject='SLA portal ticket';Category='Other';FirstMessage='Need help with the SLA check please.'} (Page '/Tickets/Create' $two)
    $portalId = [regex]::Match($portalTicket.Content, 'name="TicketId" value="(\d+)"').Groups[1].Value
    Check (([Net.WebUtility]::HtmlDecode((Page "/Tickets/Details/$portalId" $staffSession).Content)) -match 'Ưu tiên: Trung bình' ) 'portal tickets get the default priority and an SLA deadline'
    $flood = NewChat $guest $guestCsrf
    $messageLimited = $false
    for ($i=0; $i -lt 14; $i++) { if ((StatusOf { Say $flood $guest $guestCsrf "Câu hỏi số $i" }) -eq 429) { $messageLimited=$true; break } }
    Check $messageLimited 'chat message rate limit (10 per minute per session) enforced'
    if ($CaptureBrowser) {
        $cookieFile = Join-Path $run 'browser-cookies.json'
        $cookieData = @($one.Cookies.GetCookies([Uri]$base) | ForEach-Object { @{Name=$_.Name;Value=$_.Value} })
        [IO.File]::WriteAllText($cookieFile, (ConvertTo-Json -InputObject $cookieData))
        $staffCookieFile = Join-Path $run 'browser-staff-cookies.json'
        $staffCookieData = @($staffSession.Cookies.GetCookies([Uri]$base) | ForEach-Object { @{Name=$_.Name;Value=$_.Value} })
        [IO.File]::WriteAllText($staffCookieFile, (ConvertTo-Json -InputObject $staffCookieData))
        $openTicket = [regex]::Match((Page '/Tickets?status=Open' $staffSession).Content, '/Tickets/Details/\d+').Value
        & dotnet run --project (Join-Path $PSScriptRoot 'BrowserChecks\BrowserChecks.csproj') --no-build -- $base $cookieFile $artifacts $staffCookieFile $openTicket
        if ($LASTEXITCODE -ne 0) { throw 'Browser checks failed' }
    }
    # Last, because it blocks new chat sessions from this IP for a minute (the browser pass needs one).
    $sessionLimited = $false
    for ($i=0; $i -lt 25; $i++) { if ((StatusOf { ChatApi 'POST' '/api/chat/sessions' $guest $guestCsrf }) -eq 429) { $sessionLimited=$true; break } }
    Check $sessionLimited 'chat session creation limit (20 per minute per IP) enforced'
    Write-Output 'All HTTP checks passed. Production database was not modified.'
} finally {
    if ($server -and !$server.HasExited) { Stop-Process -Id $server.Id -Force }
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}

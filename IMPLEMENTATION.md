# CustomerSupport

ASP.NET Core MVC (.NET 10) portal with SQLite persistence, customer/staff/admin authorization and a Vietnamese interface.

## Run locally

From PowerShell in this folder:

```powershell
.\Start-Web.ps1
```

Open http://localhost:5180. The script uses Development mode and localhost only. Existing accounts and tickets are preserved. If no administrator exists, the explicit local setup option creates one and writes its random initial credentials to `App_Data/local-admin.txt`. Sign in and change the password. The file and all local settings are excluded from Git. An existing administrator is never overwritten.

## Pages and roles

| Page | Purpose | Access |
| --- | --- | --- |
| Home | Public introduction and entry points | Everyone |
| Dashboard | Live ticket metrics, recent requests, category breakdown | Signed in; customer sees own data, staff sees queue |
| Tickets | Create, search, filter, read and reply | Customers own tickets; staff and admin can handle all |
| Assistant | Saved conversations, relevant sources, ticket status, human handoff | Signed in; private per user |
| Knowledge | Searchable help articles and detail pages | Public; admin can create/edit |
| Activity | Recent ticket messages | Same visibility as tickets |
| Account | Profile, role and password change | Signed in |
| Admin/Users | User directory and staff account creation | Admin |
| Settings | Encrypted Gemini credentials and live connection test | Admin |

## Connect real AI

1. Sign in as an administrator and open `/Settings`.
2. Create a key in [Google AI Studio](https://aistudio.google.com/apikey).
3. Enter the key and an Gemini model available to that key, then save.
4. Use **Kiểm tra kết nối thực tế**. It makes an actual provider request and reports success only when the model answers.
5. Open `/Assistant`, ask a question and confirm the answer badge shows **AI · Gemini**.

The implementation follows Google's [generateContent API](https://ai.google.dev/api/generate-content). It sends the key in `x-goog-api-key`; no key is exposed in URLs, HTML or application logs. The local database keeps conversation history. Stored keys use ASP.NET Core Data Protection. Gemini credentials use a separate settings file and protection purpose; OpenAI keys are never sent to Gemini. Keep its key ring available when moving the app; otherwise re-enter the provider key.

Environment variables `Gemini__ApiKey` and `GEMINI_API_KEY` are also supported, with `Gemini__Model` for the model. Administrative configuration takes precedence. Selecting disconnect explicitly disables the environment key too.

The provider receives the current question, up to six recent turns, at most eight summaries of that customer's own recent tickets and matching public articles. Full payment or account credentials must never be entered in chat. Prompt instructions require the assistant to avoid inventing business policies or claiming to change tickets or payments.

Without a key, or when the provider is unavailable, the application clearly identifies **knowledge lookup**. This mode retrieves published guidance and can show the customer's recent ticket status. It is not a generative AI response. Customer handoff explicitly creates a support ticket containing up to three recent exchanges; it does not auto-submit on the customer's behalf.

## Data upgrades and verification

Existing `Users`, `Tickets` and `TicketMessages` remain intact. Startup adds `KnowledgeArticles` and `AssistantTurns` tables and an index using idempotent additive SQL; seven initial articles explain actual system behavior rather than inventing commercial policies. Existing tables are not recreated.

```powershell
dotnet build .\CustomerSupportAI.csproj
dotnet run --project .\verification\ServiceChecks\ServiceChecks.csproj
.\verification\HttpChecks.ps1
dotnet build .\verification\BrowserChecks\BrowserChecks.csproj
.\verification\HttpChecks.ps1 -CaptureBrowser
```

Service checks simulate provider responses to verify parsing, history/knowledge context, encrypted key persistence, key removal, timeout and quota fallback. HTTP checks use a separate SQLite database and separate AI settings, including registration, ticket replies, access isolation, history, CSRF, handoff, content editing, staff creation, status changes, password changes and rate limiting. Browser checks use installed Microsoft Edge to verify the actual chat JavaScript and desktop/mobile layouts and save screenshots under the ignored `verification-artifacts` folder. Test provider responses do not establish that a real provider key works: the Settings test is required for that.

## Deployment limits

This session runs the site locally, not on a public domain. Production hosting should provide HTTPS, protected persistent database/storage, backups, email verification and recovery, and an appropriate data retention policy. Account password recovery by email is not implemented or advertised as available. Usage limits or billing are controlled by the AI provider.

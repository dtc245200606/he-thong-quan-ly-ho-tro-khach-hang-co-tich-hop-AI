// Website chatbot widget. Talks to /api/chat/sessions (ChatController); keeps the session id + token in
// sessionStorage so the conversation follows the visitor across pages of this tab. Builds DOM nodes only (no innerHTML).
(() => {
const root = document.querySelector('#chat-widget');
if (!root) return;
const api = root.dataset.api, max = Number(root.dataset.max || 1000);
const storeKey = 'cs-chat:' + root.dataset.owner, openKey = 'cs-chat-open';
const panel = root.querySelector('#cw-panel'), list = root.querySelector('#cw-messages'), form = root.querySelector('#cw-form');
const input = root.querySelector('#cw-input'), send = root.querySelector('#cw-send'), status = root.querySelector('#cw-status');
const emailForm = root.querySelector('#cw-email'), emailInput = root.querySelector('#cw-email-input');
const launcher = root.querySelector('.cw-launcher'), handoff = root.querySelector('[data-cw-handoff]');
const csrf = root.querySelector('#cw-csrf input')?.value || '';
const sprite = (root.querySelector('use')?.getAttribute('href') || '').split('#')[0];
let session = read(), lastId = 0, state = null, pollTimer = null, busy = false;

function read() { try { return JSON.parse(sessionStorage.getItem(storeKey) || 'null'); } catch { return null; } }
function save(value) { try { value ? sessionStorage.setItem(storeKey, JSON.stringify(value)) : sessionStorage.removeItem(storeKey); } catch {} }
function remember(open) { try { open ? sessionStorage.setItem(openKey, '1') : sessionStorage.removeItem(openKey); } catch {} }

function icon(name) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('class', 'icon'); svg.setAttribute('aria-hidden', 'true');
  const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
  use.setAttribute('href', `${sprite}#${name}`); svg.append(use); return svg;
}
function inline(parent, text) {
  for (const part of text.split(/(\*\*[^*]+\*\*)/g)) {
    if (!part) continue;
    if (part.startsWith('**') && part.endsWith('**') && part.length > 4) { const b = document.createElement('strong'); b.textContent = part.slice(2, -2); parent.append(b); }
    else parent.append(document.createTextNode(part));
  }
}
function markdown(target, text) {
  let listEl = null;
  for (const raw of text.replace(/\r/g, '').split('\n')) {
    const line = raw.trim(), item = line.match(/^(?:[-*•]|\d+[.)])\s+(.*)$/);
    if (item) {
      if (!listEl) { listEl = document.createElement('ul'); target.append(listEl); }
      const li = document.createElement('li'); inline(li, item[1]); listEl.append(li); continue;
    }
    listEl = null;
    if (!line) continue;
    const p = document.createElement('p'); inline(p, line.replace(/^#+\s*/, '')); target.append(p);
  }
}
const providerLabel = { groq: 'AI · Groq', gemini: 'AI · Gemini', mock: 'Trả lời tự động · chế độ thử nghiệm', 'mock-fallback': 'Chế độ dự phòng · không phải AI', error: 'Hệ thống' };

function render(message) {
  if (message.id && message.id <= lastId) return;
  if (message.id) lastId = message.id;
  if (message.sender === 'system') {
    const note = document.createElement('p'); note.className = 'cw-system'; note.textContent = message.body; list.append(note); return;
  }
  const row = document.createElement('div');
  if (message.sender === 'customer') {
    row.className = 'chat-message chat-user';
    const bubble = document.createElement('div'); bubble.className = 'chat-bubble'; bubble.textContent = message.body; row.append(bubble);
  } else {
    const staff = message.sender === 'staff';
    row.className = 'chat-message chat-assistant' + (staff ? ' cw-staff' : '');
    const avatar = document.createElement('span'); avatar.className = 'bot-avatar sm' + (staff ? ' cw-avatar-staff' : ''); avatar.append(icon(staff ? 'headset' : 'bot'));
    const bubble = document.createElement('div'); bubble.className = 'chat-bubble md';
    const label = document.createElement('span'); label.className = 'reply-mode';
    label.textContent = staff ? `Nhân viên · ${message.author || 'Hỗ trợ'}` : (providerLabel[message.provider] || 'Trợ lý');
    const text = document.createElement('div'); markdown(text, message.body || '');
    bubble.append(label, text); row.append(avatar, bubble);
  }
  if (message.time) row.title = message.time;
  list.append(row);
}
function notice(text) { render({ sender: 'system', body: text }); scroll(); }
function scroll() { list.scrollTop = list.scrollHeight; }

function applyState(next) {
  if (!next) return;
  state = next;
  const human = state.mode === 'human';
  status.textContent = human
    ? `Nhân viên hỗ trợ${state.ticketNumber ? ' · ' + state.ticketNumber : ''}`
    : state.email && !state.emailVerified
      ? `Trợ lý tự động · chờ xác nhận email ${state.email}`
      : `Trợ lý tự động${state.ticketNumber ? ' · ' + state.ticketNumber : ' · trả lời ngay'}`;
  handoff.hidden = human;
  if (state.email || state.signedIn) emailForm.hidden = true;
  schedulePoll();
}

async function call(method, path, body) {
  const headers = { 'RequestVerificationToken': csrf };
  if (session) headers['X-Chat-Token'] = session.token;
  if (body) headers['Content-Type'] = 'application/json';
  const controller = new AbortController(), timer = setTimeout(() => controller.abort(), 55000);
  try {
    const response = await fetch(api + path, { method, headers, body: body ? JSON.stringify(body) : undefined, signal: controller.signal, credentials: 'same-origin' });
    const data = await response.json().catch(() => ({}));
    if (response.status === 404 && session) { save(null); session = null; }
    if (response.status === 429) throw new Error('Bạn thao tác quá nhanh. Vui lòng chờ một phút rồi thử lại.');
    if (!response.ok) throw Object.assign(new Error(data.error || data.title || 'Không gửi được. Vui lòng thử lại.'), { status: response.status });
    return data;
  } catch (error) {
    if (error.name === 'AbortError') throw new Error('Phản hồi quá lâu. Vui lòng thử lại hoặc bấm "Gặp nhân viên".');
    throw error;
  } finally { clearTimeout(timer); }
}

async function ensureSession() {
  if (session) return;
  const data = await call('POST', '');
  session = { id: data.id, token: data.token }; save(session); lastId = 0; applyState(data.state);
}

async function refresh() {
  if (!session) return;
  try { const data = await call('GET', `/${session.id}?after=${lastId}`); applyState(data.state); (data.messages || []).forEach(render); if (data.messages?.length) scroll(); }
  catch (error) { if (!session) notice('Phiên trò chuyện cũ đã hết hạn. Bạn có thể bắt đầu cuộc trò chuyện mới.'); }
}
function schedulePoll() {
  clearTimeout(pollTimer);
  if (panel.hidden || !session) return;
  // Staff replies and the email confirmation arrive by polling: fast while waiting for either, slow otherwise.
  const waiting = state?.mode === 'human' || (state?.email && !state?.emailVerified);
  pollTimer = setTimeout(async () => { await refresh(); schedulePoll(); }, waiting ? 4000 : 20000);
}

function setBusy(value) {
  busy = value; send.disabled = value; input.disabled = value;
  value ? send.setAttribute('data-loading', '') : send.removeAttribute('data-loading');
}
function typing() {
  const row = document.createElement('div'); row.className = 'chat-message chat-assistant';
  const avatar = document.createElement('span'); avatar.className = 'bot-avatar sm'; avatar.append(icon('bot'));
  const bubble = document.createElement('div'); bubble.className = 'chat-bubble';
  const dots = document.createElement('span'); dots.className = 'typing'; dots.setAttribute('aria-label', 'Đang xử lý');
  dots.append(document.createElement('i'), document.createElement('i'), document.createElement('i'));
  bubble.append(dots); row.append(avatar, bubble); list.append(row); scroll(); return row;
}

async function run(action, optimistic) {
  if (busy) return;
  setBusy(true);
  let pending = null;
  try {
    await ensureSession();
    if (optimistic) render({ sender: 'customer', body: optimistic });
    if (state?.mode !== 'human') pending = typing();
    scroll();
    const data = await action();
    pending?.remove();
    // The server returns the customer's message too; drop the optimistic copy to avoid a duplicate.
    if (optimistic) list.querySelectorAll('.chat-user:not([title])').forEach(node => node.remove());
    applyState(data.state); (data.messages || []).forEach(render);
    if (data.needsEmail && !state?.signedIn) { emailForm.hidden = false; emailInput.focus(); }
    return true;
  } catch (error) {
    pending?.remove();
    if (optimistic) { list.querySelectorAll('.chat-user:not([title])').forEach(node => node.remove()); input.value = optimistic; }
    notice(error.message || 'Không thể kết nối. Vui lòng thử lại.');
    return false;
  } finally { setBusy(false); scroll(); if (!panel.hidden) input.focus(); }
}

form.addEventListener('submit', async event => {
  event.preventDefault();
  const message = input.value.trim();
  if (!message || busy) return;
  if (message.length > max) { notice(`Tin nhắn tối đa ${max} ký tự.`); return; }
  input.value = ''; autosize();
  await run(() => call('POST', `/${session.id}/messages`, { message }), message);
});
handoff.addEventListener('click', () => run(() => call('POST', `/${session.id}/handoff`)));
emailForm.addEventListener('submit', async event => {
  event.preventDefault();
  const email = emailInput.value.trim();
  if (!email) return;
  try {
    await ensureSession();
    await call('PUT', `/${session.id}/contact`, { email });
    emailForm.hidden = true;
    await refresh(); // shows the "confirmation link sent" notice from the server
    scroll();
  } catch (error) { notice(error.message); }
});

function toggle(open) {
  panel.hidden = !open; launcher.setAttribute('aria-expanded', String(open)); root.classList.toggle('is-open', open); remember(open);
  if (open) { refresh(); input.focus(); schedulePoll(); } else { clearTimeout(pollTimer); launcher.focus(); }
}
root.querySelectorAll('[data-cw-toggle]').forEach(button => button.addEventListener('click', () => toggle(panel.hidden)));
panel.addEventListener('keydown', event => { if (event.key === 'Escape') toggle(false); });
const autosize = () => { input.style.height = 'auto'; input.style.height = Math.min(input.scrollHeight, 120) + 'px'; };
input.addEventListener('input', autosize);
input.addEventListener('keydown', event => { if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) { event.preventDefault(); form.requestSubmit(); } });
try { if (sessionStorage.getItem(openKey)) toggle(true); } catch {}
})();

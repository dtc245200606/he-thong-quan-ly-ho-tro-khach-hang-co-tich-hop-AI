// Staff chat console (/ChatAdmin/Details/{id}): polls the admin API for new messages, sends public replies or
// internal notes. DOM nodes only (no innerHTML): message text comes from customers.
(() => {
const root = document.querySelector('#chat-console');
if (!root) return;
const api = root.dataset.api, list = root.querySelector('#cc-messages'), form = root.querySelector('#cc-form'), body = root.querySelector('#cc-body');
const facts = root.querySelector('#cc-facts'), logs = root.querySelector('#cc-logs'), ticketLink = root.querySelector('#cc-ticket');
const subtitle = document.querySelector('#cc-subtitle'), closeButton = root.querySelector('#cc-close');
const token = root.querySelector('#cc-csrf input').value;
let lastId = 0, timer = null;

const senders = { customer: 'Khách hàng', bot: 'Trợ lý AI', staff: 'Nhân viên', system: 'Hệ thống' };
function render(message) {
  lastId = Math.max(lastId, message.id);
  if (message.sender === 'system') {
    const note = document.createElement('p'); note.className = 'cw-system'; note.textContent = `${message.body} · ${message.time}`; list.append(note); return;
  }
  const row = document.createElement('div');
  row.className = 'chat-message ' + (message.sender === 'customer' ? 'chat-assistant' : 'chat-user') + (message.isInternal ? ' chat-note' : '');
  const bubble = document.createElement('div'); bubble.className = 'chat-bubble';
  const label = document.createElement('span'); label.className = 'reply-mode';
  label.textContent = (message.isInternal ? 'Ghi chú nội bộ · ' : '') + (message.author && message.sender === 'staff' ? message.author : senders[message.sender] || message.sender) + ' · ' + message.time;
  const text = document.createElement('div'); text.textContent = message.body;
  bubble.append(label, text); row.append(bubble); list.append(row);
}
function fact(term, value) {
  if (value === null || value === undefined || value === '') return;
  const dt = document.createElement('dt'); dt.textContent = term;
  const dd = document.createElement('dd'); dd.textContent = value;
  facts.append(dt, dd);
}
function show(data) {
  subtitle.textContent = `${data.statusText}${data.urgent ? ' · Khẩn cấp' : ''} · ${data.mode === 'human' ? 'nhân viên đang xử lý' : 'AI đang trả lời tự động'}`;
  facts.replaceChildren();
  fact('Email liên hệ', data.email ? `${data.email} · ${data.emailVerified ? 'đã xác nhận' : 'chưa xác nhận'}` : 'Chưa cung cấp');
  fact('Tài khoản', data.customer ? `${data.customer} (đã đăng nhập)` : 'Khách vãng lai');
  fact('Trạng thái', data.statusText);
  fact('Chế độ', data.mode === 'human' ? 'Nhân viên xử lý (AI không tự trả lời)' : 'AI tự động');
  fact('Lý do chuyển nhân viên', data.handoffReason);
  if (data.ticket) {
    fact('Yêu cầu liên quan', `${data.ticket.number} · ${data.ticket.status}${data.ticket.priority ? ' · Ưu tiên ' + data.ticket.priority : ''}`);
    fact('SLA', data.ticket.sla);
    ticketLink.href = data.ticket.url; ticketLink.hidden = false;
  }
  closeButton.hidden = data.status === 'closed';
  if (logs && data.logs) {
    logs.replaceChildren();
    for (const log of data.logs) {
      const li = document.createElement('li');
      li.textContent = `${log.time} · ${log.provider}${log.fallback ? ' → dự phòng' : ''} · ${log.success ? 'thành công' : 'thất bại'} · ${log.durationMs} ms · ẩn ${log.redacted} mục · ${log.rounds} vòng công cụ` +
        (log.toolCalls ? ` · ${log.toolCalls}` : '') + (log.error ? ` · lỗi: ${log.error}` : '');
      logs.append(li);
    }
    if (!data.logs.length) { const li = document.createElement('li'); li.textContent = 'Chưa có lượt AI nào.'; logs.append(li); }
  }
}
async function load() {
  clearTimeout(timer);
  try {
    const response = await fetch(`${api}?after=${lastId}`, { credentials: 'same-origin' });
    if (response.status === 401 || response.status === 403) { subtitle.textContent = 'Phiên đăng nhập đã hết hạn. Hãy tải lại trang.'; return; }
    const data = await response.json();
    const atBottom = list.scrollHeight - list.scrollTop - list.clientHeight < 40;
    data.messages.forEach(render); show(data);
    if (data.messages.length && (atBottom || lastId === data.messages.at(-1).id)) list.scrollTop = list.scrollHeight;
  } catch { subtitle.textContent = 'Mất kết nối, đang thử lại…'; }
  if (!document.hidden) timer = setTimeout(load, 4000);
}
async function post(path, payload) {
  const response = await fetch(api + path, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token }, body: JSON.stringify(payload) });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(data.error || 'Không thực hiện được. Vui lòng thử lại.');
}
form.addEventListener('submit', async event => {
  event.preventDefault();
  const text = body.value.trim(); if (!text) return;
  const buttons = form.querySelectorAll('button'); buttons.forEach(b => b.disabled = true);
  try { await post('/replies', { body: text, internal: event.submitter?.value === 'internal' }); body.value = ''; await load(); }
  catch (error) { alert(error.message); }
  finally { buttons.forEach(b => b.disabled = false); body.focus(); }
});
closeButton.addEventListener('click', async () => {
  if (!confirm('Kết thúc hội thoại này? Khách hàng sẽ nhận thông báo trong khung chat.')) return;
  try { await post('/status', { status: 'closed' }); await load(); } catch (error) { alert(error.message); }
});
document.addEventListener('visibilitychange', () => { if (!document.hidden) load(); });
load();
})();

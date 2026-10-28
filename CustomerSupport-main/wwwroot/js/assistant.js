(() => {
const form = document.querySelector('#assistant-form'), input = document.querySelector('#assistant-input'), messages = document.querySelector('#assistant-messages'), send = document.querySelector('#assistant-send');
if (!form) return;
const token = document.querySelector('#assistant-csrf input').value;
const sprite = (document.querySelector('use')?.getAttribute('href') || '').split('#')[0];

function icon(name) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('class', 'icon'); svg.setAttribute('aria-hidden', 'true');
  const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
  use.setAttribute('href', `${sprite}#${name}`); svg.append(use); return svg;
}

// Minimal Markdown (paragraphs, - / 1. lists, **bold**, `code`) built with DOM nodes only: no innerHTML.
function inline(parent, text) {
  for (const part of text.split(/(\*\*[^*]+\*\*|`[^`]+`)/g)) {
    if (!part) continue;
    if (part.startsWith('**') && part.endsWith('**') && part.length > 4) { const b = document.createElement('strong'); b.textContent = part.slice(2, -2); parent.append(b); }
    else if (part.startsWith('`') && part.endsWith('`') && part.length > 2) { const c = document.createElement('code'); c.textContent = part.slice(1, -1); parent.append(c); }
    else parent.append(document.createTextNode(part));
  }
}
function renderMarkdown(target, text) {
  target.replaceChildren();
  let list = null;
  for (const raw of text.replace(/\r/g, '').split('\n')) {
    const line = raw.trim();
    const bullet = line.match(/^[-*•]\s+(.*)$/), numbered = line.match(/^\d+[.)]\s+(.*)$/);
    if (bullet || numbered) {
      const tag = bullet ? 'UL' : 'OL';
      if (!list || list.tagName !== tag) { list = document.createElement(tag); target.append(list); }
      const li = document.createElement('li'); inline(li, (bullet || numbered)[1]); list.append(li);
      continue;
    }
    list = null;
    if (!line) continue;
    const p = document.createElement('p'); inline(p, line.replace(/^#+\s*/, '')); target.append(p);
  }
}

function copyButton(getText) {
  const tools = document.createElement('div'); tools.className = 'chat-tools';
  const button = document.createElement('button'); button.type = 'button';
  const label = document.createElement('span'); label.textContent = 'Sao chép';
  button.append(icon('copy'), label);
  button.addEventListener('click', async () => {
    try { await navigator.clipboard.writeText(getText()); label.textContent = 'Đã sao chép'; }
    catch { label.textContent = 'Không sao chép được'; }
    setTimeout(() => { label.textContent = 'Sao chép'; }, 1800);
  });
  tools.append(button); return tools;
}

// Server-rendered history: format replies and wire copy buttons.
messages.querySelectorAll('[data-md]').forEach(bubble => {
  const text = bubble.querySelector('.reply-text');
  if (!text) return;
  const source = text.textContent.trim();
  renderMarkdown(text, source); bubble.classList.add('md');
  bubble.parentElement.append(copyButton(() => source));
});

function appendUser(text) {
  const row = document.createElement('div'); row.className = 'chat-message chat-user';
  const bubble = document.createElement('div'); bubble.className = 'chat-bubble'; bubble.textContent = text;
  row.append(bubble); messages.append(row);
}
function appendAssistant() {
  const row = document.createElement('div'); row.className = 'chat-message chat-assistant';
  const avatar = document.createElement('span'); avatar.className = 'bot-avatar sm'; avatar.append(icon('bot'));
  const column = document.createElement('div');
  const bubble = document.createElement('div'); bubble.className = 'chat-bubble';
  const typing = document.createElement('span'); typing.className = 'typing'; typing.setAttribute('aria-label', 'Trợ lý đang trả lời');
  typing.append(document.createElement('i'), document.createElement('i'), document.createElement('i'));
  bubble.append(typing); column.append(bubble); row.append(avatar, column); messages.append(row);
  return { bubble, column };
}
const scrollDown = () => { messages.scrollTop = messages.scrollHeight; };
const autosize = () => { input.style.height = 'auto'; input.style.height = Math.min(input.scrollHeight, 200) + 'px'; };

form.addEventListener('submit', async e => {
  e.preventDefault(); const message = input.value.trim(); if (!message || send.disabled) return;
  appendUser(message); input.value = ''; autosize();
  send.disabled = true; input.disabled = true; send.setAttribute('data-loading', ''); form.setAttribute('aria-busy', 'true');
  const pending = appendAssistant(); scrollDown();
  const controller = new AbortController(); const timeout = setTimeout(() => controller.abort(), 55000);
  try {
    const response = await fetch(form.closest('.assistant-workspace').dataset.endpoint, {method:'POST',headers:{'Content-Type':'application/json','RequestVerificationToken':token},body:JSON.stringify({message}),signal:controller.signal});
    if (response.redirected || response.status === 401) throw new Error('Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.');
    if (response.status === 429) throw new Error('Bạn gửi câu hỏi quá nhanh. Vui lòng thử lại sau một phút.');
    const data = await response.json(); if (!response.ok) throw new Error(data.reply || 'Không gửi được câu hỏi.');
    const label = document.createElement('span'); label.className = 'reply-mode'; label.textContent = data.mode === 'ai' ? 'AI · Gemini' : 'Tra cứu kho hướng dẫn';
    const text = document.createElement('div'); text.className = 'reply-text'; renderMarkdown(text, data.reply || '');
    pending.bubble.classList.add('md'); pending.bubble.replaceChildren(label, text);
    pending.column.append(copyButton(() => data.reply || ''));
    document.querySelector('#assistant-mode').textContent = data.notice || (data.mode === 'ai' ? 'Đang trả lời bằng AI · Gemini' : 'Đang tra cứu kho hướng dẫn');
    const sources = document.querySelector('#assistant-sources'); sources.replaceChildren();
    for (const source of data.sources || []) { const link = document.createElement('a'); link.className = 'source-link'; link.href = source.url; link.append(icon('file-text'), document.createTextNode(source.title)); sources.append(link); }
    if (!sources.childElementCount) { const p = document.createElement('p'); p.className = 'muted'; p.textContent = data.mode === 'ai' ? 'Câu trả lời từ Gemini, không sử dụng bài hướng dẫn.' : 'Hiện chưa có hướng dẫn liên quan để trả lời thay thế.'; sources.append(p); }
  } catch (error) {
    pending.bubble.classList.add('is-error');
    pending.bubble.textContent = error.name === 'AbortError' ? 'Phản hồi quá lâu. Hãy thử lại sau hoặc chuyển nhân viên.' : error.message || 'Không thể kết nối. Vui lòng thử lại.';
    input.value = message;
  } finally {
    clearTimeout(timeout); send.disabled = false; input.disabled = false; send.removeAttribute('data-loading'); form.removeAttribute('aria-busy'); autosize(); input.focus(); scrollDown();
  }
});
input.addEventListener('input', autosize);
input.addEventListener('keydown', e => { if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); form.requestSubmit(); } });
document.querySelectorAll('[data-prompt]').forEach(button => button.addEventListener('click', () => { if (send.disabled) return; input.value = button.dataset.prompt; form.requestSubmit(); }));
scrollDown();
})();

// Ticket page: reply character count and the staff AI tools (classify, draft reply, summary).
// AI output is only ever a suggestion: it is shown for review, never saved or sent automatically.
(() => {
  const reply = document.getElementById('reply-body');
  const counter = document.querySelector('[data-char-count]');
  const fmt = n => n.toLocaleString('vi-VN');
  const syncCount = () => { if (reply && counter) counter.textContent = `${fmt(reply.value.length)} / ${fmt(4000)}`; };
  reply?.addEventListener('input', syncCount);
  syncCount();

  const card = document.querySelector('[data-ai-endpoint]');
  if (!card) return;
  const endpoint = card.dataset.aiEndpoint;
  const token = card.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
  const panels = Object.fromEntries([...document.querySelectorAll('[data-ai-panel]')].map(p => [p.dataset.aiPanel, p]));
  const busy = new Set();

  const el = (tag, cls, text) => {
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text != null) node.textContent = text;
    return node;
  };
  const setState = (panel, kind, message, retryTask) => {
    const state = panel.querySelector('[data-ai-state]');
    state.replaceChildren();
    state.hidden = false;
    if (kind === 'loading') {
      state.append(el('p', 'ai-loading', message));
      state.setAttribute('aria-busy', 'true');
      return;
    }
    state.removeAttribute('aria-busy');
    if (kind === 'error') {
      const box = el('div', 'ai-error');
      box.setAttribute('role', 'alert');
      box.append(el('p', null, message));
      if (retryTask) {
        const retry = el('button', 'btn btn-ghost btn-sm', 'Thử lại');
        retry.type = 'button';
        retry.addEventListener('click', () => run(retryTask));
        box.append(retry);
      }
      state.append(box);
      return;
    }
    state.hidden = true;
  };

  async function run(task) {
    const panel = panels[task];
    if (!panel || busy.has(task)) return;
    busy.add(task);
    const triggers = document.querySelectorAll(`[data-ai-run="${task}"]`);
    triggers.forEach(b => { b.disabled = true; b.setAttribute('aria-busy', 'true'); });
    panel.hidden = false;
    panel.querySelector('[data-ai-result]')?.setAttribute('hidden', '');
    if (task === 'draft') toggleDraft(false);
    setState(panel, 'loading', task === 'draft' ? 'AI đang soạn bản nháp…' : task === 'summary' ? 'AI đang đọc lịch sử trao đổi…' : 'AI đang phân tích yêu cầu…');

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 55000);
    try {
      const body = new FormData();
      body.append('task', task);
      body.append('__RequestVerificationToken', token);
      const response = await fetch(endpoint, { method: 'POST', body, signal: controller.signal, headers: { 'RequestVerificationToken': token } });
      const data = await response.json().catch(() => ({}));
      if (response.status === 429) throw new Error('Bạn đã dùng AI quá nhiều trong một phút. Vui lòng đợi rồi thử lại.');
      if (!response.ok) throw new Error(data.error || `Không gọi được AI (HTTP ${response.status}).`);
      setState(panel, 'done');
      render[task](panel, data);
    } catch (err) {
      setState(panel, 'error', err.name === 'AbortError' ? 'AI phản hồi quá lâu. Vui lòng thử lại.' : err.message || 'Đã xảy ra lỗi khi gọi AI.', task);
    } finally {
      clearTimeout(timer);
      busy.delete(task);
      triggers.forEach(b => { b.disabled = false; b.removeAttribute('aria-busy'); });
    }
  }

  const priorityBadge = { 'Thấp': 'badge-neutral', 'Trung bình': 'badge-info', 'Cao': 'badge-warning', 'Khẩn cấp': 'badge-danger' };
  const render = {
    classify(panel, { result }) {
      const box = panel.querySelector('[data-ai-result]');
      box.querySelector('[data-field="categoryText"]').textContent = result.categoryText;
      const badge = box.querySelector('[data-field="priority"]');
      badge.textContent = result.priority;
      badge.className = 'badge ' + (priorityBadge[result.priority] || 'badge-neutral');
      box.querySelector('[data-field="reason"]').textContent = result.reason;
      box.querySelector('[data-field-value="category"]').value = result.category;
      box.querySelector('[data-ai-apply]').hidden = result.same;
      box.querySelector('[data-ai-same]').hidden = !result.same;
      box.hidden = false;
    },
    summary(panel, { result, truncated, generatedAt }) {
      const box = panel.querySelector('[data-ai-result]');
      box.replaceChildren();
      const section = (title, items) => {
        if (!items || (Array.isArray(items) && items.length === 0)) return;
        const wrap = el('div', 'ai-summary-block');
        wrap.append(el('h4', null, title));
        if (Array.isArray(items)) {
          const ul = el('ul');
          items.forEach(i => ul.append(el('li', null, i)));
          wrap.append(ul);
        } else wrap.append(el('p', null, items));
        box.append(wrap);
      };
      section('Vấn đề', result.issue);
      section('Đã thử', result.tried);
      section('Phản hồi quan trọng', result.keyPoints);
      section('Hiện trạng', result.current);
      section('Việc cần làm tiếp', result.nextSteps);
      section('Thông tin còn thiếu', result.missing);
      box.append(el('p', 'ai-meta', `Tạo lúc ${generatedAt}${truncated ? ' · có thể chưa đầy đủ' : ''} · AI có thể sai, hãy đối chiếu với lịch sử.`));
      box.hidden = false;
      panel.querySelector('[data-ai-run="summary"]').textContent = 'Làm mới';
    },
    draft(panel, { result, truncated, generatedAt }) {
      const text = panel.querySelector('[data-ai-draft-text]');
      text.value = result.text;
      panel.querySelector('[data-ai-meta]').textContent = `Tạo lúc ${generatedAt}${truncated ? ' · bị cắt do giới hạn độ dài' : ''}`;
      const sources = panel.querySelector('[data-ai-sources]');
      sources.replaceChildren();
      if (result.sources?.length) {
        sources.append(el('span', null, 'Tham khảo: '));
        result.sources.forEach((s, i) => {
          if (i) sources.append(document.createTextNode(', '));
          const a = el('a', null, s.title);
          a.href = s.url; a.target = '_blank'; a.rel = 'noopener';
          sources.append(a);
        });
      } else sources.append(el('span', null, 'Không có bài hướng dẫn liên quan; kiểm tra kỹ mọi thông tin quy trình.'));
      toggleDraft(true);
      text.focus();
    }
  };

  // Draft panel: edit, copy, insert into the reply box, regenerate or discard.
  const draft = panels.draft;
  function toggleDraft(show) {
    if (!draft) return;
    draft.querySelectorAll('[data-ai-draft-text], [data-ai-sources], [data-ai-actions]').forEach(n => { n.hidden = !show; });
  }
  draft?.querySelector('[data-ai-dismiss]')?.addEventListener('click', () => { draft.hidden = true; });
  draft?.querySelector('[data-ai-copy]')?.addEventListener('click', async e => {
    const button = e.currentTarget;
    try {
      await navigator.clipboard.writeText(draft.querySelector('[data-ai-draft-text]').value);
      button.lastChild.textContent = 'Đã sao chép';
      setTimeout(() => { button.lastChild.textContent = 'Sao chép'; }, 2000);
    } catch { button.lastChild.textContent = 'Không sao chép được'; }
  });

  // Inserting AI text requires an explicit review confirmation before the reply can be sent.
  const review = document.querySelector('[data-ai-review]');
  const reviewBox = review?.querySelector('[data-ai-review-box]');
  draft?.querySelector('[data-ai-insert]')?.addEventListener('click', () => {
    if (!reply) return;
    const text = draft.querySelector('[data-ai-draft-text]').value.trim();
    if (!text) return;
    reply.value = reply.value.trim() ? `${reply.value.trim()}\n\n${text}` : text;
    reply.value = reply.value.slice(0, 4000);
    syncCount();
    draft.hidden = true;
    if (review) { review.hidden = false; reviewBox.checked = false; reviewBox.required = true; }
    reply.focus();
  });
  reply?.addEventListener('input', () => {
    if (review && !reply.value.trim()) { review.hidden = true; reviewBox.required = false; }
  });
  reviewBox?.addEventListener('invalid', () => reviewBox.setCustomValidity('Hãy xác nhận đã xem lại nội dung AI trước khi gửi.'));
  reviewBox?.addEventListener('change', () => reviewBox.setCustomValidity(''));

  document.querySelectorAll('[data-ai-run]').forEach(b => b.addEventListener('click', () => run(b.dataset.aiRun)));
})();

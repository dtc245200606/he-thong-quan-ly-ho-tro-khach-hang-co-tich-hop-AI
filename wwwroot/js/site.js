// Shared UI behaviour: sidebar, dropdowns, toasts, confirm dialog, submit state, chart tooltips.
// Everything degrades gracefully: without JS, links and forms still work.
(() => {
  const root = document.documentElement;
  const body = document.body;
  const store = {
    get: key => { try { return localStorage.getItem(key); } catch { return null; } },
    set: (key, value) => { try { localStorage.setItem(key, value); } catch { /* storage unavailable */ } }
  };

  // Sidebar: collapse on desktop, off-canvas drawer below 1024px.
  const collapse = document.querySelector('[data-sidebar-collapse]');
  const syncCollapse = () => {
    if (!collapse) return;
    const collapsed = root.dataset.sidebar === 'collapsed';
    collapse.setAttribute('aria-expanded', String(!collapsed));
    collapse.setAttribute('aria-label', collapsed ? 'Mở rộng thanh điều hướng' : 'Thu gọn thanh điều hướng');
    const use = collapse.querySelector('use');
    if (use) use.setAttribute('href', use.getAttribute('href').replace(/#.*$/, collapsed ? '#panel-left-open' : '#panel-left-close'));
  };
  collapse?.addEventListener('click', () => {
    const collapsed = root.dataset.sidebar !== 'collapsed';
    if (collapsed) root.dataset.sidebar = 'collapsed'; else delete root.dataset.sidebar;
    store.set('cs-sidebar', collapsed ? 'collapsed' : 'expanded');
    syncCollapse();
  });
  syncCollapse();

  const openButton = document.querySelector('[data-nav-open]');
  const sidebar = document.getElementById('app-sidebar');
  const setNav = open => {
    body.classList.toggle('nav-open', open);
    openButton?.setAttribute('aria-expanded', String(open));
    if (open) sidebar?.querySelector('a, button')?.focus();
    else if (document.activeElement && sidebar?.contains(document.activeElement)) openButton?.focus();
  };
  openButton?.addEventListener('click', () => setNav(true));
  document.querySelectorAll('[data-nav-close]').forEach(el => el.addEventListener('click', () => setNav(false)));

  // Dropdowns built on <details>: one open at a time, close on outside click or Escape.
  const dropdowns = [...document.querySelectorAll('[data-dropdown]')];
  dropdowns.forEach(d => d.addEventListener('toggle', () => {
    if (d.open) dropdowns.filter(other => other !== d).forEach(other => { other.open = false; });
  }));
  document.addEventListener('click', e => dropdowns.forEach(d => { if (d.open && !d.contains(e.target)) d.open = false; }));
  document.addEventListener('keydown', e => {
    if (e.key !== 'Escape') return;
    const open = dropdowns.find(d => d.open);
    if (open) { open.open = false; open.querySelector('summary')?.focus(); }
    if (body.classList.contains('nav-open')) setNav(false);
  });

  // Toasts: success messages leave after 5s; errors stay until closed.
  const dismiss = toast => {
    toast.classList.add('is-leaving');
    setTimeout(() => toast.remove(), 220);
  };
  document.querySelectorAll('.toast').forEach(toast => {
    toast.querySelector('[data-toast-close]')?.addEventListener('click', () => dismiss(toast));
    if (toast.hasAttribute('data-autodismiss')) setTimeout(() => dismiss(toast), 5000);
  });

  // Confirm dialog for destructive forms: <form data-confirm="message" data-confirm-title="..." data-confirm-label="...">.
  const dialog = document.getElementById('confirm-dialog');
  document.addEventListener('submit', e => {
    const form = e.target;
    if (!(form instanceof HTMLFormElement) || !form.dataset.confirm || form.dataset.confirmed) return;
    if (!dialog || typeof dialog.showModal !== 'function') {
      if (!window.confirm(form.dataset.confirm)) e.preventDefault();
      return;
    }
    e.preventDefault();
    dialog.querySelector('#confirm-title').textContent = form.dataset.confirmTitle || 'Xác nhận thao tác';
    dialog.querySelector('#confirm-message').textContent = form.dataset.confirm;
    const accept = dialog.querySelector('#confirm-accept');
    accept.textContent = form.dataset.confirmLabel || 'Xác nhận';
    const danger = form.dataset.confirmTone !== 'warning';
    accept.className = 'btn ' + (danger ? 'btn-danger' : 'btn-primary');
    dialog.querySelector('[data-confirm-icon]').classList.toggle('tone-warning', !danger);
    dialog.returnValue = '';
    dialog.showModal();
    dialog.addEventListener('close', () => {
      if (dialog.returnValue !== 'confirm') return;
      form.dataset.confirmed = 'true';
      form.requestSubmit(e.submitter && form.contains(e.submitter) ? e.submitter : undefined);
    }, { once: true });
  }, true);

  // Submit state: show a spinner on the pressed button and block double submits (POST forms only).
  document.addEventListener('submit', e => {
    const form = e.target;
    if (e.defaultPrevented || !(form instanceof HTMLFormElement) || form.method.toLowerCase() !== 'post' || form.id === 'assistant-form') return;
    const button = e.submitter || form.querySelector('[type="submit"]');
    if (!button || !button.classList.contains('btn')) return;
    // Disable after the browser has captured the submitter's value.
    setTimeout(() => { button.setAttribute('data-loading', ''); button.setAttribute('aria-busy', 'true'); button.disabled = true; }, 0);
  });
  // Restore buttons when the page comes back from the bfcache.
  window.addEventListener('pageshow', ev => {
    if (!ev.persisted) return;
    document.querySelectorAll('[data-loading]').forEach(b => { b.removeAttribute('data-loading'); b.removeAttribute('aria-busy'); b.disabled = false; });
  });

  // Chart tooltips: <button class="chart-col" data-label data-value> inside .chart-wrap.
  document.querySelectorAll('.chart-wrap').forEach(wrap => {
    const tip = document.createElement('div');
    tip.className = 'chart-tooltip';
    tip.hidden = true;
    wrap.append(tip);
    const show = col => {
      const box = col.getBoundingClientRect();
      const host = wrap.getBoundingClientRect();
      tip.replaceChildren();
      const value = document.createElement('strong');
      value.textContent = col.dataset.value;
      tip.append(value, document.createTextNode(' · ' + col.dataset.label));
      tip.style.left = Math.min(Math.max(box.left - host.left + box.width / 2, 60), host.width - 60) + 'px';
      const bar = col.querySelector('.chart-bar').getBoundingClientRect();
      tip.style.top = (Math.min(bar.top, box.bottom - 4) - host.top) + 'px';
      tip.hidden = false;
    };
    wrap.querySelectorAll('.chart-col').forEach(col => {
      col.addEventListener('mouseenter', () => show(col));
      col.addEventListener('focus', () => show(col));
      col.addEventListener('mouseleave', () => { tip.hidden = true; });
      col.addEventListener('blur', () => { tip.hidden = true; });
    });
  });
})();

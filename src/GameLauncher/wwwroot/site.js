"use strict";
// Keep the viewport and controller focus when changing library filters or sort.
// Normal navigation still starts at the top; this state is consumed only once.
(() => {
  const key = 'library-navigation';
  const selector = '.library-toolbar .tab:not(.tab-add), .library-sort .sort-chip';
  const links = Array.from(document.querySelectorAll(selector));
  let saved = null;
  try {
    const raw = sessionStorage.getItem(key);
    sessionStorage.removeItem(key);
    saved = raw ? JSON.parse(raw) : null;
  } catch { /* Storage being unavailable must not block normal navigation. */ }
  if (saved && saved.url === window.location.href &&
      Number.isFinite(saved.y) && Number.isFinite(saved.x) &&
      Date.now() - saved.time >= 0 && Date.now() - saved.time < 30000) {
    const link = links.find((item) => item.href === saved.url);
    if (link) {
      if (saved.directional) document.body.classList.add('directional-navigation');
      link.focus({ preventScroll: true });
      // The deferred bundle runs after markup is parsed, before the first paint.
      // Shorter result sets naturally clamp to the new document's scroll range.
      window.scrollTo({ left: saved.x, top: saved.y, behavior: 'instant' });
    }
  }
  links.forEach((link) => {
    link.addEventListener('click', (event) => {
      if (event.defaultPrevented || event.button !== 0 || event.ctrlKey ||
          event.metaKey || event.shiftKey || event.altKey || link.target || link.hasAttribute('download')) return;
      try {
        sessionStorage.setItem(key, JSON.stringify({
          url: link.href,
          x: window.scrollX,
          y: window.scrollY,
          time: Date.now(),
          directional: document.body.classList.contains('directional-navigation')
        }));
      } catch { /* Fall back to the ordinary GET link. */ }
    });
  });
})();
// Window-only actions are allowlisted by the native host; preferences use POST forms.
document.querySelector('[data-window-fullscreen]')?.addEventListener('click', () => {
  window.chrome?.webview?.postMessage('window.fullscreen');
});
document.querySelector('[data-window-close]')?.addEventListener('click', () => {
  window.chrome?.webview?.postMessage('window.close');
});
window.chrome?.webview?.addEventListener('message', (event) => {
  if (event.data?.type !== 'window') return;
  const button = document.querySelector('[data-window-fullscreen]');
  if (!button) return;
  const label = event.data.fullscreen ? 'Exit fullscreen (F11)' : 'Enter fullscreen (F11)';
  button.setAttribute('aria-label', label);
  button.setAttribute('title', label);
  button.setAttribute('aria-pressed', String(event.data.fullscreen));
});
document.querySelectorAll("[data-toast]").forEach((toast) => {
  window.setTimeout(() => toast.remove(), 3000);
});
// Destructive forms confirm here; the CSP blocks inline onsubmit handlers.
document.querySelectorAll("form[data-confirm]").forEach((form) => {
  form.addEventListener("submit", (event) => {
    if (!window.confirm(form.dataset.confirm)) event.preventDefault();
  });
});
// Standard POST forms own every mutation and antiforgery token. No fetch is needed.
document.querySelectorAll("[data-scan-form]").forEach((form) => {
  const root = form.querySelector('input[name="Root"]');
  const picks = Array.from(form.querySelectorAll("[data-scan-folder]"));
  const normalizeFolder = (value) => value.trim().replace(/\//g, "\\").replace(/\\+$/, "").toLowerCase();
  const updatePicks = () => {
    picks.forEach((button) => button.setAttribute("aria-pressed",
      String(normalizeFolder(button.dataset.scanFolder) === normalizeFolder(root.value))));
  };
  picks.forEach((button) => {
    button.addEventListener("click", () => {
      if (form.dataset.submitting === "true") return;
      root.value = button.dataset.scanFolder;
      root.dispatchEvent(new Event("input", { bubbles: true }));
      root.dispatchEvent(new Event("change", { bubbles: true }));
      form.querySelector("[data-folder-selection-status]").textContent = `Selected ${root.value}. Ready to scan.`;
    });
  });
  root.addEventListener("input", updatePicks);
  root.addEventListener("change", updatePicks);
  updatePicks();
  form.addEventListener("submit", (event) => {
    if (form.dataset.submitting === "true") { event.preventDefault(); return; }
    const browsing = event.submitter?.hasAttribute("data-browse");
    form.dataset.submitting = "true";
    form.setAttribute("aria-busy", "true");
    if (!browsing) {
      form.querySelector(".scan-progress").hidden = false;
      form.querySelector("[data-scan-button]").textContent = "Scanning…";
    } else {
      event.submitter.textContent = "Choosing folder…";
    }
    // Defer disabling until the browser has captured the submitter's form action.
    window.setTimeout(() => form.querySelectorAll("button").forEach((button) => { button.disabled = true; }), 0);
  });
});
document.querySelectorAll("[data-busy-form]").forEach((form) => {
  form.addEventListener("submit", (event) => {
    if (form.dataset.submitting === "true") { event.preventDefault(); return; }
    form.dataset.submitting = "true";
    form.setAttribute("aria-busy", "true");
    const button = event.submitter || form.querySelector('button[type="submit"]');
    if (button) {
      button.textContent = form.dataset.busyLabel || "Working…";
      window.setTimeout(() => { button.disabled = true; }, 0);
    }
  });
});
document.querySelectorAll("[data-import-form]").forEach((form) => {
  const boxes = Array.from(form.querySelectorAll('input[name="SelectedIds"]'));
  const all = form.querySelector("[data-select-all]");
  const count = form.querySelector("[data-selection-count]");
  const update = () => {
    const selected = boxes.filter((box) => box.checked).length;
    count.textContent = `${selected} selected`;
    all.checked = boxes.length > 0 && selected === boxes.length;
    all.indeterminate = selected > 0 && selected < boxes.length;
  };
  all.addEventListener("change", () => { boxes.forEach((box) => { box.checked = all.checked; }); update(); });
  boxes.forEach((box) => box.addEventListener("change", update));
  update();
});
// Directional keyboard and standard-mapped controller navigation share actions.
(() => {
  let keyboardActive = false;
  let keyboardTarget = null;
  // Declared here: the keyboard message handler below resets it, and the gamepad
  // poll further down may never run when the browser exposes no gamepad API.
  let armed = false;
  const textField = (el) => el instanceof HTMLElement && !el.readOnly && !el.disabled &&
    el.matches('textarea, input[type="text"], input[type="search"], input[type="email"], input[type="password"], input[type="url"], input[type="tel"], input:not([type])');
  const keyboardNotice = (message) => {
    document.getElementById('keyboard-notice')?.remove();
    if (!message) return;
    const notice = document.createElement('div');
    notice.id = 'keyboard-notice';
    notice.className = 'notice toast';
    notice.setAttribute('role', 'status');
    notice.textContent = message;
    document.body.append(notice);
    window.setTimeout(() => notice.remove(), 8000);
  };
  const requestKeyboard = () => {
    if (!textField(document.activeElement) || keyboardActive) return;
    if (!window.chrome?.webview) { keyboardNotice('The Windows keyboard is available only inside the desktop launcher.'); return; }
    keyboardTarget = document.activeElement;
    keyboardActive = true;
    window.chrome.webview.postMessage('keyboard.show');
  };
  window.chrome?.webview?.addEventListener('message', (event) => {
    const data = event.data;
    if (data?.type !== 'keyboard') return;
    keyboardActive = data.state === 'requested' || data.state === 'shown';
    armed = false;
    if (data.message) keyboardNotice(data.message);
    if (data.state === 'hidden' && keyboardTarget?.isConnected) {
      keyboardTarget.focus({ preventScroll: true });
      keyboardTarget = null;
    }
  });
  const selector = 'a[href], button, input:not([type="hidden"]), select, textarea, summary';
  const visible = (el) => {
    const style = getComputedStyle(el);
    return !el.disabled && el.tabIndex >= 0 && !el.closest('[inert]') &&
      el.getClientRects().length > 0 && style.visibility !== 'hidden' && style.display !== 'none';
  };
  const focus = (el) => {
    if (!el) return;
    document.body.classList.add('directional-navigation');
    el.focus({ preventScroll: true });
    // Navigation focuses the centered Play button, but the entire card must be visible.
    const scrollTarget = el.closest('.entry-card') || el;
    scrollTarget.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
  };
  let contentFocus = null;
  const toggleSidebar = () => {
    if (document.activeElement.closest('.sidebar')) {
      focus(contentFocus?.isConnected && visible(contentFocus) ? contentFocus : initial());
    } else {
      contentFocus = document.activeElement.matches(selector) ? document.activeElement : null;
      closeMenus();
      focus(document.querySelector('.sidebar .nav-link.active') || document.querySelector('.sidebar .nav-link'));
    }
  };
  // "Add to…" is an inline disclosure inside the card menu; its items are hidden until expanded.
  const setSubmenu = (trigger, open) => {
    trigger.setAttribute('aria-expanded', String(open));
    const list = document.getElementById(trigger.getAttribute('aria-controls'));
    if (list) list.hidden = !open;
  };
  document.querySelectorAll('.submenu-trigger').forEach((trigger) => {
    trigger.addEventListener('click', () => {
      const open = trigger.getAttribute('aria-expanded') !== 'true';
      setSubmenu(trigger, open);
      if (open && document.body.classList.contains('directional-navigation')) {
        focus(document.getElementById(trigger.getAttribute('aria-controls'))?.querySelector('.submenu-item'));
      }
    });
  });
  const closeMenus = () => {
    document.querySelectorAll('.overlay-menu.is-open').forEach((menu) => {
      menu.classList.remove('is-open');
      menu.querySelector('.menu-trigger').setAttribute('aria-expanded', 'false');
    });
    document.querySelectorAll('.submenu-trigger[aria-expanded="true"]').forEach((trigger) => setSubmenu(trigger, false));
  };
  const openMenu = () => {
    const menu = document.activeElement.closest('.entry-card')?.querySelector('.overlay-menu');
    if (!menu) return;
    closeMenus();
    menu.classList.add('is-open');
    menu.querySelector('.menu-trigger').setAttribute('aria-expanded', 'true');
    focus(menu.querySelector('.menu-item'));
  };
  document.querySelectorAll('.menu-trigger').forEach((button) => {
    button.addEventListener('click', () => {
      const wasOpen = button.closest('.overlay-menu').classList.contains('is-open');
      closeMenus();
      if (!wasOpen) openMenu();
    });
  });
  document.addEventListener('focusin', (event) => {
    const menu = document.querySelector('.overlay-menu.is-open');
    if (menu && !menu.contains(event.target)) closeMenus();
  });
  document.addEventListener('pointerdown', (event) => {
    document.body.classList.remove('directional-navigation');
    if (!event.target.closest('.overlay-menu')) closeMenus();
  });
  const candidates = () => Array.from(document.querySelectorAll(selector)).filter((el) => {
    if (!visible(el) || el.classList.contains('skip-link')) return false;
    const card = el.closest('.entry-card');
    // One directional stop per game. Tab still reaches every card control.
    return !card || el.matches('.overlay-play button');
  });
  const initial = () => document.querySelector('.entry-card .overlay-play button') ||
    candidates().find((el) => el.closest('main')) || candidates()[0];
  const jumpToEdge = (bottom) => {
    closeMenus();
    const controls = candidates().filter((el) => el.closest('main'));
    const target = bottom ? controls[controls.length - 1] : controls[0];
    if (target) {
      document.body.classList.add('directional-navigation');
      // Move focus without the usual nearest-card scroll fighting the page jump.
      target.focus({ preventScroll: true });
      contentFocus = target;
    }
    window.scrollTo({
      left: window.scrollX,
      top: bottom ? document.documentElement.scrollHeight : 0,
      behavior: 'instant'
    });
  };
  const move = (direction) => {
    const active = document.activeElement;
    const menu = active.closest('.overlay-menu.is-open');
    if (menu) {
      // Collapsed submenu items are [hidden], so visible() keeps up/down to the expanded list.
      const items = Array.from(menu.querySelectorAll('.menu-item, .submenu-item')).filter(visible);
      const index = items.indexOf(active);
      const trigger = active.closest('.menu-submenu')?.querySelector('.submenu-trigger');
      if (direction === 'up' || direction === 'down') {
        focus(items[(index + (direction === 'down' ? 1 : items.length - 1)) % items.length]);
      } else if (direction === 'right' && active.matches('.submenu-trigger')) {
        setSubmenu(active, true);
        focus(document.getElementById(active.getAttribute('aria-controls'))?.querySelector('.submenu-item'));
      } else if (direction === 'left' && trigger && active !== trigger) {
        setSubmenu(trigger, false);
        focus(trigger);
      } else if (direction === 'left' && active.matches('.submenu-trigger[aria-expanded="true"]')) {
        setSubmenu(active, false);
      } else if (direction !== 'right') {
        closeMenus();
        focus(menu.closest('.entry-card').querySelector('.overlay-play button'));
      }
      return;
    }
    if (!active.matches(selector) || !visible(active)) { focus(initial()); return; }
    const bounds = (el) => (el.closest('.entry-card') || el).getBoundingClientRect();
    const from = bounds(active);
    const horizontal = direction === 'left' || direction === 'right';
    const sign = direction === 'left' || direction === 'up' ? -1 : 1;
    let best = null;
    let bestScore = Infinity;
    for (const el of candidates()) {
      if (el === active || (active.closest('.entry-card') && el.closest('.entry-card') === active.closest('.entry-card'))) continue;
      const to = bounds(el);
      const dx = (to.left + to.right - from.left - from.right) / 2;
      const dy = (to.top + to.bottom - from.top - from.bottom) / 2;
      const forward = (horizontal ? dx : dy) * sign;
      if (forward <= 1) continue;
      const gap = horizontal ? Math.max(0, from.top - to.bottom, to.top - from.bottom)
        : Math.max(0, from.left - to.right, to.left - from.right);
      const score = forward + Math.abs(horizontal ? dy : dx) * 2 + gap * 5;
      if (score < bestScore) { best = el; bestScore = score; }
    }
    focus(best);
  };
  const activate = () => {
    const active = document.activeElement;
    if (!active.matches(selector) || !visible(active)) { focus(initial()); return; }
    if (textField(active)) { requestKeyboard(); return; }
    if (active.matches('input:not([type="checkbox"]):not([type="radio"]):not([type="button"]):not([type="submit"]), select')) return;
    active.click();
  };
  const back = () => {
    const menu = document.querySelector('.overlay-menu.is-open');
    const trigger = document.activeElement.closest('.menu-submenu')?.querySelector('.submenu-trigger[aria-expanded="true"]');
    if (menu && trigger) {
      // Back steps out of the submenu first, then out of the menu.
      setSubmenu(trigger, false);
      focus(trigger);
    } else if (menu) {
      closeMenus();
      focus(menu.closest('.entry-card').querySelector('.overlay-play button'));
    } else {
      // Follow a safe GET link, never replay history containing a POST.
      document.querySelector('a.back-link')?.click();
    }
  };
  document.addEventListener('keydown', (event) => {
    if (event.defaultPrevented || event.isComposing) return;
    if (event.key === 'F11' && !event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey) {
      event.preventDefault();
      if (!event.repeat) window.chrome?.webview?.postMessage('window.fullscreen');
      return;
    }
    if (event.key === 'F2' && !event.altKey && !event.ctrlKey && !event.metaKey) {
      event.preventDefault();
      if (!event.repeat) {
        if (keyboardActive) window.chrome?.webview?.postMessage('keyboard.hide');
        else requestKeyboard();
      }
      return;
    }
    if (keyboardActive) return;
    if (event.key === 'F1' && !event.altKey && !event.ctrlKey && !event.metaKey) {
      event.preventDefault();
      if (!event.repeat) toggleSidebar();
      return;
    }
    if (event.key === 'ContextMenu' || (event.shiftKey && event.key === 'F10')) {
      if (document.activeElement.closest('.entry-card')) { event.preventDefault(); openMenu(); }
      return;
    }
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    // Leave cursor movement, selects and numeric/range inputs to the browser.
    if (event.target.matches('input, textarea, select') || event.target.isContentEditable) return;
    const direction = { ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'up', ArrowDown: 'down' }[event.key];
    if (direction) { event.preventDefault(); move(direction); }
    else if (event.key === 'Escape') { event.preventDefault(); if (!event.repeat) back(); }
    else if (event.key === 'Enter' && event.repeat) event.preventDefault();
    // Enter/Space activation and Tab traversal otherwise remain native.
  });

  if (!navigator.getGamepads) return;
  let previous = new Set();
  let heldDirection = null;
  let nextMove = 0;
  let padIndex = null;
  const poll = (now) => {
    window.requestAnimationFrame(poll);
    if (keyboardActive || document.hidden || !document.hasFocus()) { armed = false; previous.clear(); heldDirection = null; return; }
    let pad;
    try { pad = Array.from(navigator.getGamepads()).find((p) => p?.connected && p.mapping === 'standard'); }
    catch { return; }
    if (!pad) { armed = false; padIndex = null; return; }
    if (padIndex !== pad.index) { armed = false; padIndex = pad.index; }
    const pressed = new Set(pad.buttons.map((button, i) => button.pressed ? i : -1).filter((i) => i >= 0));
    const x = pad.axes[0] || 0;
    const y = pad.axes[1] || 0;
    let direction = pressed.has(12) ? 'up' : pressed.has(13) ? 'down' : pressed.has(14) ? 'left' : pressed.has(15) ? 'right' : null;
    if (!direction && Math.max(Math.abs(x), Math.abs(y)) > 0.55)
      direction = Math.abs(x) > Math.abs(y) ? (x < 0 ? 'left' : 'right') : (y < 0 ? 'up' : 'down');
    // Require release after page loads, focus changes or reconnects: held A must not launch twice.
    if (!armed) {
      armed = pressed.size === 0 && !direction;
      previous = pressed;
      heldDirection = null;
      return;
    }
    // Standard-mapped shoulders: LB = top, RB = bottom, once per press.
    const jumpTop = pressed.has(4) && !previous.has(4);
    const jumpBottom = pressed.has(5) && !previous.has(5);
    if (jumpTop || jumpBottom) {
      jumpToEdge(!jumpTop);
      heldDirection = direction;
      nextMove = now + 350;
      // Do not also activate a newly focused control on the same frame.
      previous = pressed;
      return;
    }
    if (direction && (direction !== heldDirection || now >= nextMove)) {
      move(direction);
      nextMove = now + (direction === heldDirection ? 150 : 350);
    }
    heldDirection = direction;
    if (pressed.has(8) && !previous.has(8)) toggleSidebar();
    else if (pressed.has(0) && !previous.has(0)) activate();
    else if (pressed.has(1) && !previous.has(1)) back();
    else if (pressed.has(2) && !previous.has(2)) openMenu();
    else if (pressed.has(3) && !previous.has(3)) document.activeElement.closest('.entry-card')?.querySelector('.favorite')?.click();
    previous = pressed;
  };
  window.addEventListener('blur', () => { armed = false; });
  document.addEventListener('visibilitychange', () => { armed = false; });
  window.requestAnimationFrame(poll);
})();

// Back/forward-cache restores must not leave old forms permanently busy.
window.addEventListener("pageshow", (event) => { if (event.persisted) window.location.reload(); });

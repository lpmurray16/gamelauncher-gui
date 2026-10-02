"use strict";
document.querySelectorAll("[data-toast]").forEach((toast) => {
  window.setTimeout(() => toast.remove(), 3000);
});
// Standard POST forms own every mutation and antiforgery token. No fetch is needed.
document.querySelectorAll("[data-scan-form]").forEach((form) => {
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
    el.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
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
  const closeMenus = () => {
    document.querySelectorAll('.overlay-menu.is-open').forEach((menu) => {
      menu.classList.remove('is-open');
      menu.querySelector('.menu-trigger').setAttribute('aria-expanded', 'false');
    });
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
  const move = (direction) => {
    const active = document.activeElement;
    const menu = active.closest('.overlay-menu.is-open');
    if (menu) {
      const items = Array.from(menu.querySelectorAll('.menu-item')).filter(visible);
      const index = items.indexOf(active);
      if (direction === 'up' || direction === 'down') {
        focus(items[(index + (direction === 'down' ? 1 : items.length - 1)) % items.length]);
      } else {
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
    if (active.matches('input:not([type="checkbox"]):not([type="radio"]):not([type="button"]):not([type="submit"]), textarea, select')) return;
    active.click();
  };
  const back = () => {
    const menu = document.querySelector('.overlay-menu.is-open');
    if (menu) {
      closeMenus();
      focus(menu.closest('.entry-card').querySelector('.overlay-play button'));
    } else {
      // Follow a safe GET link, never replay history containing a POST.
      document.querySelector('a.back-link')?.click();
    }
  };
  document.addEventListener('keydown', (event) => {
    if (event.defaultPrevented || event.isComposing) return;
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
  let armed = false;
  let padIndex = null;
  const poll = (now) => {
    window.requestAnimationFrame(poll);
    if (document.hidden || !document.hasFocus()) { armed = false; previous.clear(); heldDirection = null; return; }
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

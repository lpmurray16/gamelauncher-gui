"use strict";
// Decorative placeholders never replace controls or claim measurable progress.
function createSkeletonLoader(layout = 'rows') {
  const loader = document.createElement('div');
  loader.className = `skeleton-loader skeleton-${layout}`;
  loader.setAttribute('aria-hidden', 'true');
  const count = layout === 'cards' ? 4 : layout === 'rows' ? 3 : 1;
  for (let index = 0; index < count; index++) {
    const item = document.createElement('div');
    item.className = 'skeleton-item';
    loader.append(item);
  }
  return loader;
}
function initializeImageLoaders(root = document) {
  root.querySelectorAll('img[src^="/artwork/"]').forEach((image) => {
    if (image.dataset.loaderReady) return;
    image.dataset.loaderReady = 'true';
    const settle = () => {
      image.classList.remove('image-loading');
      image.removeEventListener('load', settle);
      image.removeEventListener('error', settle);
    };
    image.addEventListener('load', settle);
    image.addEventListener('error', settle);
    if (image.complete) settle();
    else image.classList.add('image-loading');
  });
}
initializeImageLoaders();
// Theme preview uses CSSOM properties (allowed by our CSP), not inline CSS text.
(() => {
  const form = document.querySelector('[data-theme-editor]');
  if (!form) return;
  const root = document.documentElement;
  const colors = Array.from(form.querySelectorAll('[data-theme-value]'));
  const radii = Array.from(form.querySelectorAll('[data-theme-radius]'));
  const status = form.querySelector('[data-theme-status]');
  const warning = form.querySelector('[data-theme-contrast]');
  const validColor = (value) => /^#[0-9a-f]{6}$/i.test(value);
  const channels = (hex) => [1, 3, 5].map((index) => parseInt(hex.slice(index, index + 2), 16));
  const luminance = (hex) => channels(hex).reduce((total, channel, index) => {
    const value = channel / 255;
    const linear = value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4;
    return total + linear * [.2126, .7152, .0722][index];
  }, 0);
  const contrast = (left, right) => {
    const a = luminance(left), b = luminance(right);
    return (Math.max(a, b) + .05) / (Math.min(a, b) + .05);
  };
  const mix = (from, to, amount) => {
    const target = channels(to);
    return '#' + channels(from).map((value, index) => Math.round(value * (1 - amount) + target[index] * amount).toString(16).padStart(2, '0')).join('');
  };
  const update = (preview = true) => {
    const palette = {};
    let valid = true;
    colors.forEach((input) => {
      const value = input.value.toLowerCase();
      const okay = validColor(value);
      input.setCustomValidity(okay ? '' : 'Enter a six-digit hex color, such as #ff8a47.');
      if (!okay) { valid = false; return; }
      palette[input.dataset.themeValue] = value;
      form.querySelector(`[data-theme-picker="${input.dataset.themeValue}"]`).value = value;
    });
    radii.forEach((input) => {
      form.querySelector(`[data-theme-radius-output="${input.dataset.themeRadius}"]`).textContent = `${input.value} px`;
    });
    if (!valid) {
      warning.hidden = true;
      status.textContent = 'Complete each hex color to preview or save. Your saved theme is unchanged.';
      return;
    }
    const onAccent = luminance(palette.Accent) > .179 ? '#16110e' : '#ffffff';
    if (preview) {
      colors.forEach((input) => root.style.setProperty(input.dataset.themeVariable, palette[input.dataset.themeValue]));
      radii.forEach((input) => root.style.setProperty(input.dataset.themeRadius, `${input.value}px`));
      // Keep derived palette rules identical to ThemePreferences.GetCss.
      root.style.setProperty('--accent-hover', palette.Accent === '#ff8a47' ? '#ffa46e' : mix(palette.Accent, '#ffffff', .2));
      root.style.setProperty('--accent-soft', palette.Accent === '#ff8a47' && palette.Background === '#101113' ? '#302219' : mix(palette.Background, palette.Accent, .16));
      root.style.setProperty('--on-accent', onAccent);
      root.style.setProperty('color-scheme', luminance(palette.Background) > .179 ? 'light' : 'dark');
    }
    const lowContrast = [palette.Background, palette.Surface, palette.Raised, palette.Header]
      .some((background) => contrast(palette.Text, background) < 4.5 || contrast(palette.Muted, background) < 4.5) ||
      contrast(onAccent, palette.Accent) < 4.5;
    warning.hidden = !lowContrast;
    warning.textContent = lowContrast ? 'Some text/background combinations have low contrast. Consider adjusting them for readability. You can still save this theme.' : '';
    const changed = [...colors, ...radii].some((input) => input.value.toLowerCase() !== input.dataset.saved.toLowerCase());
    status.textContent = changed ? 'Unsaved appearance changes. Select Save theme to keep them.' : 'No unsaved appearance changes.';
  };
  colors.forEach((input) => {
    input.addEventListener('input', () => update());
    form.querySelector(`[data-theme-picker="${input.dataset.themeValue}"]`).addEventListener('input', (event) => {
      input.value = event.target.value;
      update();
    });
  });
  radii.forEach((input) => input.addEventListener('input', () => update()));
  const restore = (source) => {
    [...colors, ...radii].forEach((input) => { input.value = input.dataset[source]; });
    update();
  };
  form.querySelector('[data-theme-discard]').addEventListener('click', () => restore('saved'));
  form.querySelector('[data-theme-defaults]').addEventListener('click', () => {
    restore('default');
    status.textContent = 'Default appearance previewed. Select Save theme to keep it, or Discard changes to return.';
  });
  // Restore attempted values after a failed save only when Appearance is visible.
  update(!form.closest('[data-settings-panel]').hidden);
})();
// Bundle ordering is local to the editor until its normal antiforgery Save POST.
(() => {
  const list = document.querySelector('[data-launch-order]');
  if (!list) return;
  const group = list.closest('.launch-group');
  const boxes = Array.from(group.querySelectorAll('input[name="CompanionIds"]'));
  const status = group.querySelector('[data-launch-order-status]');
  let dragged = null;
  const rows = () => Array.from(list.children);
  const update = (message = '') => {
    const items = rows();
    items.forEach((row, index) => {
      row.querySelector('[data-step-id]').name = `LaunchSteps[${index}].EntryId`;
      row.querySelector('[data-step-delay]').name = `LaunchSteps[${index}].DelaySeconds`;
      // Keep boundary buttons focusable so focus survives a move to either end.
      row.querySelector('[data-step-move="up"]').setAttribute('aria-disabled', String(index === 0));
      row.querySelector('[data-step-move="down"]').setAttribute('aria-disabled', String(index === items.length - 1));
    });
    const total = items.reduce((sum, row) => sum + Number(row.querySelector('[data-step-delay]').value || 0), 0);
    items.forEach((row) => row.querySelector('[data-step-delay]').setCustomValidity(total > 10 ? 'Keep total bundle delays at 10 seconds or less.' : ''));
    status.textContent = `${message}${message ? ' ' : ''}Total configured delay: ${total} seconds (maximum 10).`;
  };
  boxes.forEach((box) => box.addEventListener('change', () => {
    const existing = rows().find((row) => row.dataset.launchStep === box.value);
    if (!box.checked) existing?.remove();
    else if (!existing) {
      if (boxes.filter((item) => item.checked).length > 8) {
        box.checked = false;
        update('Choose no more than 8 companion entries.');
        return;
      }
      const template = Array.from(group.querySelectorAll('template[data-launch-template]')).find((item) => item.dataset.launchTemplate === box.value);
      if (template) {
        const main = rows().find((row) => row.dataset.launchStep === '00000000-0000-0000-0000-000000000000');
        list.insertBefore(template.content.cloneNode(true), main || null);
      }
    }
    update('Launch order updated. Save the entry to apply it.');
  }));
  list.addEventListener('input', () => update());
  list.addEventListener('click', (event) => {
    const button = event.target.closest('button');
    const row = button?.closest('[data-launch-step]');
    if (!row) return;
    if (button.hasAttribute('data-step-remove')) {
      const box = boxes.find((item) => item.value === row.dataset.launchStep);
      const next = row.nextElementSibling || row.previousElementSibling;
      if (box) box.checked = false;
      row.remove();
      (next?.querySelector('[data-step-move]') || box)?.focus();
      update('Companion removed from this bundle.');
      return;
    }
    const direction = button.dataset.stepMove;
    if (!direction || button.getAttribute('aria-disabled') === 'true') return;
    if (direction === 'up' && row.previousElementSibling) list.insertBefore(row, row.previousElementSibling);
    if (direction === 'down' && row.nextElementSibling) list.insertBefore(row.nextElementSibling, row);
    button.focus({ preventScroll: true });
    row.scrollIntoView({ block: 'nearest' });
    update(`Moved to position ${rows().indexOf(row) + 1}.`);
  });
  list.addEventListener('dragstart', (event) => {
    const handle = event.target.closest('.launch-drag');
    if (!handle) { event.preventDefault(); return; }
    dragged = handle.closest('[data-launch-step]');
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', dragged.dataset.launchStep);
    dragged.classList.add('is-dragging');
  });
  list.addEventListener('dragover', (event) => {
    if (!dragged) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
  });
  list.addEventListener('drop', (event) => {
    if (!dragged) return;
    event.preventDefault();
    const target = event.target.closest('[data-launch-step]');
    if (target && target !== dragged) {
      const rect = target.getBoundingClientRect();
      list.insertBefore(dragged, event.clientY < rect.top + rect.height / 2 ? target : target.nextSibling);
    } else if (!target) list.append(dragged);
    update(`Moved to position ${rows().indexOf(dragged) + 1}.`);
  });
  list.addEventListener('dragend', () => {
    dragged?.classList.remove('is-dragging');
    dragged = null;
  });
  update();
})();
// Settings navigation swaps existing panels so unsaved fields survive section changes.
(() => {
  const nav = document.querySelector('[data-settings-nav]');
  if (!nav) return;
  const links = Array.from(nav.querySelectorAll('[data-settings-tab]'));
  const panels = Array.from(document.querySelectorAll('[data-settings-panel]'));
  const select = (id) => {
    if (!links.some((link) => link.dataset.settingsTab === id)) return;
    panels.forEach((panel) => { panel.hidden = panel.dataset.settingsPanel !== id; });
    links.forEach((link) => {
      if (link.dataset.settingsTab === id) link.setAttribute('aria-current', 'page');
      else link.removeAttribute('aria-current');
    });
  };
  links.forEach((link) => link.addEventListener('click', (event) => {
    if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    select(link.dataset.settingsTab);
    // Drop any previous POST-handler query; each form keeps its own explicit section.
    window.history.replaceState(null, '', link.href);
  }));
})();
// Artwork POSTs replace only their region, never the user's unsaved entry fields.
(() => {
  let pending = false;
  document.addEventListener('submit', async (event) => {
    const form = event.target;
    const region = form.closest('#entry-artwork');
    if (!region) {
      if (pending && form.matches('form.editor')) {
        event.preventDefault();
        event.stopImmediatePropagation();
      }
      return;
    }
    event.preventDefault();
    event.stopImmediatePropagation();
    if (pending) return;
    pending = true;
    const body = new FormData(form);
    const buttons = Array.from(region.querySelectorAll('button'));
    const editorButtons = Array.from(document.querySelectorAll('form.editor button[type="submit"]'));
    const lockedButtons = [...buttons, ...editorButtons].filter((button) => !button.disabled);
    const focusIndex = buttons.indexOf(event.submitter);
    const collapsed = Array.from(region.querySelectorAll('details')).map((item) => !item.open);
    const scrollY = window.scrollY;
    const message = document.createElement('p');
    message.className = 'notice';
    message.setAttribute('role', 'status');
    message.textContent = form.dataset.busyLabel || 'Updating artwork…';
    region.prepend(message);
    const layout = form.matches('.match-row') ? 'cards' :
      form.matches('.option-card') ? (form.dataset.kind === 'Hero' ? 'banner' : 'cover') : 'rows';
    const loader = createSkeletonLoader(layout);
    message.after(loader);
    region.setAttribute('aria-busy', 'true');
    lockedButtons.forEach((button) => { button.disabled = true; });
    try {
      const response = await fetch(form.action, {
        method: 'POST', body, credentials: 'same-origin',
        headers: { 'X-Entry-Artwork': 'true' }
      });
      if (!response.ok) throw new Error(`Artwork request failed (${response.status}).`);
      const documentFragment = new DOMParser().parseFromString(await response.text(), 'text/html');
      const updated = documentFragment.querySelector('#entry-artwork');
      if (!updated) throw new Error('The artwork response could not be displayed.');
      // Keep the live option nodes (and their loaded thumbnails) after an enhanced download.
      // Ordinary POST responses still render their own complete provider results.
      const providerResults = form.matches('.option-card') ? region.querySelector('[data-artwork-provider]') : null;
      if (providerResults) updated.querySelector('[data-artwork-provider]')?.replaceWith(providerResults);
      region.replaceWith(updated);
      initializeImageLoaders(updated);
      updated.querySelectorAll('details').forEach((item, index) => {
        if (collapsed[index] !== undefined) item.open = !collapsed[index];
      });
      const sameAction = Array.from(updated.querySelectorAll('form')).find((item) => item.action === form.action);
      // Preserved download buttons are still locked until finally; unlock before restoring focus.
      lockedButtons.forEach((button) => { button.disabled = false; });
      const focus = providerResults?.contains(event.submitter) ? event.submitter :
        sameAction ? updated.querySelectorAll('button')[focusIndex] :
          updated.querySelector('input:not([type="hidden"]), .match-row button, .artwork-section summary');
      (focus || updated).focus({ preventScroll: true });
      window.scrollTo({ top: scrollY, behavior: 'instant' });
    } catch (error) {
      message.setAttribute('role', 'alert');
      message.textContent = `${error.message} Your entry edits are still here. If applying artwork, check the preview before retrying; the change may already have been saved.`;
    } finally {
      pending = false;
      loader.remove();
      region.removeAttribute('aria-busy');
      lockedButtons.forEach((button) => { button.disabled = false; });
    }
  }, true);
})();
// QR images are generated locally in the authenticated POST response; no code enters a URL or storage.
(() => {
  const pairing = document.querySelector('[data-companion-pairing]');
  if (!pairing) return;
  const live = pairing.querySelector('[data-pairing-live]');
  const status = pairing.querySelector('[data-pairing-status]');
  const network = pairing.querySelector('[data-pairing-network]');
  const images = Array.from(pairing.querySelectorAll('[data-pairing-host]'));
  const expires = Number(pairing.dataset.expires);
  let timer;
  const refresh = () => {
    window.clearTimeout(timer);
    const remaining = expires - Date.now();
    if (!Number.isFinite(remaining) || remaining <= 0) {
      live.hidden = true;
      live.replaceChildren();
      status.textContent = 'Pairing code expired. Generate a fresh QR / code above.';
      return;
    }
    live.hidden = false;
    images.forEach((image) => { image.hidden = image.dataset.pairingHost !== network?.value; });
    status.textContent = 'Available until the expiry above unless already used or replaced. Generate a fresh code for another phone.';
    timer = window.setTimeout(refresh, Math.min(remaining, 1000));
  };
  network?.addEventListener('change', refresh);
  document.addEventListener('visibilitychange', refresh);
  window.addEventListener('pageshow', refresh);
  window.addEventListener('pagehide', () => { live.hidden = true; window.clearTimeout(timer); });
  refresh();
})();
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
document.querySelector('[data-window-close]')?.addEventListener('click', () => {
  window.chrome?.webview?.postMessage('window.close');
});
// Publish the sticky header's live height so scrolled-to content never lands beneath it.
(() => {
  const topbar = document.querySelector('.app-topbar');
  if (!topbar || !window.ResizeObserver) return;
  new ResizeObserver(() => {
    document.documentElement.style.setProperty('--topbar-height', `${topbar.offsetHeight}px`);
  }).observe(topbar);
})();
document.querySelectorAll("[data-toast]").forEach((toast) => {
  window.setTimeout(() => toast.remove(), 3000);
});
// Shared themed confirmation. Preserve the original submitter and antiforgery POST.
(() => {
  const dialog = document.querySelector('[data-confirm-dialog]');
  if (!dialog) return;
  let pending = null;
  let approved = null;
  const dismiss = () => {
    const target = pending?.focus;
    pending = null;
    dialog.close();
    if (target?.isConnected) target.focus({ preventScroll: true });
  };
  dialog.querySelector('[data-confirm-dismiss]').addEventListener('click', dismiss);
  dialog.addEventListener('cancel', (event) => { event.preventDefault(); dismiss(); });
  dialog.querySelector('[data-confirm-accept]').addEventListener('click', () => {
    const request = pending;
    dismiss();
    if (!request || !request.form.isConnected || request.submitter?.disabled) return;
    approved = request.form;
    try {
      if (request.submitter) request.form.requestSubmit(request.submitter);
      else request.form.requestSubmit();
    } finally { approved = null; }
  });
  document.querySelectorAll('form[data-confirm]').forEach((form) => {
    form.addEventListener('submit', (event) => {
      if (approved === form) return;
      event.preventDefault();
      // Do not let busy handlers disable a form before the user confirms.
      event.stopImmediatePropagation();
      if (dialog.open) return;
      pending = { form, submitter: event.submitter,
        focus: form.closest('.overlay-menu')?.querySelector('.menu-trigger') || document.activeElement };
      const shutdown = !!form.querySelector('[data-power-start]');
      dialog.querySelector('#confirmation-title').textContent = form.dataset.confirmTitle || (shutdown ? 'Shut down PC?' : 'Confirm action');
      dialog.querySelector('[data-confirm-message]').textContent = form.dataset.confirm;
      dialog.querySelector('[data-confirm-accept]').textContent = shutdown ? 'Start 15-second countdown' : 'Confirm';
      dialog.showModal();
      dialog.querySelector('[data-confirm-dismiss]').focus({ preventScroll: true });
    });
  });
})();
// Read-only power status; all desktop mutations remain antiforgery-protected POST forms.
(() => {
  const banner = document.querySelector('[data-power-banner]');
  if (!banner) return;
  // These controls are fixed for the lifetime of the page (Settings only hides panels).
  const bannerText = banner.querySelector('[data-power-banner-text]');
  const messages = document.querySelectorAll('[data-power-message]');
  const countdowns = document.querySelectorAll('[data-power-countdown]');
  const startButtons = document.querySelectorAll('[data-power-start], [data-power-local]');
  const cancelButtons = document.querySelectorAll('[data-power-cancel]');
  const set = (el, property, value) => {
    if (el[property] !== value) el[property] = value;
  };
  const poll = async () => {
    try {
      const response = await fetch('/Power?handler=Status', { cache: 'no-store', signal: AbortSignal.timeout(4000) });
      if (!response.ok) throw new Error('Power status unavailable');
      const state = await response.json();
      const countdown = state.pending ? `Shutdown in approximately ${state.remainingSeconds} seconds.` : '';
      set(banner, 'hidden', !state.pending);
      set(bannerText, 'textContent', countdown + ' Save your work. Apps will not be forced closed.');
      messages.forEach((el) => set(el, 'textContent', state.message));
      countdowns.forEach((el) => set(el, 'textContent', countdown));
      startButtons.forEach((el) => set(el, 'disabled', state.pending || state.dispatching));
      cancelButtons.forEach((el) => set(el, 'disabled', !state.pending));
    } catch {
      // A disconnected window is not evidence that the machine powered off or cancellation succeeded.
      messages.forEach((el) => set(el, 'textContent',
        'Power status unavailable. A previous countdown may still be active; cancellation is not confirmed.'));
      if (!banner.hidden) set(bannerText, 'textContent', 'Shutdown status unavailable. The countdown may still be active.');
      startButtons.forEach((el) => set(el, 'disabled', true));
    } finally { window.setTimeout(poll, 1000); }
  };
  poll();
})();
// Standard POST forms own scanning mutations and antiforgery tokens.
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
      const progress = form.querySelector(".scan-progress");
      progress.hidden = false;
      progress.after(createSkeletonLoader());
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

// Browse path buttons on Edit page
document.querySelectorAll(".browse-path").forEach((button) => {
  button.addEventListener("click", async (event) => {
    event.preventDefault();
    const kind = button.dataset.browseKind;
    if (!kind) return;
    const input = button.closest(".input-action").querySelector("input");
    const originalText = button.innerHTML;
    button.innerHTML = '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" class="spinner"><circle cx="12" cy="12" r="10" stroke-opacity="0.25" /><path d="M12 2a10 10 0 0 1 10 10" stroke-opacity="1"><animateTransform attributeName="transform" type="rotate" from="0 12 12" to="360 12 12" dur="1s" repeatCount="indefinite" /></path></svg>';
    button.disabled = true;
    try {
      const response = await fetch("/Edit?handler=BrowsePath", {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded", "RequestVerificationToken": document.querySelector('input[name="__RequestVerificationToken"]')?.value || "" },
        body: new URLSearchParams({ kind }),
        credentials: "same-origin"
      });
      if (!response.ok) throw new Error(`Browse request failed (${response.status}).`);
      const data = await response.json();
      if (data.path) {
        input.value = data.path;
        input.dispatchEvent(new Event("input", { bubbles: true }));
        input.dispatchEvent(new Event("change", { bubbles: true }));
      } else if (data.error) {
        alert(data.error);
      }
    } catch (error) {
      alert(error.message);
    } finally {
      button.innerHTML = originalText;
      button.disabled = false;
    }
  });
});

// Directional keyboard and standard-mapped controller navigation share actions.
(() => {
  let keyboardActive = false;
  let keyboardTarget = null;
  // Declared here: the keyboard message handler below resets it, and the gamepad
  // poll further down may never run when the browser exposes no gamepad API.
  let armed = false;
  let selectStartedAt = null;
  let selectHeld = false;
  const resetSelect = () => { selectStartedAt = null; selectHeld = false; };
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
    resetSelect();
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
  const toggleNavigation = () => {
    if (keyboardActive || document.querySelector('dialog[open]')) return;
    if (document.activeElement.closest('[data-primary-nav]')) {
      focus(contentFocus?.isConnected && visible(contentFocus) ? contentFocus : initial());
    } else {
      contentFocus = document.activeElement.matches(selector) ? document.activeElement : null;
      closeMenus();
      focus(document.querySelector('[data-primary-nav] .nav-link.active') || document.querySelector('[data-primary-nav] .nav-link'));
    }
  };
  const focusGlobalSearch = () => {
    if (keyboardActive || document.querySelector('dialog[open]')) return;
    const search = document.getElementById('global-search');
    if (!search || !visible(search)) return;
    closeMenus();
    focus(search); // Keep existing text; A/F2 opens the Windows keyboard deliberately.
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
  const openMenu = (card = document.activeElement.closest('.entry-card'), touch = false) => {
    const menu = card?.matches('.overlay-menu') ? card : card?.querySelector('.overlay-menu');
    if (!menu) return;
    closeMenus();
    menu.classList.add('is-open');
    menu.querySelector('.menu-trigger').setAttribute('aria-expanded', 'true');
    if (touch) menu.querySelector('.menu-item')?.focus({ preventScroll: true });
    else focus(menu.querySelector('.menu-item:not(:disabled)') || menu.querySelector('.menu-trigger'));
  };
  document.querySelectorAll('.menu-trigger').forEach((button) => {
    button.addEventListener('click', () => {
      const wasOpen = button.closest('.overlay-menu').classList.contains('is-open');
      closeMenus();
      if (!wasOpen) openMenu(button.closest('.overlay-menu'));
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
  // Touch-only gestures; mouse, keyboard and gamepad continue through their existing paths.
  (() => {
    const surface = document.querySelector('[data-library-touch]');
    if (!surface || !window.PointerEvent) return;
    surface.classList.add('touch-library');
    const touches = new Set();
    const interactive = 'a, button, input, select, textarea, summary, [contenteditable], .overlay-menu';
    let gesture = null;
    let holdTimer = null;
    let suppressed = null;
    const clearHold = () => { window.clearTimeout(holdTimer); holdTimer = null; };
    const cancel = () => { clearHold(); gesture = null; surface.classList.remove('touch-contact'); };
    const blocked = () => keyboardActive || !!document.querySelector('dialog[open], .overlay-menu.is-open');
    const suppress = (g) => { suppressed = { id: g.id, until: touches.has(g.id) ? Infinity : performance.now() + 1200 }; };
    document.addEventListener('pointerdown', (event) => {
      if (event.pointerType !== 'touch') return;
      touches.add(event.pointerId);
      if (touches.size !== 1) { cancel(); return; }
      // A fresh touch is a new intentional tap, not the previous gesture's release click.
      suppressed = null;
      if (!event.isPrimary || blocked() || !surface.contains(event.target) || event.target.closest(interactive)) return;
      surface.classList.add('touch-contact');
      const g = gesture = { id: event.pointerId, x: event.clientX, y: event.clientY,
        card: event.target.closest('.entry-card'), moved: false, held: false, horizontal: false };
      if (g.card) holdTimer = window.setTimeout(() => {
        if (gesture !== g || touches.size !== 1 || blocked() || g.moved) return;
        g.held = true;
        suppress(g);
        openMenu(g.card, true);
      }, 550);
    }, true);
    document.addEventListener('pointermove', (event) => {
      const g = gesture;
      if (!g || event.pointerId !== g.id) return;
      const dx = event.clientX - g.x;
      const dy = event.clientY - g.y;
      if (Math.hypot(dx, dy) > 12) { g.moved = true; clearHold(); }
      if (g.held) return;
      if (!g.horizontal && Math.abs(dy) > 12 && Math.abs(dy) >= Math.abs(dx)) { cancel(); return; }
      if (Math.abs(dx) > 20 && Math.abs(dx) > Math.abs(dy) * 1.5) g.horizontal = true;
      if (g.horizontal) {
        suppress(g);
        if (event.cancelable) event.preventDefault();
      }
    }, { passive: false });
    document.addEventListener('pointerup', (event) => {
      touches.delete(event.pointerId);
      if (suppressed?.id === event.pointerId) suppressed.until = performance.now() + 1200;
      const g = gesture;
      if (!g || event.pointerId !== g.id) return;
      cancel();
      if (g.held || g.moved) suppress(g);
      if (g.held) { if (event.cancelable) event.preventDefault(); return; }
      if (blocked()) return;
      const dx = event.clientX - g.x;
      const dy = event.clientY - g.y;
      if (!g.horizontal || Math.abs(dx) < 70 || Math.abs(dx) < Math.abs(dy) * 1.5) return;
      const tabs = Array.from(document.querySelectorAll('.library-toolbar .tab:not(.tab-add)'));
      const index = tabs.findIndex((tab) => tab.classList.contains('selected'));
      const next = index + (dx < 0 ? 1 : -1);
      if (index < 0 || next < 0 || next >= tabs.length) return;
      // Reuse real filter links and their viewport-preservation handler; never reconstruct URLs.
      tabs[next].click();
    });
    document.addEventListener('pointercancel', (event) => {
      touches.delete(event.pointerId);
      if (suppressed?.id === event.pointerId) suppressed.until = performance.now() + 1200;
      if (gesture?.id === event.pointerId) cancel();
    });
    document.addEventListener('click', (event) => {
      if (!suppressed || performance.now() > suppressed.until || event.detail === 0) return;
      const fromTouch = event.pointerType === 'touch' || event.sourceCapabilities?.firesTouchEvents;
      if (!fromTouch || (event.pointerType === 'touch' && event.pointerId !== suppressed.id)) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      suppressed = null;
    }, true);
    surface.addEventListener('contextmenu', (event) => {
      // Suppress the native touch callout only; right-click and keyboard menus are unaffected.
      if (event.pointerType !== 'mouse' && (gesture || (suppressed && performance.now() < suppressed.until))) event.preventDefault();
    });
    window.addEventListener('scroll', cancel, { passive: true });
    window.addEventListener('blur', () => { cancel(); touches.clear(); });
    document.addEventListener('visibilitychange', () => { cancel(); touches.clear(); });
    window.addEventListener('pagehide', () => { cancel(); touches.clear(); });
  })();
  const candidates = () => Array.from(document.querySelectorAll(selector)).filter((el) => {
    if (el.classList.contains('skip-link')) return false;
    const card = el.closest('.entry-card');
    // Exclude non-stops before style/layout reads; Tab still reaches every card control.
    if (card && !el.matches('.overlay-play button')) return false;
    return visible(el);
  });
  const initial = () => document.querySelector('.entry-card .overlay-play button') ||
    candidates().find((el) => el.closest('main') && !el.closest('.app-topbar')) || candidates()[0];
  const jumpByThird = (forward) => {
    // Capture the card before closing a menu that might currently own focus.
    const activeCard = document.activeElement.closest('.entry-card');
    closeMenus();
    // Recompute in current DOM/filter/sort order, including cards below the viewport.
    const entries = candidates().filter((el) => el.matches('main .entry-grid .entry-card .overlay-play button'));
    if (!entries.length) return;
    let index = entries.findIndex((el) => el.closest('.entry-card') === activeCard);
    if (index < 0) index = entries.findIndex((el) => el.closest('.entry-card') === contentFocus?.closest('.entry-card'));
    const step = Math.max(1, Math.ceil(entries.length / 3));
    // With no current/remembered card, establish selection at the first entry.
    const next = index < 0 ? 0 : Math.max(0, Math.min(entries.length - 1, index + (forward ? step : -step)));
    const target = entries[next];
    document.body.classList.add('directional-navigation');
    target.focus({ preventScroll: true });
    contentFocus = target;
    target.closest('.entry-card').scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'smooth' });
  };
  const move = (direction) => {
    const active = document.activeElement;
    const dialog = document.querySelector('[data-confirm-dialog][open]');
    if (dialog) {
      const buttons = Array.from(dialog.querySelectorAll('button')).filter(visible);
      const index = buttons.indexOf(active);
      focus(buttons[(index + (direction === 'left' || direction === 'up' ? buttons.length - 1 : 1)) % buttons.length]);
      return;
    }
    const menu = active.closest('.overlay-menu.is-open');
    if (menu) {
      // Collapsed submenu items are [hidden], so visible() keeps up/down to the expanded list.
      const items = Array.from(menu.querySelectorAll('.menu-item, .submenu-item')).filter(visible);
      if (!items.length) {
        if (direction === 'left') { closeMenus(); focus(menu.querySelector('.menu-trigger')); }
        return;
      }
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
        focus(menu.closest('.entry-card')?.querySelector('.overlay-play button') || menu.querySelector('.menu-trigger'));
      }
      return;
    }
    if (!active.matches(selector) || !visible(active)) { focus(initial()); return; }
    const bounds = (el) => (el.closest('.entry-card') || el).getBoundingClientRect();
    const from = bounds(active);
    const horizontal = direction === 'left' || direction === 'right';
    const sign = direction === 'left' || direction === 'up' ? -1 : 1;
    // The sticky header stays geometrically close while content scrolls beneath it, so content
    // only enters the header when nothing else lies in that direction (or via F1 / Select).
    const fromContent = !active.closest('.app-topbar');
    let best = null;
    let bestScore = Infinity;
    let bestHeader = null;
    let bestHeaderScore = Infinity;
    const activeCard = active.closest('.entry-card');
    for (const el of candidates()) {
      if (el === active || (activeCard && el.closest('.entry-card') === activeCard)) continue;
      const to = bounds(el);
      const dx = (to.left + to.right - from.left - from.right) / 2;
      const dy = (to.top + to.bottom - from.top - from.bottom) / 2;
      const forward = (horizontal ? dx : dy) * sign;
      if (forward <= 1) continue;
      const gap = horizontal ? Math.max(0, from.top - to.bottom, to.top - from.bottom)
        : Math.max(0, from.left - to.right, to.left - from.right);
      const score = forward + Math.abs(horizontal ? dy : dx) * 2 + gap * 5;
      if (fromContent && el.closest('.app-topbar')) {
        if (score < bestHeaderScore) { bestHeader = el; bestHeaderScore = score; }
      } else if (score < bestScore) { best = el; bestScore = score; }
    }
    focus(best || bestHeader);
  };
  const activate = () => {
    const active = document.activeElement;
    if (!active.matches(selector) || !visible(active)) { focus(initial()); return; }
    if (textField(active)) { requestKeyboard(); return; }
    if (active.matches('input:not([type="checkbox"]):not([type="radio"]):not([type="button"]):not([type="submit"]), select')) return;
    active.click();
  };
  const back = () => {
    const dialog = document.querySelector('[data-confirm-dialog][open]');
    if (dialog) { dialog.querySelector('[data-confirm-dismiss]').click(); return; }
    const menu = document.querySelector('.overlay-menu.is-open');
    const trigger = document.activeElement.closest('.menu-submenu')?.querySelector('.submenu-trigger[aria-expanded="true"]');
    if (menu && trigger) {
      // Back steps out of the submenu first, then out of the menu.
      setSubmenu(trigger, false);
      focus(trigger);
    } else if (menu) {
      closeMenus();
      focus(menu.closest('.entry-card')?.querySelector('.overlay-play button') || menu.querySelector('.menu-trigger'));
    } else {
      // Follow a safe GET link, never replay history containing a POST.
      document.querySelector('a.back-link')?.click();
    }
  };
  document.addEventListener('keydown', (event) => {
    if (event.defaultPrevented || event.isComposing) return;
    if (document.querySelector('[data-confirm-dialog][open]')) {
      const direction = { ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'up', ArrowDown: 'down' }[event.key];
      if (direction) { event.preventDefault(); move(direction); }
      else if (event.key === 'Escape') { event.preventDefault(); if (!event.repeat) back(); }
      else if (event.key === 'Enter' && event.repeat) event.preventDefault();
      else if (['F1', 'F2', 'F10', 'ContextMenu'].includes(event.key)) event.preventDefault();
      return;
    }
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
      if (!event.repeat) toggleNavigation();
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
    if (keyboardActive || document.hidden || !document.hasFocus()) { armed = false; previous.clear(); heldDirection = null; resetSelect(); return; }
    let pad;
    try { pad = Array.from(navigator.getGamepads()).find((p) => p?.connected && p.mapping === 'standard'); }
    catch { armed = false; previous.clear(); heldDirection = null; resetSelect(); return; }
    if (!pad) { armed = false; padIndex = null; previous.clear(); heldDirection = null; resetSelect(); return; }
    if (padIndex !== pad.index) { armed = false; padIndex = pad.index; resetSelect(); }
    const pressed = new Set(pad.buttons.map((button, i) => button.pressed ? i : -1).filter((i) => i >= 0));
    const x = pad.axes[0] || 0;
    const y = pad.axes[1] || 0;
    let direction = pressed.has(12) ? 'up' : pressed.has(13) ? 'down' : pressed.has(14) ? 'left' : pressed.has(15) ? 'right' : null;
    if (!direction && Math.max(Math.abs(x), Math.abs(y)) > 0.55)
      direction = Math.abs(x) > Math.abs(y) ? (x < 0 ? 'left' : 'right') : (y < 0 ? 'up' : 'down');
    // Require release after page loads, focus changes or reconnects: held A must not launch twice.
    if (!armed) {
      resetSelect();
      armed = pressed.size === 0 && !direction;
      previous = pressed;
      heldDirection = null;
      return;
    }
    // Modal input cannot navigate or activate the underlying library.
    if (document.querySelector('[data-confirm-dialog][open]')) {
      resetSelect();
      if (pressed.has(1) && !previous.has(1)) back();
      else if (direction && (direction !== heldDirection || now >= nextMove)) {
        move(direction);
        nextMove = now + (direction === heldDirection ? 150 : 350);
      } else if (pressed.has(0) && !previous.has(0)) activate();
      heldDirection = direction;
      previous = pressed;
      return;
    }
    // Select/View is exclusive: release for navigation, hold for global search.
    // Do not combine a focus jump with A, triggers or stick movement on the same frame.
    if (document.querySelector('dialog[open]')) { resetSelect(); previous = pressed; return; }
    if (pressed.has(8)) {
      if (!previous.has(8)) { selectStartedAt = now; selectHeld = false; }
      if (selectStartedAt !== null && !selectHeld && now - selectStartedAt >= 550) {
        selectHeld = true;
        focusGlobalSearch();
      }
      heldDirection = null;
      previous = pressed;
      return;
    }
    if (previous.has(8)) {
      // A slow frame can cross the threshold on release without observing it held.
      const pending = selectStartedAt !== null && !selectHeld;
      const longRelease = pending && now - selectStartedAt >= 550;
      resetSelect();
      if (longRelease) focusGlobalSearch();
      else if (pending) toggleNavigation();
      armed = false; // Require neutral after release, including any simultaneously held A.
      heldDirection = null;
      previous = pressed;
      return;
    }
    // Standard-mapped triggers: previous/next dashboard category, once per squeeze.
    // Use actual category links; never let triggers leave an editor/settings page.
    const categoryBack = pressed.has(6) && !previous.has(6);
    const categoryForward = pressed.has(7) && !previous.has(7);
    if ((categoryBack || categoryForward) && document.querySelector('[data-category-dashboard]')) {
      const categories = Array.from(document.querySelectorAll('[data-primary-nav] a[data-category-nav]'));
      const index = categories.findIndex((link) => link.getAttribute('aria-current') === 'page');
      if (index >= 0) {
        closeMenus();
        const next = (index + (categoryBack ? -1 : 1) + categories.length) % categories.length;
        // Disarm before navigation: holding a trigger must not cycle again on page load.
        armed = false;
        heldDirection = null;
        previous = pressed;
        categories[next].click();
        return; // A or a shoulder pressed on this frame must not also activate/jump.
      }
    }
    // Standard-mapped shoulders: jump one third backward/forward, once per press.
    const jumpBack = pressed.has(4) && !previous.has(4);
    const jumpForward = pressed.has(5) && !previous.has(5);
    if (jumpBack || jumpForward) {
      jumpByThird(!jumpBack);
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
    if (pressed.has(0) && !previous.has(0)) activate();
    else if (pressed.has(1) && !previous.has(1)) back();
    else if (pressed.has(2) && !previous.has(2)) openMenu();
    else if (pressed.has(3) && !previous.has(3)) document.activeElement.closest('.entry-card')?.querySelector('.favorite')?.click();
    previous = pressed;
  };
  window.addEventListener('blur', () => { armed = false; resetSelect(); });
  document.addEventListener('visibilitychange', () => { armed = false; resetSelect(); });
  window.addEventListener('pagehide', () => { armed = false; resetSelect(); });
  window.addEventListener('gamepaddisconnected', () => { armed = false; padIndex = null; resetSelect(); });
  window.requestAnimationFrame(poll);
})();

// Back/forward-cache restores must not leave old forms permanently busy.
window.addEventListener("pageshow", (event) => { if (event.persisted) window.location.reload(); });

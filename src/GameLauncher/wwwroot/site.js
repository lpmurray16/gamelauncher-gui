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
// Back/forward-cache restores must not leave old forms permanently busy.
window.addEventListener("pageshow", (event) => { if (event.persisted) window.location.reload(); });

"use strict";
// Vanilla-JS client for the BookIt API. No build step, no dependencies.
const $ = (id) => document.getElementById(id);
const state = { token: null, user: null, registering: false, services: [], service: null, tab: "services" };
const STORE_KEY = "bookit.session";

try {
  const saved = JSON.parse(localStorage.getItem(STORE_KEY) || "null");
  if (saved && saved.token) { state.token = saved.token; state.user = saved.user; }
} catch { /* storage unavailable or corrupt: start logged out */ }

const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const money = (n) => Number(n).toLocaleString(undefined, { style: "currency", currency: "USD" });
const fmtDT = (iso) => new Date(iso).toLocaleString(undefined, { weekday: "short", month: "short", day: "numeric", hour: "numeric", minute: "2-digit" });
const fmtDate = (iso) => new Date(iso).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
const fmtTime = (d) => d.toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" });
const pad = (n) => String(n).padStart(2, "0");
const toLocalInput = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;

class ApiError extends Error { constructor(msg, status) { super(msg); this.status = status; } }

// ---------- toasts ----------
function toast(message, kind = "info", title = "") {
  const el = document.createElement("div");
  el.className = "toast " + kind;
  el.setAttribute("role", kind === "error" ? "alert" : "status");
  el.innerHTML = `<div>${title ? `<b>${esc(title)}</b>` : ""}${esc(message)}</div><button aria-label="Dismiss">&times;</button>`;
  const close = () => el.remove();
  el.querySelector("button").onclick = close;
  $("toasts").appendChild(el);
  setTimeout(close, kind === "error" ? 7000 : 4000);
}
function errorToast(e) {
  if (e.status === 409) toast(e.message, "error", "Time slot unavailable");
  else if (e.status === 402) toast("The mock payment was declined. Nothing was charged - you can try again.", "error", "Payment declined");
  else toast(e.message || "Something went wrong.", "error");
}

// ---------- api ----------
async function api(path, { method = "GET", body } = {}) {
  const headers = {};
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (state.token) headers["Authorization"] = "Bearer " + state.token;
  let res;
  try { res = await fetch("/api" + path, { method, headers, body: body !== undefined ? JSON.stringify(body) : undefined }); }
  catch { throw new ApiError("Cannot reach the server. Check your connection.", 0); }
  if (res.status === 401 && state.token) { logout(); throw new ApiError("Your session expired. Please log in again.", 401); }
  const data = res.status === 204 ? null : await res.json().catch(() => null);
  if (!res.ok) {
    const msg = data?.error || (data?.errors && Object.values(data.errors).flat()[0]) || data?.title || res.statusText || "Request failed";
    throw new ApiError(msg, res.status);
  }
  return data;
}

async function withBusy(btn, fn) {
  btn.disabled = true; btn.classList.add("loading");
  try { return await fn(); } finally { btn.disabled = false; btn.classList.remove("loading"); }
}

// ---------- shell ----------
function render() {
  const loggedIn = !!state.token;
  $("auth").hidden = loggedIn;
  $("nav").hidden = !loggedIn;
  $("account").hidden = !loggedIn;
  if (!loggedIn) { document.querySelectorAll(".tab").forEach((t) => (t.hidden = true)); return; }
  const name = state.user?.fullName || state.user?.email || "";
  $("who").textContent = state.user?.email || "";
  $("avatar").textContent = (name[0] || "?").toUpperCase();
  showTab(state.tab);
}
function showTab(name) {
  state.tab = name;
  document.querySelectorAll(".tab-btn").forEach((b) => { if (b.dataset.tab === name) b.setAttribute("aria-current", "page"); else b.removeAttribute("aria-current"); });
  document.querySelectorAll(".tab").forEach((t) => (t.hidden = t.id !== "tab-" + name));
  ({ services: loadServices, bookings: loadBookings, invoices: loadInvoices }[name])().catch(errorToast);
}
function setSession(auth) {
  state.token = auth.token; state.user = { email: auth.email, fullName: auth.fullName }; state.tab = "services";
  try { localStorage.setItem(STORE_KEY, JSON.stringify({ token: state.token, user: state.user })); } catch { /* ignore */ }
  render();
}
function logout() {
  state.token = null; state.user = null;
  try { localStorage.removeItem(STORE_KEY); } catch { /* ignore */ }
  if ($("book-dialog").open) $("book-dialog").close();
  if ($("inv-dialog").open) $("inv-dialog").close();
  render();
}
document.querySelectorAll(".tab-btn").forEach((b) => (b.onclick = () => showTab(b.dataset.tab)));
$("logout").onclick = () => { logout(); toast("You have been logged out."); };

const skeletons = (n = 3) => Array.from({ length: n }, () => '<div class="skeleton" aria-hidden="true"></div>').join("");
const empty = (icon, title, hint) => `<div class="empty"><span class="big" aria-hidden="true">${icon}</span><strong>${esc(title)}</strong><p>${esc(hint)}</p></div>`;

// ---------- auth form ----------
function setFieldError(inputId, msg) {
  const input = $(inputId), err = $(inputId + "-err");
  err.textContent = msg || "";
  if (msg) input.setAttribute("aria-invalid", "true"); else input.removeAttribute("aria-invalid");
  return !msg;
}
function validateAuth() {
  let ok = true, first = null;
  const check = (id, msg) => { if (!setFieldError(id, msg)) { ok = false; first = first || $(id); } };
  const email = $("email").value.trim(), pw = $("password").value;
  check("email", !email ? "Email is required." : /^\S+@\S+\.\S+$/.test(email) ? "" : "Enter a valid email address.");
  check("password", !pw ? "Password is required." : state.registering && pw.length < 8 ? "Use at least 8 characters." : "");
  if (state.registering) check("fullName", $("fullName").value.trim() ? "" : "Full name is required.");
  first?.focus();
  return ok;
}
["email", "password", "fullName"].forEach((id) => $(id).addEventListener("input", () => setFieldError(id, "")));

$("toggle-auth").onclick = () => {
  state.registering = !state.registering;
  const r = state.registering;
  $("auth-title").textContent = r ? "Create your account" : "Welcome back";
  $("auth-sub").textContent = r ? "Register to start booking." : "Log in to manage your bookings and invoices.";
  $("auth-submit").textContent = r ? "Register" : "Log in";
  $("name-row").hidden = !r;
  $("password").autocomplete = r ? "new-password" : "current-password";
  $("toggle-auth").textContent = r ? "Have an account? Log in" : "Need an account? Register";
  ["email", "password", "fullName"].forEach((id) => setFieldError(id, ""));
  if (r) { $("email").value = ""; $("password").value = ""; $("fullName").focus(); }
  else { $("email").value = "demo@bookit.test"; $("password").value = "Demo#12345"; }
};
$("auth-form").onsubmit = async (e) => {
  e.preventDefault();
  if (!validateAuth()) return;
  const body = { email: $("email").value.trim(), password: $("password").value };
  if (state.registering) body.fullName = $("fullName").value.trim();
  try {
    const auth = await withBusy($("auth-submit"), () => api(state.registering ? "/auth/register" : "/auth/login", { method: "POST", body }));
    setSession(auth);
    toast(`Signed in as ${auth.email}`, "success");
  } catch (err) {
    if (err.status === 401) setFieldError("password", "Incorrect email or password.");
    else if (err.status === 409) setFieldError("email", "That email is already registered.");
    else errorToast(err);
  }
};

// ---------- services ----------
async function loadServices() {
  $("services").innerHTML = skeletons();
  state.services = await api("/services");
  $("services").innerHTML = state.services.map((s) => `
    <article class="card">
      <div class="svc-top"><h3>${esc(s.name)}</h3><span class="price">${money(s.price)}</span></div>
      <p class="muted">${esc(s.description || "")}</p>
      <div class="svc-meta"><span class="chip">${s.durationMinutes >= 60 ? (s.durationMinutes / 60) + " h" : s.durationMinutes + " min"}</span>
        <button class="btn" data-book="${s.id}" aria-label="Book ${esc(s.name)}">Book</button></div>
    </article>`).join("") || empty("&#128197;", "No services available", "Check back soon.");
  $("services").querySelectorAll("[data-book]").forEach((b) => (b.onclick = () => openBooking(Number(b.dataset.book))));
}

// ---------- booking dialog ----------
function updateEnd() {
  const v = $("book-start").value, s = state.service;
  const d = v ? new Date(v) : null;
  $("book-end").textContent = d && !isNaN(d) && s ? `Ends at ${fmtTime(new Date(d.getTime() + s.durationMinutes * 60000))} (${s.durationMinutes} min)` : "";
}
function openBooking(id) {
  state.service = state.services.find((s) => s.id === id);
  if (!state.service) return;
  $("book-title").textContent = "Book " + state.service.name;
  $("book-summary").textContent = `${money(state.service.price)} - ${state.service.durationMinutes} minutes`;
  const now = new Date(); now.setSeconds(0, 0);
  now.setMinutes(Math.ceil(now.getMinutes() / 15) * 15);
  $("book-start").min = toLocalInput(now);
  const def = new Date(); def.setDate(def.getDate() + 1); def.setHours(10, 0, 0, 0);
  $("book-start").value = toLocalInput(def);
  $("book-notes").value = "";
  setFieldError("book-start", ""); $("book-form-err").textContent = "";
  updateEnd();
  $("book-dialog").showModal();
  $("book-start").focus();
}
$("book-start").addEventListener("input", () => { setFieldError("book-start", ""); $("book-form-err").textContent = ""; updateEnd(); });
$("book-cancel").onclick = () => $("book-dialog").close();
$("book-form").onsubmit = async (e) => {
  e.preventDefault();
  const v = $("book-start").value;
  if (!v) { setFieldError("book-start", "Choose a date and time."); $("book-start").focus(); return; }
  const d = new Date(v);
  if (isNaN(d) || d <= new Date()) { setFieldError("book-start", "Pick a time in the future."); $("book-start").focus(); return; }
  try {
    await withBusy($("book-submit"), () => api("/bookings", { method: "POST", body: { serviceId: state.service.id, startTime: d.toISOString(), notes: $("book-notes").value.trim() || null } }));
    $("book-dialog").close();
    toast(`${state.service.name} on ${fmtDT(d)}`, "success", "Booking confirmed");
    showTab("bookings");
  } catch (err) {
    if (err.status === 409) { $("book-form-err").textContent = "Sorry, that slot overlaps an existing booking. Please choose another time."; errorToast(err); }
    else if (err.status === 400) setFieldError("book-start", err.message);
    else errorToast(err);
  }
};

// ---------- bookings ----------
const statusBadge = (s) => `<span class="badge ${s === "Cancelled" ? "bad" : s === "Completed" ? "neutral" : s === "Confirmed" ? "ok" : "warn"}">${esc(s)}</span>`;
async function loadBookings() {
  $("bookings").innerHTML = skeletons(2);
  const items = await api("/bookings");
  $("bookings").innerHTML = items.map((b) => `
    <article class="card row-card">
      <div class="main"><div class="t">${esc(b.serviceName)} ${statusBadge(b.status)}</div>
        <div class="dt">${fmtDT(b.startTime)} &ndash; ${fmtTime(new Date(b.endTime))}</div>
        ${b.notes ? `<div class="dt">Note: ${esc(b.notes)}</div>` : ""}</div>
      <div class="acts">
        ${b.status === "Cancelled" ? "" : b.invoiceId
          ? `<button class="btn ghost sm" data-viewinv="${b.invoiceId}">View invoice</button>`
          : `<button class="btn sm" data-invoice="${b.id}">Create invoice</button>`}
        ${b.status === "Confirmed" || b.status === "Pending" ? `<button class="btn danger sm" data-cancel="${b.id}">Cancel</button>` : ""}
      </div>
    </article>`).join("") || empty("&#128466;", "No bookings yet", "Head to Services to book your first appointment.");
  $("bookings").querySelectorAll("[data-invoice]").forEach((b) => (b.onclick = async () => {
    try { const inv = await withBusy(b, () => api(`/bookings/${b.dataset.invoice}/invoice`, { method: "POST" })); toast(inv.number, "success", "Invoice created"); showTab("invoices"); }
    catch (err) { errorToast(err); }
  }));
  $("bookings").querySelectorAll("[data-viewinv]").forEach((b) => (b.onclick = () => openInvoice(Number(b.dataset.viewinv))));
  $("bookings").querySelectorAll("[data-cancel]").forEach((b) => (b.onclick = async () => {
    if (!confirm("Cancel this booking?")) return;
    try { await withBusy(b, () => api(`/bookings/${b.dataset.cancel}/cancel`, { method: "POST" })); toast("The time slot is free again.", "info", "Booking cancelled"); loadBookings(); }
    catch (err) { errorToast(err); }
  }));
}

// ---------- invoices ----------
const paidBadge = (i) => `<span class="badge ${i.isPaid ? "ok" : "warn"}">${i.isPaid ? "Paid" : "Unpaid"}</span>`;
const linesTable = (i) => `<table class="lines"><thead><tr><th>Item</th><th class="num">Qty</th><th class="num">Amount</th></tr></thead><tbody>${
  i.lineItems.map((l) => `<tr><td>${esc(l.description)}</td><td class="num">${l.quantity}</td><td class="num">${money(l.lineTotal)}</td></tr>`).join("")
}</tbody></table><div class="total"><span>Total</span><span>${money(i.total)}</span></div>`;

async function loadInvoices() {
  $("invoices").innerHTML = skeletons(2);
  const items = await api("/invoices");
  $("invoices").innerHTML = items.map((i) => `
    <article class="card">
      <div class="inv-head"><h3>${esc(i.number)}</h3>${paidBadge(i)}</div>
      <p class="muted small">Issued ${fmtDate(i.issuedAt)}</p>
      ${linesTable(i)}
      <div class="acts">
        <button class="btn ghost sm" data-open="${i.id}">Details</button>
        ${i.isPaid ? "" : `<button class="btn pay sm" data-pay="${i.id}">Pay (mock)</button>`}
      </div>
    </article>`).join("") || empty("&#129534;", "No invoices yet", "Create one from a booking on the My bookings page.");
  bindInvoiceButtons($("invoices"));
}
function bindInvoiceButtons(root) {
  root.querySelectorAll("[data-open]").forEach((b) => (b.onclick = () => openInvoice(Number(b.dataset.open))));
  root.querySelectorAll("[data-pay]").forEach((b) => (b.onclick = () => pay(Number(b.dataset.pay), true, b)));
  root.querySelectorAll("[data-decline]").forEach((b) => (b.onclick = () => pay(Number(b.dataset.decline), false, b)));
}
async function pay(id, succeed, btn) {
  try {
    const inv = await withBusy(btn, () => api(`/invoices/${id}/pay`, { method: "POST", body: { succeed } }));
    toast(`${inv.number} is paid (ref ${inv.paymentReference}).`, "success", "Mock payment successful");
    if ($("inv-dialog").open) openInvoice(id);
    if (state.tab === "invoices") loadInvoices();
  } catch (err) { errorToast(err); }
}
async function openInvoice(id) {
  const dlg = $("inv-dialog");
  $("inv-body").innerHTML = '<div class="skeleton" aria-hidden="true"></div>';
  if (!dlg.open) dlg.showModal();
  try {
    const i = await api("/invoices/" + id);
    $("inv-body").innerHTML = `
      <div class="inv-head"><h2 id="inv-title">${esc(i.number)}</h2>${paidBadge(i)}</div>
      <p class="muted small">Issued ${fmtDate(i.issuedAt)} &middot; Booking #${i.bookingId}${i.paidAt ? ` &middot; Paid ${fmtDate(i.paidAt)}` : ""}</p>
      ${linesTable(i)}
      ${i.paymentReference ? `<p class="muted small">Payment reference: ${esc(i.paymentReference)}</p>` : ""}
      <div class="actions">
        <button class="btn ghost" id="inv-close">Close</button>
        ${i.isPaid ? "" : `<button class="btn danger" data-decline="${i.id}">Simulate decline</button><button class="btn pay" data-pay="${i.id}">Pay (mock)</button>`}
      </div>`;
    $("inv-close").onclick = () => dlg.close();
    bindInvoiceButtons($("inv-body"));
  } catch (err) { dlg.close(); errorToast(err); }
}
// close dialogs when clicking the backdrop
document.querySelectorAll("dialog").forEach((d) => d.addEventListener("click", (e) => { if (e.target === d) d.close(); }));

render();

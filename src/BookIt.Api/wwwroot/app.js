"use strict";
// Minimal vanilla-JS client for the BookIt API. No build step.
const $ = (id) => document.getElementById(id);
const state = { token: null, user: null, registering: false, selectedService: null };

try {
  const saved = JSON.parse(localStorage.getItem("bookit.session") || "null");
  if (saved) { state.token = saved.token; state.user = saved.user; }
} catch { /* ignore corrupt storage */ }

function esc(s) {
  return String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}
const money = (n) => Number(n).toLocaleString(undefined, { style: "currency", currency: "USD" });
const fmt = (iso) => new Date(iso).toLocaleString();

let toastTimer;
function toast(msg, isErr = false) {
  const t = $("toast");
  t.textContent = msg; t.className = isErr ? "err" : ""; t.hidden = false;
  clearTimeout(toastTimer); toastTimer = setTimeout(() => (t.hidden = true), 4000);
}

async function api(path, { method = "GET", body } = {}) {
  const headers = {};
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (state.token) headers["Authorization"] = "Bearer " + state.token;
  const res = await fetch("/api" + path, { method, headers, body: body !== undefined ? JSON.stringify(body) : undefined });
  if (res.status === 401 && state.token) { logout(); throw new Error("Session expired, please log in again."); }
  const data = res.status === 204 ? null : await res.json().catch(() => null);
  if (!res.ok) {
    const msg = data?.error || data?.title || (data?.errors && Object.values(data.errors).flat().join(" ")) || res.statusText;
    throw new Error(msg);
  }
  return data;
}

function render() {
  const loggedIn = !!state.token;
  $("auth").hidden = loggedIn;
  $("nav").hidden = !loggedIn;
  if (!loggedIn) { document.querySelectorAll(".tab").forEach((t) => (t.hidden = true)); return; }
  $("who").textContent = state.user?.email || "";
  showTab(document.querySelector("nav button.active")?.dataset.tab || "services");
}

function showTab(name) {
  document.querySelectorAll("nav button[data-tab]").forEach((b) => b.classList.toggle("active", b.dataset.tab === name));
  document.querySelectorAll(".tab").forEach((t) => (t.hidden = t.id !== "tab-" + name));
  ({ services: loadServices, bookings: loadBookings, invoices: loadInvoices }[name])().catch((e) => toast(e.message, true));
}

function setSession(auth) {
  state.token = auth.token; state.user = { email: auth.email, fullName: auth.fullName };
  try { localStorage.setItem("bookit.session", JSON.stringify({ token: state.token, user: state.user })); } catch { /* private mode */ }
  render();
}
function logout() {
  state.token = null; state.user = null;
  try { localStorage.removeItem("bookit.session"); } catch { /* ignore */ }
  render();
}

// ---- auth ----
$("toggle-auth").onclick = (e) => {
  e.preventDefault();
  state.registering = !state.registering;
  $("auth-title").textContent = $("auth-submit").textContent = state.registering ? "Register" : "Log in";
  $("name-row").hidden = !state.registering;
  $("toggle-auth").textContent = state.registering ? "Have an account? Log in" : "Need an account? Register";
  if (state.registering) { $("email").value = ""; $("password").value = ""; }
};
$("auth-form").onsubmit = async (e) => {
  e.preventDefault();
  try {
    const body = { email: $("email").value, password: $("password").value };
    if (state.registering) body.fullName = $("fullName").value || body.email;
    setSession(await api(state.registering ? "/auth/register" : "/auth/login", { method: "POST", body }));
  } catch (err) { toast(err.message, true); }
};
$("logout").onclick = logout;
document.querySelectorAll("nav button[data-tab]").forEach((b) => (b.onclick = () => showTab(b.dataset.tab)));

// ---- services ----
async function loadServices() {
  const services = await api("/services");
  $("services").innerHTML = services.map((s) => `
    <div class="card">
      <h3>${esc(s.name)}</h3>
      <p class="muted">${esc(s.description)}</p>
      <div class="price">${money(s.price)}</div>
      <p class="muted small">${s.durationMinutes} minutes</p>
      <button data-book="${s.id}" data-name="${esc(s.name)}">Book</button>
    </div>`).join("") || "<p>No services yet.</p>";
  $("services").querySelectorAll("[data-book]").forEach((b) => (b.onclick = () => {
    state.selectedService = Number(b.dataset.book);
    $("book-service-name").textContent = b.dataset.name;
    $("book-card").hidden = false;
    $("book-card").scrollIntoView({ behavior: "smooth" });
  }));
}
$("book-cancel").onclick = () => ($("book-card").hidden = true);
$("book-form").onsubmit = async (e) => {
  e.preventDefault();
  try {
    await api("/bookings", { method: "POST", body: {
      serviceId: state.selectedService,
      startTime: new Date($("book-start").value).toISOString(),
      notes: $("book-notes").value || null } });
    toast("Booking confirmed!");
    $("book-card").hidden = true; $("book-form").reset();
    showTab("bookings");
  } catch (err) { toast(err.message, true); }
};

// ---- bookings ----
async function loadBookings() {
  const items = await api("/bookings");
  $("bookings").innerHTML = items.map((b) => `
    <tr>
      <td>${esc(b.serviceName)}</td><td>${fmt(b.startTime)}</td><td>${fmt(b.endTime)}</td>
      <td><span class="badge">${esc(b.status)}</span></td>
      <td>
        ${b.status === "Cancelled" ? "" : `
          <button data-invoice="${b.id}" ${b.invoiceId ? "disabled" : ""}>${b.invoiceId ? "Invoiced" : "Create invoice"}</button>
          <button class="danger" data-cancel="${b.id}">Cancel</button>`}
      </td>
    </tr>`).join("") || '<tr><td colspan="5" class="muted">No bookings yet.</td></tr>';
  $("bookings").querySelectorAll("[data-invoice]").forEach((b) => (b.onclick = async () => {
    try { await api(`/bookings/${b.dataset.invoice}/invoice`, { method: "POST" }); toast("Invoice created."); showTab("invoices"); }
    catch (err) { toast(err.message, true); }
  }));
  $("bookings").querySelectorAll("[data-cancel]").forEach((b) => (b.onclick = async () => {
    try { await api(`/bookings/${b.dataset.cancel}/cancel`, { method: "POST" }); toast("Booking cancelled."); loadBookings(); }
    catch (err) { toast(err.message, true); }
  }));
}

// ---- invoices ----
async function loadInvoices() {
  const items = await api("/invoices");
  $("invoices").innerHTML = items.map((i) => `
    <div class="card">
      <strong>${esc(i.number)}</strong>
      <span class="badge ${i.isPaid ? "paid" : "unpaid"}">${i.isPaid ? "PAID" : "UNPAID"}</span>
      <p class="muted small">Issued ${fmt(i.issuedAt)}</p>
      <table class="inv-lines">${i.lineItems.map((l) =>
        `<tr><td>${esc(l.description)}</td><td>${l.quantity} x ${money(l.unitPrice)}</td><td style="text-align:right">${money(l.lineTotal)}</td></tr>`).join("")}</table>
      <div class="total"><span>Total</span><span>${money(i.total)}</span></div>
      ${i.isPaid ? `<p class="muted small">Ref ${esc(i.paymentReference)}</p>`
                 : `<button class="pay" data-pay="${i.id}">Pay (mock)</button>`}
    </div>`).join("") || "<p>No invoices yet. Create one from a booking.</p>";
  $("invoices").querySelectorAll("[data-pay]").forEach((b) => (b.onclick = async () => {
    try { await api(`/invoices/${b.dataset.pay}/pay`, { method: "POST", body: { succeed: true } }); toast("Mock payment successful."); loadInvoices(); }
    catch (err) { toast(err.message, true); }
  }));
}

render();

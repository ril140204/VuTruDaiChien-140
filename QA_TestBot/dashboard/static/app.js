/* ============================================================
   QA TestBot Dashboard - app logic
   ============================================================ */

const state = {
  accounts: [],      // danh sach tai khoan trong accounts.json (co thay doi chua luu)
  rows: [],          // ban sao de edit
  status: null,
  selected: null,
  timer: null,
};

const $ = (id) => document.getElementById(id);

/* ---------------- Helpers ---------------- */
function toast(msg, kind = '') {
  const el = $('toast');
  el.textContent = msg;
  el.className = 'toast show ' + kind;
  clearTimeout(el._t);
  el._t = setTimeout(() => { el.className = 'toast ' + kind; }, 4200);
}

async function api(path, opts = {}) {
  const res = await fetch(path, {
    headers: { 'Content-Type': 'application/json' },
    ...opts,
  });
  const text = await res.text();
  let data;
  try { data = text ? JSON.parse(text) : {}; }
  catch { data = { error: text }; }
  if (!res.ok) throw new Error(data.error || data.message || `HTTP ${res.status}`);
  return data;
}

function esc(s) {
  return String(s == null ? '' : s)
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

/* ---------------- Status / account list ---------------- */
function accountStatusOf(a) {
  const ev = a.events || {};
  if (ev.success) return 'success';
  if (ev.last_activity && ev.last_state && ev.last_state !== 'NotRun') {
    // Co phat sinh su kien nhung chua vao map -> dang chay / that bai
    return 'failed';
  }
  return 'failed';
}

function statusLabel(st) {
  if (st === 'success') return 'Vào map';
  if (st === 'failed') return 'Chưa vào';
  return '—';
}

function renderAccountList() {
  const box = $('account-list');
  const q = $('filter-search').value.trim().toLowerCase();
  const filter = $('filter-status').value;

  const merged = new Map();
  // Tai khoan trong config (co the chua chay)
  for (const r of state.rows) merged.set(r.username, { username: r.username, status: 'failed', events: {}, snapshots: {}, in_config: true });
  // Tai khoan da chay (co du lieu logs)
  for (const a of (state.status?.accounts || [])) {
    merged.set(a.username, { ...a, status: accountStatusOf(a) });
  }

  const list = [...merged.values()].filter(a => {
    if (q && !a.username.toLowerCase().includes(q)) return false;
    if (!filter) return true;
    if (filter === 'success') return a.status === 'success';
    if (filter === 'failed') return a.status === 'failed';
    if (filter === 'running') return a.status === 'running';
    return true;
  });

  if (!list.length) {
    box.innerHTML = '<div class="empty">Chưa có tài khoản nào.</div>';
    return;
  }

  box.innerHTML = list.map(a => {
    const ev = a.events || {};
    const dur = ev.duration != null ? `${ev.duration.toFixed(2)}s` : '—';
    const snaps = (a.snapshots?.total_records) || 0;
    const sub = ev.exists
      ? `${ev.last_state || '—'} · ${ev.events_count || 0} sự kiện · ${dur} · ${snaps} snapshot`
      : 'Chưa chạy';
    return `
      <div class="account-item ${state.selected === a.username ? 'selected' : ''}" data-acc="${esc(a.username)}">
        <span class="dot ${a.status}"></span>
        <div class="account-meta">
          <div class="account-name">${esc(a.username)}</div>
          <div class="account-sub">${esc(sub)}</div>
        </div>
        <span class="badge ${a.status === 'success' ? 'ok' : ''}">${statusLabel(a.status)}</span>
      </div>`;
  }).join('');

  box.querySelectorAll('.account-item').forEach(el => {
    el.addEventListener('click', () => {
      state.selected = el.dataset.acc;
      $('events-account').value = state.selected;
      renderAccountList();
      loadEvents();
    });
  });
}

/* ---------------- Config tab ---------------- */
function fillConfig(cfg) {
  cfg = cfg || {};
  $('cfg-server-name').value = cfg.server_name || '';
  $('cfg-allowed').value = (cfg.allowed_server_names || []).join('\n');
  $('cfg-max-concurrent').value = cfg.max_concurrent ?? 1;
  $('cfg-stagger').value = cfg.stagger_seconds ?? 6;
  $('cfg-timeout').value = cfg.step_timeout ?? 45;
  $('cfg-snapshot').value = cfg.snapshot_interval ?? 5;
  $('cfg-suppress-nre').checked = cfg.suppress_unity_null_reference !== false;
  $('cfg-suppressed').value = (cfg.suppressed_log_patterns || []).join('\n');
}

function collectConfig() {
  const lines = (v) => v.split('\n').map(s => s.trim()).filter(Boolean);
  return {
    server_name: $('cfg-server-name').value.trim(),
    allowed_server_names: lines($('cfg-allowed').value),
    max_concurrent: Number($('cfg-max-concurrent').value) || 1,
    stagger_seconds: Number($('cfg-stagger').value) || 0,
    step_timeout: Number($('cfg-timeout').value) || 45,
    snapshot_interval: Number($('cfg-snapshot').value) || 5,
    suppress_unity_null_reference: $('cfg-suppress-nre').checked,
    suppressed_log_patterns: lines($('cfg-suppressed').value),
  };
}

/* ---------------- Accounts table ---------------- */
function renderAccountsTable() {
  const tbody = $('accounts-tbody');
  $('accounts-empty').style.display = state.rows.length ? 'none' : '';

  tbody.innerHTML = state.rows.map((r, i) => `
    <tr data-i="${i}">
      <td>${i + 1}</td>
      <td><input type="text" data-k="username" value="${esc(r.username)}" placeholder="tên tài khoản"></td>
      <td><input type="password" data-k="password" value="${esc(r.password)}"></td>
      <td><input type="text" data-k="server" value="${esc(r.server || '')}" placeholder="tuỳ chọn"></td>
      <td><input type="text" data-k="note" value="${esc(r.note || '')}" placeholder="ghi chú"></td>
      <td><button class="row-del" title="Xoá">✕</button></td>
    </tr>`).join('');

  tbody.querySelectorAll('tr').forEach(tr => {
    const i = Number(tr.dataset.i);
    tr.querySelectorAll('input[data-k]').forEach(inp => {
      inp.addEventListener('input', () => {
        state.rows[i][inp.dataset.k] = inp.value;
        renderAccountList();
      });
    });
    tr.querySelector('.row-del').addEventListener('click', () => {
      state.rows.splice(i, 1);
      renderAccountsTable();
      renderAccountList();
    });
  });
}

/* ---------------- Events ---------------- */
async function loadEvents() {
  const acc = $('events-account').value || state.selected;
  if (!acc) return;
  const view = $('events-view');
  try {
    const data = await api(`/api/events?account=${encodeURIComponent(acc)}&limit=300`);
    const rows = data.events || [];
    if (!rows.length) {
      view.innerHTML = '<div class="empty">Chưa có sự kiện nào cho tài khoản này.</div>';
      return;
    }
    view.innerHTML = rows.map(r => `
      <div class="log-line ${esc(r.event_type)}">
<span class="ts">${esc(r.timestamp)}</span>  <span class="ev">[${esc(r.event_type)}]</span>  <span class="msg">${esc(r.message)}</span>
</div>`).join('');
    view.scrollTop = view.scrollHeight;
  } catch (e) {
    view.innerHTML = `<div class="empty">Lỗi tải nhật ký: ${esc(e.message)}</div>`;
  }
}

/* ---------------- Report ---------------- */
async function loadReport() {
  const box = $('report-content');
  try {
    const r = await api('/api/report');
    const cards = `
      <div class="report-cards">
        <div class="report-card"><div class="k">Tài khoản</div><div class="v">${r.total_accounts}</div></div>
        <div class="report-card"><div class="k">Vào map</div><div class="v ok">${r.success_accounts}</div></div>
        <div class="report-card"><div class="k">Thất bại</div><div class="v ${r.failed_accounts ? 'bad' : ''}">${r.failed_accounts}</div></div>
        <div class="report-card"><div class="k">Tỉ lệ</div><div class="v">${r.success_rate}%</div></div>
        <div class="report-card"><div class="k">Nhanh nhất</div><div class="v">${r.duration_min != null ? r.duration_min.toFixed(2) + 's' : '—'}</div></div>
        <div class="report-card"><div class="k">Trung bình</div><div class="v">${r.duration_avg != null ? r.duration_avg.toFixed(2) + 's' : '—'}</div></div>
        <div class="report-card"><div class="k">Chậm nhất</div><div class="v">${r.duration_max != null ? r.duration_max.toFixed(2) + 's' : '—'}</div></div>
      </div>`;

    const types = Object.entries(r.object_types || {});
    const typeTable = types.length ? `
      <h3 style="font-size:12px;margin:18px 0 8px;color:var(--text-dim)">Đối tượng ghi nhận trong khu</h3>
      <table><thead><tr><th>Loại</th><th style="text-align:right">Số lượt ghi</th></tr></thead><tbody>
      ${types.map(([k, v]) => `<tr><td>${esc(k)}</td><td class="num">${v}</td></tr>`).join('')}
      </tbody></table>` : '';

    const accTable = `
      <h3 style="font-size:12px;margin:18px 0 8px;color:var(--text-dim)">Chi tiết tài khoản</h3>
      <table><thead><tr><th>Tài khoản</th><th>Trạng thái</th><th>Thời gian vào</th><th>Sự kiện</th><th>Snapshot</th><th>Trạng thái cuối</th></tr></thead><tbody>
      ${(state.status?.accounts || []).map(a => {
        const ev = a.events || {};
        const ok = ev.success;
        return `<tr>
          <td style="font-family:var(--mono)">${esc(a.username)}</td>
          <td><span class="badge ${ok ? 'ok' : ''}">${ok ? 'SUCCESS' : 'FAILED'}</span></td>
          <td class="num">${ev.duration != null ? ev.duration.toFixed(2) + 's' : '—'}</td>
          <td class="num">${ev.events_count || 0}</td>
          <td class="num">${(a.snapshots?.total_records) || 0}</td>
          <td>${esc(ev.last_state || '—')}</td>
        </tr>`;
      }).join('')}
      </tbody></table>`;

    box.innerHTML = cards + typeTable + accTable;
  } catch (e) {
    box.innerHTML = `<div class="empty">Lỗi tải báo cáo: ${esc(e.message)}</div>`;
  }
}

/* ---------------- Main refresh ---------------- */
async function refresh() {
  try {
    const s = await api('/api/status');
    state.status = s;

    $('stat-server').textContent = s.server_name || '-';
    $('stat-accounts').textContent = (s.accounts || []).length;
    $('stat-success').textContent = (s.accounts || []).filter(a => a.events?.success).length;
    $('stat-concurrent').textContent = s.config?.max_concurrent ?? '-';
    $('subtitle').textContent =
      `Server đích: ${s.server_name || '-'} · Whitelist: ${(s.allowed_server_names || []).length} · ` +
      `Launcher: ${s.launcher_running ? 'đang chạy' : 'đã dừng'}` +
      (s.stop_flag ? ' · stop.flag đang bật' : '');

    $('btn-start').disabled = !!s.launcher_running;
    $('btn-stop').disabled = !s.launcher_running && !s.stop_flag;

    if (!state._configLoaded) {
      fillConfig(s.config);
      state._configLoaded = true;
    }

    renderAccountList();

    // Dong bo danh sach tai khoan (chi khi chua sua trong UI)
    if (!state._accountsLoaded) {
      const d = await api('/api/accounts');
      state.rows = (d.accounts || []).map(a => ({
        username: a.username || '', password: a.password || '',
        server: a.server || '', note: a.note || '',
      }));
      renderAccountsTable();
      state._accountsLoaded = true;
    }

    // Danh sach chon tai khoan de xem nhat ky
    const sel = $('events-account');
    const cur = sel.value;
    const names = (s.accounts || []).map(a => a.username);
    if (JSON.stringify([...sel.options].map(o => o.value)) !== JSON.stringify(names)) {
      sel.innerHTML = names.map(n => `<option value="${esc(n)}">${esc(n)}</option>`).join('');
      if (cur && names.includes(cur)) sel.value = cur;
    }
    if (!state.selected && names.length) {
      state.selected = names[0];
      sel.value = names[0];
    }
  } catch (e) {
    toast('Lỗi tải trạng thái: ' + e.message, 'error');
  }
}

/* ---------------- Events wiring ---------------- */
function initTabs() {
  document.querySelectorAll('.tab').forEach(t => {
    t.addEventListener('click', () => {
      document.querySelectorAll('.tab').forEach(x => x.classList.remove('active'));
      document.querySelectorAll('.tab-panel').forEach(x => x.classList.remove('active'));
      t.classList.add('active');
      $('tab-' + t.dataset.tab).classList.add('active');
      if (t.dataset.tab === 'report') loadReport();
      if (t.dataset.tab === 'events') loadEvents();
    });
  });
}

function initActions() {
  $('btn-refresh').addEventListener('click', refresh);
  $('filter-search').addEventListener('input', renderAccountList);
  $('filter-status').addEventListener('change', renderAccountList);

  $('btn-add').addEventListener('click', () => {
    state.rows.push({ username: '', password: '', server: '', note: '' });
    renderAccountsTable();
    const last = $('accounts-tbody').querySelector('tr:last-child input');
    if (last) last.focus();
  });

  $('btn-save-acc').addEventListener('click', async () => {
    try {
      const d = await api('/api/accounts/save', {
        method: 'POST',
        body: JSON.stringify({ accounts: state.rows }),
      });
      toast(`Đã lưu ${d.count} tài khoản.`, 'success');
      state._accountsLoaded = false;
      await refresh();
    } catch (e) { toast('Lỗi lưu tài khoản: ' + e.message, 'error'); }
  });

  $('btn-save-config').addEventListener('click', async () => {
    try {
      await api('/api/config/save', { method: 'POST', body: JSON.stringify(collectConfig()) });
      toast('Đã lưu cấu hình.', 'success');
      state._configLoaded = false;
      await refresh();
    } catch (e) { toast('Lỗi lưu cấu hình: ' + e.message, 'error'); }
  });

  $('btn-start').addEventListener('click', async () => {
    try {
      const d = await api('/api/launcher/start', { method: 'POST', body: '{}' });
      toast(d.message || 'Đã bắt đầu.', 'success');
      await refresh();
    } catch (e) { toast(e.message, 'error'); }
  });

  $('btn-stop').addEventListener('click', async () => {
    try {
      const d = await api('/api/launcher/stop', { method: 'POST', body: '{}' });
      toast(d.message || 'Đã yêu cầu dừng.', 'success');
      await refresh();
    } catch (e) { toast(e.message, 'error'); }
  });

  $('btn-gen-report').addEventListener('click', async () => {
    try {
      const d = await api('/api/report/generate', { method: 'POST', body: '{}' });
      toast(d.ok ? 'Đã xuất summary.md và summary.csv.' : 'Xuất báo cáo lỗi.', d.ok ? 'success' : 'error');
      loadReport();
    } catch (e) { toast(e.message, 'error'); }
  });

  $('btn-clear-events').addEventListener('click', async () => {
    const acc = $('events-account').value || state.selected;
    if (!acc) return;
    if (!confirm(`Xoá toàn bộ log của tài khoản "${acc}"?`)) return;
    try {
      await api('/api/output/clear', { method: 'POST', body: JSON.stringify({ account: acc }) });
      toast('Đã xoá log.', 'success');
      await refresh();
      loadEvents();
    } catch (e) { toast(e.message, 'error'); }
  });

  $('events-account').addEventListener('change', () => {
    state.selected = $('events-account').value;
    renderAccountList();
    loadEvents();
  });
}

/* ---------------- Boot ---------------- */
document.addEventListener('DOMContentLoaded', () => {
  initTabs();
  initActions();
  refresh();
  loadReport();
  state.timer = setInterval(refresh, 3000);
});

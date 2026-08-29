// utils.js — Funciones de uso general, sin dependencias externas

function esc(str) {
    if (!str) return '';
    return String(str)
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

function fmtMoney(n) {
    return parseFloat(n || 0).toLocaleString('es-MX', { minimumFractionDigits: 2 });
}

function fmtFecha(str) {
    if (!str) return '';
    const d = new Date(str);
    return isNaN(d) ? str : d.toLocaleDateString('es-MX');
}

function statusClass(s) {
    if (!s) return 'pendiente';
    const l = s.toLowerCase();
    if (l.includes('timbra') || l.includes('vigente')) return 'vigente';
    if (l.includes('cancela')) return 'cancelado';
    return 'pendiente';
}

function statusLabel(s) {
    if (!s) return 'Desconocido';
    const l = s.toLowerCase();
    if (l.includes('timbra')) return 'Vigente';
    if (l.includes('cancela')) return 'Cancelado';
    if (l.includes('pendiente')) return 'Pend. cancelación';
    return s;
}

function validarRFC(rfc) {
    const re = /^([A-ZÑ&]{3,4})(\d{6})([A-Z0-9]{3})$/;
    return re.test(rfc.toUpperCase());
}

function validarUUID(uuid) {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(uuid);
}

function showToast(type, msg) {
    const icons = {
        success: 'fa-circle-check',
        warning: 'fa-triangle-exclamation',
        error: 'fa-circle-xmark',
        info: 'fa-circle-info'
    };
    const el = document.createElement('div');
    el.className = `toast-item toast-${type}`;
    el.innerHTML = `<i class="fas ${icons[type]}"></i><span class="toast-msg">${esc(msg)}</span>`;
    document.getElementById('toastContainer').appendChild(el);
    setTimeout(() => el.remove(), 4000);
}

// ── Helpers de construcción de HTML ──────────────────────────────────────────

function fiscalAlert(level, icon, title, body) {
    const cls = {
        danger: 'alert-danger-custom',
        warning: 'alert-warning-custom',
        info: 'alert-info-custom',
        success: 'alert-success-custom'
    };
    return `<div class="alert-custom ${cls[level]} mb-3">
        <i class="fas ${icon}"></i>
        <div><strong>${title}</strong> ${body}</div>
    </div>`;
}

function formRow(label, html, note = '') {
    return `<div>
        <label class="form-label-custom">${label}</label>
        ${html}
        ${note ? `<div style="font-size:.72rem;color:var(--text-light);margin-top:.25rem">${note}</div>` : ''}
    </div>`;
}

function fInput(id, val = '', opts = '') {
    return `<input class="form-control-custom" id="${id}" value="${esc(val)}" ${opts}>`;
}

function fSelect(id, optsHtml, extra = '') {
    return `<select class="form-control-custom" id="${id}" ${extra}>${optsHtml}</select>`;
}

function sectionTitle(label) {
    return `<div style="font-size:.73rem;font-weight:700;text-transform:uppercase;letter-spacing:.07em;
        color:var(--text-light);padding:.5rem 0 .25rem;border-bottom:1px solid var(--border-light);
        margin-bottom:.75rem">${label}</div>`;
}

function catOpts(arr, selectedKey, keyField = 'clave', labelField = 'desc') {
    return arr.map(i =>
        `<option value="${esc(i[keyField])}" ${i[keyField] === selectedKey ? 'selected' : ''}>
            ${esc(i[labelField])}
        </option>`
    ).join('');
}

// ── Validaciones de campos ────────────────────────────────────────────────────

function markError(id, msg) {
    const el = document.getElementById(id);
    if (!el) return;
    el.style.borderColor = 'var(--danger-color)';
    el.style.boxShadow = '0 0 0 3px rgba(220,38,38,0.15)';
    let hint = el.nextElementSibling;
    if (!hint || !hint.classList.contains('val-hint')) {
        hint = document.createElement('div');
        hint.className = 'val-hint';
        hint.style.cssText = 'font-size:.72rem;color:var(--danger-color);margin-top:.2rem';
        el.after(hint);
    }
    hint.textContent = msg;
}

function clearError(id) {
    const el = document.getElementById(id);
    if (!el) return;
    el.style.borderColor = '';
    el.style.boxShadow = '';
    const hint = el.nextElementSibling;
    if (hint && hint.classList.contains('val-hint')) hint.remove();
}

function addValidationEvents(fields) {
    fields.forEach(({ id, fn, msg }) => {
        const el = document.getElementById(id);
        if (!el) return;
        el.addEventListener('blur', () => {
            const val = el.value.trim();
            if (val && !fn(val)) markError(id, msg);
            else clearError(id);
        });
        el.addEventListener('input', () => clearError(id));
    });
}

function soloNums(el) {
    el.value = el.value.replace(/\D/g, '');
}
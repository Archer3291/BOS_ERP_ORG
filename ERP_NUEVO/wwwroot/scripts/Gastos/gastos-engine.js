/**
 * gastos-engine.js
 * ─────────────────────────────────────────────────────────────────────────────
 * Motor de renderizado y validación de campos dinámicos.
 * Depende de: gastos-schema.js (debe cargarse antes).
 * No contiene referencias al wizard ni al estado del formulario.
 * ─────────────────────────────────────────────────────────────────────────────
 */

const Engine = (() => {

    /* ─────────────────────────── RENDERIZADO ──────────────────────────────── */

    /**
     * Renderiza todos los campos de una subcategoría dentro de un contenedor.
     * @param {HTMLElement} container  - div donde se inyectará el HTML
     * @param {Object}      sub        - objeto subcategoría del schema
     * @param {string}      catColor   - color hex de la categoría padre
     */
    function renderFields(container, sub, catColor) {
        if (!sub?.fields?.length) { container.innerHTML = ''; return; }

        const html = `
      <div class="dyn-section">
        <div class="dyn-section-head" style="--cat-color:${catColor}">
          <div class="dyn-icon" style="background:${catColor}">
            <i class="fa-solid fa-list-check"></i>
          </div>
          <div>
            <div class="dyn-title">Campos específicos</div>
            <div class="dyn-sub">${sub.name}</div>
          </div>
        </div>
        <div class="dyn-body">
          <div class="dyn-grid" id="dyn-grid-root">
            ${sub.fields.map(f => renderField(f)).join('')}
          </div>
        </div>
      </div>`;

        container.innerHTML = html;

        // Inicializa counters
        container.querySelectorAll('.counter-input').forEach(initCounter);
        // Inicializa tag-inputs
        container.querySelectorAll('.tag-input-wrap').forEach(initTagInput);
        // Aplica visibilidad condicional inicial
        refreshConditionals(container, sub);
        // Escucha cambios para actualizar condicionales
        container.addEventListener('change', () => refreshConditionals(container, sub));
        container.addEventListener('input', () => refreshConditionals(container, sub));
    }

    /* ── Despachador por tipo ── */
    function renderField(f) {
        const wrapClass = `dyn-field-wrap ${f.type === 'textarea' || f.type === 'tag-input' || f.type === 'radio-grid' || f.type === 'date-range' ? 'dyn-full' : ''}`;
        const required = f.rules?.required ? '<span class="req-star">*</span>' : '';
        const showClass = f.showIf ? 'dyn-conditional' : '';

        return `<div class="${wrapClass} ${showClass}" data-field-id="${f.id}" data-show-if='${f.showIf ? JSON.stringify(f.showIf) : ''}'>
      <div class="dyn-field">
        <label class="dyn-label">${f.label}${required}</label>
        ${renderInput(f)}
        <div class="field-hint" id="${f.id}-hint"></div>
      </div>
    </div>`;
    }

    function renderInput(f) {
        switch (f.type) {
            case 'text': return `<input type="text"   id="${f.id}" class="dyn-input" placeholder="${f.placeholder || ''}" ${f.rules?.maxLength ? `maxlength="${f.rules.maxLength}"` : ''} />`;
            case 'number': return `<input type="number" id="${f.id}" class="dyn-input" placeholder="${f.placeholder || ''}" min="${f.rules?.min ?? 0}" ${f.rules?.max ? `max="${f.rules.max}"` : ''} step="0.01" />`;
            case 'date': return `<input type="date"   id="${f.id}" class="dyn-input" />`;
            case 'email': return `<input type="email"  id="${f.id}" class="dyn-input" placeholder="${f.placeholder || ''}" />`;
            case 'person': return `<div class="person-wrap"><i class="fa-solid fa-user person-icon"></i><input type="text" id="${f.id}" class="dyn-input person-input" placeholder="${f.placeholder || ''}" /></div>`;
            case 'textarea': return `<textarea id="${f.id}" class="dyn-input dyn-textarea" placeholder="${f.placeholder || ''}" ${f.rules?.maxLength ? `maxlength="${f.rules.maxLength}"` : ''}></textarea>`;
            case 'select': return renderSelect(f);
            case 'radio-grid': return renderRadioGrid(f);
            case 'counter': return renderCounter(f);
            case 'date-range': return renderDateRange(f);
            case 'tag-input': return renderTagInput(f);
            case 'currency': return `<div class="input-group-dyn"><span class="input-prefix-dyn">$</span><input type="number" id="${f.id}" class="dyn-input" placeholder="0.00" min="${f.rules?.min ?? 0}" step="0.01" /></div>`;
            default: return `<input type="text" id="${f.id}" class="dyn-input" placeholder="${f.placeholder || ''}" />`;
        }
    }

    function renderSelect(f) {
        const opts = f.options.map(o => `<option value="${o}">${o}</option>`).join('');
        return `<select id="${f.id}" class="dyn-input dyn-select"><option value="">— Selecciona —</option>${opts}</select>`;
    }

    function renderRadioGrid(f) {
        const cards = f.options.map(o => `
      <label class="rg-card" data-value="${o.value}">
        <input type="radio" name="${f.id}" value="${o.value}" class="rg-hidden-input" />
        <div class="rg-icon"><i class="${o.icon}"></i></div>
        <div class="rg-label">${o.label}</div>
        ${o.hint ? `<div class="rg-hint">${o.hint}</div>` : ''}
        <div class="rg-check"><i class="fa-solid fa-check"></i></div>
      </label>`).join('');
        return `<div class="rg-grid" id="${f.id}">${cards}</div>`;
    }

    function renderCounter(f) {
        const def = f.defaultValue ?? 1;
        return `
      <div class="counter-wrap">
        <button type="button" class="counter-btn counter-minus" data-target="${f.id}"><i class="fa-solid fa-minus"></i></button>
        <input type="number" id="${f.id}" class="dyn-input counter-input"
          value="${def}" min="${f.rules?.min ?? 0}" ${f.rules?.max ? `max="${f.rules.max}"` : ''} readonly />
        <button type="button" class="counter-btn counter-plus" data-target="${f.id}"><i class="fa-solid fa-plus"></i></button>
      </div>`;
    }

    function renderDateRange(f) {
        return `
      <div class="date-range-wrap">
        <input type="date" id="${f.id}-from" class="dyn-input" />
        <span class="date-range-sep"><i class="fa-solid fa-arrow-right"></i></span>
        <input type="date" id="${f.id}-to"   class="dyn-input" />
      </div>`;
    }

    function renderTagInput(f) {
        return `
      <div class="tag-input-wrap" id="${f.id}-wrap">
        <div class="tag-chips" id="${f.id}-chips"></div>
        <input type="text" class="tag-input-field" placeholder="${f.placeholder || 'Escribe y presiona Enter'}" data-target="${f.id}" />
        <input type="hidden" id="${f.id}" value="[]" />
      </div>`;
    }

    /* ── Inicialización de widgets ── */

    function initCounter(input) {
        const wrap = input.closest('.counter-wrap');
        if (!wrap) return;
        wrap.querySelector('.counter-minus')?.addEventListener('click', () => {
            const min = parseInt(input.min ?? 0, 10);
            const v = parseInt(input.value, 10);
            if (v > min) { input.value = v - 1; input.dispatchEvent(new Event('change', { bubbles: true })); }
        });
        wrap.querySelector('.counter-plus')?.addEventListener('click', () => {
            const max = input.max ? parseInt(input.max, 10) : Infinity;
            const v = parseInt(input.value, 10);
            if (v < max) { input.value = v + 1; input.dispatchEvent(new Event('change', { bubbles: true })); }
        });
    }

    function initTagInput(wrap) {
        const field = wrap.querySelector('.tag-input-field');
        const chipsEl = wrap.querySelector('.tag-chips');
        const hidden = wrap.querySelector('input[type=hidden]');
        if (!field || !chipsEl || !hidden) return;
        let tags = [];

        const refresh = () => {
            hidden.value = JSON.stringify(tags);
            chipsEl.innerHTML = tags.map((t, i) => `
        <span class="chip">${t}
          <button type="button" class="chip-rm" data-idx="${i}"><i class="fa-solid fa-xmark"></i></button>
        </span>`).join('');
            chipsEl.querySelectorAll('.chip-rm').forEach(btn => {
                btn.addEventListener('click', () => { tags.splice(+btn.dataset.idx, 1); refresh(); });
            });
        };

        field.addEventListener('keydown', e => {
            if ((e.key === 'Enter' || e.key === ',') && field.value.trim()) {
                e.preventDefault();
                tags.push(field.value.trim()); field.value = ''; refresh();
            }
        });
    }

    /* ── Visibilidad condicional ── */

    function refreshConditionals(container, sub) {
        const wraps = container.querySelectorAll('.dyn-conditional');
        wraps.forEach(wrap => {
            const rule = (() => { try { return JSON.parse(wrap.dataset.showIf || 'null'); } catch { return null; } })();
            if (!rule) return;
            const visible = evalCondition(rule, container);
            wrap.style.display = visible ? '' : 'none';
            if (!visible) clearField(container.querySelector(`#${wrap.dataset.fieldId}`));
        });
    }

    function evalCondition({ field, op, value }, container) {
        const el = container.querySelector(`#${field}`);
        if (!el) return false;
        const v = getFieldValue(el);
        switch (op) {
            case '==': return v === value;
            case '!=': return v !== value;
            case '>': return parseFloat(v) > parseFloat(value);
            case '<': return parseFloat(v) < parseFloat(value);
            case 'includes': return String(v).includes(value);
            default: return false;
        }
    }

    function getFieldValue(el) {
        if (!el) return '';
        // radio-grid: obtén el radio checked dentro del grid div
        if (el.classList.contains('rg-grid')) {
            return el.querySelector('input[type=radio]:checked')?.value ?? '';
        }
        return el.value ?? '';
    }

    function clearField(el) {
        if (!el) return;
        if (el.tagName === 'SELECT' || el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') el.value = '';
    }

    /* ─────────────────────────── VALIDACIÓN ───────────────────────────────── */

    /**
     * Valida todos los campos visibles del contenedor dinámico.
     * @returns {Array} lista de { id, message } de errores encontrados.
     */
    function validateDynamic(container, sub) {
        if (!sub?.fields?.length) return [];
        const errors = [];

        sub.fields.forEach(f => {
            const wrap = container.querySelector(`[data-field-id="${f.id}"]`);
            if (!wrap || wrap.style.display === 'none') return;      // campo oculto: skip

            const val = getFieldValueById(container, f.id);
            const hint = document.getElementById(`${f.id}-hint`);
            clearHint(hint);

            if (f.rules?.required && !val) {
                markError(container, f.id, `${f.label} es obligatorio`);
                errors.push({ id: f.id, message: `${f.label} es obligatorio` });
                return;
            }

            if (val && f.rules?.min !== undefined && parseFloat(val) < f.rules.min) {
                markError(container, f.id, `${f.label}: mínimo ${f.rules.min}`);
                errors.push({ id: f.id, message: `${f.label}: mínimo ${f.rules.min}` });
            }

            if (val && f.rules?.max !== undefined && parseFloat(val) > f.rules.max) {
                markError(container, f.id, `${f.label}: máximo ${f.rules.max}`);
                errors.push({ id: f.id, message: `${f.label}: máximo ${f.rules.max}` });
            }
        });

        return errors;
    }

    /**
     * Ejecuta las reglas de negocio de la subcategoría y devuelve alertas.
     * @returns {Array} lista de { message, level } (level: 'warn' | 'error')
     */
    function evalBusinessRules(sub, monto) {
        const alerts = [];
        if (!sub?.businessRules) return alerts;
        const br = sub.businessRules;

        // Límite de monto
        if (br.montoMax && monto > br.montoMax) {
            alerts.push({
                level: 'error',
                message: `El monto ($${fmt(monto)}) supera el máximo permitido para esta subcategoría ($${fmt(br.montoMax)}).`,
            });
        } else if (br.montoWarn && monto > br.montoWarn) {
            alerts.push({
                level: 'warn',
                message: `El monto ($${fmt(monto)}) supera el umbral de atención ($${fmt(br.montoWarn)}). Se requerirá documentación adicional.`,
            });
        }

        // Alertas personalizadas
        if (br.alertas?.length) {
            const fields = collectFieldValues();  // objeto { fieldId: value }
            br.alertas.forEach(a => {
                if (a.condition(fields, monto)) {
                    alerts.push({ level: a.level, message: a.message });
                }
            });
        }

        return alerts;
    }

    /**
     * Sugiere el aprobador según la subcategoría y el monto.
     */
    function suggestAprobador(sub, monto) {
        if (sub?.businessRules?.aprobadorSugerido) {
            return sub.businessRules.aprobadorSugerido(monto);
        }
        return getGlobalAprobador(monto);
    }

    /* ── Helpers DOM ── */

    function getFieldValueById(container, id) {
        const el = container?.querySelector(`#${id}`) ?? document.getElementById(id);
        if (!el) return '';
        if (el.classList.contains('rg-grid')) return el.querySelector('input:checked')?.value ?? '';
        return el.value?.trim() ?? '';
    }

    function collectFieldValues() {
        const out = {};
        document.querySelectorAll('[data-field-id]').forEach(wrap => {
            const id = wrap.dataset.fieldId;
            if (!id) return;
            const el = wrap.querySelector(`#${id}`);
            if (el) out[id] = getFieldValue(el);
        });
        return out;
    }

    function markError(container, id, msg) {
        const el = container?.querySelector(`#${id}`) ?? document.getElementById(id);
        const hint = document.getElementById(`${id}-hint`);
        if (el) { el.classList.add('dyn-input-error'); el.addEventListener('input', () => el.classList.remove('dyn-input-error'), { once: true }); }
        if (hint) { hint.textContent = msg; hint.className = 'field-hint hint-error'; }
    }

    function clearHint(hint) {
        if (hint) { hint.textContent = ''; hint.className = 'field-hint'; }
    }

    function fmt(n) {
        return n.toLocaleString('es-MX', { minimumFractionDigits: 0 });
    }

    /* ─────────────────────────── API PÚBLICA ──────────────────────────────── */
    return { renderFields, validateDynamic, evalBusinessRules, suggestAprobador, collectFieldValues };

})();

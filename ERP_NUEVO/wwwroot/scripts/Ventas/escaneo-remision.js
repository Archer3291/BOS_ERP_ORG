/**
 * Verificación de mercancía por escáner antes de aceptar una remisión.
 *
 * FLUJO: al generar la remisión se abre un modal con las partidas del documento y
 * la cantidad en 0. Cada código escaneado (la CLAVE del producto, cve_prod, que es
 * lo que trae el código de barras) marca su partida como escaneada y revela la
 * cantidad solicitada. Cuando no queda ninguna pendiente se confirma y el guardado
 * continúa; si se cancela, la remisión no se guarda.
 *
 * El setting `escaneo_remision_obligatorio` (tabla settings) decide el modo:
 *   true  → el botón de guardar abre el modal y no continúa hasta completarlo.
 *   false → se guarda directo; el modal queda disponible desde el botón
 *           "Verificar con escáner" para quien lo quiera usar.
 * El valor llega renderizado en `data-obligatorio` del modal y el backend lo vuelve
 * a leer al guardar (VNRemision.Guardar): esto de aquí es la capa de UX, no la de
 * seguridad.
 *
 * La pistola de código de barras es keyboard-wedge: teclea el código y manda Enter
 * (algunos mandan Tab). Por eso basta con mantener el foco en el input del modal.
 *
 * IDs esperados (derivados del prefix, ver ids()):
 *   {prefix}-modalEscaneo / -esc-input / -esc-tbody / -esc-bar / -esc-contador /
 *   -esc-feedback / -esc-confirmar / -esc-reiniciar / -esc-box / -esc-modo
 */
(function () {
    'use strict';

    // Unidades sin pieza física que escanear: los servicios entran ya verificados
    // (p. ej. SERVICIO DE CORTE, udm SRV, que la remisión consolida como partida).
    const UNIDADES_SIN_ESCANEO = ['SRV'];

    const registros = new Map();

    const $ = id => document.getElementById(id);
    const num = v => { const n = parseFloat(v); return Number.isFinite(n) ? n : 0; };
    const norm = v => (v === null || v === undefined ? '' : v.toString()).trim().toUpperCase();
    const escapar = t => { const d = document.createElement('div'); d.textContent = t ?? ''; return d.innerHTML; };
    // Para atributos: las descripciones traen comillas (p. ej. TUBO 2") y romperían el HTML.
    const attr = t => escapar(t).replace(/"/g, '&quot;');
    const fmtCant = n => (Math.round(num(n) * 100) / 100).toString();

    function ids(prefix) {
        return {
            modal: `${prefix}-modalEscaneo`,
            input: `${prefix}-esc-input`,
            box: `${prefix}-esc-box`,
            tbody: `${prefix}-esc-tbody`,
            bar: `${prefix}-esc-bar`,
            contador: `${prefix}-esc-contador`,
            feedback: `${prefix}-esc-feedback`,
            confirmar: `${prefix}-esc-confirmar`,
            reiniciar: `${prefix}-esc-reiniciar`,
            modo: `${prefix}-esc-modo`
        };
    }

    /* ── Aviso sonoro: en almacén el operador no mira la pantalla en cada disparo ── */
    let audioCtx = null;
    function pitido(ok) {
        try {
            const Ctx = window.AudioContext || window.webkitAudioContext;
            if (!Ctx) return;
            audioCtx = audioCtx || new Ctx();
            const osc = audioCtx.createOscillator();
            const gain = audioCtx.createGain();
            osc.connect(gain);
            gain.connect(audioCtx.destination);
            osc.type = 'square';
            osc.frequency.value = ok ? 880 : 220;
            gain.gain.setValueAtTime(0.04, audioCtx.currentTime);
            osc.start();
            osc.stop(audioCtx.currentTime + (ok ? 0.08 : 0.22));
        } catch (e) {
            /* sin audio disponible: el feedback visual basta */
        }
    }

    /**
     * @param {object} cfg
     * @param {string} cfg.prefix           'vn-rem'
     * @param {function} cfg.getProductos   devuelve las partidas capturadas del documento
     * @param {string} [cfg.btnAbrirId]     botón para abrir el modal a mano (modo opcional)
     */
    function registrar(cfg) {
        if (!cfg || !cfg.prefix) return;

        const el = ids(cfg.prefix);
        const modalEl = $(el.modal);
        if (!modalEl) return;   // la vista no incluye el modal: el módulo se queda inerte

        const reg = {
            cfg,
            el,
            modalEl,
            partidas: [],
            firma: '',          // firma de la lista verificada (cambia si editan partidas)
            verificado: false,
            bloqueante: false,  // abierto desde el guardado (true) o a mano (false)
            resolver: null,
            flash: -1           // índice de la fila recién escaneada
        };

        registros.set(cfg.prefix, reg);
        enlazar(reg);
    }

    function enlazar(reg) {
        const input = $(reg.el.input);

        input?.addEventListener('keydown', e => {
            // La pistola termina el código con Enter (algunas con Tab).
            if (e.key !== 'Enter' && e.key !== 'Tab') return;
            e.preventDefault();
            const codigo = input.value;
            input.value = '';
            procesar(reg, codigo);
        });

        // El escáner solo funciona si el input tiene el foco: se recupera al abrir
        // el modal y cada vez que se hace clic en un espacio no interactivo.
        reg.modalEl.addEventListener('shown.bs.modal', () => enfocar(reg));
        reg.modalEl.addEventListener('click', e => {
            if (e.target.closest('button, input, select, textarea, a')) return;
            enfocar(reg);
        });

        // Cerrar sin confirmar = cancelar la verificación (y con ella el guardado).
        reg.modalEl.addEventListener('hidden.bs.modal', () => {
            const resolver = reg.resolver;
            reg.resolver = null;
            if (resolver) resolver(reg.verificado);
        });

        $(reg.el.confirmar)?.addEventListener('click', () => {
            if (pendientes(reg) > 0) return;
            reg.verificado = true;
            bootstrap.Modal.getInstance(reg.modalEl)?.hide();
        });

        $(reg.el.reiniciar)?.addEventListener('click', () => {
            reg.partidas.forEach(p => { p.escaneado = !p.requiere; });
            reg.flash = -1;
            reg.verificado = false;
            render(reg);
            feedback(reg, '', '');
            enfocar(reg);
        });

        // Deshacer una partida escaneada por error.
        $(reg.el.tbody)?.addEventListener('click', e => {
            const btn = e.target.closest('[data-esc-undo]');
            if (!btn) return;
            const partida = reg.partidas[parseInt(btn.dataset.escUndo, 10)];
            if (!partida || !partida.requiere) return;
            partida.escaneado = false;
            reg.verificado = false;
            render(reg);
            enfocar(reg);
        });

        if (reg.cfg.btnAbrirId) {
            $(reg.cfg.btnAbrirId)?.addEventListener('click', () => abrir(reg.cfg.prefix, false));
        }
    }

    function enfocar(reg) {
        const input = $(reg.el.input);
        if (input) setTimeout(() => input.focus(), 30);
    }

    function esObligatorio(reg) {
        return (reg.modalEl.dataset.obligatorio || '').toLowerCase() === 'true';
    }

    function productosDe(reg) {
        const lista = typeof reg.cfg.getProductos === 'function' ? reg.cfg.getProductos() : [];
        return Array.isArray(lista) ? lista : [];
    }

    // Si cambian las partidas o sus cantidades, lo verificado deja de valer.
    function firmaDe(productos) {
        return productos.map(p => `${norm(p.productoId)}:${num(p.cantidad)}`).join('|');
    }

    function construirPartidas(productos) {
        return productos.map(p => {
            const unidad = p.unidad || p.udm || '';
            const requiere = !UNIDADES_SIN_ESCANEO.includes(norm(unidad));
            return {
                clave: norm(p.productoId),
                claveMostrar: (p.productoId ?? '').toString(),
                descripcion: p.descripcion || '',
                cantidad: num(p.cantidad),
                unidad,
                requiere,
                escaneado: !requiere    // los servicios entran ya dados por buenos
            };
        });
    }

    const pendientes = reg => reg.partidas.filter(p => !p.escaneado).length;

    function render(reg) {
        const tbody = $(reg.el.tbody);
        if (!tbody) return;

        if (reg.partidas.length === 0) {
            tbody.innerHTML = `
                <tr><td colspan="6" class="text-center text-muted py-4">
                    Sin partidas que verificar
                </td></tr>`;
        } else {
            tbody.innerHTML = reg.partidas.map((p, i) => {
                const estado = !p.requiere
                    ? `<span class="vn-esc-badge vn-esc-badge-na"><i class="fas fa-minus"></i> No aplica</span>`
                    : p.escaneado
                        ? `<span class="vn-esc-badge vn-esc-badge-ok"><i class="fas fa-check"></i> Escaneado</span>
                           <button type="button" class="vn-esc-undo" data-esc-undo="${i}" title="Deshacer">
                               <i class="fas fa-rotate-left"></i>
                           </button>`
                        : `<span class="vn-esc-badge vn-esc-badge-pendiente"><i class="fas fa-clock"></i> Pendiente</span>`;

                // La cantidad arranca en 0 y solo se revela cuando la partida se escanea.
                const cantidad = p.escaneado ? fmtCant(p.cantidad) : '0';

                return `
                <tr class="${p.escaneado ? 'is-escaneado' : 'is-pendiente'}${reg.flash === i ? ' is-flash' : ''}">
                    <td class="text-center fw-bold">${i + 1}</td>
                    <td class="vn-esc-codigo">${escapar(p.claveMostrar)}</td>
                    <td style="max-width:340px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;"
                        title="${attr(p.descripcion)}">${escapar(p.descripcion)}</td>
                    <td class="text-end vn-esc-cant ${p.escaneado ? 'is-lista' : 'is-cero'}">${cantidad}</td>
                    <td>${escapar(p.unidad)}</td>
                    <td>${estado}</td>
                </tr>`;
            }).join('');
        }

        const total = reg.partidas.length;
        const hechas = total - pendientes(reg);

        const contador = $(reg.el.contador);
        if (contador) contador.textContent = `${hechas} de ${total}`;

        const bar = $(reg.el.bar);
        if (bar) bar.style.width = total === 0 ? '0%' : `${Math.round((hechas / total) * 100)}%`;

        const btn = $(reg.el.confirmar);
        if (btn) {
            const completo = total > 0 && hechas === total;
            btn.disabled = !completo;
            btn.innerHTML = completo
                ? '<i class="fas fa-check"></i> Confirmar y continuar'
                : `<i class="fas fa-check"></i> Confirmar (${hechas}/${total})`;
        }
    }

    function feedback(reg, tipo, mensaje) {
        const el = $(reg.el.feedback);
        if (el) {
            el.className = `vn-esc-feedback mb-2${tipo ? ' vn-esc-feedback-' + tipo : ''}`;
            const icono = tipo === 'ok' ? 'fa-circle-check'
                : tipo === 'warn' ? 'fa-triangle-exclamation'
                    : tipo === 'error' ? 'fa-circle-xmark' : '';
            el.innerHTML = mensaje ? `<i class="fas ${icono}"></i> ${escapar(mensaje)}` : '';
        }

        const box = $(reg.el.box);
        if (box) {
            box.classList.remove('is-ok', 'is-error');
            if (tipo === 'ok') box.classList.add('is-ok');
            if (tipo === 'error' || tipo === 'warn') box.classList.add('is-error');
            setTimeout(() => box.classList.remove('is-ok', 'is-error'), 700);
        }
    }

    function procesar(reg, codigoCrudo) {
        const codigo = norm(codigoCrudo);
        if (!codigo) return;

        // Se marca la PRIMERA partida pendiente con esa clave: un producto puede venir
        // en dos partidas (p. ej. consolidado de stock + modula) y cada escaneo cierra una.
        const idx = reg.partidas.findIndex(p => p.requiere && !p.escaneado && p.clave === codigo);

        if (idx >= 0) {
            const partida = reg.partidas[idx];
            partida.escaneado = true;
            reg.flash = idx;
            render(reg);
            reg.flash = -1;
            feedback(reg, 'ok',
                `${partida.claveMostrar} — ${fmtCant(partida.cantidad)} ${partida.unidad || ''}`.trim());
            pitido(true);

            if (pendientes(reg) === 0) {
                feedback(reg, 'ok', 'Todas las partidas verificadas. Puedes confirmar.');
                setTimeout(() => $(reg.el.confirmar)?.focus(), 60);
            }
            return;
        }

        if (reg.partidas.some(p => p.clave === codigo)) {
            feedback(reg, 'warn', `El código ${codigoCrudo.trim()} ya está verificado en esta remisión`);
            pitido(false);
            return;
        }

        feedback(reg, 'error', `El código ${codigoCrudo.trim()} no pertenece a esta remisión`);
        pitido(false);
    }

    /**
     * Abre el modal y devuelve una promesa: true si se confirmó la verificación,
     * false si se cerró sin completarla.
     */
    function abrir(prefix, bloqueante) {
        const reg = registros.get(prefix);
        if (!reg) return Promise.resolve(true);

        const productos = productosDe(reg);
        if (productos.length === 0) {
            window.toastMixin?.fire({ icon: 'warning', title: 'La remisión no tiene partidas que verificar' });
            return Promise.resolve(true);
        }

        reg.bloqueante = !!bloqueante;
        reg.partidas = construirPartidas(productos);
        reg.firma = firmaDe(productos);
        reg.verificado = false;
        reg.flash = -1;

        const input = $(reg.el.input);
        if (input) input.value = '';
        feedback(reg, '', '');
        render(reg);

        // backdrop estático: un clic fuera no debe tirar el conteo a medias.
        const modal = bootstrap.Modal.getOrCreateInstance(reg.modalEl, {
            backdrop: 'static',
            keyboard: false
        });

        return new Promise(resolve => {
            reg.resolver = resolve;
            modal.show();
        });
    }

    /**
     * Puerta de entrada del guardado. Devuelve true cuando la remisión puede
     * continuar: porque la verificación no es obligatoria, porque ya se hizo sobre
     * estas mismas partidas, o porque el usuario acaba de completarla.
     */
    async function asegurar(prefix) {
        const reg = registros.get(prefix);
        if (!reg || !esObligatorio(reg)) return true;

        const productos = productosDe(reg);
        if (productos.length === 0) return true;                 // nada que verificar
        if (reg.verificado && reg.firma === firmaDe(productos)) return true;

        return await abrir(prefix, true);
    }

    /** Resultado de la verificación para mandarlo al backend (escaneoJSON). */
    function datos(prefix) {
        const reg = registros.get(prefix);
        if (!reg) return [];
        return reg.partidas.map(p => ({
            productoId: p.claveMostrar,
            unidad: p.unidad,
            cantidad: p.cantidad,
            cantidadEscaneada: p.escaneado ? p.cantidad : 0,
            escaneado: !!p.escaneado,
            requiere: !!p.requiere
        }));
    }

    /** Invalida lo verificado (p. ej. tras cargar otro documento). */
    function limpiar(prefix) {
        const reg = registros.get(prefix);
        if (!reg) return;
        reg.partidas = [];
        reg.firma = '';
        reg.verificado = false;
    }

    function obligatorio(prefix) {
        const reg = registros.get(prefix);
        return !!reg && esObligatorio(reg);
    }

    window.EscaneoRemision = { registrar, abrir, asegurar, datos, limpiar, obligatorio };
})();

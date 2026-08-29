/**
 * Validación de crédito compartida por los documentos de venta
 * (cotización → pedido → remisión → factura).
 *
 * REGLA: solo aplica cuando el tipo de pago es CRÉDITO. En contado o anticipo el
 * cliente paga al momento, no se compromete línea de crédito y el módulo no pinta
 * banner, no avisa y no bloquea nada.
 *
 * Modos:
 *   'advertir'  → informa el estado del crédito, nunca bloquea (cotización).
 *   'bloquear'  → además deshabilita el botón de guardar y ofrece pedir
 *                 autorización al gerente (pedido, remisión, factura).
 *
 * IDs esperados (derivados del prefix, mismo patrón en los 4 documentos):
 *   {prefix}-credit-banner / -icon / -title / -sub / -bar / -bar-wrap / -pct
 *
 * El backend vuelve a validar lo mismo en cada Guardar (Helpers/CreditoVentasHelper.cs);
 * lo de aquí es la capa de UX, no la de seguridad.
 */
(function () {
    'use strict';

    const UMBRAL_AVISO = 0.8;   // a partir de 80% de uso se avisa, sin bloquear
    const registros = new Map();

    const fmt = n => (Number(n) || 0).toLocaleString('es-MX', {
        style: 'currency', currency: 'MXN'
    });

    const $ = id => document.getElementById(id);
    const num = v => {
        const n = parseFloat(v);
        return Number.isFinite(n) ? n : 0;
    };

    /**
     * Normaliza el cliente venga de donde venga: /DatosGenerales/BuscarCliente
     * (porcentaje_uso) o /DatosGenerales/BuscarDocumento (porcentaje_credito).
     */
    function normalizarCliente(cliente) {
        if (!cliente) return null;
        return {
            id: cliente.id || cliente.cliente || '',
            descripcion: cliente.descripcion || cliente.n_cli || '',
            limite: num(cliente.lim_crd),
            usado: num(cliente.credito_usado),
            disponible: num(cliente.credito_disponible),
            estatusCliente: (cliente.estatus_cliente || '').toString().toLowerCase()
        };
    }

    function ids(prefix) {
        return {
            banner: `${prefix}-credit-banner`,
            icon: `${prefix}-credit-icon`,
            title: `${prefix}-credit-title`,
            sub: `${prefix}-credit-sub`,
            bar: `${prefix}-credit-bar`,
            barWrap: `${prefix}-credit-bar-wrap`,
            pct: `${prefix}-credit-pct`,
            btnGerente: `${prefix}-btn-email-gerente`
        };
    }

    /**
     * @param {object} cfg
     * @param {string} cfg.prefix            'vi-cot' | 'vi-ped' | 'vi-rem' | 'vi-fac'
     * @param {string} cfg.modo              'advertir' | 'bloquear'
     * @param {string} cfg.documento         'cotizacion' | 'pedido' | 'remision' | 'factura'
     * @param {function} cfg.getTipoPago     devuelve 'contado' | 'credito' | 'anticipo'
     * @param {string} cfg.totalId           input hidden con el total del documento
     * @param {string} cfg.submitSelector    botón de guardar
     * @param {string} [cfg.clienteInputId]  input con la clave del cliente
     * @param {string} [cfg.folioId]
     * @param {string} [cfg.documentIdId]
     * @param {function} [cfg.getProductos]  para adjuntar el detalle al correo del gerente
     * @param {function} [cfg.getTotales]
     */
    function registrar(cfg) {
        if (!cfg || !cfg.prefix) return;
        registros.set(cfg.prefix, {
            cfg: Object.assign({
                modo: 'advertir',
                documento: 'documento',
                claseBanner: 'vi-credit-banner',
                endpointSolicitud: '/VIPedido/EnviarSolicitudGerente'
            }, cfg),
            el: ids(cfg.prefix),
            cliente: null,
            autorizado: false,     // el gerente ya autorizó este documento (o un padre)
            autorizacionDocId: null // documento para el que se consultó, para no repetir
        });
    }

    /** Guarda los datos de crédito del cliente y repinta. */
    function setCliente(prefix, cliente) {
        const reg = registros.get(prefix);
        if (!reg) return;

        const anterior = reg.cliente?.id;
        reg.cliente = normalizarCliente(cliente);

        // Al cambiar de cliente empieza otra operación: ni la autorización ni el pedido
        // pendiente del cliente anterior aplican.
        if (anterior && anterior !== reg.cliente?.id) {
            reg.token = null;
            reg.autorizado = false;
            reg.autorizacionDocId = null;
            reg.documentoGenerado = false;
            reg.folioGenerado = null;
        }

        evaluar(prefix);
    }

    function limpiarCliente(prefix) {
        const reg = registros.get(prefix);
        if (!reg) return;
        reg.cliente = null;
        evaluar(prefix);
    }

    function esCredito(prefix) {
        const reg = registros.get(prefix);
        if (!reg) return false;
        const tipo = (reg.cfg.getTipoPago ? reg.cfg.getTipoPago() : '') || '';
        return tipo.toString().trim().toLowerCase() === 'credito';
    }

    /** Estado actual del crédito con el total que hoy tiene el documento. */
    function estado(prefix) {
        const reg = registros.get(prefix);
        if (!reg) return null;

        if (!esCredito(prefix)) return { aplica: false, bloquea: false };
        if (!reg.cliente) return { aplica: false, bloquea: false };

        const c = reg.cliente;
        const total = num($(reg.cfg.totalId)?.value);
        const comprometido = c.usado + total;
        const suspendido = c.estatusCliente === 'suspendido';
        const sinLimite = c.limite <= 0;
        const excedido = !sinLimite && comprometido > c.limite;
        const porVencer = !sinLimite && !excedido && (comprometido / c.limite) >= UMBRAL_AVISO;

        // Si gerencia ya autorizó este documento (o el pedido del que viene), no se vuelve
        // a frenar en los pasos siguientes: se informa, pero se deja continuar.
        const bloquea = reg.cfg.modo === 'bloquear'
            && (suspendido || excedido)
            && !reg.autorizado;

        return {
            aplica: true,
            cliente: c,
            total,
            comprometido,
            suspendido,
            sinLimite,
            excedido,
            porVencer,
            bloquea,
            autorizado: !!reg.autorizado,
            porcentaje: sinLimite ? 0 : (comprometido / c.limite) * 100
        };
    }

    /**
     * Consulta al servidor si el documento cargado ya trae autorización de gerencia.
     * Solo pregunta una vez por documento; sin documento (captura desde cero) no hay
     * autorización posible y se bloquea normalmente.
     */
    async function refrescarAutorizacion(prefix) {
        const reg = registros.get(prefix);
        if (!reg) return false;

        const docId = ($(reg.cfg.documentIdId)?.value || '').toString().trim();
        const hayDocumento = !!docId && docId !== '0';

        // Con un documento cargado la autorización DEBE venir de su propia cadena. El token
        // suelto y el pedido pendiente pertenecen a otra captura (misma pantalla, mismo
        // cliente, otro documento) y reutilizarlos hacía que una cotización recién creada
        // apareciera ya autorizada.
        if (hayDocumento) {
            olvidarTokenSolicitud(prefix);
            reg.documentoGenerado = false;
            reg.folioGenerado = null;
        }

        const token = hayDocumento ? '' : tokenSolicitud(prefix);

        // Sin documento y sin token no hay forma de tener autorización.
        if (!hayDocumento && !token) {
            reg.autorizado = false;
            reg.autorizacionDocId = null;
            return false;
        }

        // Clave de lo ya consultado: documento + token + total, para no repetir la llamada.
        // El total entra porque la autorización por token está topada al monto aprobado.
        const total = num($(reg.cfg.totalId)?.value);
        const clave = `${docId || '0'}|${token || ''}|${total}`;
        if (reg.autorizacionDocId === clave) return reg.autorizado;

        try {
            const cliente = $(reg.cfg.clienteInputId)?.value || '';
            const resp = await fetch(
                `/DatosGenerales/ConsultarAutorizacionCredito?documentoId=${encodeURIComponent(docId || 0)}` +
                `&cliente=${encodeURIComponent(cliente)}` +
                `&token=${encodeURIComponent(token || '')}` +
                `&total=${encodeURIComponent(total)}`);
            const data = await resp.json();

            reg.autorizado = !!data.autorizado;
            reg.autorizacionDocId = clave;
        } catch (err) {
            console.warn('No se pudo verificar la autorización de crédito:', err);
            reg.autorizado = false;
            reg.autorizacionDocId = null;
        }

        evaluar(prefix);
        return reg.autorizado;
    }

    // ── Token de la solicitud ────────────────────────────────────────────────────
    // Cuando el documento aún no existe (un pedido capturado sin cotización de origen),
    // la autorización no se puede colgar de ningún encabezado: se identifica por el token
    // que devolvió la solicitud. Se guarda por prefix + cliente en sessionStorage para que
    // sobreviva a una recarga mientras el vendedor espera la respuesta del gerente.

    function claveToken(prefix) {
        const reg = registros.get(prefix);
        const cliente = $(reg?.cfg.clienteInputId)?.value || '';
        return `creditoVentas:${prefix}:${cliente}`;
    }

    function guardarTokenSolicitud(prefix, token) {
        const reg = registros.get(prefix);
        if (!reg || !token) return;

        reg.token = token;
        try { sessionStorage.setItem(claveToken(prefix), token); } catch { /* modo privado */ }
    }

    function tokenSolicitud(prefix) {
        const reg = registros.get(prefix);
        if (!reg) return '';
        if (reg.token) return reg.token;

        try { return sessionStorage.getItem(claveToken(prefix)) || ''; }
        catch { return ''; }
    }

    function olvidarTokenSolicitud(prefix) {
        const reg = registros.get(prefix);
        if (!reg) return;

        reg.token = null;
        reg.autorizado = false;
        reg.autorizacionDocId = null;
        try { sessionStorage.removeItem(claveToken(prefix)); } catch { /* modo privado */ }
    }

    /** Recalcula banner y bloqueo. Llamar al cambiar cliente, total o tipo de pago. */
    function evaluar(prefix) {
        const reg = registros.get(prefix);
        if (!reg) return;

        const st = estado(prefix);
        const banner = $(reg.el.banner);

        // El documento ya se generó en espera de autorización: no se guarda de nuevo desde
        // aquí (sería un duplicado) ni se puede volver a solicitar. Va antes del chequeo de
        // "aplica" para que cambiar el toggle a contado no lo desbloquee.
        if (reg.documentoGenerado) {
            pintarBannerPendiente(reg);
            aplicarBloqueo(reg, true,
                `El pedido ${reg.folioGenerado || ''} está pendiente de autorización`.trim());
            quitarBotonGerente(reg);
            return;
        }

        if (!st || !st.aplica) {
            if (banner) banner.style.display = 'none';
            quitarBotonGerente(reg);
            aplicarBloqueo(reg, false, '');
            return;
        }

        // Si va a bloquear pero aún no se ha consultado, puede que ya venga autorizado
        // (desde el pedido de origen o por una solicitud propia): se pregunta y evaluar()
        // se repite con la respuesta.
        // Mismo criterio que refrescarAutorizacion: con documento cargado manda su cadena;
        // el token solo cuenta cuando el documento aún no existe.
        const docId = ($(reg.cfg.documentIdId)?.value || '').toString().trim();
        const hayDocumento = !!docId && docId !== '0';
        const token = hayDocumento ? '' : tokenSolicitud(reg.cfg.prefix);
        const clave = `${docId || '0'}|${token || ''}|${st.total}`;

        if ((st.suspendido || st.excedido)
            && reg.cfg.modo === 'bloquear'
            && (hayDocumento || token)
            && reg.autorizacionDocId !== clave) {
            refrescarAutorizacion(reg.cfg.prefix);
        }

        pintarBanner(reg, st);
        aplicarBloqueo(reg, st.bloquea, st.suspendido
            ? 'El cliente está suspendido'
            : 'El documento supera el crédito disponible');

        // Con autorización vigente no se vuelve a pedir otra.
        const requiereAutorizacion = reg.cfg.modo === 'bloquear'
            && (st.suspendido || st.excedido)
            && !st.autorizado;

        if (requiereAutorizacion) insertarBotonGerente(reg, st);
        else quitarBotonGerente(reg);
    }

    /** Banner de "ya se solicitó y el documento está esperando resolución". */
    function pintarBannerPendiente(reg) {
        const banner = $(reg.el.banner);
        if (!banner) return;

        banner.className = reg.cfg.claseBanner;
        banner.classList.add('por-vencer');
        banner.style.display = 'flex';

        const icon = $(reg.el.icon);
        const title = $(reg.el.title);
        const sub = $(reg.el.sub);
        const barWrap = $(reg.el.barWrap);
        const pct = $(reg.el.pct);

        if (icon) icon.textContent = '⏳';
        if (title) {
            title.textContent = reg.folioGenerado
                ? `Pedido ${reg.folioGenerado} pendiente de autorización`
                : 'Pedido pendiente de autorización';
        }
        if (sub) {
            sub.textContent = 'Se generó y quedó en espera de que gerencia autorice el crédito. ' +
                              'No es necesario volver a guardarlo.';
        }
        if (barWrap) barWrap.style.display = 'none';
        if (pct) pct.textContent = '';
    }

    function pintarBanner(reg, st) {
        const banner = $(reg.el.banner);
        if (!banner) return;

        const icon = $(reg.el.icon);
        const title = $(reg.el.title);
        const sub = $(reg.el.sub);
        const bar = $(reg.el.bar);
        const barWrap = $(reg.el.barWrap);
        const pct = $(reg.el.pct);
        const c = st.cliente;

        banner.className = reg.cfg.claseBanner;
        banner.style.display = 'flex';

        if (st.sinLimite) {
            banner.classList.add('sin-limite');
            if (icon) icon.textContent = 'ℹ️';
            if (title) title.textContent = 'Cliente sin límite de crédito configurado';
            if (sub) sub.textContent = 'No se aplicarán restricciones de crédito';
            if (barWrap) barWrap.style.display = 'none';
            if (pct) pct.textContent = '';
            return;
        }

        if (barWrap) barWrap.style.display = '';
        if (bar) bar.style.width = Math.min(st.porcentaje, 100) + '%';
        if (pct) pct.textContent = st.porcentaje.toFixed(1) + '%';
        if (sub) {
            sub.textContent =
                `Límite: ${fmt(c.limite)} · Usado: ${fmt(c.usado)} · ` +
                `Disponible: ${fmt(c.limite - c.usado)} · Documento: ${fmt(st.total)}`;
        }

        // Autorizado por gerencia: se informa la situación del crédito, pero sin alarma
        // ni bloqueo, porque la operación ya fue aprobada en un paso anterior.
        if (st.autorizado && (st.suspendido || st.excedido)) {
            banner.classList.add('por-vencer');
            if (icon) icon.textContent = '🔓';
            if (title) {
                title.textContent = st.suspendido
                    ? 'Cliente suspendido — operación autorizada por gerencia'
                    : 'Crédito excedido — operación autorizada por gerencia';
            }
            return;
        }

        if (st.suspendido) {
            banner.classList.add('excedido');
            if (icon) icon.textContent = '🚫';
            if (title) {
                title.textContent = reg.cfg.modo === 'bloquear'
                    ? 'Cliente suspendido — no se puede continuar'
                    : 'Cliente suspendido';
            }
            return;
        }

        if (st.excedido) {
            banner.classList.add('excedido');
            if (icon) icon.textContent = '🚫';
            if (title) {
                title.textContent = reg.cfg.modo === 'bloquear'
                    ? 'Crédito excedido — el documento supera el límite disponible'
                    : 'Crédito excedido — el pedido no podrá generarse sin autorización';
            }
            return;
        }

        if (st.porVencer) {
            banner.classList.add('por-vencer');
            if (icon) icon.textContent = '⚠️';
            if (title) title.textContent = 'Crédito próximo al límite';
            return;
        }

        banner.classList.add('disponible');
        if (icon) icon.textContent = '✅';
        if (title) title.textContent = 'Crédito disponible';
    }

    /**
     * Solo revierte el bloqueo que puso este módulo; si el formulario está en modo
     * consulta u otra rutina deshabilitó el botón, no se toca.
     */
    function aplicarBloqueo(reg, bloquear, motivo) {
        const btn = document.querySelector(reg.cfg.submitSelector);
        if (!btn) return;

        if (bloquear) {
            btn.disabled = true;
            btn.dataset.creditoBloqueado = '1';
            btn.title = motivo;
            return;
        }

        if (btn.dataset.creditoBloqueado === '1') {
            btn.disabled = false;
            btn.title = '';
            delete btn.dataset.creditoBloqueado;
        }
    }

    function quitarBotonGerente(reg) {
        $(reg.el.btnGerente)?.remove();
    }

    function insertarBotonGerente(reg, st) {
        if ($(reg.el.btnGerente)) return;

        const anclaje = $(reg.el.sub) || $(reg.el.banner);
        if (!anclaje) return;

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.id = reg.el.btnGerente;
        btn.title = 'Solicitar autorización al gerente por correo';
        btn.style.cssText = `
            margin-top: 6px;
            padding: 6px 14px;
            border-radius: 8px;
            border: 1.5px solid #dc2626;
            background: white;
            color: #dc2626;
            font-size: 12px;
            font-weight: 600;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 6px;
            white-space: nowrap;
            transition: background 0.2s, color 0.2s;
        `;
        btn.innerHTML = '✉️ Solicitar autorización al gerente';
        btn.addEventListener('mouseenter', () => {
            btn.style.background = '#dc2626'; btn.style.color = 'white';
        });
        btn.addEventListener('mouseleave', () => {
            btn.style.background = 'white'; btn.style.color = '#dc2626';
        });
        btn.addEventListener('click', () => solicitarAutorizacion(reg.cfg.prefix));

        anclaje.insertAdjacentElement('afterend', btn);
    }

    /** Correo al gerente con el detalle del documento y el link de autorización. */
    async function solicitarAutorizacion(prefix) {
        const reg = registros.get(prefix);
        if (!reg || !window.Swal) return;

        const st = estado(prefix);
        if (!st || !st.aplica) return;

        const c = st.cliente;
        const clienteId = $(reg.cfg.clienteInputId)?.value || c.id || '';
        const folio = $(reg.cfg.folioId)?.value || 'Sin folio';

        const { value: correo } = await Swal.fire({
            title: 'Solicitar autorización',
            html: `
                <p style="margin-bottom:12px; font-size:13px; color:#475569; text-align:left;">
                    El cliente <strong>${clienteId}</strong>
                    ${st.suspendido ? 'está <strong>suspendido</strong>' : 'excede su crédito disponible'}.<br>
                    Se enviará un correo al gerente con el detalle para que autorice la operación.
                </p>
                <div style="background:#fef2f2; border:1px solid #fecaca; border-radius:8px; padding:10px 14px; margin-bottom:14px; text-align:left; font-size:12px; color:#991b1b;">
                    <strong>Límite:</strong> ${fmt(c.limite)} &nbsp;·&nbsp;
                    <strong>Usado:</strong> ${fmt(c.usado)} &nbsp;·&nbsp;
                    <strong>Disponible:</strong> ${fmt(c.limite - c.usado)}<br>
                    <strong>Total del documento:</strong> ${fmt(st.total)}
                </div>
                <input id="swal-gerente-email" class="swal2-input" type="email"
                       placeholder="Correo del gerente" style="margin:0; width:100%;">
            `,
            confirmButtonText: 'Enviar solicitud',
            cancelButtonText: 'Cancelar',
            showCancelButton: true,
            focusConfirm: false,
            preConfirm() {
                const email = document.getElementById('swal-gerente-email')?.value?.trim();
                if (!email || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
                    Swal.showValidationMessage('Ingresa un correo válido');
                    return false;
                }
                return email;
            }
        });

        if (!correo) return;

        const productos = reg.cfg.getProductos ? (reg.cfg.getProductos() || []) : [];
        const totales = reg.cfg.getTotales ? (reg.cfg.getTotales() || {}) : {};

        // Si el documento sabe armar su formulario completo, se manda entero: hay canales
        // (Nacional) que generan el pedido junto con la solicitud, y necesitan todos los
        // campos para repartir las partidas igual que en un guardado normal.
        const formData = reg.cfg.getFormData
            ? (reg.cfg.getFormData() || new FormData())
            : new FormData();
        // set (no append): con getFormData algunos de estos campos ya vienen del formulario.
        formData.set('gerenteEmail', correo);
        formData.set('folio', folio);
        formData.set('cliente', clienteId);
        formData.set('pedido', $(reg.cfg.documentIdId)?.value?.trim() || '');
        formData.set('origen', reg.cfg.documento);
        // set en todos: limiteCredito y productosJSON también los manda el formulario, y un
        // campo repetido llega al servidor como "valor1,valor2" (rompería el JSON).
        formData.set('limiteCredito', c.limite.toFixed(2));
        formData.set('creditoUsado', c.usado.toFixed(2));
        formData.set('creditoDisp', (c.limite - c.usado).toFixed(2));
        formData.set('totalPedido', st.total.toFixed(2));
        formData.set('productosJSON', JSON.stringify(productos));
        formData.set('totalStr', JSON.stringify(totales));

        // Configuración capturada en este formulario (forma de pago, uso CFDI, plazo…).
        // La usan los canales que reconstruyen el pedido al autorizar en vez de generarlo
        // junto con la solicitud.
        if (reg.cfg.getConfiguracion) {
            formData.set('configuracionJSON',
                JSON.stringify(reg.cfg.getConfiguracion() || {}));
        }

        try {
            Swal.fire({
                title: 'Enviando...', allowOutsideClick: false,
                didOpen: () => Swal.showLoading()
            });

            const resp = await fetch(reg.cfg.endpointSolicitud, {
                method: 'POST', body: formData
            });
            const result = await resp.json();

            if (result.success) {
                // Se conserva el token: es lo que permitirá reconocer la autorización
                // cuando el gerente la apruebe, incluso si el documento aún no existe.
                guardarTokenSolicitud(prefix, result.token);

                // Con getFormData el documento ya quedó generado: no se puede volver a
                // solicitar desde esta pantalla o se crearía un pedido duplicado.
                if (reg.cfg.getFormData) {
                    reg.documentoGenerado = true;
                    reg.folioGenerado = result.folio_generado || folio;
                    evaluar(prefix);
                }

                // Con getFormData el documento ya quedó generado en espera de la autorización;
                // sin él, el vendedor tendrá que volver a guardarlo cuando lo autoricen.
                Swal.fire({
                    icon: 'success', title: 'Solicitud enviada',
                    html: reg.cfg.getFormData
                        ? `El gerente (<strong>${correo}</strong>) fue notificado.<br>` +
                          `El pedido <strong>${result.folio_generado || folio}</strong> quedó ` +
                          `registrado en espera de su autorización.`
                        : `El gerente (<strong>${correo}</strong>) fue notificado. En cuanto ` +
                          `autorice, vuelve a este documento y podrás guardarlo.`
                });
            } else {
                Swal.fire({
                    icon: 'error', title: 'Error',
                    text: result.message || 'No se pudo enviar el correo.'
                });
            }
        } catch (err) {
            console.error(err);
            Swal.fire({
                icon: 'error', title: 'Error de red',
                text: 'Revisa la conexión e intenta de nuevo.'
            });
        }
    }

    /**
     * Chequeo previo al submit. Devuelve [] si se puede guardar, o la lista de
     * errores para sumarla a las validaciones del formulario.
     */
    function validarAntesDeGuardar(prefix) {
        const reg = registros.get(prefix);
        if (!reg || reg.cfg.modo !== 'bloquear') return [];

        if (reg.documentoGenerado) {
            return [`El pedido ${reg.folioGenerado || ''} ya se generó y está pendiente de ` +
                    `autorización de gerencia; no debe guardarse de nuevo.`.replace('  ', ' ')];
        }

        const st = estado(prefix);
        if (!st || !st.aplica || !st.bloquea) return [];

        if (st.suspendido) {
            return ['El cliente está suspendido. Se requiere autorización de gerencia ' +
                    'para una venta a crédito.'];
        }

        const c = st.cliente;
        return [`El documento excede el crédito disponible del cliente ` +
                `(disponible ${fmt(c.limite - c.usado)}, documento ${fmt(st.total)}). ` +
                `Se requiere autorización de gerencia.`];
    }

    window.CreditoVentas = {
        registrar,
        setCliente,
        limpiarCliente,
        evaluar,
        estado,
        esCredito,
        validarAntesDeGuardar,
        solicitarAutorizacion,
        refrescarAutorizacion,
        /** Token de la solicitud, para adjuntarlo al guardar el documento. */
        token: tokenSolicitud,
        olvidarToken: olvidarTokenSolicitud
    };
})();

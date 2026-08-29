/* ==========================================================================
   Taller de barras — orquestador de la vista

   Une tres cosas que antes no existían en Administración de Cortes:
     · la escena 3D (barras-3d.js) alimentada por /AdminCortes/EscenaBarras
     · el escaneo por código de barras de una pieza física
     · la impresión de etiquetas Code128 (codigo-barras.js)

   Las pestañas de tablas que ya existían (Disponibles / Usados) siguen
   viviendo en adminCortes.js; aquí solo se añade la pestaña nueva.
   ========================================================================== */

(function (window, document) {
    'use strict';

    var RUTA = '/AdminCortes';

    var _productos = [];
    var _seleccion = null;      // { pieza, producto } de la pieza marcada
    var _filtros = { busqueda: '', tipo: '' };
    var _cargando = false;

    // Filtro del panel "Material": sobre lo ya cargado, sin ir al servidor
    // (a diferencia de _filtros.busqueda, que sí recarga la escena entera).
    var _filtroLista = '';

    // Cadencia de lector: los USB teclean muy rápido y cierran con Enter.
    var SCAN_GAP_MS = 45;
    var _scanBuffer = '';
    var _scanUltima = 0;

    function $(sel) { return document.querySelector(sel); }
    function esc(v) {
        return String(v == null ? '' : v).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function num(v, d) {
        var n = parseFloat(v);
        return isNaN(n) ? '—' : n.toFixed(d === undefined ? 2 : d);
    }

    function aviso(icono, titulo, texto) {
        if (typeof window.toastMixin !== 'undefined') {
            window.toastMixin.fire({ icon: icono, title: titulo, text: texto || '' });
        } else if (icono === 'error') {
            console.error(titulo, texto || '');
        }
    }

    async function pedir(url) {
        var r = await fetch(url, { headers: { Accept: 'application/json' } });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        return r.json();
    }

    // ─────────────────────────────────────────────────────── carga de escena

    /**
     * La existencia que aún no tiene piezas físicas (folio/código) no viene
     * en `cortes` — EscenaBarras solo arma piezas para lo ya desglosado. Se
     * agrega aquí como una barra sintética (`_sinDesglosar: true`) para que
     * la escena la dibuje y se pueda "Configurar" desde ahí mismo.
     */
    function _marcarPendiente(prod) {
        var existencia = parseFloat(prod.existencia_total) || 0;
        var configurado = parseFloat(prod.metros_configurados) || 0;
        prod._metrosConfigurados = configurado;
        prod._metrosPendientes = Math.max(0, existencia - configurado);

        if (prod._metrosPendientes > 0.01) {
            prod.cortes = (prod.cortes || []).concat([{
                id_corte: null, folio: null, cantidad: 1,
                piezas: [{
                    id_pieza: 'pendiente-' + prod.cve_prod,
                    codigo: null, longitud: prod._metrosPendientes, estado: 'pendiente',
                    fecha_creacion: null, pieza_madre_id: null,
                    _sinDesglosar: true,
                }],
            }]);
        }
    }

    async function cargar() {
        if (_cargando) return;
        _cargando = true;

        var lienzo = $('#ac-3d-placeholder');
        if (lienzo) {
            lienzo.classList.remove('ac-off');
            lienzo.innerHTML = '<i class="fas fa-spinner fa-spin"></i><p>Cargando el material…</p>';
        }

        try {
            var qs = new URLSearchParams();
            if (_filtros.busqueda) qs.set('busqueda', _filtros.busqueda);
            if (_filtros.tipo) qs.set('tipo', _filtros.tipo);

            var r = await pedir(RUTA + '/EscenaBarras?' + qs.toString());
            if (!r.ok) throw new Error(r.message || 'No se pudo cargar el material.');

            _productos = r.productos || [];
            _productos.forEach(_marcarPendiente);

            if (!window.Barras3D || !window.Barras3D.init()) {
                _mostrarFallo('No se pudo iniciar la vista 3D (WebGL no disponible). ' +
                    'Usa las pestañas de tabla mientras tanto.');
                return;
            }

            window.Barras3D.build(_productos);
            window.Barras3D.mount();

            if (lienzo) lienzo.classList.add('ac-off');

            _pintarLeyenda();
            _pintarListaProductos();
            await _pintarResumen();

            if (!_productos.length) {
                _mostrarFallo('No hay piezas con existencia para los filtros actuales.');
            }
        } catch (e) {
            _mostrarFallo(e.message);
            aviso('error', 'No se pudo cargar el taller', e.message);
        } finally {
            _cargando = false;
        }
    }

    function _mostrarFallo(msg) {
        var lienzo = $('#ac-3d-placeholder');
        if (!lienzo) return;
        lienzo.classList.remove('ac-off');
        lienzo.innerHTML = '<i class="fas fa-triangle-exclamation"></i><p>' + esc(msg) + '</p>';
    }

    async function _pintarResumen() {
        var cont = $('#ac-taller-resumen');
        if (!cont) return;

        try {
            var r = await pedir(RUTA + '/ResumenTaller');
            if (!r.ok) return;

            var tubos = (r.resumen || []).find(function (x) { return x.es_tubo === true; }) || {};
            var barras = (r.resumen || []).find(function (x) { return x.es_tubo === false; }) || {};

            // "Sin desglosar" es sobre lo que ya está cargado en la escena
            // (filtros actuales), no una consulta aparte: ya se calculó al
            // recibir EscenaBarras.
            var conPendiente = _productos.filter(function (p) { return (p._metrosPendientes || 0) > 0.01; });
            var metrosPendientes = conPendiente.reduce(function (s, p) { return s + p._metrosPendientes; }, 0);

            cont.innerHTML =
                _chip('fa-circle-notch', 'Tubos', tubos.piezas || 0, num(tubos.metros) + ' m') +
                _chip('fa-grip-lines', 'Barras', barras.piezas || 0, num(barras.metros) + ' m') +
                (metrosPendientes > 0.01
                    ? _chip('fa-triangle-exclamation', 'Sin desglosar', conPendiente.length, num(metrosPendientes) + ' m')
                    : '');
        } catch { /* el resumen es accesorio: si falla, no se estorba */ }
    }

    function _chip(icono, etiqueta, piezas, metros) {
        return '<div class="ac-chip"><i class="fas ' + icono + '"></i>' +
            '<span class="ac-chip-num">' + piezas + '</span>' +
            '<span class="ac-chip-lbl">' + esc(etiqueta) + '</span>' +
            '<span class="ac-chip-sub">' + esc(metros) + '</span></div>';
    }

    function _pintarLeyenda() {
        var el = $('#ac-3d-leyenda');
        if (!el) return;
        var porLongitud = window.Barras3D.getModo() === 'longitud';
        el.innerHTML = porLongitud
            ? '<span class="ac-lg"><i style="background:hsl(0,68%,50%)"></i>Recorte corto</span>' +
              '<span class="ac-lg"><i style="background:hsl(60,68%,50%)"></i>Medio</span>' +
              '<span class="ac-lg"><i style="background:hsl(120,68%,50%)"></i>Barra larga</span>'
            : '<span class="ac-lg"><i style="background:var(--accent-blue)"></i>Tubo</span>' +
              '<span class="ac-lg"><i style="background:var(--warning-color)"></i>Barra</span>';
    }

    /** Índice lateral: producto → sus piezas, para llegar sin girar la cámara. */
    function _pintarListaProductos() {
        var cont = $('#ac-taller-lista');
        if (!cont) return;

        if (!_productos.length) {
            cont.innerHTML = '<div class="ac-vacio">Sin material que mostrar.</div>';
            return;
        }

        var q = _filtroLista.trim().toLowerCase();
        var lista = !q ? _productos : _productos.filter(function (p) {
            return (p.cve_prod || '').toLowerCase().indexOf(q) !== -1 ||
                   (p.descr_prod || '').toLowerCase().indexOf(q) !== -1;
        });

        if (!lista.length) {
            cont.innerHTML = '<div class="ac-vacio">Sin coincidencias para «' + esc(_filtroLista.trim()) + '».</div>';
            return;
        }

        cont.innerHTML = lista.map(function (p) {
            var piezasReales = (p.cortes || []).reduce(function (s, c) {
                return s + (c.piezas || []).filter(function (x) { return !x._sinDesglosar; }).length;
            }, 0);
            var pendiente = p._metrosPendientes || 0;

            var numTxt = piezasReales > 0
                ? piezasReales + ' pz<br><small>' + num(p._metrosConfigurados || 0) + ' m</small>'
                : '<small>sin desglosar</small>';
            var pendienteTxt = pendiente > 0.01
                ? '<span class="ac-prod-pendiente">+' + num(pendiente) + ' m</span>' : '';

            return '<button type="button" class="ac-prod" data-prod="' + esc(p.cve_prod) + '">' +
                '<span class="ac-prod-tipo ' + (p.es_tubo ? 'tubo' : 'barra') + '">' +
                    '<i class="fas ' + (p.es_tubo ? 'fa-circle-notch' : 'fa-grip-lines') + '"></i></span>' +
                '<span class="ac-prod-txt">' +
                    '<span class="ac-prod-cve">' + esc(p.cve_prod) + '</span>' +
                    '<span class="ac-prod-desc">' + esc(p.descr_prod || '') + '</span>' +
                '</span>' +
                '<span class="ac-prod-num">' + numTxt + pendienteTxt + '</span>' +
            '</button>';
        }).join('');
    }

    // ───────────────────────────────────────────────── detalle de una pieza

    function mostrarPieza(entrada) {
        var panel = $('#ac-pieza-panel');
        if (!panel) return;

        if (!entrada) {
            _seleccion = null;
            panel.classList.remove('abierto');
            return;
        }

        _seleccion = entrada;
        var p = entrada.pieza, prod = entrada.producto;
        var esPendiente = !!p._sinDesglosar;
        // Solo se puede dividir/editar/desactivar si la pieza viene de un
        // corte real (tarima_productos_cortes); una remota fuera de filtros
        // también trae ese dato si sigue disponible (ver _mostrarPiezaRemota).
        var tieneCorte = !esPendiente && !!(p.corte && p.corte.id_corte);

        $('#ac-pieza-cve').textContent = prod.cve_prod || '—';
        $('#ac-pieza-desc').textContent = prod.descr_prod || '';
        $('#ac-pieza-tipo').textContent = prod.es_tubo ? 'Tubo' : 'Barra';
        $('#ac-pieza-tipo').className = 'ac-badge ' + (prod.es_tubo ? 'tubo' : 'barra');

        if (esPendiente) {
            $('#ac-pieza-codigo').textContent = '—';
            $('#ac-pieza-dt1').textContent = 'Existencia total';
            $('#ac-pieza-longitud').textContent = num(prod.existencia_total) + ' m';
            $('#ac-pieza-dt2').textContent = 'Configurado';
            $('#ac-pieza-folio').textContent = num(prod._metrosConfigurados || 0) + ' m';
            $('#ac-pieza-dt3').textContent = 'Pendiente';
            $('#ac-pieza-ubicacion').textContent = num(prod._metrosPendientes || 0) + ' m';
        } else {
            $('#ac-pieza-codigo').textContent = p.codigo || '—';
            $('#ac-pieza-dt1').textContent = 'Longitud';
            $('#ac-pieza-longitud').textContent = num(p.longitud) + ' m';
            $('#ac-pieza-dt2').textContent = 'Corte';
            $('#ac-pieza-folio').textContent = p.folio || (p.corte && p.corte.folio) || '—';
            $('#ac-pieza-dt3').textContent = 'Ubicación';
            $('#ac-pieza-ubicacion').textContent =
                [prod.almacen, prod.rack, prod.ulocation].filter(Boolean).join(' · ') || '—';
        }

        var nota = $('#ac-pieza-nota');
        if (nota && esPendiente) {
            nota.className = 'ac-nota';
            nota.innerHTML = '<i class="fas fa-circle-info"></i><div>' +
                'Esta franja representa la existencia de <strong>' + esc(prod.cve_prod) + '</strong> que ' +
                'todavía no se desglosó en piezas físicas con código. Configúrala para poder escanearla ' +
                'y etiquetarla.</div>';
            nota.classList.remove('ac-off');
        } else if (nota && entrada.truncada) {
            // Aviso si la escena la dibuja acortada, para que la longitud de
            // la ficha (la real) no parezca contradecir lo que se ve.
            nota.className = 'ac-nota ac-nota-aviso';
            nota.innerHTML = '<i class="fas fa-ruler-horizontal"></i><div>' +
                'Mide <strong>' + num(p.longitud) + ' m</strong>: en la escena se dibuja acortada ' +
                'para que quepa junto al resto del material. Suele ser existencia todavía sin desglosar.' +
                '</div>';
            nota.classList.remove('ac-off');
        } else if (nota) {
            nota.classList.add('ac-off');
        }

        // El código de barras se dibuja en canvas: se ve igual en pantalla que
        // en la etiqueta impresa, que sale del mismo codificador.
        var lienzo = $('#ac-pieza-barcode');
        if (lienzo && p.codigo && window.CodigoBarras) {
            try {
                window.CodigoBarras.enCanvas(lienzo, p.codigo, { modulo: 2, alto: 54, tamTexto: 12 });
                lienzo.classList.remove('ac-off');
            } catch (e) {
                lienzo.classList.add('ac-off');
            }
        } else if (lienzo) {
            lienzo.classList.add('ac-off');
        }

        $('#ac-pieza-acciones-real')?.classList.toggle('ac-off', !tieneCorte);
        $('#ac-pieza-acciones-pseudo')?.classList.toggle('ac-off', !esPendiente);

        panel.classList.add('abierto');
    }

    // ──────────────────────────────────────────────────────────── escaneo

    async function buscarCodigo(codigo) {
        codigo = (codigo || '').trim();
        if (!codigo) return;

        // Si la pieza está en la escena se llega sin ir al servidor
        var local = window.Barras3D?.piezaPorCodigo(codigo);
        if (local) {
            await window.Barras3D.focusPieza(local.pieza.id_pieza);
            mostrarPieza(local);
            aviso('success', 'Pieza localizada', codigo);
            return;
        }

        try {
            var r = await pedir(RUTA + '/BuscarPieza?codigo=' + encodeURIComponent(codigo));
            if (!r.ok) {
                aviso(r.noEncontrado ? 'warning' : 'error',
                      r.noEncontrado ? 'Código desconocido' : 'Error al buscar', r.message);
                return;
            }
            _mostrarPiezaRemota(r.pieza, r.usos);
        } catch (e) {
            aviso('error', 'Error al buscar', e.message);
        }
    }

    /** Pieza que no está en la escena (otra sucursal, ya consumida, dada de baja). */
    function _mostrarPiezaRemota(pieza, usos) {
        mostrarPieza({
            pieza: {
                codigo: pieza.codigo, longitud: pieza.longitud, folio: pieza.folio,
                // Solo se ofrece dividir/editar/desactivar si el corte del que
                // salió sigue disponible: una pieza ya usada no tiene nada que mutar.
                corte: pieza.estado === 'disponible' ? {
                    id_corte: pieza.id_corte, folio: pieza.folio,
                    longitud: pieza.longitud_corte, cantidad: pieza.cantidad_corte,
                    comentario: pieza.comentario_corte,
                } : null,
            },
            producto: {
                cve_prod: pieza.cve_prod, descr_prod: pieza.descr_prod,
                es_tubo: pieza.es_tubo === true,
                almacen: pieza.cve_almacen, rack: pieza.rack, ulocation: pieza.ulocation,
            },
        });

        var nota = $('#ac-pieza-nota');
        if (!nota) return;

        if (pieza.estado === 'disponible') {
            nota.className = 'ac-nota';
            nota.innerHTML = '<i class="fas fa-circle-info"></i><div>Esta pieza está disponible, ' +
                'pero fuera de los filtros actuales de la escena.</div>';
        } else {
            var lista = (usos || []).slice(0, 3).map(function (u) {
                return '<li>Pedido <strong>' + esc(u.folio_pedido || '—') + '</strong>' +
                    (u.cli_prov ? ' · ' + esc(u.cli_prov) : '') +
                    (u.longitud_solicitada ? ' · ' + num(u.longitud_solicitada) + ' m' : '') + '</li>';
            }).join('');

            nota.className = 'ac-nota ac-nota-aviso';
            nota.innerHTML = '<i class="fas fa-triangle-exclamation"></i><div>' +
                'Esta pieza ya no está disponible (<strong>' + esc(pieza.estado) + '</strong>).' +
                (lista ? '<br>El corte del que salió se usó en:<ul>' + lista + '</ul>' : '') +
                '</div>';
        }
        nota.classList.remove('ac-off');
    }

    function _onTeclaLector(e) {
        var panel = $('#ac-panel-taller');
        if (!panel || panel.classList.contains('d-none')) return;

        var enCampo = /^(INPUT|TEXTAREA|SELECT)$/.test(e.target && e.target.tagName || '');
        var enEscaner = e.target && e.target.id === 'ac-scan-input';
        if (enCampo && !enEscaner) return;

        var ahora = performance.now();

        if (e.key === 'Enter') {
            var fueRafaga = _scanBuffer.length >= 3 && (ahora - _scanUltima) < SCAN_GAP_MS * 4;
            if (fueRafaga || enEscaner) {
                var cod = (enEscaner ? e.target.value : _scanBuffer).trim();
                if (cod) { buscarCodigo(cod); if (enEscaner) e.target.select(); }
            }
            _scanBuffer = '';
            return;
        }

        if (e.key.length !== 1) return;
        if (ahora - _scanUltima > SCAN_GAP_MS) _scanBuffer = '';
        _scanBuffer += e.key;
        _scanUltima = ahora;
    }

    // ─────────────────────────────────────────────────────────── etiquetas
    // La hoja la genera EtiquetasBarras, que vive aparte para poder imprimir
    // también desde las pestañas de tabla y desde el desglose.
    function imprimirEtiquetas(params) {
        if (!window.EtiquetasBarras) {
            aviso('error', 'No se cargó el módulo de etiquetas');
            return;
        }
        return window.EtiquetasBarras.imprimir(params);
    }

    // ──────────────────────────────────────────────────────────── eventos

    function conectar() {
        window.Barras3D.onPiezaClick = function (entrada) { mostrarPieza(entrada); };

        window.Barras3D.onPiezaHover = function (entrada, ev, corte) {
            var tip = $('#ac-3d-tooltip');
            if (!tip) return;
            if (!entrada && !corte) { tip.classList.remove('show'); return; }

            if (corte) {
                tip.innerHTML = '<b><i class="fas fa-scissors"></i> Marcar corte aquí</b><br>' +
                    'Corte: ' + num(corte.longitudCorte) + ' m<br>' +
                    'Sobrante: ' + num(corte.sobrante) + ' m';
            } else if (entrada) {
                var p = entrada.pieza, prod = entrada.producto;
                tip.innerHTML = '<b>' + esc(prod.cve_prod) + '</b><br>' +
                    (p._sinDesglosar
                        ? 'Sin desglosar · ' + num(p.longitud) + ' m pendientes'
                        : esc(p.codigo) + '<br>' + num(p.longitud) + ' m') +
                    (entrada.truncada ? '<br><span class="ac-tip-corte">dibujada más corta</span>' : '');
            } else {
                tip.classList.remove('show');
                return;
            }

            tip.classList.add('show');

            if (ev) {
                var r = $('#ac-3d-container').getBoundingClientRect();
                tip.style.left = Math.min(ev.clientX - r.left + 14, r.width - 170) + 'px';
                tip.style.top = Math.max(8, ev.clientY - r.top - 10) + 'px';
            }
        };

        // Filtros
        var buscar = $('#ac-taller-buscar');
        var timer = null;
        buscar?.addEventListener('input', function () {
            clearTimeout(timer);
            timer = setTimeout(function () {
                _filtros.busqueda = buscar.value.trim();
                cargar();
            }, 350);
        });

        document.querySelectorAll('.ac-tipo-btn').forEach(function (btn) {
            btn.addEventListener('click', function () {
                document.querySelectorAll('.ac-tipo-btn')
                    .forEach(function (b) { b.classList.toggle('active', b === btn); });
                _filtros.tipo = btn.dataset.tipo || '';
                cargar();
            });
        });

        document.querySelectorAll('.ac-cam-btn').forEach(function (btn) {
            btn.addEventListener('click', function () {
                document.querySelectorAll('.ac-cam-btn')
                    .forEach(function (b) { b.classList.toggle('active', b === btn); });
                window.Barras3D?.setPreset(btn.dataset.preset);
            });
        });

        $('#ac-modo-color')?.addEventListener('change', function (e) {
            window.Barras3D?.setModo(e.target.value);
            _pintarLeyenda();
        });

        $('#ac-taller-fit')?.addEventListener('click', function () { window.Barras3D?.zoomFit(); });
        $('#ac-taller-recargar')?.addEventListener('click', function () { cargar(); });

        // Buscador del panel "Material": filtra al instante lo ya cargado
        // (no toca el servidor ni reconstruye la escena 3D).
        $('#ac-taller-lista-buscar')?.addEventListener('input', function (e) {
            _filtroLista = e.target.value;
            _pintarListaProductos();
        });

        // Índice lateral → enfoca la primera pieza de ese producto
        $('#ac-taller-lista')?.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-prod]');
            if (!btn) return;

            var prod = _productos.find(function (p) { return p.cve_prod === btn.dataset.prod; });
            var primera = prod && (prod.cortes || []).flatMap(function (c) { return c.piezas || []; })[0];
            if (!primera) return;

            window.Barras3D.focusPieza(primera.id_pieza);
            var entrada = window.Barras3D.piezaPorId(primera.id_pieza);
            if (entrada) mostrarPieza(entrada);
        });

        // Escáner
        $('#ac-scan-input')?.addEventListener('keydown', _onTeclaLector);
        document.addEventListener('keydown', _onTeclaLector);
        $('#ac-scan-btn')?.addEventListener('click', function () {
            buscarCodigo($('#ac-scan-input').value);
        });

        // Panel de pieza
        $('#ac-pieza-cerrar')?.addEventListener('click', function () { mostrarPieza(null); });

        $('#ac-pieza-etiqueta')?.addEventListener('click', function () {
            if (!_seleccion) return;
            var corte = _seleccion.pieza.corte;
            if (corte && corte.id_corte) imprimirEtiquetas({ idCorte: corte.id_corte });
            else imprimirEtiquetas({ productoId: _seleccion.producto.cve_prod });
        });

        $('#ac-pieza-etiquetas-prod')?.addEventListener('click', function () {
            if (!_seleccion) return;
            imprimirEtiquetas({ productoId: _seleccion.producto.cve_prod });
        });

        $('#ac-pieza-etiqueta-una')?.addEventListener('click', function () {
            if (!_seleccion || !_seleccion.pieza.codigo) return;
            imprimirEtiquetas({ codigo: _seleccion.pieza.codigo });
        });

        // Configurar / dividir / editar / desactivar delegan en los modales
        // que ya existen en la pestaña Disponibles (AdminCortesApp): no hace
        // falta duplicar esa lógica, solo pasarle los datos que ya se tienen
        // cargados en la escena.
        $('#ac-pieza-configurar')?.addEventListener('click', function () {
            if (!_seleccion || !window.AdminCortesApp) return;
            var cve = _seleccion.producto.cve_prod;
            mostrarPieza(null);
            window.AdminCortesApp.abrirModalCrearPara(cve);
        });

        $('#ac-pieza-dividir')?.addEventListener('click', function () {
            var corte = _seleccion && _seleccion.pieza.corte;
            if (!corte || !corte.id_corte || !window.AdminCortesApp) return;
            var prod = _seleccion.producto;
            mostrarPieza(null);
            window.AdminCortesApp.abrirModalDividir({
                idCorte: corte.id_corte, folio: corte.folio,
                longitud: corte.longitud,
                producto: prod.cve_prod + ' - ' + (prod.descr_prod || ''),
            });
        });

        $('#ac-pieza-editar')?.addEventListener('click', function () {
            var corte = _seleccion && _seleccion.pieza.corte;
            if (!corte || !corte.id_corte || !window.AdminCortesApp) return;
            var prod = _seleccion.producto;
            mostrarPieza(null);
            window.AdminCortesApp.abrirModalEditar({
                id_corte: corte.id_corte, folio: corte.folio,
                producto_id: prod.cve_prod, producto_descripcion: prod.descr_prod,
                longitud: corte.longitud, cantidad: corte.cantidad, comentario: corte.comentario,
            });
        });

        $('#ac-pieza-desactivar')?.addEventListener('click', async function () {
            var corte = _seleccion && _seleccion.pieza.corte;
            if (!corte || !corte.id_corte || !window.AdminCortesApp) return;
            mostrarPieza(null);
            await window.AdminCortesApp.desactivarPieza(corte.id_corte);
        });

        // Modo "marcar corte": clic sobre una barra real para elegir dónde
        // cortar, sin pelear con el arrastre de OrbitControls.
        $('#ac-taller-modo-corte')?.addEventListener('click', function () {
            var btn = $('#ac-taller-modo-corte');
            var activo = !btn.classList.contains('active');
            btn.classList.toggle('active', activo);
            window.Barras3D?.setModoCorte(activo);

            var ayuda = $('#ac-ayuda-clic');
            if (ayuda) ayuda.innerHTML = activo ? '<kbd>Clic</kbd> marcar corte' : '<kbd>Clic</kbd> ver la pieza';

            if (activo) {
                aviso('info', 'Modo marcar corte activado',
                    'Haz clic sobre una barra configurada para elegir dónde cortar.');
            }
        });

        // Ver códigos: solo se pintan los de las piezas cerca de la cámara,
        // así que conviene acercarse (rueda/zoom) para que aparezcan.
        $('#ac-taller-ver-codigos')?.addEventListener('click', function () {
            var btn = $('#ac-taller-ver-codigos');
            var activo = !btn.classList.contains('active');
            btn.classList.toggle('active', activo);
            window.Barras3D?.setMostrarCodigos(activo);

            if (activo) {
                aviso('info', 'Códigos de barras activados',
                    'Acércate a una barra con la rueda del mouse para ver su código.');
            }
        });

        window.Barras3D.onPiezaCorte = function (entrada, longitudCorte, sobrante) {
            var corte = entrada.pieza.corte;
            if (!corte || !corte.id_corte || !window.AdminCortesApp) return;

            var prod = entrada.producto;
            window.AdminCortesApp.abrirModalDividir({
                idCorte: corte.id_corte, folio: corte.folio,
                longitud: entrada.pieza.longitud,
                producto: prod.cve_prod + ' - ' + (prod.descr_prod || ''),
            });

            // El modal arranca con una subpieza vacía; se rellena con lo
            // marcado en 3D pero sigue totalmente editable antes de guardar.
            var inputLongitud = document.querySelector('#ac-div-lista .ac-div-longitud');
            if (inputLongitud) {
                inputLongitud.value = longitudCorte.toFixed(2);
                inputLongitud.dispatchEvent(new Event('input', { bubbles: true }));
            }
        };

        // El tema del layout cambia sin recargar: la escena debe seguirlo
        new MutationObserver(function () { window.Barras3D?.refrescarTema(); })
            .observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
    }

    // ─────────────────────────────────────────────────────────── arranque

    function iniciar() {
        if (!document.getElementById('ac-3d-container')) return;
        conectar();
        cargar();
    }

    if (window.Barras3D) {
        document.addEventListener('DOMContentLoaded', iniciar);
    } else {
        document.addEventListener('barras3d:ready', function () {
            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', iniciar);
            } else {
                iniciar();
            }
        });
    }

    window.TallerBarras = {
        recargar: cargar,
        buscarCodigo: buscarCodigo,
        imprimirEtiquetas: imprimirEtiquetas,
        get productos() { return _productos; },
    };

})(window, document);

/* ──────────────────────────────────────────────────────────────────────────
   Tablero de indicadores del Home — NÚCLEO.

   Aquí vive todo lo que comparten las áreas: estado, filtro de sucursal,
   pestañas, formato, paleta, helpers de ECharts y las tablas gemelas.
   Cada área (ventas.js, cobranza.js, almacen.js…) sólo registra sus
   tarjetas con D.registrar({ id, area, render }).

   Para agregar un área nueva (compras, por ejemplo):
     1. da de alta sus consultas en ENDPOINTS con area: 'compras',
     2. crea el panel en Views/Home/Dashboard/_PanelCompras.cshtml,
     3. crea wwwroot/scripts/Home/dashboard/compras.js y registra ahí sus
        tarjetas, y
     4. agrega la pestaña en el partial.
   El núcleo no se toca: pide los datos del área sólo cuando el usuario
   abre esa pestaña.

   Gráficas con Apache ECharts (wwwroot/lib/echarts) y peticiones con
   GetData de wwwroot/scripts/site1.js. Sin jQuery.
   ────────────────────────────────────────────────────────────────────────── */
window.Dashboard = (function () {
    'use strict';

    const ALL = '__all__';

    // Cada consulta declara su área: "base" alimenta los KPIs de arriba y se
    // pide siempre; las demás se piden al abrir su pestaña.
    const ENDPOINTS = {
        totales: { path: '/Home/GetTotalVentasDia', area: 'base' },
        cobros: { path: '/Home/MontoRecaudadoDia', area: 'base' },
        vence: { path: '/Home/GetFacturasVencidas', area: 'base' },

        ventas: { path: '/Home/GetVentasDia', area: 'ventas' },
        tendencia: { path: '/Home/TendenciaTreintaDias', area: 'ventas' },
        prodTop: { path: '/Home/TopDiezProductosMasVendidos', area: 'ventas' },
        prodBottom: { path: '/Home/TopDiezProductosMenosVendidos', area: 'ventas' },
        prodImporte: { path: '/Home/ProductosPorImporte', area: 'ventas' },
        cliTop: { path: '/Home/TopDiezClientesMasCompran', area: 'ventas' },
        cliBottom: { path: '/Home/TopDiezClientesMenosCompran', area: 'ventas' },
        meses: { path: '/Home/VentasMesComparativo', area: 'ventas' },

        aging: { path: '/Home/AntiguedadSaldos', area: 'cobranza' },
        riesgo: { path: '/Home/ClientesRiesgo', area: 'cobranza' },
        pagos: { path: '/Home/FacturacionFormaPago', area: 'cobranza' },

        cortes: { path: '/Home/TodosCortes', area: 'almacen' },
        cortesProd: { path: '/Home/CantidadCortes', area: 'almacen' }
    };

    // Pareja tipográfica del tablero: display para texto, mono para cifras.
    const FONT = "'Space Grotesk', system-ui, -apple-system, 'Segoe UI', sans-serif";
    const FONT_NUM = "'IBM Plex Mono', monospace";

    // ── Formato ────────────────────────────────────────────────────────────
    const fmtMoney = new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' });
    const fmtInt = new Intl.NumberFormat('es-MX', { maximumFractionDigits: 0 });
    const fmtDec1 = new Intl.NumberFormat('es-MX', { maximumFractionDigits: 1 });
    const fmtUnits = new Intl.NumberFormat('es-MX', { maximumFractionDigits: 2 });
    const fmtHora = new Intl.DateTimeFormat('es-MX', { hour: '2-digit', minute: '2-digit' });
    const fmtFecha = new Intl.DateTimeFormat('es-MX', { day: '2-digit', month: 'short' });

    const money = v => fmtMoney.format(num(v));
    const units = v => fmtUnits.format(num(v));

    function moneyShort(v) {
        const n = num(v), a = Math.abs(n);
        if (a >= 1e6) return '$' + fmtDec1.format(n / 1e6) + 'M';
        if (a >= 1e3) return '$' + fmtDec1.format(n / 1e3) + 'k';
        return '$' + fmtInt.format(n);
    }

    function unitsShort(v) {
        const n = num(v), a = Math.abs(n);
        if (a >= 1e6) return fmtDec1.format(n / 1e6) + 'M';
        if (a >= 1e3) return fmtDec1.format(n / 1e3) + 'k';
        return fmtUnits.format(n);
    }

    function num(v) {
        if (typeof v === 'number') return isFinite(v) ? v : 0;
        if (v === null || v === undefined || v === '') return 0;
        const n = parseFloat(String(v).replace(/,/g, ''));
        return isFinite(n) ? n : 0;
    }

    function text(v) {
        return (v === null || v === undefined) ? '' : String(v).trim();
    }

    // "2026-08-01" a secas lo interpreta el navegador como UTC y en México se
    // recorre un día; se le pega la hora para que se lea como fecha local.
    function aFecha(v) {
        if (v instanceof Date) return isNaN(v) ? null : v;
        let s = text(v);
        if (!s) return null;
        if (/^\d{4}-\d{2}-\d{2}$/.test(s)) s += 'T00:00:00';
        const d = new Date(s);
        return isNaN(d) ? null : d;
    }

    // clave de día en hora local (toISOString brincaría de día por la zona)
    function claveDia(d) {
        return d.getFullYear() + '-' +
            String(d.getMonth() + 1).padStart(2, '0') + '-' +
            String(d.getDate()).padStart(2, '0');
    }

    function fecha(v, withHour) {
        const d = aFecha(v);
        if (!d) return v ? text(v) : '—';
        return withHour ? fmtFecha.format(d) + ' ' + fmtHora.format(d) : fmtFecha.format(d);
    }

    function trunc(s, max) {
        s = text(s);
        return s.length > max ? s.slice(0, max - 1) + '…' : s;
    }

    function esc(s) {
        return text(s).replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    // ── Estado ─────────────────────────────────────────────────────────────
    let root;
    const charts = {};            // id del contenedor -> instancia ECharts
    const pendientes = new Set(); // tarjetas ocultas: se dibujan al mostrarlas
    const cargadas = new Set();   // áreas ya pedidas al servidor
    const TARJETAS = {};          // id -> { id, area, render }
    const AREAS = {};             // area -> [ids]

    const estado = {
        data: {},                 // crudo por clave de endpoint
        sucursal: ALL,
        area: 'ventas',
        rank: { productos: 'top', clientes: 'top' },
        metric: { productos: 'unidades' },
        cargado: false
    };

    function registrar(def) {
        TARJETAS[def.id] = def;
        (AREAS[def.area] = AREAS[def.area] || []).push(def.id);
    }

    // ── Arranque ───────────────────────────────────────────────────────────
    function init() {
        root = document.getElementById('homeDashboard');
        if (!root) return;

        if (typeof echarts === 'undefined') {
            console.warn('ECharts no está cargado: el tablero del Home no se dibuja.');
            return;
        }
        if (typeof GetData !== 'function') {
            console.warn('site1.js no está cargado: el tablero del Home no puede pedir datos.');
            return;
        }

        const inicial = root.querySelector('.dash-tab.is-active');
        if (inicial) estado.area = inicial.dataset.tab;

        bindUI();
        arrancar();

        // El tema se marca con data-theme en <body>; al cambiar hay que releer
        // las variables CSS porque los colores van escritos en la opción.
        new MutationObserver(muts => {
            if (muts.some(m => m.attributeName === 'data-theme') && estado.cargado) pintar();
        }).observe(document.body, { attributes: true });

        let resizeTimer = null;
        window.addEventListener('resize', () => {
            clearTimeout(resizeTimer);
            resizeTimer = setTimeout(() => {
                Object.keys(charts).forEach(id => charts[id].resize());
            }, 150);
        });
    }

    function bindUI() {
        const select = root.querySelector('#dashSucursal');
        select.addEventListener('change', () => {
            estado.sucursal = select.value;
            pintar();
        });

        root.querySelector('#dashRefresh').addEventListener('click', () => arrancar(true));

        root.querySelectorAll('.dash-tab').forEach(tab => {
            tab.addEventListener('click', () => irA(tab.dataset.tab));
        });

        root.addEventListener('click', ev => {
            const btn = ev.target.closest('.dash-switch-btn');
            if (!btn) return;
            const card = btn.closest('.dash-card');
            const nombre = card.dataset.card;
            const group = btn.closest('.dash-switch');

            if (btn.dataset.view) {
                activar(group, btn);
                card.querySelectorAll('.dash-body > .dash-view').forEach(v => {
                    v.hidden = v.dataset.view !== btn.dataset.view;
                });
                // Oculto, el contenedor mide 0: se dibuja o se remide al volver.
                if (btn.dataset.view === 'chart') {
                    if (pendientes.has(nombre)) dibujar(nombre);
                    else Object.keys(charts).forEach(id => {
                        if (card.contains(charts[id].getDom())) charts[id].resize();
                    });
                }
            } else if (btn.dataset.rank) {
                activar(group, btn);
                estado.rank[nombre] = btn.dataset.rank;
                dibujar(nombre);
            } else if (btn.dataset.metric) {
                activar(group, btn);
                estado.metric[nombre] = btn.dataset.metric;
                dibujar(nombre);
            }
        });
    }

    function activar(group, btn) {
        group.querySelectorAll('.dash-switch-btn').forEach(b => b.classList.toggle('is-active', b === btn));
    }

    // ── Pestañas ───────────────────────────────────────────────────────────
    function irA(area) {
        if (!area || area === estado.area) return;
        estado.area = area;

        root.querySelectorAll('.dash-tab').forEach(b =>
            b.classList.toggle('is-active', b.dataset.tab === area));
        root.querySelectorAll('.dash-panel').forEach(p =>
            p.hidden = p.dataset.panel !== area);

        if (cargadas.has(area)) pintar();
        else {
            marcar(true, 'Cargando…');
            cargarArea(area).then(() => {
                marcar(false);
                llenarSucursales();
                pintar();
                sello();
            });
        }
    }

    function sello() {
        root.querySelector('#dashUpdated').textContent =
            'Actualizado ' + fmtHora.format(new Date());
    }

    // ── Datos ──────────────────────────────────────────────────────────────
    function getJson(path) {
        return GetData({ path })
            .then(filas => (Array.isArray(filas) ? filas.map(minusculas) : []))
            .catch(() => []);
    }

    // Las columnas llegan tal cual las nombra el SQL; se normalizan a minúsculas
    // para no depender de la política de serialización del servidor.
    function minusculas(row) {
        const out = {};
        Object.keys(row || {}).forEach(k => { out[k.toLowerCase()] = row[k]; });
        return out;
    }

    function cargarArea(area) {
        cargadas.add(area);
        const claves = Object.keys(ENDPOINTS).filter(k => ENDPOINTS[k].area === area);
        return Promise.all(claves.map(k =>
            getJson(ENDPOINTS[k].path).then(filas => { estado.data[k] = filas; })
        ));
    }

    function arrancar(esRefresco) {
        marcar(true, esRefresco ? 'Actualizando…' : 'Cargando…');
        if (esRefresco) cargadas.clear();

        // Se espera la tipografía: ECharts mide el texto de los ejes y, con la
        // fuente a medio cargar, la medida sale corta y las etiquetas bailan.
        const fuentes = document.fonts ? document.fonts.ready.catch(() => null) : Promise.resolve();

        Promise.all([cargarArea('base'), cargarArea(estado.area), fuentes]).then(() => {
            estado.cargado = true;
            marcar(false);
            llenarSucursales();
            pintar();
            sello();
        });
    }

    function marcar(cargando, leyenda) {
        root.classList.toggle('is-refreshing', !!(cargando && estado.cargado));
        root.classList.toggle('is-loading', !!(cargando && !estado.cargado));
        if (leyenda) root.querySelector('#dashUpdated').textContent = leyenda;
    }

    function llenarSucursales() {
        const select = root.querySelector('#dashSucursal');
        const nombres = new Set();
        Object.keys(estado.data).forEach(k => {
            (estado.data[k] || []).forEach(r => {
                const s = text(r.descripcion);
                if (s) nombres.add(s);
            });
        });

        const previo = estado.sucursal;
        const orden = Array.from(nombres).sort((a, b) => a.localeCompare(b, 'es'));
        select.innerHTML = '<option value="' + ALL + '">Todas las sucursales</option>' +
            orden.map(s => '<option value="' + esc(s) + '">' + esc(s) + '</option>').join('');

        select.value = orden.indexOf(previo) >= 0 ? previo : ALL;
        estado.sucursal = select.value;
    }

    // Filas del endpoint, ya recortadas al filtro de sucursal.
    function rows(clave) {
        const data = estado.data[clave] || [];
        if (estado.sucursal === ALL) return data;
        return data.filter(r => text(r.descripcion) === estado.sucursal);
    }

    // Sin filtrar: para consultas que no traen sucursal (cortes) o para
    // repartos que se calculan contra el total (la dona de participación).
    function rowsAll(clave) {
        return estado.data[clave] || [];
    }

    // ── Render ─────────────────────────────────────────────────────────────
    // Una tarjeta que truene no puede llevarse el resto del panel: cada una
    // se dibuja aislada y el error queda en consola con su id.
    function pintar() {
        const t = tokens();
        renderKpis();
        (AREAS[estado.area] || []).forEach(id => dibujar(id, t));
    }

    function dibujar(id, t) {
        const def = TARJETAS[id];
        if (!def) return;
        try {
            def.render(t || tokens());
        } catch (err) {
            console.error('Tablero: falló la tarjeta "' + id + '"', err);
        }
    }

    function totales() {
        let facturado = 0, facturas = 0, cobrado = 0, cobros = 0, vence = 0;
        rows('totales').forEach(r => { facturado += num(r.monto_total); facturas += num(r.total_facturas); });
        rows('cobros').forEach(r => { cobrado += num(r.total_cobrado); cobros += num(r.total_cobros); });
        const porVencer = rows('vence');
        porVencer.forEach(r => { vence += num(r.saldo_pendiente); });
        return { facturado, facturas, cobrado, cobros, vence, venceCount: porVencer.length };
    }

    function renderKpis() {
        const k = totales();
        const ticket = k.facturas ? k.facturado / k.facturas : 0;

        el('#kpiFacturado').textContent = money(k.facturado);
        el('#kpiFacturadoFoot').textContent =
            fmtInt.format(k.facturas) + ' factura' + (k.facturas === 1 ? '' : 's') + ' emitida' +
            (k.facturas === 1 ? '' : 's') + ' hoy';

        el('#kpiCobrado').textContent = money(k.cobrado);
        el('#kpiCobradoFoot').textContent =
            fmtInt.format(k.cobros) + ' cobro' + (k.cobros === 1 ? '' : 's') +
            ' registrado' + (k.cobros === 1 ? '' : 's');

        el('#kpiTicket').textContent = money(ticket);
        el('#kpiTicketFoot').textContent = k.facturas
            ? 'Promedio de ' + fmtInt.format(k.facturas) + ' facturas del día'
            : 'Sin facturas hoy';

        el('#kpiVence').textContent = money(k.vence);
        el('#kpiVenceFoot').textContent =
            k.venceCount + ' factura' + (k.venceCount === 1 ? '' : 's') + ' de crédito con saldo';
    }

    // ── Colores / chrome ───────────────────────────────────────────────────
    function tokens() {
        const cs = getComputedStyle(root);
        const v = n => cs.getPropertyValue(n).trim();
        return {
            s1: v('--dv-s1'), s2: v('--dv-s2'), s3: v('--dv-s3'),
            s4: v('--dv-s4'), s5: v('--dv-s5'), s6: v('--dv-s6'),
            ord: [v('--dv-ord1'), v('--dv-ord2'), v('--dv-ord3'), v('--dv-ord4'), v('--dv-ord5')],
            warning: v('--dv-warning'), critical: v('--dv-critical'), good: v('--dv-good'),
            surface: v('--dv-surface'), plane: v('--dv-plane'),
            ink: v('--dv-ink'), ink2: v('--dv-ink-2'),
            muted: v('--dv-muted'), grid: v('--dv-grid'), rail: v('--dv-rail')
        };
    }

    function alpha(hex, a) {
        const h = text(hex).replace('#', '');
        if (h.length !== 3 && h.length !== 6) return hex;
        const full = h.length === 3 ? h.split('').map(c => c + c).join('') : h;
        const n = parseInt(full, 16);
        return 'rgba(' + ((n >> 16) & 255) + ',' + ((n >> 8) & 255) + ',' + (n & 255) + ',' + a + ')';
    }

    // Degradado suave hacia el extremo del dato: la barra se ve menos plana
    // sin ganar peso visual (el color pleno queda del lado del valor).
    function relleno(color, horizontal) {
        const stops = [{ offset: 0, color: alpha(color, 0.78) }, { offset: 1, color: color }];
        return horizontal
            ? new echarts.graphic.LinearGradient(0, 0, 1, 0, stops)
            : new echarts.graphic.LinearGradient(0, 1, 0, 0, stops);
    }

    function tooltipBase(t) {
        return {
            backgroundColor: t.ink,
            borderWidth: 0,
            padding: [9, 11],
            textStyle: { color: t.surface, fontFamily: FONT, fontSize: 12 },
            extraCssText: 'border-radius:8px;box-shadow:0 6px 18px rgba(0,0,0,.18);'
        };
    }

    const etiquetaEje = t => ({ color: t.muted, fontFamily: FONT, fontSize: 11 });

    // Eje de valores: rejilla en pelo de cabello, sin línea ni marcas.
    // Las cifras van en mono para que la columna de ticks alinee.
    function ejeValor(t, formato) {
        return {
            type: 'value',
            splitLine: { lineStyle: { color: t.grid, width: 1 } },
            axisLine: { show: false },
            axisTick: { show: false },
            axisLabel: {
                color: t.muted, fontFamily: FONT_NUM, fontSize: 10.5, formatter: formato
            }
        };
    }

    // Eje de categorías de barras horizontales: ECharts recorta la etiqueta
    // con puntos suspensivos, así que nunca se pierde contra el borde.
    function ejeCategoriaY(t, datos, ancho) {
        return {
            type: 'category',
            inverse: true,
            data: datos,
            axisLine: { lineStyle: { color: t.grid } },
            axisTick: { show: false },
            axisLabel: Object.assign(etiquetaEje(t), {
                width: ancho, overflow: 'truncate', ellipsis: '…'
            })
        };
    }

    // El canal se reserva a mano (containLabel mide el texto y, si la fuente
    // aún no cargó, la medida se queda corta y recorta la primera letra).
    function rejillaBarras(anchoEtiqueta, derecha) {
        return {
            top: 8,
            left: anchoEtiqueta + 22,
            right: derecha,
            bottom: 28,
            containLabel: false
        };
    }

    // ── Instancias ─────────────────────────────────────────────────────────
    function make(nombre, id, option) {
        const nodo = document.getElementById(id);
        if (!nodo) return null;

        if (!nodo.clientWidth || !nodo.clientHeight) {   // tarjeta o panel oculto
            pendientes.add(nombre);
            return null;
        }
        pendientes.delete(nombre);

        let chart = charts[id];
        if (!chart || chart.isDisposed()) {
            chart = echarts.init(nodo, null, { renderer: 'svg' });
            charts[id] = chart;
        }
        chart.setOption(option, true);
        chart.resize();
        return chart;
    }

    function soltar(id) {
        if (charts[id]) {
            charts[id].dispose();
            delete charts[id];
        }
    }

    // Las barras horizontales crecen con el número de filas: así una sola
    // barra no queda flotando en una tarjeta alta ni diez se apelmazan.
    function altoPorFilas(nombre, filas, porFila, minimo) {
        const wrap = view(nombre, 'chart').querySelector('.dash-plot');
        if (wrap) {
            wrap.style.height =
                Math.min(400, Math.max(minimo || 150, filas * (porFila || 30) + 56)) + 'px';
        }
    }

    // ── Vistas vacías / tablas ─────────────────────────────────────────────
    function el(selector) {
        return root.querySelector(selector);
    }

    function card(nombre) {
        return root.querySelector('.dash-card[data-card="' + nombre + '"]');
    }

    function view(nombre, tipo) {
        return card(nombre).querySelector('.dash-view[data-view="' + tipo + '"]');
    }

    function vacio(nombre, hayDatos, mensaje) {
        const v = view(nombre, 'chart');
        if (!v) return hayDatos;
        const wrap = v.querySelector('.dash-plot');
        let empty = v.querySelector('.dash-empty');

        if (!hayDatos) {
            if (wrap) wrap.style.display = 'none';
            if (!empty) {
                empty = document.createElement('div');
                empty.className = 'dash-empty';
                v.appendChild(empty);
            }
            empty.hidden = false;
            empty.innerHTML = '<i class="fas fa-inbox"></i><span>' + esc(mensaje) + '</span>';
        } else {
            if (wrap) wrap.style.display = '';
            if (empty) empty.hidden = true;
        }
        return hayDatos;
    }

    function tabla(nombre, columnas, filas, mensajeVacio) {
        const cont = view(nombre, 'table');
        if (!cont) return;

        if (!filas.length) {
            cont.innerHTML = '<div class="dash-empty"><i class="fas fa-inbox"></i><span>' +
                esc(mensajeVacio) + '</span></div>';
            return;
        }

        const head = columnas.map(c =>
            '<th' + (c.num ? ' class="num"' : '') + '>' + esc(c.label) + '</th>').join('');

        const body = filas.map(f => '<tr>' + columnas.map(c => {
            const val = c.value(f);
            return '<td' + (c.num ? ' class="num"' : '') + '>' + (c.html ? val : esc(val)) + '</td>';
        }).join('') + '</tr>').join('');

        cont.innerHTML = '<div class="dash-tablewrap"><table class="dash-table">' +
            '<thead><tr>' + head + '</tr></thead><tbody>' + body + '</tbody></table></div>';
    }

    // Cuerpo del tooltip: punto de la serie + etiqueta en tinta, valor en negrita.
    function filaTooltip(marker, etiqueta, valor) {
        return '<div style="display:flex;align-items:center;gap:8px;margin-top:4px;">' +
            marker + '<span style="opacity:.85">' + esc(etiqueta) + '</span>' +
            '<b style="margin-left:auto">' + esc(valor) + '</b></div>';
    }

    function tituloTooltip(txt) {
        return '<div style="font-weight:600;max-width:280px;white-space:normal;">' + esc(txt) + '</div>';
    }

    // Los módulos de área se cargan después de este archivo y se registran
    // al vuelo; el arranque va hasta DOMContentLoaded para que ya estén todos.
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else setTimeout(init, 0);

    return {
        registrar,
        // datos
        rows, rowsAll, estado, ALL, totales,
        // formato
        num, text, trunc, esc, fecha, aFecha, claveDia,
        money, moneyShort, units, unitsShort, fmtInt, fmtDec1, fmtHora,
        // chrome
        tokens, alpha, relleno, tooltipBase, etiquetaEje, ejeValor, ejeCategoriaY,
        rejillaBarras, filaTooltip, tituloTooltip, FONT, FONT_NUM,
        // render
        make, soltar, altoPorFilas, tabla, vacio, card, view, el
    };
})();

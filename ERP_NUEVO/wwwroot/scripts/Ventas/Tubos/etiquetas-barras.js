/* ==========================================================================
   Etiquetas de barras — hoja imprimible con código Code128

   Vive aparte del taller 3D a propósito: etiquetar es una acción del
   inventario, no de una vista concreta. Así el mismo generador se usa desde
   la escena 3D, desde la tabla de disponibles, desde el desglose de un
   producto y justo después de dividir una pieza.

   Depende solo de codigo-barras.js.

   Uso:
     EtiquetasBarras.imprimir({ idCorte: 42 })
     EtiquetasBarras.imprimir({ productoId: 'TUB-2-CED40' })
   ========================================================================== */

(function (window, document) {
    'use strict';

    var RUTA = '/AdminCortes';

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

    /**
     * Pide las piezas al servidor y abre la hoja.
     * @param {{idCorte?:number, productoId?:string, codigo?:string}} params
     */
    async function imprimir(params) {
        if (!params || (!params.idCorte && !params.productoId && !params.codigo)) {
            aviso('warning', 'Indica un corte, un producto o un código para etiquetar');
            return;
        }

        try {
            // El backend prioriza codigo > idCorte > productoId si llegara a
            // recibir más de uno; aquí solo se pasa lo que venga.
            var qs = new URLSearchParams();
            if (params.codigo) qs.set('codigo', params.codigo);
            if (params.idCorte) qs.set('idCorte', params.idCorte);
            if (params.productoId) qs.set('productoId', params.productoId);

            var r = await fetch(RUTA + '/PiezasParaEtiquetar?' + qs.toString(),
                { headers: { Accept: 'application/json' } });
            if (!r.ok) throw new Error('HTTP ' + r.status);

            var data = await r.json();
            if (!data.ok) throw new Error(data.message || 'No se pudieron obtener las piezas.');

            if (!data.piezas || !data.piezas.length) {
                aviso('warning', 'Sin piezas que etiquetar',
                    'Ese corte no tiene barras disponibles. ¿Ya se ejecutó sql/cortes_piezas.sql?');
                return;
            }

            abrirHoja(data.piezas);
            return data.piezas.length;
        } catch (e) {
            aviso('error', 'No se pudieron generar las etiquetas', e.message);
        }
    }

    /** Abre una ventana con una etiqueta por pieza, lista para imprimir. */
    function abrirHoja(piezas) {
        if (!window.CodigoBarras) {
            aviso('error', 'Falta el generador de código de barras');
            return;
        }

        // El SVG se genera aquí y se incrusta: así la hoja no depende de que
        // la ventana nueva cargue ningún script.
        var etiquetas = piezas.map(function (p) {
            var svg;
            try {
                svg = window.CodigoBarras.svg(p.codigo, {
                    modulo: 2, alto: 46, tamTexto: 12, margen: 6,
                });
            } catch (e) {
                svg = '<em>' + esc(p.codigo) + '</em>';
            }

            return '<div class="et">' +
                '<div class="et-cve">' + esc(p.cve_prod) + '</div>' +
                '<div class="et-desc">' + esc(p.descr_prod || '') + '</div>' +
                '<div class="et-bc">' + svg + '</div>' +
                '<div class="et-pie">' +
                    '<span><b>' + num(p.longitud) + ' m</b></span>' +
                    '<span>' + esc(p.es_tubo ? 'TUBO' : 'BARRA') + '</span>' +
                    '<span>' + esc(p.ulocation || p.rack || '') + '</span>' +
                '</div>' +
                '<div class="et-folio">Corte ' + esc(p.folio) + '</div>' +
            '</div>';
        }).join('');

        var w = window.open('', '_blank');
        if (!w) {
            aviso('warning', 'El navegador bloqueó la ventana de impresión',
                'Permite las ventanas emergentes para este sitio.');
            return;
        }

        w.document.write(
            '<!doctype html><html lang="es"><head><meta charset="utf-8">' +
            '<title>Etiquetas de barras (' + piezas.length + ')</title><style>' +
            '@page{size:A4;margin:8mm}' +
            'body{margin:0;font-family:system-ui,sans-serif;color:#000}' +
            '.hoja{display:grid;grid-template-columns:repeat(3,1fr);gap:4mm}' +
            '.et{border:1px dashed #999;border-radius:3mm;padding:3mm;page-break-inside:avoid;text-align:center}' +
            '.et-cve{font-weight:700;font-size:11pt}' +
            '.et-desc{font-size:7pt;color:#444;height:2.2em;overflow:hidden;margin-bottom:1mm}' +
            '.et-bc svg{max-width:100%;height:auto}' +
            '.et-pie{display:flex;justify-content:space-between;font-size:7.5pt;margin-top:1mm}' +
            '.et-folio{font-size:6.5pt;color:#666;margin-top:.5mm}' +
            '@media print{.no-print{display:none}}' +
            '</style></head><body>' +
            '<div class="no-print" style="padding:4mm 0">' +
            '<button onclick="window.print()" style="padding:6px 14px;font-size:12pt;cursor:pointer">Imprimir</button> ' +
            '<span style="font-size:9pt;color:#555">' + piezas.length + ' etiqueta(s)</span></div>' +
            '<div class="hoja">' + etiquetas + '</div></body></html>'
        );
        w.document.close();
    }

    window.EtiquetasBarras = { imprimir: imprimir, abrirHoja: abrirHoja };

})(window, document);

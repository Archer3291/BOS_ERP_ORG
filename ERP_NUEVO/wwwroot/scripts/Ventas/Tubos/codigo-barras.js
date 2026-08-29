/* ==========================================================================
   Code128 — generador de código de barras sin dependencias

   Se implementa a mano en vez de traer una librería por CDN porque esta
   pantalla se usa en el piso del almacén, donde puede no haber internet.
   Son ~150 líneas y el estándar está cerrado desde hace décadas.

   Se usa el subconjunto B (ASCII 32-126), que cubre los códigos que genera
   `fn_generar_codigo_pieza()` (BRR + dígitos) y cualquier folio alfanumérico.
   Con Code C se ganaría densidad en los tramos numéricos, pero a costa de
   complicar el codificador para etiquetas que ya caben de sobra.

   API:
     CodigoBarras.svg(texto, opciones)     → cadena SVG
     CodigoBarras.enCanvas(canvas, texto, opciones)
   ========================================================================== */

(function (window) {
    'use strict';

    // Patrones de barras/espacios de Code128. Cada entrada son 6 dígitos:
    // anchos alternados empezando por barra. Índice = valor del símbolo.
    var PATRONES = [
        /*   0 */ '212222', '222122', '222221', '121223', '121322', '131222', '122213', '122312',
        /*   8 */ '132212', '221213', '221312', '231212', '112232', '122132', '122231', '113222',
        /*  16 */ '123122', '123221', '223211', '221132', '221231', '213212', '223112', '312131',
        /*  24 */ '311222', '321122', '321221', '312212', '322112', '322211', '212123', '212321',
        /*  32 */ '232121', '111323', '131123', '131321', '112313', '132113', '132311', '211313',
        /*  40 */ '231113', '231311', '112133', '112331', '132131', '113123', '113321', '133121',
        /*  48 */ '313121', '211331', '231131', '213113', '213311', '213131', '311123', '311321',
        /*  56 */ '331121', '312113', '312311', '332111', '314111', '221411', '431111', '111224',
        /*  64 */ '111422', '121124', '121421', '141122', '141221', '112214', '112412', '122114',
        /*  72 */ '122411', '142112', '142211', '241211', '221114', '413111', '241112', '134111',
        /*  80 */ '111242', '121142', '121241', '114212', '124112', '124211', '411212', '421112',
        /*  88 */ '421211', '212141', '214121', '412121', '111143', '111341', '131141', '114113',
        /*  96 */ '114311', '411113', '411311', '113141', '114131', '311141', '411131', '211412',
        /* 104 */ '211214', '211232',
        /* 106 */ '2331112'   // patrón de parada: 7 módulos, no 6
    ];

    // El desplazamiento de un solo patrón corrompe START/STOP y el dígito de
    // control, y el fallo solo se vería al escanear una etiqueta ya impresa.
    // Por eso la tabla se comprueba al cargar.
    if (PATRONES.length !== 107) {
        throw new Error('Code128: la tabla debe tener 107 patrones, tiene ' + PATRONES.length);
    }

    var INICIO_B = 104;   // START B
    var PARADA = 106;

    /** Convierte el texto a la secuencia de valores de símbolo, con checksum. */
    function codificar(texto) {
        var valores = [INICIO_B];
        var suma = INICIO_B;

        for (var i = 0; i < texto.length; i++) {
            var cod = texto.charCodeAt(i);
            if (cod < 32 || cod > 126) {
                throw new Error('Code128B no admite el carácter «' + texto[i] + '».');
            }
            var valor = cod - 32;          // el subconjunto B arranca en el espacio
            valores.push(valor);
            suma += valor * (i + 1);       // ponderación por posición, desde 1
        }

        valores.push(suma % 103);          // dígito de control
        valores.push(PARADA);
        return valores;
    }

    /** Devuelve [{ancho, esBarra}] a partir de los valores de símbolo. */
    function aModulos(valores) {
        var tramos = [];
        valores.forEach(function (v) {
            var patron = PATRONES[v];
            for (var i = 0; i < patron.length; i++) {
                tramos.push({ ancho: parseInt(patron[i], 10), esBarra: i % 2 === 0 });
            }
        });
        return tramos;
    }

    function opcionesPorDefecto(o) {
        o = o || {};
        return {
            modulo: o.modulo || 2,          // ancho en px del módulo más estrecho
            alto: o.alto || 60,             // alto de las barras
            margen: o.margen != null ? o.margen : 10,  // zona muda a los lados
            color: o.color || '#000000',
            fondo: o.fondo || '#ffffff',
            mostrarTexto: o.mostrarTexto !== false,
            fuente: o.fuente || 'monospace',
            tamTexto: o.tamTexto || 13,
        };
    }

    function medir(texto, cfg) {
        var tramos = aModulos(codificar(texto));
        var modulos = tramos.reduce(function (s, t) { return s + t.ancho; }, 0);
        var ancho = modulos * cfg.modulo + cfg.margen * 2;
        var alto = cfg.alto + (cfg.mostrarTexto ? cfg.tamTexto + 6 : 0);
        return { tramos: tramos, ancho: ancho, alto: alto };
    }

    /** Código de barras como SVG. Es lo que conviene para imprimir: vectorial. */
    function svg(texto, opciones) {
        var cfg = opcionesPorDefecto(opciones);
        var m = medir(texto, cfg);

        var partes = [];
        var x = cfg.margen;

        m.tramos.forEach(function (t) {
            var w = t.ancho * cfg.modulo;
            if (t.esBarra) {
                partes.push('<rect x="' + x + '" y="0" width="' + w +
                    '" height="' + cfg.alto + '" fill="' + cfg.color + '"/>');
            }
            x += w;
        });

        var etiqueta = '';
        if (cfg.mostrarTexto) {
            etiqueta = '<text x="' + (m.ancho / 2) + '" y="' + (cfg.alto + cfg.tamTexto + 1) +
                '" text-anchor="middle" font-family="' + cfg.fuente +
                '" font-size="' + cfg.tamTexto + '" letter-spacing="1" fill="' + cfg.color + '">' +
                String(texto).replace(/[&<>]/g, function (c) {
                    return { '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c];
                }) + '</text>';
        }

        return '<svg xmlns="http://www.w3.org/2000/svg" width="' + m.ancho + '" height="' + m.alto +
            '" viewBox="0 0 ' + m.ancho + ' ' + m.alto + '" role="img" aria-label="Código ' + texto + '">' +
            '<rect width="' + m.ancho + '" height="' + m.alto + '" fill="' + cfg.fondo + '"/>' +
            partes.join('') + etiqueta + '</svg>';
    }

    /** Dibuja en un <canvas> ya existente (para vistas en pantalla). */
    function enCanvas(canvas, texto, opciones) {
        var cfg = opcionesPorDefecto(opciones);
        var m = medir(texto, cfg);
        var dpr = Math.min(window.devicePixelRatio || 1, 3);

        canvas.width = Math.round(m.ancho * dpr);
        canvas.height = Math.round(m.alto * dpr);
        canvas.style.width = m.ancho + 'px';
        canvas.style.height = m.alto + 'px';

        var g = canvas.getContext('2d');
        g.setTransform(dpr, 0, 0, dpr, 0, 0);
        g.fillStyle = cfg.fondo;
        g.fillRect(0, 0, m.ancho, m.alto);

        g.fillStyle = cfg.color;
        var x = cfg.margen;
        m.tramos.forEach(function (t) {
            var w = t.ancho * cfg.modulo;
            if (t.esBarra) g.fillRect(x, 0, w, cfg.alto);
            x += w;
        });

        if (cfg.mostrarTexto) {
            g.font = cfg.tamTexto + 'px ' + cfg.fuente;
            g.textAlign = 'center';
            g.textBaseline = 'top';
            g.fillText(texto, m.ancho / 2, cfg.alto + 3);
        }

        return canvas;
    }

    window.CodigoBarras = { svg: svg, enCanvas: enCanvas, medir: medir };

})(window);

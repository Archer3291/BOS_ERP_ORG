/*
 * Capa 3 de la captura de errores: la red del navegador.
 *
 * QUÉ ATRAPA QUE EL SERVIDOR NO VE
 *   - JavaScript reventado (window.onerror) y promesas sin catch
 *   - Llamadas AJAX que fallan de verdad (ajaxError)
 *   - Respuestas success:false de los catch que aún NO se han migrado
 *
 * EL MARCADOR esError SIGNIFICA "NO REPORTES ESTO"
 * Es al revés de lo que parece. Lo emiten únicamente CapturaErroresMiddleware y
 * Utilities.ErrorConTicket, o sea los caminos donde el servidor YA registró el
 * error y ya levantó su ticket. Si el navegador lo reportara otra vez se crearía
 * una segunda huella -otro tipo, otro origen- para el mismo incidente.
 *
 * CÓMO SE RECONOCE UN CATCH SIN MIGRAR
 * No hay marcador para ellos, y un success:false a secas puede ser tanto una
 * falla técnica como una validación normal ("Todos los campos son obligatorios").
 * Se distinguen por el texto: 367 de los 441 catch del proyecto concatenan
 * ex.Message en la respuesta, así que el mensaje trae rastros inconfundibles
 * ("Npgsql", "System.", un SqlState). Sólo esos se reportan. Es una heurística,
 * y por eso estos errores entran con prioridad baja.
 */
(function () {
    'use strict';

    var ENDPOINT = '/Soporte/ErrorCliente/Reportar';

    /* Un bucle de render puede disparar miles de onerror por minuto. */
    var MAX_POR_PAGINA = 10;
    var enviados = 0;

    /* Dedup local: el mismo error en la misma carga se manda una vez. */
    var vistos = {};

    /* Rastros de que un mensaje viene de una excepción y no de una validación. */
    var TECNICO = new RegExp([
        'Exception',
        'Npgsql',
        'System\\.',
        'NullReference',
        'Object reference not set',
        'no existe la relaci',
        'no existe la columna',
        'llave duplicada',
        'duplicate key',
        'viola la llave',
        'violates',
        'SqlState',
        'Timeout',
        'at BOS_ERP',
        '\\b\\d{2}[A-Z0-9]{3}\\b'      /* SqlState de Postgres: 42P01, 23505... */
    ].join('|'), 'i');

    /* ¿La respuesta dice que algo falló?
     *
     * No basta con mirar `success`: en el proyecto conviven dos formas. La
     * mayoría de los catch devuelven { success: false, message } pero unos 30
     * -ModulaController entre ellos- devuelven { ok: false, mensaje }. Mirando
     * sólo `success`, esos eran invisibles para esta capa por muy técnico que
     * fuera su mensaje.
     */
    function fallo(r) {
        return !!r && (r.success === false || r.ok === false);
    }

    /* Y el texto vive en cualquiera de los tres nombres. */
    function textoDe(r) {
        return (r && (r.message || r.error || r.mensaje)) || '';
    }

    function token() {
        var meta = document.querySelector('meta[name="bos-af-token"]');
        return meta ? meta.getAttribute('content') : '';
    }

    /* "Ventas/PuntoDeVenta" a partir de la ruta actual. */
    function modulo() {
        var partes = (window.location.pathname || '').split('/').filter(Boolean);
        return partes.length ? partes.slice(0, 2).join('/') : 'Navegador';
    }

    /* Referencia al fetch original, capturada ANTES de parchearlo más abajo.
       El reporte se manda con ésta: si usara el parcheado, cada reporte se
       inspeccionaría a sí mismo. */
    var fetchOriginal = window.fetch ? window.fetch.bind(window) : null;

    function reportar(datos) {
        try {
            if (enviados >= MAX_POR_PAGINA) return;

            var clave = (datos.tipo || '') + '|' + (datos.mensaje || '') + '|' + (datos.origen || '');
            if (vistos[clave]) return;
            vistos[clave] = true;
            enviados++;

            datos.url = window.location.href;
            datos.modulo = datos.modulo || modulo();

            if (!fetchOriginal) return;

            fetchOriginal(ENDPOINT, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token()
                },
                body: JSON.stringify(datos),
                credentials: 'same-origin'
            })['catch'](function () {
                /* Si falla el propio reporte se descarta en silencio: intentar
                   avisar de que no se pudo avisar es un bucle. */
            });
        } catch (e) { /* nunca romper la página por el reporter */ }
    }

    /* ------------------------------------------------------------------
     * 1) JavaScript reventado
     * ---------------------------------------------------------------- */
    window.addEventListener('error', function (e) {
        /* Este evento también salta cuando una <img> o un <script> no cargan.
           Ésos no traen .error ni .message y no son errores de código. */
        if (!e || (!e.error && !e.message)) return;

        var archivo = e.filename || '';

        /* Ruido de terceros: extensiones del navegador y CDNs. No son nuestros
           y no hay nada que arreglar en el ERP. */
        if (archivo && archivo.indexOf(window.location.origin) !== 0) return;

        /* Sin filename -código de la consola, eval, o un handler inline- el origen
           quedaría en ":1" y no ubicaría nada. En ese caso sirve más la página. */
        var origen = archivo
            ? archivo.replace(window.location.origin, '') + ':' + (e.lineno || 0) + ':' + (e.colno || 0)
            : '(sin archivo) ' + window.location.pathname;

        reportar({
            tipo: 'js',
            mensaje: e.message || String(e.error),
            origen: origen,
            stack: (e.error && e.error.stack) ? String(e.error.stack).substring(0, 4000) : ''
        });
    });

    /* ------------------------------------------------------------------
     * 2) Promesas sin catch
     * ---------------------------------------------------------------- */
    window.addEventListener('unhandledrejection', function (e) {
        if (!e) return;

        var motivo = e.reason;
        var mensaje = motivo && motivo.message ? motivo.message : String(motivo);

        reportar({
            tipo: 'promesa',
            mensaje: mensaje,
            origen: 'unhandledrejection',
            stack: (motivo && motivo.stack) ? String(motivo.stack).substring(0, 4000) : ''
        });
    });

    /* ------------------------------------------------------------------
     * 3) fetch  — LA RED PRINCIPAL DE ESTE ERP
     *
     * Los hooks ajaxError/ajaxSuccess de más abajo sólo ven llamadas hechas
     * con jQuery. Este proyecto usa fetch en 66 archivos de scripts y jQuery
     * en 12, así que sin este parche la mayor parte de las llamadas del ERP
     * -incluida toda la facturación de Ventas- pasaba por debajo del radar.
     *
     * Se envuelve window.fetch conservando su contrato: la respuesta se
     * devuelve intacta y los errores se relanzan. Para mirar el cuerpo se usa
     * res.clone(), porque un body sólo se puede leer una vez y consumirlo
     * aquí dejaría al llamador sin sus datos.
     * ---------------------------------------------------------------- */
    if (fetchOriginal) {
        window.fetch = function (entrada, opciones) {
            var url = typeof entrada === 'string'
                ? entrada
                : (entrada && entrada.url) || '';
            var metodo = (opciones && opciones.method)
                || (entrada && entrada.method)
                || 'GET';

            return fetchOriginal(entrada, opciones).then(function (res) {
                try {
                    if (url.indexOf(ENDPOINT) === -1) inspeccionar(res, url, metodo);
                } catch (e) { }
                return res;   // intacta, siempre
            }, function (err) {
                try {
                    if (url.indexOf(ENDPOINT) === -1) {
                        reportar({
                            tipo: 'red',
                            mensaje: 'Falló la petición a ' + url + ': ' + (err && err.message),
                            origen: url,
                            ruta: url,
                            metodo: metodo
                        });
                    }
                } catch (e) { }
                throw err;    // el llamador tiene que seguir viendo su error
            });
        };
    }

    function inspeccionar(res, url, metodo) {
        /* Sesión expirada o sin permiso: comportamiento esperado, no un bug. */
        if (res.status === 401 || res.status === 403) return;

        var esJson = (res.headers.get('content-type') || '').indexOf('json') !== -1;

        if (!res.ok) {
            /* El 500 que ya pasó por el middleware trae su marca; se salta para
               no duplicar la huella. Si el cuerpo no es JSON, se reporta igual. */
            if (!esJson) {
                reportar({
                    tipo: 'fetch', mensaje: 'HTTP ' + res.status + ' en ' + url,
                    origen: url, ruta: url, metodo: metodo, estadoHttp: res.status
                });
                return;
            }

            res.clone().json().then(function (r) {
                if (r && r.esError === true) return;
                reportar({
                    tipo: 'fetch', mensaje: 'HTTP ' + res.status + ' en ' + url,
                    origen: url, ruta: url, metodo: metodo, estadoHttp: res.status
                });
            })['catch'](function () { });
            return;
        }

        /* 200 con success:false — el caso de los catch sin migrar. */
        if (!esJson) return;

        res.clone().json().then(function (r) {
            if (!fallo(r)) return;
            if (r.esError === true) return;          /* el servidor ya lo registró */

            var texto = textoDe(r);
            if (!texto || !TECNICO.test(texto)) return;   /* es una validación */

            reportar({
                tipo: 'respuesta',
                mensaje: texto,
                origen: url,
                ruta: url,
                metodo: metodo
            });
        })['catch'](function () { });
    }

    /* ------------------------------------------------------------------
     * 4) jQuery — la minoría de las llamadas, pero existen (12 archivos).
     * ---------------------------------------------------------------- */
    if (typeof window.jQuery === 'undefined') return;

    /* ------------------------------------------------------------------
     * 3) Llamadas AJAX que fallan
     * ---------------------------------------------------------------- */
    jQuery(document).ajaxError(function (evt, xhr, settings) {
        try {
            if (!settings || settings.url === ENDPOINT) return;

            /* status 0 = petición abortada. Casi siempre es el usuario navegando
               a otra página con una llamada en vuelo, no un error. */
            if (!xhr || xhr.status === 0) return;

            /* Sesión expirada o sin permiso: es comportamiento esperado del
               sistema, no algo que Sistemas tenga que arreglar. */
            if (xhr.status === 401 || xhr.status === 403) return;

            /* El 500 que ya pasó por el middleware trae su marca: lo saltamos
               para no duplicar la huella. */
            var r = xhr.responseJSON;
            if (r && r.esError === true) return;

            reportar({
                tipo: 'ajax',
                mensaje: 'HTTP ' + xhr.status + ' en ' + settings.url,
                origen: settings.url,
                ruta: settings.url,
                metodo: settings.type || 'GET',
                estadoHttp: xhr.status
            });
        } catch (e) { }
    });

    /* ------------------------------------------------------------------
     * 4) Respuestas success:false de catch sin migrar
     * ---------------------------------------------------------------- */
    jQuery(document).ajaxSuccess(function (evt, xhr, settings) {
        try {
            if (!settings || settings.url === ENDPOINT) return;

            var r = xhr && xhr.responseJSON;
            if (!fallo(r)) return;

            /* Ya lo registró el servidor. Ver la nota de arriba. */
            if (r.esError === true) return;

            var texto = textoDe(r);
            if (!texto || !TECNICO.test(texto)) return;

            reportar({
                tipo: 'respuesta',
                mensaje: texto,
                origen: settings.url,
                ruta: settings.url,
                metodo: settings.type || 'POST'
            });
        } catch (e) { }
    });
})();

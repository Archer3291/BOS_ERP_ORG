/**
 * Reglas de precio compartidas por los documentos de venta
 * (cotización → pedido → remisión → factura).
 *
 * CONTRATO de obtener_precio_final (ver sql/obtener_precio_final.sql). Los tres
 * campos son distintos y confundirlos es justo lo que hacía que las reglas no se
 * aplicaran igual en cada documento:
 *
 *   precio_final     → valor con el que arranca el input de precio.
 *   precio_minimo    → PISO permitido. 0 = sin piso (producto sin regla: precio libre,
 *                      solo no negativo). En PRECIO_FIJO el piso es sobre el NETO.
 *   descuento_maximo → tope de descuento. 0 es un tope real ("no se permite descuento",
 *                      que es lo que devuelve PRECIO_FIJO); solo la ausencia del campo
 *                      significa "sin configurar".
 *   descuento_sugerido → solo se pre-llena cuando aplicar_automatico es true.
 *
 * El backend revalida lo mismo en cada Guardar (Helpers/ReglasPrecioHelper.cs); esto es
 * la capa de UX, no la de seguridad.
 */
(function () {
    'use strict';

    const num = v => {
        const n = parseFloat(v);
        return Number.isFinite(n) ? n : 0;
    };

    const esVerdadero = v => v === true || v === 'true' || v === 1 || v === '1';

    /**
     * Normaliza la respuesta de BuscarProducto / BuscarProductoCotizacion a los campos
     * que toda partida necesita. Devuelve siempre valores usables aunque el endpoint no
     * mande los campos de la regla (canales que aún no se migraron).
     */
    function camposDeRegla(detalle) {
        const d = detalle || {};
        const esAuto = esVerdadero(d.aplicar_automatico);

        // 0 es un tope legítimo; el 100 solo aplica si el campo no viene.
        const topeCrudo = parseFloat(d.descuento_maximo);
        const descuentoMaximo = Number.isFinite(topeCrudo) ? topeCrudo : 100;

        const precio = Math.max(num(d.precio), 0);

        return {
            precio: precio,
            precioOriginal: precio,
            precioMinimo: Math.max(num(d.precio_minimo), 0),
            descuento: esAuto ? Math.max(num(d.descuento_sugerido), 0) : 0,
            descuentoMaximo: descuentoMaximo,
            aplicarAutomatico: esAuto,
            tipoRegla: d.tipo_regla || 'BASE'
        };
    }

    /**
     * Valida un precio capturado a mano.
     * Devuelve { valor, aviso } — 'aviso' es null cuando el valor se acepta tal cual.
     */
    function validarPrecio(producto, nuevoPrecio, tienePrecioToken) {
        const precioMinimo = Math.max(num(producto.precioMinimo), 0);

        // Sin referencia no se puede hablar de "precio modificado". Pasa con las partidas
        // que vienen de un documento previo y no guardaron precioOriginal: tratarlas como
        // modificadas revertía la partida a 0 en cuanto se tocaba el renglón.
        const tieneReferencia = producto.precioOriginal !== null
                             && producto.precioOriginal !== undefined;
        const precioOriginal = num(producto.precioOriginal);

        if (tieneReferencia && !tienePrecioToken
         && Math.abs(nuevoPrecio - precioOriginal) > 0.001)
            return {
                valor: precioOriginal,
                aviso: 'Se requiere autorización para modificar el precio'
            };

        // Nunca, con o sin regla y con o sin token.
        if (nuevoPrecio < 0)
            return { valor: 0, aviso: 'El precio no puede ser negativo' };

        // Solo hay piso cuando la regla define uno: sin regla el precio es libre.
        if (precioMinimo > 0 && nuevoPrecio < precioMinimo - 0.001)
            return {
                valor: precioMinimo,
                aviso: `Precio mínimo permitido: $${precioMinimo.toFixed(2)}`
            };

        return { valor: nuevoPrecio, aviso: null };
    }

    /**
     * Valida un descuento capturado a mano.
     */
    function validarDescuento(producto, nuevoDescuento, tieneDescuentoToken) {
        // Ausente = sin tope (100). Un 0 sí es un tope real, pero una partida cargada
        // desde un documento previo todavía no trae el campo, y tratarla como 0 borraba
        // el descuento que ya traía la cotización en cuanto se tocaba el renglón.
        const tope = producto.descuentoMaximo == null ? 100 : num(producto.descuentoMaximo);

        if (!tieneDescuentoToken && nuevoDescuento > 0)
            return {
                valor: num(producto.descuento),
                aviso: 'Se requiere autorización para aplicar descuento',
                revertir: true
            };

        if (nuevoDescuento > tope + 0.001)
            return {
                valor: tope,
                aviso: `Descuento máximo permitido: ${tope}%`
            };

        return { valor: Math.min(Math.max(nuevoDescuento, 0), 100), aviso: null };
    }

    /**
     * En PRECIO_FIJO el piso es sobre el importe neto: si solo se valida el unitario, un
     * descuento hunde la partida por debajo del precio fijo y la regla deja de servir.
     * Devuelve { descuento, aviso } con el descuento ya recortado.
     */
    function ajustarPorPisoNeto(producto) {
        const piso = num(producto.precioMinimo);
        const precio = num(producto.precio);
        const descuento = num(producto.descuento);

        if (producto.tipoRegla !== 'PRECIO_FIJO' || piso <= 0 || precio <= 0)
            return { descuento: descuento, aviso: null };

        if (precio * (1 - descuento / 100) >= piso - 0.001)
            return { descuento: descuento, aviso: null };

        const permitido = Math.max(0, (1 - piso / precio) * 100);
        return {
            descuento: permitido,
            aviso: `Precio fijo de $${piso.toFixed(2)}: descuento máximo ${permitido.toFixed(2)}%`
        };
    }

    /**
     * Tokens de autorización del usuario, cuando tiene permiso directo (sin contraseña).
     * Son los que le permiten bajar del piso o pasarse del tope; el servidor los revalida.
     * Devuelve ambos en null si no tiene permisos, que es el caso normal de un vendedor.
     */
    async function cargarPermisos() {
        const vacio = { precioToken: null, descuentoToken: null };

        try {
            const resp = await fetch('/DatosGenerales/ObtenerPermisosUsuario');
            if (!resp.ok) return vacio;

            const permisos = await resp.json();
            return {
                precioToken: permisos.tienePrecio && permisos.tokenPrecio ? permisos.tokenPrecio : null,
                descuentoToken: permisos.tieneDescuento && permisos.tokenDescuento ? permisos.tokenDescuento : null
            };
        } catch (err) {
            console.warn('[ReglasPrecio] no se pudieron leer los permisos del usuario', err);
            return vacio;
        }
    }

    /**
     * id_cliente a partir de la clave que se ve en el input del documento.
     *
     * Hace falta porque el cliente no siempre llega por el modal de búsqueda: en pedido,
     * remisión y factura lo normal es que venga al cargar la cotización de origen, y por
     * esa vía solo se conoce la clave. Sin resolverlo, las consultas de precio viajaban
     * con clienteId = 0 y el servidor caía a la sesión.
     */
    async function resolverClienteId(cveCli) {
        if (!cveCli) return 0;

        try {
            const resp = await fetch(`/DatosGenerales/BuscarCliente?id=${encodeURIComponent(cveCli)}`);
            if (!resp.ok) return 0;
            const data = await resp.json();
            return data && data[0] ? (parseInt(data[0].id_cliente, 10) || 0) : 0;
        } catch (err) {
            console.warn('[ReglasPrecio] no se pudo resolver el cliente', cveCli, err);
            return 0;
        }
    }

    /**
     * URL del detalle de producto con el cliente del documento. Sin este parámetro el
     * servidor cae a Session["idCliente"], que es global al usuario: con dos pestañas
     * abiertas con clientes distintos se cotiza con la lista de precios ajena.
     */
    function urlDetalleProducto(endpoint, productoId, clienteId) {
        return `${endpoint}?id=${encodeURIComponent(productoId)}`
             + `&clienteId=${encodeURIComponent(clienteId || 0)}`;
    }

    /**
     * Vuelve a consultar el precio de cada partida con la lista del cliente indicado.
     * El precio se resuelve al agregar el producto y no se recalcula solo, así que sin
     * esto cambiar de cliente deja el documento con los precios del cliente anterior.
     *
     * Descarta los ajustes manuales a propósito: pertenecían al cliente anterior.
     * Devuelve el número de partidas actualizadas.
     */
    async function recotizar(productos, clienteId, endpoint) {
        if (!Array.isArray(productos) || productos.length === 0) return 0;

        const detalles = await Promise.all(productos.map(async (p) => {
            try {
                const resp = await fetch(urlDetalleProducto(endpoint, p.productoId, clienteId));
                if (!resp.ok) return null;
                const data = await resp.json();
                return data && data[0] ? { producto: p, detalle: data[0] } : null;
            } catch (err) {
                console.warn('[ReglasPrecio] no se pudo recotizar', p.productoId, err);
                return null;
            }
        }));

        let actualizadas = 0;

        detalles.forEach(item => {
            if (!item) return;
            Object.assign(item.producto, camposDeRegla(item.detalle));
            actualizadas++;
        });

        return actualizadas;
    }

    /**
     * Completa el piso y el tope de partidas que vienen de un documento previo
     * (cotización → pedido → remisión → factura). Esas partidas traen el precio y el
     * descuento que se pactaron, pero no los límites de la regla, así que sin esto el
     * documento no sabe cuál es su piso y solo se enteraba al ser rechazado al guardar.
     *
     * NO toca precio, precioOriginal ni descuento: el documento hijo tiene que respetar
     * lo que se cotizó, no re-precificarse con la regla de hoy.
     */
    async function hidratarLimites(productos, clienteId, endpoint) {
        if (!Array.isArray(productos) || productos.length === 0) return 0;

        const detalles = await Promise.all(productos.map(async (p) => {
            try {
                const resp = await fetch(urlDetalleProducto(endpoint, p.productoId, clienteId));
                if (!resp.ok) return null;
                const data = await resp.json();
                return data && data[0] ? { producto: p, detalle: data[0] } : null;
            } catch (err) {
                console.warn('[ReglasPrecio] no se pudieron leer los límites de', p.productoId, err);
                return null;
            }
        }));

        let hidratadas = 0;

        detalles.forEach(item => {
            if (!item) return;
            const campos = camposDeRegla(item.detalle);
            item.producto.precioMinimo = campos.precioMinimo;
            item.producto.descuentoMaximo = campos.descuentoMaximo;
            item.producto.aplicarAutomatico = campos.aplicarAutomatico;
            item.producto.tipoRegla = campos.tipoRegla;
            hidratadas++;
        });

        return hidratadas;
    }

    function avisar(mensaje, icono) {
        if (!mensaje) return;
        if (window.toastMixin) {
            toastMixin.fire({ icon: icono || 'warning', title: mensaje });
        } else {
            console.warn('[ReglasPrecio]', mensaje);
        }
    }

    window.ReglasPrecio = {
        camposDeRegla,
        validarPrecio,
        validarDescuento,
        ajustarPorPisoNeto,
        urlDetalleProducto,
        cargarPermisos,
        resolverClienteId,
        recotizar,
        hidratarLimites,
        avisar
    };
})();

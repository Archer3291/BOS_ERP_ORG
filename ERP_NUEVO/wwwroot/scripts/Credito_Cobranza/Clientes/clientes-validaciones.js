/* ─────────────────────────────────────────────────────────────
   clientes-validaciones.js
   Responsabilidad única: reglas de validación de cliente
   compartidas por los modales nacional e internacional.
   Devuelven arreglos de mensajes; no tocan el DOM ni muestran
   alertas (de eso se encarga cada modal).
   ───────────────────────────────────────────────────────────── */
(function () {
    'use strict';

    /**
     * Verifica que estén presentes los campos obligatorios.
     * @param {object} data              payload a validar
     * @param {object} camposObligatorios { campo: 'Etiqueta visible' }
     * @returns {string[]} etiquetas de los campos faltantes
     */
    function validarCamposObligatorios(data, camposObligatorios) {
        const errores = [];

        for (const [campo, nombre] of Object.entries(camposObligatorios)) {
            if (!data[campo]) errores.push(nombre);
        }

        return errores;
    }

    /**
     * Valida la pestaña Financiero: crédito, descuentos, cuentas
     * bancarias y comentarios.
     * @returns {string[]} mensajes de error (vacío si todo está bien)
     */
    function validarFinanciero(data) {
        const errores = [];

        // Validar crédito
        if (data.lim_crd) {
            const lim = parseFloat(data.lim_crd);
            if (isNaN(lim) || lim < 0) errores.push('Límite de crédito inválido');
        } else {
            errores.push('Límite de crédito es obligatorio');
        }

        if (data.pl_crd) {
            const plazo = parseInt(data.pl_crd);
            if (isNaN(plazo) || plazo < 0 || plazo > 365) errores.push('Plazo de crédito debe ser entre 0 y 365 días');
        } else {
            errores.push('Plazo de crédito es obligatorio');
        }

        if (data.com_por) {
            const com = parseFloat(data.com_por);
            if (isNaN(com) || com < 0 || com > 100) errores.push('Comisión debe ser un número entre 0 y 100');
        }

        // Descuentos
        if (data.dto) {
            const dto = parseFloat(data.dto);
            if (isNaN(dto) || dto < 0 || dto > 100) errores.push('Descuento 1 debe ser un porcentaje válido (0-100)');
        }
        if (data.dto2) {
            const dto2 = parseFloat(data.dto2);
            if (isNaN(dto2) || dto2 < 0 || dto2 > 100) errores.push('Descuento 2 debe ser un porcentaje válido (0-100)');
        }

        // Cuentas bancarias
        const bancos = [
            { bco: data.bco_cli1, cta: data.cta_bco_cli1, num: 1 },
            { bco: data.bco_cli2, cta: data.cta_bco_cli2, num: 2 },
            { bco: data.bco_cli3, cta: data.cta_bco_cli3, num: 3 },
        ];

        bancos.forEach(({ bco, cta, num }) => {
            if (bco && !cta) errores.push(`Cuenta bancaria ${num} no tiene número de cuenta`);
            if (cta && !bco) errores.push(`Cuenta bancaria ${num} no tiene nombre de banco`);

            if (cta && !/^\d{8,20}$/.test(cta))
                errores.push(`Cuenta ${num} debe tener entre 8 y 20 dígitos numéricos`);
        });

        // Comentarios
        if (data.coment1 && data.coment1.length > 500)
            errores.push('Comentario 1 demasiado largo (máx. 500 caracteres)');

        return errores;
    }

    /** Muestra un toast con la lista de errores. */
    function mostrarErrores(titulo, errores) {
        toastMixin.fire({
            icon: 'error',
            title: `${titulo}<br>- ` + errores.join('<br>- ')
        });
    }

    /* ── API pública ──────────────────────────────────────────────── */
    window.ClientesValidaciones = {
        validarCamposObligatorios,
        validarFinanciero,
        mostrarErrores
    };
})();

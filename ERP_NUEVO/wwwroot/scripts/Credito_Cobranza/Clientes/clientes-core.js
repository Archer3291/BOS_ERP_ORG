/* ─────────────────────────────────────────────────────────────
   clientes-core.js
   Responsabilidad única: estado compartido de la pantalla de
   Clientes y utilidades de construcción de Tom Selects en
   cascada (estado → municipio → cp → colonia).

   No conoce ningún modal en concreto: los parciales lo consumen.
   ───────────────────────────────────────────────────────────── */
(function () {
    'use strict';

    /* ── Caché de catálogos (evita repetir la misma petición) ──────── */
    const _cache = {};

    /**
     * Igual que GetData, pero memoriza la respuesta por path + payload.
     */
    async function GetDataCached({ path, data }) {
        const key = path + JSON.stringify(data);

        if (_cache[key]) return _cache[key];

        const res = await GetData({ path, data });
        _cache[key] = res;
        return res;
    }

    /* ── Estado compartido entre parciales ────────────────────────── */
    const ClientesState = {
        /* Colecciones que se editan en memoria y se envían al guardar */
        correos: [],
        telefonos: [],
        transportes: [],

        /* Una instancia de TomSelectManager por modal */
        tom: {
            crearNacional: null,
            crearInternacional: null,
            editarNacional: null,
            editarInternacional: null
        },

        /** Catálogo fijo de tipos de transporte (clientes internacionales) */
        tiposTransporte: [
            { value: 'Tren', label: 'Tren' },
            { value: 'Aereo', label: 'Aéreo' },
            { value: 'Maritimo', label: 'Marítimo' },
            { value: 'Terrestre', label: 'Terrestre' }
        ],

        /** true si el modal de edición internacional es el que está abierto */
        esEdicionInternacional() {
            return !!document.getElementById('modalEditarClienteInternacional')?.classList.contains('show');
        },

        /** id_cliente del modal de edición que esté abierto en este momento */
        idClienteActivo() {
            return this.esEdicionInternacional()
                ? document.getElementById('edit_id_cliente_internacional')?.value
                : document.getElementById('edit_id_cliente_nacional')?.value;
        },

        /** Destruye todos los Tom Select vivos de un manager */
        destruirManager(manager) {
            if (!manager?.instances) return;
            Object.keys(manager.instances).forEach(key => {
                try { manager.instances[key].destroy(); } catch { }
            });
        },

        /** Destruye los Tom Select de todos los <select> de un modal */
        limpiarSelectsDeModal(selectorModal, excepciones = '') {
            const query = excepciones
                ? `${selectorModal} select:not(${excepciones})`
                : `${selectorModal} select`;

            document.querySelectorAll(query).forEach(select => {
                if (select.tomselect) {
                    try { select.tomselect.destroy(); } catch { }
                }
                select.innerHTML = '';
            });
        }
    };

    /* ── Construcción de un Tom Select ────────────────────────────── */
    async function crearSelect({ key, selector, options = [], valueField, labelName, searchField, placeholder, next, clearBelow = [], instanceName, render }) {
        const selectElement = document.querySelector(selector);
        if (!selectElement) return;

        // Destruye instancia anterior si existe
        if (selectElement.tomselect) {
            try { selectElement.tomselect.destroy(); } catch { }
        }

        // Limpia también la referencia guardada en el manager
        if (instanceName?.instances?.[key]) {
            try { instanceName.instances[key].destroy(); } catch { }
            delete instanceName.instances[key];
        }

        selectElement.innerHTML = `<option>Cargando...</option>`;

        try {
            // Limpieza de selects dependientes
            clearBelow.forEach(k => {
                const el = document.querySelector(`#${k}`);

                if (el?.tomselect) {
                    try { el.tomselect.destroy(); } catch { }
                }
                if (el) el.innerHTML = '';
            });

            // Sin color en línea: lo pone content/tom-select-theme.css. Un
            // style aquí ganaría siempre y dejaría el texto fijo también al
            // pasar el cursor por la opción.
            const renderConfig = render || {
                option: (data, escape) => `<div>${escape(data[labelName])}</div>`,
                item: (data, escape) => `<div>${escape(data[labelName])}</div>`
            };

            const instance = new TomSelect(selectElement, {
                options: options,
                valueField,
                searchField,
                labelField: labelName,
                placeholder,
                maxItems: 1,
                create: false,
                render: renderConfig,
                onChange: async (value) => {
                    if (next && value) await next(value);
                }
            });

            if (instanceName) {
                instanceName.instances[key] = instance;
            }

            return options;
        } catch (error) {
            console.error(`Error al cargar ${key}:`, error);
            selectElement.innerHTML = `<option>Error al cargar</option>`;
        }
    }

    /**
     * Busca una opción por su texto y selecciona el valor equivalente,
     * sin disparar el onChange (para no recargar los selects hijos).
     */
    async function setSelectValueByText(instanceKey, opciones, text, valueField = 'id', displayField = 'nombre', manager = null) {
        if (!text || !Array.isArray(opciones) || !manager) return null;

        const userValue = typeof text == "string" ? text.toLowerCase() : text;
        const match = opciones.find(opt => {
            const arrayValue = typeof opt[displayField] == "string" ? opt[displayField].toLowerCase() : opt[displayField];
            return arrayValue === userValue;
        });
        if (!match || !manager.instances[instanceKey]) return null;

        const instance = manager.instances[instanceKey];

        // guardamos onChange original
        const originalOnChange = instance.settings.onChange;
        instance.settings.onChange = () => { };

        instance.setValue(match[valueField], true);

        // restauramos
        instance.settings.onChange = originalOnChange;

        return match[valueField];
    }

    /**
     * Carga un nivel de la cascada de dirección declarado en
     * clientes-direcciones.config.js y, si el cliente ya trae valor,
     * lo preselecciona y baja al siguiente nivel.
     *
     * @param {object} direccionConfig  direccionConfigNacional | direccionConfigInternacional
     * @param {object} cliente          registro con los valores actuales (o null al crear)
     * @param {string} tipo             "create" | "edit"
     * @param {string} nivel            clave del nivel dentro de la config
     * @param {*}      valorAnterior    valor del nivel padre (para dataExtra)
     * @param {object} extra            overrides, típicamente { instanceName }
     */
    async function cargarDireccion(direccionConfig, cliente, tipo, nivel, valorAnterior = null, extra = {}) {
        const base = direccionConfig[tipo][nivel];
        if (!base) return;

        // mezclamos config del JSON + overrides
        const cfg = { ...base, ...extra };

        let data = cfg.data || {};
        if (cfg.dataExtra && valorAnterior) {
            data = { ...data, ...cfg.dataExtra(valorAnterior) };
        }

        const opciones = cfg.path ? await GetDataCached({ path: cfg.path, data }) : cfg.options;

        await crearSelect({
            key: cfg.key,
            selector: cfg.selector,
            options: opciones,
            data,
            valueField: cfg.valueField,
            labelName: cfg.labelName,
            searchField: cfg.searchField || ["nombre"],
            placeholder: "Selecciona...",
            clearBelow: cfg.clearBelow,
            instanceName: cfg.instanceName,
            next: cfg.next
                ? async v => cargarDireccion(direccionConfig, cliente, tipo, cfg.next, v, extra)
                : null,
            render: cfg.render,
        });

        if (cliente && cliente[cfg.clientField]) {
            const val = await setSelectValueByText(
                cfg.key,
                opciones,
                cliente[cfg.clientField],
                cfg.valueField,
                cfg.displayField,
                cfg.instanceName
            );

            if (val && cfg.next) {
                await cargarDireccion(direccionConfig, cliente, tipo, cfg.next, val, extra);
            }
        }
    }

    /* ── API pública ──────────────────────────────────────────────── */
    window.ClientesState = ClientesState;
    window.GetDataCached = GetDataCached;
    window.crearSelect = crearSelect;
    window.setSelectValueByText = setSelectValueByText;
    window.cargarDireccion = cargarDireccion;
})();

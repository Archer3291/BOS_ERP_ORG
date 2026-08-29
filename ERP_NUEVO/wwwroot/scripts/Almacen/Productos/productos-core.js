(function () {
    'use strict';

    /* ── Estado compartido ────────────────────────────────────────── */
    const ProductosState = {
        tomSelects: {},
        proveedores: {},

        /** Prefijo de los ids del formulario según el modo */
        pref(mode) {
            return mode === 'crear' ? 'c_' : 'e_';
        },

        /** Marca un checkbox de toggle */
        setToggle(id, checked) {
            const el = document.getElementById(id);
            if (el) el.checked = !!checked;
        }
    };

    /* ── Catálogos: se piden una sola vez por sesión de página ────── */
    let _catalogos = null;

    async function cargarCatalogos() {
        if (_catalogos) return _catalogos;

        const res = await fetch('/Almacen/producto/datos');
        _catalogos = await res.json();
        return _catalogos;
    }

    /* ── Construcción del formulario ──────────────────────────────── */

    /** Llena un <select> normal con una lista de catálogo */
    function llenarSelect(id, lista, valueKey, textKey) {
        const el = document.getElementById(id);
        if (!el) return;

        el.innerHTML = '<option value="">Selecciona una opción</option>';
        lista.forEach(item => {
            const opt = document.createElement('option');
            opt.value = item[valueKey];
            opt.textContent = item[textKey];
            el.appendChild(opt);
        });
    }

    const MONEDAS_VENTA = [['MXN', 'Pesos Mexicanos'], ['USD', 'Dólares'], ['EUR', 'Euros'], ['GBP', 'Libras']];
    const MONEDAS_COMPRA = [['PESOS', 'Pesos Mexicanos'], ['DLLS', 'Dólares'], ['EURO', 'Euros'], ['GBP', 'Libras']];

    function llenarMonedas(id, opciones) {
        const el = document.getElementById(id);
        if (!el) return;

        el.innerHTML = '<option value="">Selecciona moneda</option>';
        opciones.forEach(([valor, texto]) => el.appendChild(new Option(texto, valor)));
    }

    function montarTomSelect(mode, key, selectorId, config, loadFn) {
        const selector = `#${ProductosState.pref(mode)}${selectorId}`;
        if (!document.querySelector(selector)) return;

        if (!ProductosState.tomSelects[mode]) ProductosState.tomSelects[mode] = {};
        ProductosState.tomSelects[mode][key]?.destroy();

        const cfg = { create: false, ...config };
        if (loadFn) {
            cfg.load = loadFn;
            cfg.shouldLoad = q => q.length >= 3;
        }

        ProductosState.tomSelects[mode][key] = new TomSelect(selector, cfg);
    }

    async function initForm(mode) {
        const data = await cargarCatalogos();
        const pref = ProductosState.pref(mode);

        // Catálogos simples
        llenarSelect(pref + 'lineaProducto', data.lineas, 'c1', 'c2');
        llenarSelect(pref + 'tipoProducto', data.tipos, 'c1', 'c2');
        llenarSelect(pref + 'grupoProducto', data.marcas, 'c1', 'c2');
        llenarSelect(pref + 'naturalezaProducto', data.naturalezaProducto, 'key', 'value');
        llenarSelect(pref + 'unidadProducto', data.udm, 'c1', 'c2');

        llenarMonedas(pref + 'moneda', MONEDAS_VENTA);
        llenarMonedas(pref + 'monedaCompra', MONEDAS_COMPRA);

        // Catálogos del SAT
        montarTomSelect(mode, 'unidadMedida', 'unidadMedida', {
            options: data.unidadsat,
            valueField: 'cve_unidad_sat',
            labelField: 'descripcion',
            searchField: ['cve_unidad_sat', 'descripcion'],
            placeholder: 'Clave unidad SAT'
        });

        montarTomSelect(mode, 'unidadSecundaria', 'unidadSecundaria', {
            options: data.unidadsat,
            valueField: 'cve_unidad_sat',
            labelField: 'descripcion',
            searchField: ['cve_unidad_sat', 'descripcion'],
            placeholder: 'Clave unidad SAT'
        });

        montarTomSelect(mode, 'objetoImpuesto', 'objetoImpuesto', {
            options: data.objetoimpuestosat,
            valueField: 'cve_objeto_importacion',
            labelField: 'nombre',
            searchField: ['nombre'],
            placeholder: 'Objeto de impuesto'
        });

        // El catálogo SAT es enorme: se busca contra el servidor
        montarTomSelect(mode, 'prod_sat', 'prod_sat', {
            options: [],
            valueField: 'cve_producto',
            labelField: 'descripcion',
            searchField: ['cve_producto', 'descripcion'],
            placeholder: 'Buscar producto SAT...'
        }, (q, cb) => {
            fetch(`/Almacen/producto/BuscarProductosSat?q=${encodeURIComponent(q)}`)
                .then(r => r.json())
                .then(cb)
                .catch(() => cb());
        });

        // Proveedores
        if (!ProductosState.proveedores[mode]) ProductosState.proveedores[mode] = {};

        ['proveedor1', 'proveedor2'].forEach(id => {
            const selector = `#${pref}${id}`;
            if (!document.querySelector(selector)) return;

            ProductosState.proveedores[mode][id]?.destroy();
            ProductosState.proveedores[mode][id] = new TomSelect(selector, {
                create: false,
                maxItems: 1,
                options: data.proveedores,
                valueField: 'clave',
                labelField: 'nombre',
                searchField: ['clave', 'nombre'],
                // Sin color en línea: lo pone content/tom-select-theme.css
                render: {
                    option: (d, escape) => `<div>${escape(d.nombre)}</div>`,
                    item: (d, escape) => `<div>${escape(d.nombre)}</div>`
                }
            });
        });
    }

    /* ── Guardado ─────────────────────────────────────────────────── */
    function camposFaltantes(form) {
        const faltantes = [];

        form.querySelectorAll('[required]').forEach(el => {
            if (el.value.trim()) {
                el.classList.remove('is-invalid');
                return;
            }

            el.classList.add('is-invalid', 'shake');
            el.addEventListener('animationend', () => el.classList.remove('shake'), { once: true });

            // Nombre visible del campo, sin el asterisco de requerido
            const label = form.querySelector(`label[for="${el.id}"]`);
            const clon = label?.cloneNode(true);
            clon?.querySelector('.req')?.remove();
            const nombre = clon?.textContent?.trim() || el.name || el.id;

            // Pestaña que lo contiene
            const pane = el.closest('.prod-tab-pane');
            const wrapper = el.closest('.prod-tabs-wrapper');
            const tabBtn = pane?.id && wrapper
                ? wrapper.querySelector(`.prod-tab-btn[data-target="#${pane.id}"]`)
                : null;

            faltantes.push({ nombre, tab: tabBtn?.textContent?.trim() || 'General' });
        });

        return faltantes;
    }

    function avisarCamposFaltantes(faltantes) {
        const porTab = {};
        faltantes.forEach(({ nombre, tab }) => {
            (porTab[tab] ??= []).push(nombre);
        });

        const html = Object.entries(porTab).map(([tab, campos]) => `
            <div style="margin-bottom:.5rem">
                <span style="font-size:.75rem;font-weight:700;text-transform:uppercase;letter-spacing:.06em;opacity:.6">${tab}</span><br>
                ${campos.map(c => `&bull; ${c}`).join('<br>')}
            </div>`).join('');

        _Swal.fire({
            icon: 'error',
            title: 'Campos requeridos incompletos',
            html: `<div style="text-align:left;font-size:.88rem;line-height:1.8">${html}</div>`,
            showCancelButton: false,
        });
    }

    function guardarProducto(mode) {
        const formId = mode === 'crear' ? 'formCrear' : 'formEditar';
        const form = document.getElementById(formId);

        const faltantes = camposFaltantes(form);
        if (faltantes.length > 0) {
            avisarCamposFaltantes(faltantes);
            return;
        }

        postFormData(formId, '/Almacen/producto/CrearProducto').then(res => {
            if (!res.success) {
                toastMixin.fire({ icon: 'error', title: res.message });
                return;
            }

            const modalId = mode === 'crear' ? 'modalCrearProducto' : 'modalEditarProducto';
            bootstrap.Modal.getInstance(document.getElementById(modalId))?.hide();
            toastMixinReload({ icon: 'success', title: res.message });
            window.tablaProductos?.reload?.();
        });
    }

    /* ── Interacciones del formulario ─────────────────────────────── */
    document.addEventListener('click', e => {
        const btn = e.target.closest('.prod-tab-btn');
        if (!btn) return;

        const wrapper = btn.closest('.prod-tabs-wrapper');
        if (!wrapper) return;

        wrapper.querySelectorAll('.prod-tab-btn').forEach(b => b.classList.remove('active'));
        wrapper.querySelectorAll('.prod-tab-pane').forEach(p => p.classList.remove('active'));

        btn.classList.add('active');
        wrapper.querySelector(btn.dataset.target)?.classList.add('active');
    });

    document.addEventListener('change', e => {
        if (!e.target.classList.contains('prod-img-input')) return;

        const file = e.target.files[0];
        const preview = document.getElementById(ProductosState.pref(e.target.dataset.mode) + 'imagenPreview');
        if (!file || !preview) return;

        const reader = new FileReader();
        reader.onload = ev => {
            preview.src = ev.target.result;
            preview.style.display = 'block';
            preview.previousElementSibling?.classList.add('d-none');
        };
        reader.readAsDataURL(file);
    });

    function badgeEstado(valor) {
        const activo = (valor || '').trim().toUpperCase() === 'A';

        return activo
            ? '<span class="prod-tag prod-tag--activo"><i class="fa-solid fa-circle-check me-1"></i>Activo</span>'
            : '<span class="prod-tag prod-tag--inactivo"><i class="fa-solid fa-circle-xmark me-1"></i>Inactivo</span>';
    }

    /* ── API pública ──────────────────────────────────────────────── */
    window.ProductosState = ProductosState;
    window.cargarCatalogos = cargarCatalogos;
    window.initForm = initForm;
    window.guardarProducto = guardarProducto;
    window.badgeEstado = badgeEstado;
})();

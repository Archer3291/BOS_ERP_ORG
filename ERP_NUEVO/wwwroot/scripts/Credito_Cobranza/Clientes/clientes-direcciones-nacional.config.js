/* ─────────────────────────────────────────────────────────────
   clientes-direcciones-nacional.config.js
   Responsabilidad única: describir, de forma declarativa, los
   Tom Selects en cascada del cliente NACIONAL.

   Cada nivel indica de dónde salen las opciones (path), a qué
   <select> pertenece (selector), qué campo del cliente lo
   preselecciona (clientField) y cuál es el nivel siguiente (next).
   Lo consume cargarDireccion() de clientes-core.js.
   ───────────────────────────────────────────────────────────── */
const direccionConfigNacional = {
    create: {
        estado: {
            key: "estados_create_nacional",
            selector: "#nacional_est",
            path: "/Clientes/GetEstados",
            data: { internacional: false },
            valueField: "id_estado",
            displayField: "nombre",
            labelName: "nombre",
            searchField: ["nombre"],
            clearBelow: ["municipios_create_nacional", "codigos_create_nacional", "colonias_create_nacional"],
            next: "municipio"
        },
        municipio: {
            key: "municipios_create_nacional",
            selector: "#nacional_mun",
            path: "/Clientes/GetMunicipios",
            dataExtra: estado => ({ estado: estado, internacional: false }),
            valueField: "id_municipio",
            displayField: "nombre",
            labelName: "nombre",
            searchField: ["nombre"],
            clearBelow: ["codigos_create_nacional", "colonias_create_nacional"],
            next: "cp"
        },
        cp: {
            key: "codigos_create_nacional",
            selector: "#nacional_cp",
            path: "/Clientes/GetCodigosPostales",
            dataExtra: municipio => ({ municipio_id: municipio, internacional: false }),
            valueField: "cp",
            displayField: "cp",
            labelName: "cp",
            searchField: ["cp"],
            clearBelow: ["colonias_create_nacional"],
            next: "colonia"
        },
        colonia: {
            key: "colonias_create_nacional",
            selector: "#nacional_col",
            path: "/Clientes/GetColonias",
            dataExtra: cp => ({ cp: cp, internacional: false }),
            valueField: "id_colonia",
            displayField: "nombre",
            labelName: "nombre",
            searchField: ["nombre"]
        },
        vendedores: {
            key: 'vendedores_create_nacional',
            selector: '#nacional_cve_vdr',
            path: '/Clientes/GetVendedores',
            data: { internacional: false },
            valueField: 'clave_vendedor',
            displayField: 'nombre',
            labelName: 'nombre',
            searchField: ['nombre'],
            render: {
                option: (data, escape) => `<div>${escape(data.clave_vendedor)} - ${escape(data.nombre)}</div>`,
                item: (data, escape) => `<div>${escape(data.clave_vendedor)} - ${escape(data.nombre)}</div>`
            }
        },
    },
    edit: {
        estado: {
            key: "estados_edit_nacional",
            selector: "#edit_cve_est_nacional",
            path: "/Clientes/GetEstados",
            data: { internacional: false },
            valueField: "id_estado",
            displayField: "nombre",
            labelName: "nombre",
            searchField: ["nombre"],
            clientField: "pob",
            clearBelow: ["municipios_edit_nacional", "codigos_edit_nacional", "colonias_edit_nacional"],
            next: "municipio"
        },
        municipio: {
            key: "municipios_edit_nacional",
            selector: "#edit_mpio_nacional",
            path: "/Clientes/GetMunicipios",
            dataExtra: estado => ({ estado: estado, internacional: false }),
            valueField: "id_municipio",
            displayField: "nombre",
            labelName: "nombre",
            searchField: ["nombre"],
            clientField: "municipio",
            clearBelow: ["codigos_edit_nacional", "colonias_edit_nacional"],
            next: "cp"
        },
        cp: {
            key: "codigos_edit_nacional",
            selector: "#edit_cp_nacional",
            path: "/Clientes/GetCodigosPostales",
            dataExtra: municipio => ({ municipio_id: municipio, internacional: false }),
            valueField: "cp",
            displayField: "cp",
            labelName: "cp",
            searchField: ["cp"],
            clientField: "cp",
            clearBelow: ["colonias_edit_nacional"],
            next: "colonia"
        },
        colonia: {
            key: "colonias_edit_nacional",
            selector: "#edit_col_nacional",
            path: "/Clientes/GetColonias",
            dataExtra: cp => ({ cp: cp, internacional: false }),
            valueField: "id_colonia",
            displayField: "nombre",
            labelName: "nombre",
            searchField: ["nombre"],
            clientField: "col",
        },
        vendedores: {
            key: 'vendedores_edit_nacional',
            selector: '#edit_cve_vdr_nacional',
            path: '/Clientes/GetVendedores',
            data: { internacional: false },
            valueField: 'clave_vendedor',
            displayField: 'clave_vendedor',
            labelName: 'nombre',
            searchField: ['nombre'],
            clientField: 'cve_vdr',
            render: {
                option: (data, escape) => `<div>${escape(data.clave_vendedor)} - ${escape(data.nombre)}</div>`,
                item: (data, escape) => `<div>${escape(data.clave_vendedor)} - ${escape(data.nombre)}</div>`
            }
        },

        /* ── Pestaña Datos de Facturación ─────────────────────────── */
        fact_estado: {
            key: "fact_estados_edit_nacional",
            selector: "#fact_cve_est_nacional",
            path: "/Clientes/GetEstados",
            data: { internacional: false },
            valueField: "id_estado",
            displayField: "cve_estado",
            labelName: "nombre",
            searchField: ["nombre"],
            clientField: "estado",
            clearBelow: ["fact_municipios_edit_nacional", "fact_codigos_edit_nacional", "fact_colonias_edit_nacional"],
            next: "fact_municipio"
        },
        fact_municipio: {
            key: "fact_municipios_edit_nacional",
            selector: "#fact_mpio_nacional",
            path: "/Clientes/GetMunicipios",
            dataExtra: estado => ({ estado: estado, internacional: false }),
            valueField: "id_municipio",
            displayField: "numero_municipio",
            labelName: "nombre",
            searchField: ["nombre"],
            clientField: "municipio",
            clearBelow: ["fact_codigos_edit_nacional", "fact_colonias_edit_nacional"],
            next: "fact_cp"
        },
        fact_cp: {
            key: "fact_codigos_edit_nacional",
            selector: "#fact_cp_nacional",
            path: "/Clientes/GetCodigosPostales",
            dataExtra: municipio => ({ municipio_id: municipio, internacional: false }),
            valueField: "cp",
            displayField: "cp",
            labelName: "cp",
            searchField: ["cp"],
            clientField: "codigo_postal",
            clearBelow: ["fact_colonias_edit_nacional"],
            next: "fact_colonia"
        },
        fact_colonia: {
            key: "fact_colonias_edit_nacional",
            selector: "#fact_col_nacional",
            path: "/Clientes/GetColonias",
            dataExtra: cp => ({ cp: cp, internacional: false }),
            valueField: "id_colonia",
            displayField: "nombre",
            labelName: "nombre",
            searchField: ["nombre"],
            clientField: "colonia",
        },
        fact_regimen: {
            key: 'fact_regimen_edit_nacional',
            selector: '#fact_reg_soc_nacional',
            valueField: 'clave',
            displayField: 'clave',
            labelName: 'descripcion',
            searchField: ['clave', 'descripcion'],
            clientField: 'regimen_fiscal',
            render: {
                option: (data, escape) => `<div>${data.clave} - ${escape(data.descripcion)}</div>`,
                item: (data, escape) => `<div><span>${data.clave} - ${escape(data.descripcion)}</span></div>`
            },
        },
        fact_cfdi: {
            key: 'fact_uso_cfdi_edit_nacional',
            selector: '#fact_uso_sug_nacional',
            valueField: 'clave',
            displayField: 'clave',
            labelName: 'descripcion',
            searchField: ['clave', 'descripcion'],
            clientField: 'uso_sugerido',
            render: {
                option: (data, escape) => `<div>${data.clave} - ${escape(data.descripcion)}</div>`,
                item: (data, escape) => `<div><span>${data.clave} - ${escape(data.descripcion)}</span></div>`
            },
        },
        fact_pago: {
            key: 'fact_forma_pago_edit_nacional',
            selector: '#fact_forma_pago_nacional',
            valueField: 'cve_sat',
            displayField: 'cve_sat',
            labelName: 'descripcion',
            searchField: ['cve_sat', 'descripcion'],
            clientField: 'forma_pago',
            render: {
                option: (data, escape) => `<div>${data.cve_sat} - ${escape(data.descripcion)}</div>`,
                item: (data, escape) => `<div><span>${data.cve_sat} - ${escape(data.descripcion)}</span></div>`
            },
        }
    }
};

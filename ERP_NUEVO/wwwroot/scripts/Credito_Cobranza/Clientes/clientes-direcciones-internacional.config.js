/* ─────────────────────────────────────────────────────────────
   clientes-direcciones-internacional.config.js
   Responsabilidad única: describir, de forma declarativa, los
   Tom Selects en cascada del cliente INTERNACIONAL
   (país → estado → ciudad) más incoterms y datos fiscales.

   Lo consume cargarDireccion() de clientes-core.js.
   ───────────────────────────────────────────────────────────── */
const direccionConfigInternacional = {
    create: {
        pais: {
            key: "pais_create_int",
            selector: "#pais_create_int",
            path: "/Clientes/GetPaises",
            data: { internacional: true },
            valueField: "id_pais",
            displayField: "cve_iso",
            labelName: "nombre",
            searchField: ["nombre"],
            next: "estado",
            clearBelow: ["estado_create_int", "municipio_create_int"],
        },
        estado: {
            key: "estado_create_int",
            selector: "#estado_create_int",
            path: "/Clientes/GetEstados",
            dataExtra: pais => ({ pais, internacional: true }),
            valueField: "id_estado",
            displayField: "nombre",
            labelName: "nombre",
            searchField: ["nombre"],
            next: "municipio",
            clearBelow: ["municipio_create_int"],
        },
        municipio: {
            key: "municipio_create_int",
            selector: "#municipio_create_int",
            path: "/Clientes/GetMunicipios",
            dataExtra: estado => ({ estado, internacional: true }),
            valueField: "id_municipio",
            displayField: "nombre",
            labelName: "nombre",
            searchField: ["nombre"]
        },
        vendedores: {
            key: 'vendedores_create_int',
            selector: '#internacional_cve_vdr',
            path: '/Clientes/GetVendedores',
            data: { internacional: true },
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
        pais: {
            key: "pais_edit_int",
            selector: "#edit_pais_internacional",
            path: "/Clientes/GetPaises",
            data: { internacional: true },
            valueField: "id_pais",
            displayField: "cve_iso",
            labelName: "nombre",
            searchField: ["nombre"],
            clientField: "cve_pais",
            clearBelow: ["estado_edit_int", "municipio_edit_int"],
            next: 'estado',
        },
        estado: {
            key: "estado_edit_int",
            selector: "#edit_estado_internacional",
            path: "/Clientes/GetEstados",
            dataExtra: pais => ({ internacional: true, pais }),
            valueField: "id_estado",
            displayField: "nombre",
            labelName: "nombre",
            searchField: ["nombre"],
            clientField: "pob",
            clearBelow: ["municipio_edit_int"],
            next: "municipio"
        },
        municipio: {
            key: "municipio_edit_int",
            selector: "#edit_municipio_internacional",
            path: "/Clientes/GetMunicipios",
            dataExtra: estado => ({ internacional: true, estado }),
            valueField: "id_municipio",
            displayField: "nombre",
            labelName: "nombre",
            searchField: ["nombre"],
            clientField: "municipio"
        },
        vendedores: {
            key: 'vendedores_edit_int',
            selector: '#edit_cve_vdr_internacional',
            path: '/Clientes/GetVendedores',
            data: { internacional: true },
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
        fact_pais: {
            key: "fact_pais_edit_int",
            selector: "#fact_pais_edit",
            path: "/Clientes/GetPaises",
            data: { internacional: true },
            valueField: "id_pais",
            displayField: "cve_iso",
            labelName: "nombre",
            searchField: ["nombre"],
            clientField: "pais",
            clearBelow: ["fact_estado_edit_int", "fact_municipio_edit_int"],
            next: 'fact_estado',
        },
        fact_estado: {
            key: "fact_estado_edit_int",
            selector: "#fact_est_edit_int",
            path: "/Clientes/GetEstados",
            dataExtra: pais => ({ internacional: true, pais }),
            valueField: "id_estado",
            displayField: "nombre",
            labelName: "nombre",
            searchField: ["nombre"],
            clientField: "estado",
            clearBelow: ["fact_municipio_edit_int"],
            next: "fact_municipio"
        },
        fact_municipio: {
            key: "fact_municipio_edit_int",
            selector: "#fact_mpio_edit_int",
            path: "/Clientes/GetMunicipios",
            dataExtra: estado => ({ internacional: true, estado }),
            valueField: "id_municipio",
            displayField: "nombre",
            labelName: "nombre",
            searchField: ["nombre"],
            clientField: "municipio"
        },
        fact_regimen: {
            key: 'fact_regimen_edit_int',
            selector: '#fact_reg_soc_edit_int',
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
            key: 'fact_uso_cfdi_edit_int',
            selector: '#fact_uso_sug_edit_int',
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
            key: 'fact_forma_pago_edit_int',
            selector: '#fact_forma_pago_edit_int',
            valueField: 'cve_sat',
            displayField: 'cve_sat',
            labelName: 'descripcion',
            searchField: ['cve_sat', 'descripcion'],
            clientField: 'forma_pago',
            render: {
                option: (data, escape) => `<div>${data.cve_sat} - ${escape(data.descripcion)}</div>`,
                item: (data, escape) => `<div><span>${data.cve_sat} - ${escape(data.descripcion)}</span></div>`
            },
        },
        incoterns: {
            key: 'incotern',
            selector: '#edit_incot_internacional',
            path: '/Clientes/GetIncoterms',
            valueField: 'id_icoterm',
            displayField: 'id_icoterm',
            labelName: 'descripcion',
            searchField: ['codigo', 'descripcion'],
            clientField: 'incoterm_id',
            render: {
                option: (data, escape) => `<div>${data.codigo} - ${escape(data.descripcion)}</div>`,
                item: (data, escape) => `<div><span>${data.codigo} - ${escape(data.descripcion)}</span></div>`
            },
        }
    }
};

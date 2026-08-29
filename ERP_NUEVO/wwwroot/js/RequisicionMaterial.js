let endpointBusqueda = '/Almacen/Producto/Buscar';

$('#smartwizard').smartWizard({
    selected: 0,
    theme: 'basic',
    justified: true,
    autoAdjustHeight: true,
    backButtonSupport: true,
    enableUrlHash: true,
    enableFormValidation: true,
    transition: {
        animation: 'slideHorizontal',
        speed: '400',
    },
    toolbar: {
        position: 'both',
        showNextButton: true,
        showPreviousButton: true,
    },
    anchor: {
        enableNavigation: true,
        enableNavigationAlways: false,
        enableDoneState: true,
        markPreviousStepsAsDone: true,
        unDoneOnBackNavigation: false,
        enableDoneStateNavigation: true
    },
    lang: {
        next: 'Siguiente',
        previous: 'Anterior'
    }
});

// Validación manual con clases
$("#smartwizard").on("leaveStep", function (e, anchorObject, currentStepIdx, nextStepIdx, stepDirection) {
    if (stepDirection === 'forward') {
        const form = document.getElementById('form-' + (currentStepIdx + 1));
        const navItem = document.querySelector(`.nav-link[href="#step-${currentStepIdx + 1}"]`);

        const tabla = document.getElementById('tabla-materiales');
        const tipoSeleccionado = document.querySelector('input[name="tpnom"]:checked');
        const dataOption = tipoSeleccionado ? tipoSeleccionado.closest('.product-card')?.dataset.option : null;
        const esVenta = dataOption === "venta";
        const inputModoCompra = document.getElementById('modoCompraDirecta');
        const esCompraDirecta = inputModoCompra ? inputModoCompra.checked : false;

        // Mostrar u ocultar columna de precio venta
        $(tabla).find('.columna-venta').toggle(esVenta);
        $(tabla).find('.columna-descuento').toggle(esCompraDirecta);

        if (form && !form.checkValidity()) {
            form.classList.add('was-validated');
            if ((currentStepIdx + 1) === 2) {
                navItem?.classList.add('error');
                toastMixin.fire({
                    icon: 'error',
                    title: 'Debes seleccionar un usuario.'
                })
                return false;
            }

            // Marca el paso como inválido visualmente
            navItem?.classList.add('error');

            toastMixin.fire({
                icon: 'error',
                title: 'Debes seleccionar el tipo de producto.'
            })

            return false;
        }

        // Quita el estilo de error si ya es válido
        if ((currentStepIdx + 1) === 2) {
            var validate = validarDatos();
            if (!validate) {
                navItem?.classList.add('error');
                return false;
            }

            actualizarColumnasDeVenta()
            generarResumen();
            navItem?.classList.remove('error');
        }

        actualizarColumnasDeVenta()
        navItem?.classList.remove('error');
    }
});

$('#smartwizard').smartWizard("reset");

document.querySelectorAll('.product-card').forEach(card => {
    card.addEventListener('click', function () {
        // Remover selección anterior
        document.querySelectorAll('.product-card').forEach(c => c.classList.remove('selected'));

        // Seleccionar la tarjeta actual
        this.classList.add('selected');

        // Marcar el radio button
        const radio = this.querySelector('input[type="radio"]');
        radio.checked = true;
    });
});

function calcularTotales() {
    let subtotal = 0;
    let totalImpuestos = 0;

    const filas = document.querySelectorAll("#tabla-materiales tbody tr");

    filas.forEach((row, i) => {
        // Numeración
        row.cells[1].innerText = i + 1;

        const cantidad = limpiarNumero(row.cells[2].innerText);
        const costoUnitario = limpiarNumero(row.cells[6].innerText);

        const base = cantidad * costoUnitario;

        // IVA seleccionado
        let impuestoFila = 0;

        const totalFila = base + impuestoFila;

        row.querySelector(".total-fila").innerText =
            totalFila ? `$ ${totalFila.toFixed(2)}` : "$ 0.00";

        subtotal += base;
        totalImpuestos += impuestoFila;
    });

    const totalFinal = subtotal + totalImpuestos;

    document.getElementById("total").value = `$ ${totalFinal.toFixed(2)}`;
}

let proveedoresList = [];
async function cargarProveedores() {
    const prov = await fetch('/Requisicion/getListaProveedores');
    proveedoresList = await prov.json();
}

cargarProveedores();

async function agregarFila(datosProducto = null) {
    const tbody = document.querySelector("#tabla-materiales tbody");
    const fila = document.createElement("tr");

    const c1 = datosProducto?.id || '';
    const c2 = datosProducto?.descripcion || '';
    const c11 = datosProducto?.udm || '';
    const inputModoCompra = document.getElementById('modoCompraDirecta');
    const esCompraDirecta = inputModoCompra ? inputModoCompra.checked : false;
    const esVenta = document.getElementById("venta")?.checked === true;

    let html =
        `<td class="text-center"><input type="checkbox" class="seleccionar-fila"></td>` +
        `<td></td>` +
        `<td contenteditable="true" class="input-cantidad"></td>` +
        `<td contenteditable="true" class="input-codigo">${c1}</td>` +
        `<td contenteditable="true" class="input-descripcion">${c2}</td>` +
        `<td contenteditable="true" class="input-unidad">${c11}</td>` +
        `<td contenteditable="true" class="input-costo precio-unitario">$ 0.00</td>`;
    ;

    if (esVenta) {
        html += `<td contenteditable="true" class="input-venta precio-venta">$ 0.00</td>`;
    }

    if (esCompraDirecta) {
        html += `<td contenteditable="true" class="input-descuento descuento">$ 0.00</td>`;
    }

    html += `<td class="total-fila">$ 0.00</td>`;

    fila.innerHTML = html;

    if (esCompraDirecta) {
        const td = document.createElement('td');
        td.classList.add('proveedor');
        const select = document.createElement('select');
        select.classList.add('proveedorId');
        select.setAttribute('autocomplete', 'off');
        td.appendChild(select);
        fila.appendChild(td);

        if (proveedoresList.length === 0) {
            cargarProveedores().then(() => {
                inicializarTomSelectProveedor(select);
            });
        } else {
            inicializarTomSelectProveedor(select);
        }
    }

    fila.querySelectorAll("td[contenteditable='true']").forEach(cell => {
        cell.addEventListener("input", calcularTotales);
    });

    tbody.appendChild(fila);
}

function inicializarTomSelectProveedor(select, valor = null) {
    const ts = new TomSelect(select, {
        persist: false,
        createOnBlur: true,
        create: true,
        dropdownParent: `body`,
        options: proveedoresList,
        valueField: "id",
        labelField: "nombre",
        searchField: ["clave", "nombre"],
        render: {
            option: function (data, escape) {
                return `<div style="color: var(--text-dark)">${escape(data.nombre)}</div>`;
            },
            item: function (data, escape) {
                return `<div style="color: var(--text-dark)">${escape(data.nombre)}</div>`;
            }
        }
    });

    if (valor) ts.setValue(valor);
}

function eliminarFilasSeleccionadas() {
    const checkboxes = document.querySelectorAll(".seleccionar-fila:checked");
    checkboxes.forEach(chk => {
        chk.closest("tr").remove();
    });
    calcularTotales();
}

// Escuchar cambios en celdas iniciales
document.querySelectorAll("#tabla-materiales tbody td[contenteditable='true']").forEach(cell => {
    cell.addEventListener("input", calcularTotales);
});

calcularTotales();

const limpiarNumero = (str) => str.replace(/[^\d.]/g, '');

// Función para buscar productos con paginación
let pageSize = 50; // Número de productos por página
let totalProducts = 0; // <-- usar let en lugar de const
let currentPage = 1

document.getElementById('pageSizeSelector').addEventListener('change', () => buscarProductos("", currentPage));

function buscarProductos(nombre, page = 1) {
    currentPage = page;
    pageSize = parseInt(document.getElementById('pageSizeSelector').value);
    fetch(`${endpointBusqueda}?nombre=${encodeURIComponent(nombre)}&page=${page}&pageSize=${pageSize}`)
        .then(res => res.json())
        .then(response => {
            const lista = document.getElementById('listaResultados');
            lista.innerHTML = ''; // Limpiar resultados anteriores
            updateRecordInfo(page)

            // ⚠️ Ajusta aquí según cómo devuelva tu backend
            // Si regresa { data, total }:
            const data = response.data || response;
            totalProducts = response.total ?? data.length;

            if (data.length === 0) {
                lista.innerHTML = '<div class="p-3">No se encontraron productos.</div>';
                return;
            }

            // Crear tabla
            const tabla = document.createElement('table');
            tabla.classList.add('table', 'table-hover', 'table-bordered');

            tabla.innerHTML = `
                <thead class="table-secondary">
                    <tr>
                        <th style="width: 30%;">Clave</th>
                        <th>Descripción</th>
                    </tr>
                </thead>
                <tbody></tbody>
            `;

            const tbody = tabla.querySelector('tbody');

            data.forEach(producto => {
                const fila = document.createElement('tr');
                fila.style.cursor = 'pointer';
                fila.innerHTML = `
                    <td>${producto.id}</td>
                    <td>${producto.descripcion}</td>
                `;

                fila.addEventListener('click', () => {
                    fetch(`/Almacen/Producto/BuscarProducto?idproducto=${producto.id}`)
                        .then(res => res.json())
                        .then(detalle => {
                            agregarProductoDesdeModal(detalle); // Usar la nueva función
                        });
                });

                tbody.appendChild(fila);
            });

            lista.appendChild(tabla);

            // Actualizar la paginación con el total real
            actualizarPaginacion(page, data.length);

            lista.scrollTop = 0;
        })
        .catch(err => {
            console.error('Error al buscar: ', err);
        });
}

// Función para actualizar los controles de paginación
function actualizarPaginacion(page, currentPageResults) {
    const totalPages = Math.ceil(totalProducts / pageSize); // totalProducts debe estar definido globalmente
    const pagination = document.getElementById('paginationControls');

    if (totalPages <= 1) {
        pagination.innerHTML = '';
        return;
    }

    let html = '';

    // Botón Primera página
    html += `<button class="pagination-btn ${page === 1 ? 'disabled' : ''}"
                    onclick="buscarProductos(document.getElementById('inputBuscarNombre').value, 1)"
                    ${page === 1 ? 'disabled' : ''} title="Primera página">
                    <i class="fas fa-angle-double-left"></i>
                </button>`;

    // Botón Anterior
    html += `<button class="pagination-btn ${page === 1 ? 'disabled' : ''}"
                    onclick="buscarProductos(document.getElementById('inputBuscarNombre').value, ${page - 1})"
                    ${page === 1 ? 'disabled' : ''} title="Página anterior">
                    <i class="fas fa-angle-left"></i>
                </button>`;

    // Calcular rango de páginas visibles
    let start = Math.max(1, page - 2);
    let end = Math.min(totalPages, page + 2);

    if (end - start < 4) {
        if (start === 1) {
            end = Math.min(totalPages, start + 4);
        } else {
            start = Math.max(1, end - 4);
        }
    }

    // Primera página y puntos suspensivos si aplica
    if (start > 1) {
        html += `<button class="pagination-btn"
                        onclick="buscarProductos(document.getElementById('inputBuscarNombre').value, 1)">1</button>`;
        if (start > 2) {
            html += `<span class="pagination-dots">...</span>`;
        }
    }

    // Páginas del rango actual
    for (let i = start; i <= end; i++) {
        html += `<button class="pagination-btn ${i === page ? 'active' : ''}"
                        onclick="buscarProductos(document.getElementById('inputBuscarNombre').value, ${i})">${i}</button>`;
    }

    // Última página y puntos suspensivos si aplica
    if (end < totalPages) {
        if (end < totalPages - 1) {
            html += `<span class="pagination-dots">...</span>`;
        }
        html += `<button class="pagination-btn"
                        onclick="buscarProductos(document.getElementById('inputBuscarNombre').value, ${totalPages})">${totalPages}</button>`;
    }

    // Botón Siguiente
    html += `<button class="pagination-btn ${page === totalPages ? 'disabled' : ''}"
                    onclick="buscarProductos(document.getElementById('inputBuscarNombre').value, ${page + 1})"
                    ${page === totalPages ? 'disabled' : ''} title="Página siguiente">
                    <i class="fas fa-angle-right"></i>
                </button>`;

    // Botón Última página
    html += `<button class="pagination-btn ${page === totalPages ? 'disabled' : ''}"
                    onclick="buscarProductos(document.getElementById('inputBuscarNombre').value, ${totalPages})"
                    ${page === totalPages ? 'disabled' : ''} title="Última página">
                    <i class="fas fa-angle-double-right"></i>
                </button>`;

    pagination.innerHTML = html;
    updateRecordInfo(page)
}

function updateRecordInfo(page) {
    const from = (page - 1) * pageSize + 1;
    const to = Math.min(page * pageSize, totalProducts);

    document.getElementById('recordsFrom').textContent = from;
    document.getElementById('recordsTo').textContent = to;
    document.getElementById('totalRecords').textContent = totalProducts;
}

// Inicializar la búsqueda cuando se abre el modal
document.getElementById('modalBuscarProducto').addEventListener('shown.bs.modal', () => {
    buscarProductos(''); // Realiza una búsqueda sin filtro (muestra todos los productos)
});

// Llamada para buscar productos cuando se escribe en el input
document.getElementById('inputBuscarNombre').addEventListener('input', () => {
    buscarProductos(document.getElementById('inputBuscarNombre').value);
});

// Buscar automáticamente al abrir el modal
const modalBuscarProducto = document.getElementById('modalBuscarProducto');

modalBuscarProducto.addEventListener('shown.bs.modal', () => {
    const nombre = inputBuscarNombre.value.trim();
    buscarProductos(nombre);
});


document.getElementById('btnLimpiarResultados').addEventListener('click', () => {
    //document.getElementById('listaResultados').innerHTML = '';
    buscarProductos('');
    document.getElementById('inputBuscarNombre').value = '';
});

document.getElementById('btnLimpiarResultados').addEventListener('click', () => {
    //document.getElementById('listaResultados').innerHTML = '';
    buscarProductos('');
    document.getElementById('inputBuscarNombre').value = '';
    document.getElementById('inputBuscarNombre').focus();
});

document.getElementById("btn-enviar").addEventListener("click", async function () {
    const warningIcon = `<svg width="64" height="64" viewBox="0 0 64 64" xmlns="http://www.w3.org/2000/svg" style="display: block; margin: auto;">
            <style>
                .flip {
                    animation: spinY 2s linear infinite;
                    transform-origin: center;
                }
                @@keyframes spinY {
                    0%   { transform: rotateY(0deg); }
                    100% { transform: rotateY(360deg); }
                }
            </style>
            <g class="flip">
                <polygon points="32,4 4,60 60,60" fill="#ffc107" stroke="#ff9800" stroke-width="2"/>
                <line x1="32" y1="20" x2="32" y2="38" stroke="#000" stroke-width="4" stroke-linecap="round"/>
                <circle cx="32" cy="48" r="3" fill="#000"/>
            </g>
        </svg>`;

    _Swal.fire({
        iconHtml: warningIcon,
        title: 'Seguro?',
        text: '¿Está seguro que desea crear una solicitud?',
        footer: '<b style="color: red;">ADVERTENCIA - Este cambio no se puede revertir</b>',
        customClass: {
            icon: 'p-0 bg-transparent border-0 shadow-none'
        },
        showCancelButton: true,
        confirmButtonText: 'Sí, crear',
        cancelButtonText: 'Cancelar',
        preConfirm: async () => {
            const inputModoCompra = document.getElementById('modoCompraDirecta');
            const esCompraDirecta = inputModoCompra ? inputModoCompra.checked : false;
            const esVenta = document.getElementById("venta")?.checked === true;

            // VALIDAR DATOS ANTES DE CONTINUAR
            if (!validarDatos()) {
                return false; // Detener si la validación falla
            }

            const observaciones = document.getElementById("observaciones").value.trim();
            const comprador = document.getElementById("comprador").value;
            const tipoSeleccionado = document.querySelector('input[name="tpnom"]:checked').value;
            const presupuesto = document.getElementById("productoPresupuesto");
            const archivo = document.getElementById("archivo");

            // Validar comprador
            if (!comprador) {
                Swal.fire('Error', 'Debes seleccionar un comprador.', 'error');
                return false;
            }

            let datosEnvio;

            if (esCompraDirecta) {
                // ============================================
                // MODO COMPRA DIRECTA
                // ============================================
                const director = document.getElementById("director")?.value;
                const centro = document.getElementById("centro").value;

                // Validaciones adicionales para compra directa
                @{
                    if(Utilities.GetEmpresaName(User.Identity.Name) == "SRS")
    {
        <text>
            if (!director) {
                Swal.fire('Error', 'Debes seleccionar un director.', 'error');
            return false;
                            }
        </text>
    }
}

                    if (!centro) {
    Swal.fire('Error', 'Debes seleccionar un centro de costos.', 'error');
    return false;
}

if (totalGeneralCompra <= 0) {
    Swal.fire('Error', 'El total debe ser mayor a 0.', 'error');
    return false;
}

// Recopilar datos por proveedor
const proveedores = [];

for (const [proveedorId, proveedorData] of Object.entries(proveedoresActivos)) {
    const cardId = proveedorData.id;
    const filas = document.querySelectorAll(`#tabla-${cardId} tbody tr`);
    const productos = [];

    filas.forEach((fila) => {
        const CantUd = limpiarNumero(fila.cells[2].innerText.trim());
        const CveProd = fila.cells[3].innerText.trim();
        const DescrProd = fila.cells[4].innerText.trim();
        const Ud = fila.cells[5].innerText.trim();
        const PvProd = limpiarNumero(fila.cells[6].innerText.trim());

        let CtoVtaPart = null;
        let Dto1, total;

        if (esVenta) {
            CtoVtaPart = limpiarNumero(fila.cells[7].innerText.trim());
            Dto1 = limpiarNumero(fila.cells[8].innerText.trim());
            total = limpiarNumero(fila.cells[9].innerText.trim());
        } else {
            Dto1 = limpiarNumero(fila.cells[7].innerText.trim());
            total = limpiarNumero(fila.cells[8].innerText.trim());
        }

        productos.push({
            CantUd,
            CveProd,
            DescrProd,
            Ud,
            PvProd,
            total,
            Dto1,
            ...(esVenta && { CtoVtaPart })
        });
    });

    const impuestoSelect = document.querySelectorAll(`.select-impuesto-${cardId}`);

    const impuestos = [];

    for (const select of impuestoSelect) {
        if (!select.value.trim()) {
            Swal.fire('Error', 'Todos los impuestos deben seleccionarse.', 'error');
            return false;
        }

        impuestos.push({ IdImpuesto: parseInt(select.value) });
    }

    proveedores.push({
        Id_Prov: parseInt(proveedorId),
        N_Prov: proveedorData.nombre,
        Impuestos: impuestos,
        totalProveedor: proveedorData.total,
        Productos: productos
    });
}

datosEnvio = {
    observaciones,
    compradorseleccionado: comprador,
    centro_costos: centro,
    total: totalGeneralCompra,
    proveedores: JSON.stringify(proveedores),
    tpnom: tipoSeleccionado,
    tipo_producto: esVenta ? 'directo' : 'indirecto',
    archivo: archivo.files[0],
                        @{
    if(Utilities.GetEmpresaName(User.Identity.Name) == "SRS")
                            {
    <text>
        directorSeleccionado: director,
        presupuesto: presupuesto.checked,
    </text>
}
                        }
                    };

                } else {
    // ============================================
    // MODO NORMAL
    // ============================================
    const total = limpiarNumero(document.getElementById("total").value.trim());
    const centro = document.getElementById("centro").value;

    if (total <= 0) {
        Swal.fire('Error', 'El total debe ser mayor a 0.', 'error');
        return false;
    }

    if (!centro) {
        Swal.fire('Error', 'Debes seleccionar un centro de costos.', 'error');
        return false;
    }

    // Recopilar datos de la tabla única
    const filasMateriales = [];
    const tabla = document.querySelector("#tabla-materiales tbody");
    const filas = tabla.querySelectorAll("tr");

    filas.forEach((fila) => {
        const cantidad = limpiarNumero(fila.cells[2].innerText.trim());
        const codigo = fila.cells[3].innerText.trim();
        const descripcion = fila.cells[4].innerText.trim();
        const unidad = fila.cells[5].innerText.trim();
        const costoUnitario = limpiarNumero(fila.cells[6].innerText.trim());

        let precioVenta = null;
        let descuento, total;

        if (esVenta) {
            precioVenta = limpiarNumero(fila.cells[7].innerText.trim());
            total = limpiarNumero(fila.cells[8].innerText.trim());
        } else {
            total = limpiarNumero(fila.cells[7].innerText.trim());
        }

        filasMateriales.push({
            cantidad,
            codigo,
            descripcion,
            unidad,
            costoUnitario,
            total,
            ...(esVenta && { precioVenta })
        });
    });

    datosEnvio = {
        observaciones,
        centro_costos: centro,
        compradorseleccionado: comprador,
        total,
        materiales: JSON.stringify(filasMateriales),
        tpnom: tipoSeleccionado,
        tipo_producto: esVenta ? 'directo' : 'indirecto',
        archivo: archivo.files[0],
                        @{
        if(Utilities.GetEmpresaName(User.Identity.Name) == "SRS")
                            {
        <text>
            presupuesto: presupuesto.checked,
        </text>
    }


}
                    };
                }

// Mostrar loader
Swal.fire({
    title: esCompraDirecta ? 'Generando orden de compra directa' : 'Creando solicitud',
    allowOutsideClick: false,
    allowEscapeKey: false,
    showConfirmButton: false,
    didOpen: () => {
        Swal.showLoading();
    }
});

try {
    const res = await GetData({
        path: esCompraDirecta ? '/Requisicion/OCDirecta' : '/Requisition/Create',
        data: datosEnvio
    });

    Swal.close();

    if (res.success) {
        _SwalReload({
            icon: 'success',
            title: esCompraDirecta ? 'Orden de compra directa creada' : 'Solicitud creada',
            html: res.message,
            showCancelButton: false,
        });
    } else {
        Swal.fire('Error', res.message || 'No se pudo completar el proceso.', 'error');
    }
} catch (err) {
    Swal.close();
    console.error('Error al enviar:', err);
    Swal.fire('Error', 'Ocurrió un error al enviar la solicitud.', 'error');
}
            }
        });
    });

// Validar datos de la tabla
function validarDatos() {
    const inputModoCompra = document.getElementById('modoCompraDirecta');
    const esCompraDirecta = inputModoCompra ? inputModoCompra.checked : false;
    const esVenta = document.getElementById("venta")?.checked === true;

    if (esCompraDirecta) {
        // Validar modo compra directa
        return validarDatosCompraDirecta(esVenta);
    } else {
        // Validar modo normal
        return validarDatosNormal(esVenta);
    }
}

// Validar datos en modo normal
function validarDatosNormal(esVenta) {
    const filasMateriales = [];
    let errorFila = false;

    const tabla = document.querySelector("#tabla-materiales tbody");

    if (!tabla) {
        toastMixin.fire({
            icon: 'error',
            title: 'No se encontró la tabla de materiales.'
        });
        return false;
    }

    const filas = tabla.querySelectorAll("tr");

    if (filas.length === 0) {
        toastMixin.fire({
            icon: 'error',
            title: 'Debes agregar al menos un producto.'
        });
        return false;
    }

    filas.forEach((fila) => {
        const cantidad = limpiarNumero(fila.cells[2].innerText.trim());
        const codigo = fila.cells[3].innerText.trim();
        const descripcion = fila.cells[4].innerText.trim();
        const unidad = fila.cells[5].innerText.trim();
        const costoUnitario = limpiarNumero(fila.cells[6].innerText.trim());

        let total, precioVenta = null;

        if (esVenta) {
            precioVenta = limpiarNumero(fila.cells[7].innerText.trim());
            total = limpiarNumero(fila.cells[8].innerText.trim());
        } else {
            total = limpiarNumero(fila.cells[7].innerText.trim());
        }

        // Validación base
        let filaValida =
            cantidad > 0 &&
            codigo !== "" &&
            descripcion !== "" &&
            unidad !== "";

        if (esVenta && filaValida) {
            filaValida = precioVenta > 0;
        }

        if (filaValida) {
            filasMateriales.push({
                cantidad,
                codigo,
                descripcion,
                unidad,
                costoUnitario,
                total,
                ...(esVenta && { precioVenta })
            });
        } else {
            errorFila = true;
        }
    });

    if (filasMateriales.length === 0 || errorFila) {
        toastMixin.fire({
            icon: 'error',
            title: 'Hay filas incompletas o vacías. Revisa que todos los campos obligatorios estén llenos.'
        });
        return false;
    }

    return true;
}

// Validar datos en modo compra directa
function validarDatosCompraDirecta(esVenta) {
    // Verificar que haya al menos un proveedor
    if (Object.keys(proveedoresActivos).length === 0) {
        toastMixin.fire({
            icon: 'error',
            title: 'Debes agregar al menos un proveedor.'
        });
        return false;
    }

    let hayErrores = false;
    let proveedorSinProductos = false;

    // Validar cada proveedor
    for (const [proveedorId, proveedorData] of Object.entries(proveedoresActivos)) {
        const cardId = proveedorData.id;
        const tabla = document.querySelector(`#tabla-${cardId} tbody`);

        if (!tabla) {
            toastMixin.fire({
                icon: 'error',
                title: `No se encontró la tabla del proveedor ${proveedorData.nombre}.`
            });
            return false;
        }

        const filas = tabla.querySelectorAll("tr");

        if (filas.length === 0) {
            proveedorSinProductos = true;
            toastMixin.fire({
                icon: 'error',
                title: `El proveedor "${proveedorData.nombre}" no tiene productos agregados.`
            });
            return false;
        }

        // Validar cada fila del proveedor
        filas.forEach((fila, index) => {
            const cantidad = limpiarNumero(fila.cells[2].innerText.trim());
            const codigo = fila.cells[3].innerText.trim();
            const descripcion = fila.cells[4].innerText.trim();
            const unidad = fila.cells[5].innerText.trim();
            const costoUnitario = limpiarNumero(fila.cells[6].innerText.trim());

            let precioVenta = null;
            let descuento, total;

            if (esVenta) {
                precioVenta = limpiarNumero(fila.cells[7].innerText.trim());
                descuento = limpiarNumero(fila.cells[8].innerText.trim());
                total = limpiarNumero(fila.cells[9].innerText.trim());
            } else {
                descuento = limpiarNumero(fila.cells[7].innerText.trim());
                total = limpiarNumero(fila.cells[8].innerText.trim());
            }

            // Validación base
            let filaValida =
                cantidad > 0 &&
                codigo !== "" &&
                descripcion !== "" &&
                unidad !== "" &&
                costoUnitario > 0 &&
                total > 0;

            @{
                if(Utilities.GetEmpresaName(User.Identity.Name) == "SRS")
        {
            <text>
                                // Validación adicional para venta
                if (esVenta && filaValida) {
                    filaValida = precioVenta > 0;
                                }
            </text>
        }
    }

    if (!filaValida) {
        hayErrores = true;
        toastMixin.fire({
            icon: 'error',
            title: `Hay campos incompletos en el proveedor "${proveedorData.nombre}", fila ${index + 1}.`
        });
    }
});

if (hayErrores) {
    return false;
}
        }

// Validar total general
if (totalGeneralCompra <= 0) {
    toastMixin.fire({
        icon: 'error',
        title: 'El total general debe ser mayor a 0.'
    });
    return false;
}

// Validar campos adicionales de compra directa
const director = document.getElementById('director');
const centro = document.getElementById('centro');

@{
    if(Utilities.GetEmpresaName(User.Identity.Name) == "srs")
            {
    <text>
        if (director && !director.value) {
            toastMixin.fire({
                icon: 'error',
                title: 'Debes seleccionar un director.'
            });
        return false;
                }
    </text>
}
        }

if (centro && !centro.value) {
    toastMixin.fire({
        icon: 'error',
        title: 'Debes seleccionar un centro de costos.'
    });
    return false;
}

return true;
    }

// Formatear campos numericos
const radios = document.querySelectorAll('input[name="tpnom"]');
const tabla = document.querySelector(`#tabla-materiales`);

tabla.addEventListener("blur", (e) => {
    if (e.target.matches("td.precio-unitario") || e.target.matches("td.precio-venta") || e.target.matches("td.descuento")) {
        const tr = e.target.closest("tr");
        const tdPrecio = tr.querySelector(".precio-unitario");
        const tdVenta = tr.querySelector(".precio-venta");
        const esVenta = tdVenta ? true : false;
        const tdTotal = tr.querySelector(".total-fila");
        const tdDto = tr.querySelector(".descuento");

        // Sanitizar ambos
        let precioStr = tdPrecio.innerText.replace(/[^0-9.,]/g, '').replace(',', '.');
        let precio = parseFloat(precioStr);
        if (isNaN(precio)) precio = 0;
        tdPrecio.innerText = `$ ${precio.toFixed(2)}`;

        if (esVenta) {
            let ventaStr = tdVenta.innerText.replace(/[^0-9.,]/g, '').replace(',', '.');
            let precioV = parseFloat(ventaStr);
            if (isNaN(precioV)) precioV = 0;
            tdVenta.innerText = `$ ${precioV.toFixed(2)}`;
        }

        if (tdDto) {
            let descuentoStr = tdDto.innerText.replace(/[^0-9.,]/g, '').replace(',', '.');
            let descuento = parseFloat(descuentoStr);
            if (isNaN(descuento)) descuento = 0;
            tdDto.innerText = `$ ${descuento.toFixed(2)}`;
        }

        const cantidad = parseFloat(tr.querySelector(".input-cantidad")?.innerText) || 0;
        const total = precio * cantidad;
        tdTotal.innerText = `$ ${total.toFixed(2)}`;
    }
}, true);

// Limpieza visual al enfocar ambos campos
tabla.addEventListener("focusin", (e) => {
    if (e.target.matches("td.precio-unitario") || e.target.matches("td.precio-venta") || e.target.matches("td.descuento")) {
        const raw = e.target.innerText.replace(/[^0-9.,]/g, '').replace(',', '.');
        e.target.innerText = raw ? parseFloat(raw) : '';
    }
});

tabla.addEventListener("focusin", (e) => {
    if (e.target.matches("td.precio-unitario")) {
        const raw = e.target.innerText.replace(/[^0-9.,]/g, '').replace(',', '.');
        e.target.innerText = raw ? parseFloat(raw) : '';
    }
});

// Quitar precio de venta de la tabla
function actualizarColumnasDeVenta() {
    const esVenta = document.getElementById("venta")?.checked === true;
    const esServicio = document.getElementById("servicio")?.checked === true;

    // Actualizar cuerpo
    const filas = tabla.querySelectorAll("tbody tr");
    filas.forEach(fila => {
        const celdas = fila.querySelectorAll("td");
        const yaTiene = fila.querySelector(".precio-venta");

        if (esVenta && !yaTiene) {
            const nueva = document.createElement("td");
            nueva.className = "input-venta precio-venta";
            nueva.contentEditable = "true";
            nueva.textContent = "$ 0.00";
            fila.insertBefore(nueva, celdas[celdas.length - 1]);
            nueva.addEventListener("input", calcularTotales);
        } else if (!esVenta && yaTiene) {
            yaTiene.remove();
        }
    });

    if (esServicio) {
        document.getElementById('buscar_producto').classList.add('d-none');
    } else {
        document.getElementById('buscar_producto').classList.remove('d-none');
    }
}

// Escuchar cambios
radios.forEach(radio => {
    radio.addEventListener("change", actualizarColumnasDeVenta);
});

// Por si ya viene seleccionado
actualizarColumnasDeVenta();

// Resumen para el tercer paso del wizard
// Resumen para el tercer paso del wizard
function generarResumen() {
    const resumenDiv = document.getElementById('resumen');
    resumenDiv.innerHTML = '';

    const esVenta = document.getElementById("venta")?.checked === true;
    const esCompraDirecta = document.getElementById("modoCompraDirecta")?.checked === true;

    const comprador = document.getElementById("comprador")?.selectedOptions?.[0]?.text || '—';
    const director = document.getElementById("director")?.selectedOptions?.[0]?.text || '—';
    const observaciones = document.getElementById("observaciones")?.value.trim() || '—';

    let total;
    if (esCompraDirecta) {
        total = totalGeneralCompra;
    } else {
        total = limpiarNumero(document.getElementById("total")?.value || "0");
    }

    const datos = [
        { titulo: "Comprador", valor: comprador },
        ...(esCompraDirecta ? [{ titulo: "Director", valor: director }] : []),
        { titulo: "Total", valor: `${Currency.format(total)}`, badge: true },
        { titulo: "Observaciones", valor: observaciones },
    ];

    let cardsHtml = '';
    datos.forEach(d => {
        const colClass = d.titulo === 'Observaciones' ? 'col-12' : 'col-md-4';
        cardsHtml += `
            <div class="${colClass}">
                <div class="card-resumen mb-3">
                    <h6>${d.titulo}</h6>
                    <p>
                        ${d.badge ? `<span class="badge-tag">${d.valor}</span>` : d.valor}
                    </p>
                </div>
            </div>
        `;
    });

    // Check box
    let tipoMensaje = '';
    if (document.getElementById("venta")?.checked) {
        tipoMensaje = 'Estos productos fueron seleccionados para <strong>venta</strong>.';
    } else if (document.getElementById("interno")?.checked) {
        tipoMensaje = 'Estos productos fueron seleccionados para <strong>uso interno</strong>.';
    } else if (document.getElementById("servicio")?.checked) {
        tipoMensaje = 'Esta solicitud es para la <strong>contratación de un servicio</strong>.';
    }

    let tablasHTML = '';

    if (esCompraDirecta) {
        // Generar resumen por proveedor
        for (const [proveedorId, proveedorData] of Object.entries(proveedoresActivos)) {
            const cardId = proveedorData.id;
            const materiales = [];

            const filas = document.querySelectorAll(`#tabla-${cardId} tbody tr`);
            filas.forEach((fila) => {
                const cantidad = fila.cells[2].innerText.trim();
                const codigo = fila.cells[3].innerText.trim();
                const descripcion = fila.cells[4].innerText.trim();
                const unidad = fila.cells[5].innerText.trim();
                const costoUnitario = fila.cells[6].innerText.trim().replace('$', '').replace(',', '');

                let precioVenta, descuento, total;

                if (esVenta) {
                    precioVenta = fila.cells[7].innerText.trim().replace('$', '').replace(',', '');
                    descuento = fila.cells[8].innerText.trim().replace('$', '').replace(',', '');
                    total = fila.cells[9].innerText.trim().replace('$', '').replace(',', '');
                } else {
                    descuento = fila.cells[7].innerText.trim().replace('$', '').replace(',', '');
                    total = fila.cells[8].innerText.trim().replace('$', '').replace(',', '');
                }

                materiales.push({
                    cantidad,
                    codigo,
                    descripcion,
                    unidad,
                    costoUnitario,
                    ...(esVenta && { precioVenta }),
                    descuento,
                    total
                });
            });

            tablasHTML += `
                <div class="col-12 mb-3">
                    <div class="card-resumen">
                        <h6 style="color: var(--primary-blue); margin-bottom: 1rem;">
                            <i class="fas fa-building"></i> ${proveedorData.nombre}
                        </h6>
                        <div class="table-responsive">
                            <table class="table-resumen">
                                <thead>
                                    <tr>
                                        <th>Cantidad</th>
                                        <th>Código</th>
                                        <th>Descripción</th>
                                        <th>Unidad</th>
                                        <th>Costo Unitario</th>
                                        ${esVenta ? '<th>Precio Venta</th>' : ''}
                                        <th>Descuento</th>
                                        <th>Total</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    ${materiales.map(mat => `
                                        <tr>
                                            <td>${mat.cantidad}</td>
                                            <td>${mat.codigo}</td>
                                            <td>${mat.descripcion}</td>
                                            <td>${mat.unidad}</td>
                                            <td>${Currency.format(mat.costoUnitario)}</td>
                                            ${esVenta ? `<td>${Currency.format(mat.precioVenta)}</td>` : ''}
                                            <td>${Currency.format(mat.descuento)}</td>
                                            <td>${Currency.format(mat.total)}</td>
                                        </tr>
                                    `).join('')}
                                </tbody>
                                <tfoot>
                                    <tr style="background-color: rgba(59, 130, 246, 0.1); font-weight: 600;">
                                        <td colspan="${esVenta ? 7 : 6}" style="text-align: right;">Total Proveedor:</td>
                                        <td>${Currency.format(proveedorData.total)}</td>
                                    </tr>
                                </tfoot>
                            </table>
                        </div>
                    </div>
                </div>
            `;
        }
    } else {
        // Modo normal - tabla única
        const materiales = [];
        document.querySelectorAll("#tabla-materiales tbody tr").forEach((fila) => {
            const cantidad = fila.cells[2].innerText.trim();
            const codigo = fila.cells[3].innerText.trim();
            const descripcion = fila.cells[4].innerText.trim();
            const unidad = fila.cells[5].innerText.trim();
            const costoUnitario = fila.cells[6].innerText.trim().replace('$', '').replace(',', '');

            let total, precioVenta;

            if (esVenta) {
                precioVenta = fila.cells[7].innerText.trim().replace('$', '').replace(',', '');
                total = fila.cells[8].innerText.trim().replace('$', '').replace(',', '');
            } else {
                total = fila.cells[7].innerText.trim().replace('$', '').replace(',', '');
            }

            materiales.push({
                cantidad,
                codigo,
                descripcion,
                unidad,
                costoUnitario,
                ...(esVenta && { precioVenta }),
                total
            });
        });

        tablasHTML = `
            <div class="col-12">
                <div class="card-resumen">
                    <h6>Materiales</h6>
                    <div class="table-responsive">
                        <table class="table-resumen">
                            <thead>
                                <tr>
                                    <th>Cantidad</th>
                                    <th>Código</th>
                                    <th>Descripción</th>
                                    <th>Unidad</th>
                                    <th>Costo Unitario</th>
                                    ${esVenta ? '<th>Precio Venta</th>' : ''}
                                    <th>Total</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${materiales.map(mat => `
                                    <tr>
                                        <td>${mat.cantidad}</td>
                                        <td>${mat.codigo}</td>
                                        <td>${mat.descripcion}</td>
                                        <td>${mat.unidad}</td>
                                        <td>${Currency.format(mat.costoUnitario)}</td>
                                        ${esVenta ? `<td>${Currency.format(mat.precioVenta)}</td>` : ''}
                                        <td>${Currency.format(mat.total)}</td>
                                    </tr>
                                `).join('')}
                            </tbody>
                        </table>
                    </div>
                </div>
            </div>
        `;
    }

    resumenDiv.innerHTML = `
        <div class="alert-info-resumen">
            ${tipoMensaje}
        </div>
        <div class="row">
            ${cardsHtml}
        </div>
        <div class="row">
            ${tablasHTML}
        </div>
    `;
}
//Cambio a seervicios
document.querySelectorAll('.product-card').forEach(card => {
    card.addEventListener('click', function () {
        document.querySelectorAll('.product-card').forEach(c => c.classList.remove('selected'));
        this.classList.add('selected');
        const radio = this.querySelector('input[type="radio"]');
        radio.checked = true;
        radio.dispatchEvent(new Event('change', { bubbles: true })); // ← AQUÍ
    });
});

document.addEventListener('change', function (e) {
    if (e.target.matches('input[name="tpnom"]')) {
        const btnSpan = document.querySelector('#buscar_producto span');
        if (e.target.value === 'SGTO') {
            endpointBusqueda = '/Almacen/Producto/BuscarServicio';
            if (btnSpan) btnSpan.textContent = 'Consultar Servicios';
        } else {
            endpointBusqueda = '/Almacen/Producto/Buscar';
            if (btnSpan) btnSpan.textContent = 'Consultar Productos';
        }
        console.log('endpointBusqueda ahora es:', endpointBusqueda);
    }
});
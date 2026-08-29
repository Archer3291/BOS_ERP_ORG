let availableProducts = [];
let currentPage = 1;
let totalPages = 1;
let isLoadingProducts = false;
let searchTimeout = null;

// Variables para kits
let availableKits = [];
let currentKitPage = 1;
let totalKitPages = 1;
let isLoadingKits = false;
let kitSearchTimeout = null;

var selectedKitId = 0;

// Productos seleccionados para el BOM
let selectedProducts = [];
let currentEditingRow = null;
let kitSelectInstance = null;

// Inicialización
document.addEventListener('DOMContentLoaded', function () {
    loadProducts(1); // Cargar primera página
    loadKits(1); // Cargar kits
    document.getElementById('productionDate').valueAsDate = new Date();
    initializeKitSelect();
    updateStepIndicators();
});

// Cargar kits con paginación
function loadKits(page = 1, search = '') {
    if (isLoadingKits) return;

    isLoadingKits = true;
    currentKitPage = page;

    GetData({
        path: `/Kits/ObtenerKits?page=${page}&pageSize=50&search=${encodeURIComponent(search)}`,
    }).then((_res) => {
        availableKits = _res.kits;

        // Actualizar info de paginación
        const pagination = _res.pagination;
        currentKitPage = pagination.currentPage;
        totalKitPages = pagination.totalPages;

        // Actualizar Tom Select si está inicializado
        if (kitSelectInstance) {
            kitSelectInstance.clearOptions();
            kitSelectInstance.addOptions(availableKits);
        }

        isLoadingKits = false;
    }).catch(error => {
        console.error('Error cargando kits:', error);
        isLoadingKits = false;
    });
}

// Cargar productos con paginación
function loadProducts(page = 1, search = '') {
    if (isLoadingProducts) return;

    isLoadingProducts = true;
    currentPage = page;

    // Mostrar indicador de carga en el modal si está abierto
    const productList = document.getElementById('productList');
    if (productList) {
        productList.innerHTML = '<div class="text-center py-4"><div class="spinner-border text-primary" role="status"><span class="visually-hidden">Cargando...</span></div></div>';
    }

    GetData({
        path: `/Kits/DatosSelect?page=${page}&pageSize=50&search=${encodeURIComponent(search)}`,
    }).then((_res) => {
        availableProducts = _res.productos;

        // Actualizar info de paginación
        const pagination = _res.pagination;
        currentPage = pagination.currentPage;
        totalPages = pagination.totalPages;

        // Actualizar UI si el modal está abierto
        if (productList) {
            renderProductList();
            renderPagination(pagination);
        }

        isLoadingProducts = false;
    }).catch(error => {
        console.error('Error cargando productos:', error);
        if (productList) {
            productList.innerHTML = '<div class="alert alert-danger">Error al cargar productos. Intente nuevamente.</div>';
        }
        isLoadingProducts = false;
    });
}

// Renderizar controles de paginación
function renderPagination(pagination) {
    const paginationContainer = document.getElementById('productPagination');
    if (!paginationContainer) return;

    let html = '<nav aria-label="Paginación de productos"><ul class="pagination pagination-sm justify-content-center mb-0">';

    // Botón anterior
    html += `
        <li class="page-item ${!pagination.hasPreviousPage ? 'disabled' : ''}">
            <a class="page-link" href="#" onclick="changePage(${pagination.currentPage - 1}); return false;">
                <i class="bi bi-chevron-left"></i>
            </a>
        </li>
    `;

    // Páginas
    const startPage = Math.max(1, pagination.currentPage - 2);
    const endPage = Math.min(pagination.totalPages, pagination.currentPage + 2);

    if (startPage > 1) {
        html += `<li class="page-item"><a class="page-link" href="#" onclick="changePage(1); return false;">1</a></li>`;
        if (startPage > 2) {
            html += `<li class="page-item disabled"><span class="page-link">...</span></li>`;
        }
    }

    for (let i = startPage; i <= endPage; i++) {
        html += `
            <li class="page-item ${i === pagination.currentPage ? 'active' : ''}">
                <a class="page-link" href="#" onclick="changePage(${i}); return false;">${i}</a>
            </li>
        `;
    }

    if (endPage < pagination.totalPages) {
        if (endPage < pagination.totalPages - 1) {
            html += `<li class="page-item disabled"><span class="page-link">...</span></li>`;
        }
        html += `<li class="page-item"><a class="page-link" href="#" onclick="changePage(${pagination.totalPages}); return false;">${pagination.totalPages}</a></li>`;
    }

    // Botón siguiente
    html += `
        <li class="page-item ${!pagination.hasNextPage ? 'disabled' : ''}">
            <a class="page-link" href="#" onclick="changePage(${pagination.currentPage + 1}); return false;">
                <i class="bi bi-chevron-right"></i>
            </a>
        </li>
    `;

    html += '</ul></nav>';

    // Agregar info de registros
    html += `
        <div class="text-center mt-2">
            <small class="text-muted">
                Mostrando ${((pagination.currentPage - 1) * pagination.pageSize) + 1} - 
                ${Math.min(pagination.currentPage * pagination.pageSize, pagination.totalRecords)} 
                de ${pagination.totalRecords} productos
            </small>
        </div>
    `;

    paginationContainer.innerHTML = html;
}

// Cambiar página
function changePage(page) {
    if (page < 1 || page > totalPages || isLoadingProducts) return;
    const search = document.getElementById('searchProduct')?.value || '';
    loadProducts(page, search);
}

// Búsqueda con debounce
function filterProducts() {
    clearTimeout(searchTimeout);
    const search = document.getElementById('searchProduct').value;

    searchTimeout = setTimeout(() => {
        loadProducts(1, search);
    }, 500); // Esperar 500ms después de que el usuario deje de escribir
}

// Mostrar modal actualizado
function showProductModal() {
    const modal = new bootstrap.Modal(document.getElementById('productModal'));
    modal.show();

    // Cargar productos si no están cargados
    if (availableProducts.length === 0) {
        loadProducts(1);
    } else {
        renderProductList();
        // Cargar info de paginación actual
        loadProducts(currentPage, document.getElementById('searchProduct')?.value || '');
    }
}

function initializeKitSelect() {
    // Inicializar Tom Select para selección de kit
    kitSelectInstance = new TomSelect('#kitSelect', {
        valueField: 'id',
        labelField: 'name',
        searchField: ['code', 'name', 'description'],
        options: availableKits, // Ya cargados en DOMContentLoaded
        create: false,
        placeholder: 'Buscar kit por código o nombre...',
        load: function (query, callback) {
            if (!query.length) return callback();

            // Búsqueda dinámica en el servidor
            clearTimeout(kitSearchTimeout);
            kitSearchTimeout = setTimeout(() => {
                loadKits(1, query);
            }, 300);
            callback();
        },
        render: {
            option: function (data, escape) {
                return `<div>
                    <div class="fw-bold" style="color: var(--text-dark)">${escape(data.code)} - ${escape(data.name)}</div>
                    <div class="small text-muted">${escape(data.description || '')}</div>
                    <div class="small">
                        <span class="badge ${data.hascomponents ? 'bg-success' : 'bg-warning'}">
                            ${data.hascomponents ? 'Con BOM' : 'Sin BOM'}
                        </span>
                        <span class="badge bg-info">${data.stock || 0} ${data.unit}</span>
                    </div>
                </div>`;
            },
            item: function (data, escape) {
                return `<div>${escape(data.code)} - ${escape(data.name)}</div>`;
            }
        },
        onChange: function (value) {
            if (value) {
                loadKitData(value);
            } else {
                clearKitData();
            }
        }
    });
}

function loadKitData(kitId) {
    const kit = availableKits.find(k => k.id == kitId);
    if (!kit) return;

    selectedKitId = kitId;

    // Llenar campos con datos del kit
    document.getElementById('kitCode').value = kit.code;
    document.getElementById('kitName').value = kit.name;
    document.getElementById('kitDescription').value = kit.description || '';

    // Deshabilitar campos de código y nombre (ya que el kit existe)
    document.getElementById('kitCode').disabled = true;
    document.getElementById('kitName').disabled = true;

    // Verificar si el kit tiene componentes definidos
    if (kit.hascomponents) {
        // Kit con BOM definido: cargar componentes desde el servidor
        GetData({
            path: `/Kits/ObtenerComponentesKit?kitId=${kitId}`,
        }).then((_res) => {
            if (_res.components && _res.components.length > 0) {
                loadKitComponents(_res.components);
                showKitComponentsStatus(true);
            } else {
                // No se encontraron componentes
                selectedProducts = [];
                updateBomTable();
                showKitComponentsStatus(false);
            }
        }).catch(error => {
            toastMixin.fire({
                icon: 'error',
                title: 'Error al cargar los componentes del kit. Intente nuevamente.'
            })
        });
    } else {
        // Kit sin BOM: permitir agregar componentes
        selectedProducts = [];
        updateBomTable();
        showKitComponentsStatus(false);
    }

    updateStepIndicators();
    validateForm();
}

function loadKitComponents(components) {
    selectedProducts = [];

    components.forEach(comp => {
        // Los componentes ya vienen con toda la info del servidor
        selectedProducts.push({
            id: comp.productid,
            name: comp.name,
            sku: comp.code,
            icon: comp.icon || '📦',
            stock: comp.stock || 0,
            unit: comp.unit,
            id_udm: comp.id_udm,
            requiredPerKit: comp.requiredperkit
        });
    });

    updateBomTable();
    calculateRequirements();
}

function showKitComponentsStatus(hasComponents) {
    const addButton = document.querySelector('.btn-outline-primary[onclick="addProductRow()"]');
    const emptyState = document.getElementById('emptyState');
    const statusAlert = document.getElementById('kitComponentsStatus');

    // Siempre mostrar el botón de agregar productos
    if (addButton) {
        addButton.style.display = 'inline-block';
    }

    if (hasComponents) {
        // Kit con BOM definido - mostrar alerta informativa pero permitir edición
        if (statusAlert) {
            statusAlert.innerHTML = `
                <div class="alert alert-info mb-3">
                    <i class="bi bi-info-circle"></i>
                    <strong>BOM definido:</strong> Este kit tiene componentes predefinidos. 
                    Puedes modificar las cantidades, agregar o eliminar productos según necesites.
                </div>
            `;
        } else {
            const container = document.querySelector('#productsContainer').parentElement;
            const alert = document.createElement('div');
            alert.id = 'kitComponentsStatus';
            alert.innerHTML = `
                <div class="alert alert-info mb-3">
                    <i class="bi bi-info-circle"></i>
                    <strong>BOM definido:</strong> Este kit tiene componentes predefinidos. 
                    Puedes modificar las cantidades, agregar o eliminar productos según necesites.
                </div>
            `;
            container.insertBefore(alert, container.firstChild.nextSibling);
        }
    } else {
        // Kit sin BOM
        if (statusAlert) {
            statusAlert.innerHTML = `
                <div class="alert alert-warning mb-3">
                    <i class="bi bi-exclamation-triangle"></i>
                    <strong>Sin BOM definido:</strong> Este kit no tiene componentes asignados. 
                    Por favor, agrega los productos necesarios para completar la configuración.
                </div>
            `;
        } else {
            const container = document.querySelector('#productsContainer').parentElement;
            const alert = document.createElement('div');
            alert.id = 'kitComponentsStatus';
            alert.innerHTML = `
                <div class="alert alert-warning mb-3">
                    <i class="bi bi-exclamation-triangle"></i>
                    <strong>Sin BOM definido:</strong> Este kit no tiene componentes asignados. 
                    Por favor, agrega los productos necesarios para completar la configuración.
                </div>
            `;
            container.insertBefore(alert, container.firstChild.nextSibling);
        }
    }
}

function clearKitData() {
    document.getElementById('kitCode').value = '';
    document.getElementById('kitName').value = '';
    document.getElementById('kitDescription').value = '';
    document.getElementById('kitCode').disabled = false;
    document.getElementById('kitName').disabled = false;

    selectedProducts = [];
    updateBomTable();
    updateStepIndicators();
    validateForm();

    // Remover alerta de estado del kit si existe
    const statusAlert = document.getElementById('kitComponentsStatus');
    if (statusAlert) {
        statusAlert.remove();
    }

    // Mostrar botón de agregar producto
    const addButton = document.querySelector('.btn-outline-primary[onclick="addProductRow()"]');
    if (addButton) {
        addButton.style.display = 'inline-block';
    }
}

function addProductRow() {
    currentEditingRow = null;
    showProductModal();
}

function renderProductList() {
    const list = document.getElementById('productList');

    if (availableProducts.length === 0) {
        list.innerHTML = '<div class="text-center py-4 text-muted">No se encontraron productos</div>';
        return;
    }

    list.innerHTML = '';

    availableProducts.forEach(product => {
        const isSelected = selectedProducts.some(p => p.id === product.id);

        const item = document.createElement('button');
        item.className = `list-group-item list-group-item-action ${isSelected ? 'disabled' : ''}`;
        item.onclick = () => !isSelected && selectProduct(product);
        item.innerHTML = `
            <div class="d-flex justify-content-between align-items-center">
                <div>
                    <span class="me-2" style="font-size: 1.5rem">${product.icon}</span>
                    <strong>${product.name}</strong>
                    <small class="text-muted ms-2">(${product.sku})</small>
                </div>
                <div class="text-end">
                    <div>
                        <small class="text-muted">Stock:</small> 
                        <strong>${product.stock} ${product.unit}</strong>
                    </div>
                    ${isSelected ? '<span class="badge bg-secondary">Ya agregado</span>' : ''}
                </div>
            </div>
        `;
        list.appendChild(item);
    });
}

function selectProduct(product) {
    bootstrap.Modal.getInstance(
        document.getElementById('productModal')
    ).hide();

    Swal.fire({
        title: 'Cantidad requerida',
        text: `¿Cuántas unidades de "${product.name}" se requieren por kit?`,
        input: 'number',
        inputLabel: 'Cantidad por kit',
        inputValue: 1,
        inputAttributes: {
            min: 0.01,
            step: 0.01
        },
        showCancelButton: true,
        confirmButtonText: 'Agregar',
        cancelButtonText: 'Cancelar',
        inputValidator: (value) => {
            if (!value || value <= 0) {
                return 'Ingresa una cantidad válida mayor a 0';
            }
        }
    }).then((result) => {
        if (!result.isConfirmed) return;

        selectedProducts.push({
            ...product,
            requiredPerKit: parseFloat(result.value)
        });

        updateBomTable();
        calculateRequirements();
        updateStepIndicators();
        validateForm();
    });
}


function updateBomTable() {
    const container = document.getElementById('bomTableContainer');
    const emptyState = document.getElementById('emptyState');
    const tbody = document.getElementById('bomTable');

    if (selectedProducts.length === 0) {
        container.style.display = 'none';
        emptyState.style.display = 'block';
        return;
    }

    container.style.display = 'block';
    emptyState.style.display = 'none';
    tbody.innerHTML = '';

    selectedProducts.forEach((product, index) => {
        const tr = document.createElement('tr');
        const quantity = parseFloat(document.getElementById('quantity').value) || 10;
        const totalRequired = product.requiredPerKit * quantity;

        tr.innerHTML = `
            <td>
                <span style="font-size: 1.5rem; margin-right: 0.5rem">${product.icon}</span>
                <strong>${product.name}</strong>
            </td>
            <td class="text-center"><code class="text-muted">${product.sku}</code></td>
            <td class="text-center">
                <div class="input-group input-group-sm" style="width: 150px; margin: 0 auto;">
                    <input type="number" class="form-control" 
                           value="${product.requiredPerKit}" min="0.01" step="0.01"
                           onchange="updateProductQuantity(${index}, this.value)"
                           id="qty-input-${index}">
                    <span class="input-group-text">${product.unit}</span>
                </div>
            </td>
            <td class="text-center">
                <span class="badge bg-light text-dark">${product.stock} ${product.unit}</span>
            </td>
            <td class="text-center">
                <span class="badge bg-info text-dark">${totalRequired.toFixed(2)} ${product.unit}</span>
            </td>
            <td class="text-center">
                <button class="btn btn-sm btn-outline-danger btn-remove" 
                        onclick="removeProduct(${index})" 
                        title="Eliminar"
                        id="remove-btn-${index}">
                    <i class="fa fa-trash"></i>
                </button>
            </td>
        `;
        tbody.appendChild(tr);
    });
}

function updateProductQuantity(index, newQuantity) {
    selectedProducts[index].requiredPerKit = parseFloat(newQuantity);
    calculateRequirements();
}

function removeProduct(index) {
    if (confirm('¿Eliminar este producto del BOM?')) {
        selectedProducts.splice(index, 1);
        updateBomTable();
        calculateRequirements();
        updateStepIndicators();
        validateForm();
    }
}

function calculateRequirements() {
    if (selectedProducts.length === 0) {
        document.getElementById('statsSection').style.display = 'none';
        document.getElementById('stockAlert').className = 'alert alert-secondary mb-0';
        document.getElementById('stockMessage').innerHTML = 'Sin productos';
        return;
    }

    document.getElementById('statsSection').style.display = 'flex';

    const quantity = parseFloat(document.getElementById('quantity').value) || 10;
    let okCount = 0;
    let warningCount = 0;
    let errorCount = 0;
    let canFabricate = true;

    selectedProducts.forEach(product => {
        const totalRequired = product.requiredPerKit * quantity;

        if (product.stock >= totalRequired) {
            okCount++;
        } else if (product.stock >= totalRequired * 0.7) {
            warningCount++;
        } else {
            errorCount++;
            canFabricate = false;
        }
    });

    document.getElementById('statsOk').textContent = okCount;
    document.getElementById('statsWarning').textContent = warningCount;
    document.getElementById('statsError').textContent = errorCount;

    const alertDiv = document.getElementById('stockAlert');
    const messageSpan = document.getElementById('stockMessage');

    if (errorCount > 0) {
        alertDiv.className = 'alert alert-danger mb-0';
        messageSpan.innerHTML = '<strong>Stock insuficiente</strong>';
    } else if (warningCount > 0) {
        alertDiv.className = 'alert alert-warning mb-0';
        messageSpan.innerHTML = '<strong>Stock bajo</strong>';
    } else {
        alertDiv.className = 'alert alert-success mb-0';
        messageSpan.innerHTML = '<strong>Stock suficiente</strong>';
    }

    const summaryParts = [];
    if (okCount > 0) summaryParts.push(`${okCount} producto${okCount !== 1 ? 's' : ''} OK`);
    if (warningCount > 0) summaryParts.push(`${warningCount} con stock bajo`);
    if (errorCount > 0) summaryParts.push(`${errorCount} insuficiente${errorCount !== 1 ? 's' : ''}`);

    document.getElementById('summaryText').innerHTML = summaryParts.join(' · ') || 'Completa la información';

    // Actualizar tabla para reflejar totales
    updateBomTable();
    validateForm();
}

function validateForm() {
    const kitCode = document.getElementById('kitCode').value.trim();
    const kitName = document.getElementById('kitName').value.trim();
    const hasProducts = selectedProducts.length > 0;

    const quantity = parseFloat(document.getElementById('quantity').value) || 0;
    let canFabricate = true;

    if (hasProducts) {
        selectedProducts.forEach(product => {
            const totalRequired = product.requiredPerKit * quantity;
            if (product.stock < totalRequired) {
                canFabricate = false;
            }
        });
    }

    const isValid = kitCode && kitName && hasProducts && canFabricate && quantity > 0;
    document.getElementById('createBtn').disabled = !isValid;
}

function updateStepIndicators() {
    const kitCode = document.getElementById('kitCode').value.trim();
    const kitName = document.getElementById('kitName').value.trim();
    const hasProducts = selectedProducts.length > 0;

    // Step 1
    const step1 = document.getElementById('step1');
    if (kitCode && kitName) {
        step1.classList.add('completed');
    } else {
        step1.classList.remove('completed');
    }

    // Step 2
    const step2 = document.getElementById('step2');
    if (hasProducts) {
        step2.classList.add('active', 'completed');
    } else {
        step2.classList.remove('completed');
    }

    // Step 3
    const step3 = document.getElementById('step3');
    if (kitCode && kitName && hasProducts) {
        step3.classList.add('active');
    } else {
        step3.classList.remove('active');
    }
}

function createAndFabricateKit() {
    const kitCode = document.getElementById('kitCode').value.trim();
    const kitName = document.getElementById('kitName').value.trim();
    const kitDescription = document.getElementById('kitDescription').value.trim();
    const quantity = document.getElementById('quantity').value;

    // Llenar modal de confirmación
    document.getElementById('confirmKitCode').textContent = kitCode;
    document.getElementById('confirmKitName').textContent = kitName;
    document.getElementById('confirmQuantity').textContent = quantity;


    const materialsList = document.getElementById('confirmMaterials');
    materialsList.innerHTML = '';

    selectedProducts.forEach(product => {
        const totalRequired = product.requiredPerKit * quantity;
        const div = document.createElement('div');
        div.className = 'list-group-item';
        div.innerHTML = `
            <div class="d-flex justify-content-between align-items-center">
                <span>
                    <span class="me-2" style="font-size: 1.5rem">${product.icon}</span>
                    <strong>${product.name}</strong> 
                    <small class="text-muted">(${product.sku})</small>
                </span>
                <div class="text-end">
                    <div><span class="badge bg-secondary">${product.requiredPerKit} ${product.unit} por kit</span></div>
                    <div class="mt-1"><span class="badge bg-primary">${totalRequired.toFixed(2)} ${product.unit} totales</span></div>
                </div>
            </div>
        `;
        materialsList.appendChild(div);
    });

    // Mostrar modal
    const modal = new bootstrap.Modal(document.getElementById('confirmModal'));
    modal.show();
}

function confirmCreation() {
    bootstrap.Modal.getInstance(document.getElementById('confirmModal')).hide();

    const btn = document.getElementById('createBtn');
    const spinner = btn.querySelector('.loading-spinner');
    spinner.style.display = 'inline-block';
    btn.disabled = true;

    const quantity = parseFloat(document.getElementById('quantity').value);
    const productionDate = document.getElementById('productionDate').value;
    const kit = availableKits.find(k => k.id == selectedKitId);

    const formData = new FormData();

    formData.append('__RequestVerificationToken',
        document.querySelector('input[name="__RequestVerificationToken"]').value
    );

    formData.append('KitId', kit ? kit.id : 0);
    formData.append('Cantidad', quantity);

    selectedProducts.forEach((p, i) => {
        formData.append(`Materiales[${i}].ProductoId`, p.id);
        formData.append(`Materiales[${i}].Cantidad`, p.requiredPerKit * quantity);
        formData.append(`Materiales[${i}].Unidad`, p.id_udm ?? 9);
        formData.append(`Materiales[${i}].EsKit`, false);
        formData.append(`Materiales[${i}].Orden`, i + 1);
    });

    fetch('/Kits/GenerarKit', {
        method: 'POST',
        body: formData
    })
        .then(r => r.json())
        .then(result => {
            spinner.style.display = 'none';
            btn.disabled = false;          
            //swal.fire(result)
            if (result.icon === 'success') {
                _SwalReload(result, "/Kits/KitsManagement")
            } else {
                _Swal.fire(result)
            }
        })
        .catch(err => {
            spinner.style.display = 'none';
            btn.disabled = false;
            showResultModal('danger', 'Error', 'No se pudo conectar con el servidor');
            console.error(err);
        });
}

function showResultModal(type, title, body) {
    const modal = new bootstrap.Modal(document.getElementById('resultModal'));
    const header = document.getElementById('resultHeader');

    if (type === 'success') {
        header.style.background = 'linear-gradient(135deg, #10b981, #059669)';
    } else {
        header.style.background = 'linear-gradient(135deg, #ef4444, #dc2626)';
    }

    document.getElementById('resultTitle').innerHTML = title;
    document.getElementById('resultBody').innerHTML = body;
    modal.show();
}

// Event listeners para validación en tiempo real
document.getElementById('kitCode').addEventListener('input', () => {
    updateStepIndicators();
    validateForm();
});

document.getElementById('kitName').addEventListener('input', () => {
    updateStepIndicators();
    validateForm();
});

document.getElementById('quantity').addEventListener('input', () => {
    updateBomTable();
    calculateRequirements();
});
let selectedPaymentMethods = [];
let currentPaymentField = null;
// Estado de la aplicación
let itemCounter = 0;
let activeTooltip = null;
let documentID = 0;
let cve_cli = ""
let loadedFolio = null; // Almacena el folio cargado
let isEditMode = false; // Indica si estamos editando una venta existente
let qrScanner = null; // Instancia del escáner QR
let videoStream = null; // Stream de la cámara
let selectedDocumentForInvoice = null;
let modalStack = []; // Pila para guardar el historial de modales

let facturaQRScanner = null;
let facturaVideoStream = null;


const paymentMethodsData = {
    cash: {
        id: 'payCash',
        name: 'Efectivo',
        icon: 'fa-money-bill',
        code: '01',
        description: 'Pago en efectivo',
        hasSubtype: false
    },
    card: {
        id: 'payCard',
        name: 'Tarjeta',
        icon: 'fa-credit-card',
        code: '04',
        description: 'Tarjeta débito o crédito',
        hasSubtype: true
    },
    transfer: {
        id: 'payTransfer',
        name: 'Transferencia',
        icon: 'fa-exchange-alt',
        code: '03',
        description: 'Transferencia bancaria',
        hasSubtype: false
    },
    check: {
        id: 'payCheck',
        name: 'Cheque',
        icon: 'fa-money-check',
        code: '02',
        description: 'Cheque nominativo',
        hasSubtype: false
    },
    creditNote: {
        id: 'payCreditNote',
        name: 'Nota de Crédito',
        icon: 'fa-ticket-alt',
        code: '99',
        description: 'Aplicar nota de crédito',
        hasSubtype: false
    }
};

function getInputValue(id) {
    const el = document.getElementById(id);
    if (!el) return 0;
    const value = parseFloat(el.value);
    return isNaN(value) ? 0 : value;
}

function guardarModalEnStack(modalData) {
    modalStack.push(modalData);
}

function volverModalAnterior() {
    if (modalStack.length > 0) {
        const modalAnterior = modalStack.pop();
        mostrarResumenExitoso(modalAnterior.result, modalAnterior.totalDocumentos, true);
    }
}

function limpiarStackModales() {
    modalStack = [];
}
// Inicializar
window.onload = function () {
    updateDate();
    initializeTooltips();
    mostrarTablaVacia();
};

function updateDate() {
    const today = new Date().toISOString().split('T')[0];
}
function mostrarTablaVacia() {
    const table = document.getElementById('itemsTable');
    table.innerHTML = `
        <tr>
            <td colspan="8" style="text-align: center; padding: 3rem; color: #6b7280;">
                <i class="fas fa-inbox" style="font-size: 3rem; margin-bottom: 1rem; display: block; color: #d1d5db;"></i>
                <p style="font-size: 1.1rem; margin-bottom: 0.5rem; color: #374151;">No hay artículos agregados</p>
                <p style="font-size: 0.9rem;">
                      <strong style="color: #059669;"> Cargue un documento para comenzar </strong>
                </p>
            </td>
        </tr>
    `;
}

function openPaymentMethodSelector() {
    // Crear modal si no existe
    let modal = document.getElementById('paymentSelectorModal');

    if (!modal) {
        const modalHTML = `
            <div class="payment-selector-modal" id="paymentSelectorModal">
                <div class="payment-selector-content">
                    <div class="payment-selector-header">
                        <div class="payment-selector-title">
                            <i class="fas fa-wallet"></i>
                            Seleccionar Formas de Pago
                        </div>
                        <button class="qr-close-btn" onclick="closePaymentMethodSelector()">
                            <i class="fas fa-times"></i>
                        </button>
                    </div>

                    <div id="paymentMethodsList">
                        ${Object.entries(paymentMethodsData).map(([key, method]) => `
                            <label class="payment-option">
                                <input type="checkbox"
                                       id="check_${key}"
                                       value="${key}"
                                       onchange="updatePaymentMethodSelection()">
                                <div class="payment-option-info">
                                    <div class="payment-option-name">
                                        <i class="fas ${method.icon}"></i>
                                        ${method.name}
                                    </div>
                                    <div class="payment-option-desc">${method.description}</div>
                                </div>
                            </label>
                        `).join('')}
                    </div>

                    <div style="margin-top: 1.5rem; display: flex; gap: 0.75rem;">
                        <button class="btn btn-primary btn-lg" style="flex: 1;" onclick="applyPaymentMethods()">
                            <i class="fas fa-check"></i> Aplicar
                        </button>
                        <button class="btn btn-outline btn-lg" onclick="closePaymentMethodSelector()">
                            <i class="fas fa-times"></i> Cancelar
                        </button>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', modalHTML);
        modal = document.getElementById('paymentSelectorModal');
    }

    // Marcar los métodos ya seleccionados
    selectedPaymentMethods.forEach(method => {
        const checkbox = document.getElementById(`check_${method}`);
        if (checkbox) checkbox.checked = true;
    });

    modal.classList.add('active');
}

function closePaymentMethodSelector() {
    const modal = document.getElementById('paymentSelectorModal');
    if (modal) {
        modal.classList.remove('active');
    }
}

function updatePaymentMethodSelection() {
    // Solo para feedback visual, la selección real se hace en applyPaymentMethods
}

function applyPaymentMethods() {
    const checkboxes = document.querySelectorAll('#paymentMethodsList input[type="checkbox"]:checked');
    const newSelection = Array.from(checkboxes).map(cb => cb.value);

    if (newSelection.length === 0) {
        toastMixin.fire({
            icon: 'warning',
            title: 'Seleccione al menos una forma de pago'
        });
        return;
    }

    selectedPaymentMethods = newSelection;
    renderPaymentInputs();
    closePaymentMethodSelector();

    toastMixin.fire({
        icon: 'success',
        title: `${newSelection.length} forma(s) de pago seleccionada(s)`
    });
}

function renderPaymentInputs() {
    const container = document.getElementById('paymentMethodsContainer');

    if (selectedPaymentMethods.length === 0) {
        container.innerHTML = `
            <div class="empty-state" style="padding: 2rem 1rem;">
                <i class="fas fa-hand-pointer"></i>
                <p style="margin-top: 1rem; color: #6b7280;">
                    Haga clic en "Agregar" para seleccionar las formas de pago
                </p>
            </div>
        `;
        return;
    }

    container.innerHTML = selectedPaymentMethods.map(methodKey => {
        const method = paymentMethodsData[methodKey];
        return createPaymentInputHTML(methodKey, method);
    }).join('');

    // Recalcular totales
    calculateChange();
}

function createPaymentInputHTML(methodKey, method) {
    const currentValue = getCurrentPaymentValue(method.id);

    let html = `
        <div class="payment-input-group" id="group_${methodKey}">
            <div class="payment-input-header">
                <div class="payment-input-label">
                    <i class="fas ${method.icon}"></i>
                    ${method.name}
                </div>
                <button class="remove-payment-btn"
                        onclick="removePaymentMethod('${methodKey}')"
                        type="button"
                        title="Eliminar forma de pago">
                    <i class="fas fa-times"></i>
                </button>
            </div>

            <div class="payment-input-field">
                <input type="number"
                       id="${method.id}"
                       class="form-control"
                       value="${currentValue}"
                       step="0.01"
                       min="0"
                       placeholder="0.00"
                       oninput="handlePaymentInputChange('${methodKey}')"
                       onchange="handlePaymentInputChange('${methodKey}')">
                <button class="payment-max-btn"
                        onclick="fillMaxAmount('${method.id}')"
                        type="button">
                    MAX
                </button>
            </div>
    `;

    // Agregar toggle de tipo de tarjeta si es necesario
    if (method.hasSubtype) {
        html += `
            <div class="card-type-toggle" style="display: flex; margin-top: 0.75rem;">
                <label class="toggle-option">
                    <input type="radio" name="cardType" value="debit" checked onchange="updateCardTypeLabel()">
                    <span class="toggle-label">
                        <i class="fas fa-credit-card"></i> Débito
                    </span>
                </label>
                <label class="toggle-option">
                    <input type="radio" name="cardType" value="credit" onchange="updateCardTypeLabel()">
                    <span class="toggle-label">
                        <i class="fas fa-credit-card"></i> Crédito
                    </span>
                </label>
            </div>
        `;
    }

    html += `
            <div class="payment-remaining-info">
                <span>Restante por cubrir:</span>
                <span class="payment-remaining-amount" id="remaining_${methodKey}">$0.00</span>
            </div>
        </div>
    `;

    return html;
}

function getCurrentPaymentValue(inputId) {
    const input = document.getElementById(inputId);
    return input ? parseFloat(input.value) || 0 : 0;
}

function handlePaymentInputChange(methodKey) {
    const method = paymentMethodsData[methodKey];
    const input = document.getElementById(method.id);
    let value = parseFloat(input.value) || 0;

    // Validar que no sea negativo
    if (value < 0) {
        input.value = 0;
        value = 0;
    }

    // Obtener total a pagar
    const totalText = document.getElementById('grandTotal').textContent
        .replace(/[$,]/g, '')
        .trim();
    const total = parseFloat(totalText) || 0;

    // Calcular suma actual de todos los pagos
    const totalPaid = calculateTotalPaid();

    // Si excede el total, ajustar
    if (totalPaid > total) {
        const currentMethodValue = value;
        const otherMethodsTotal = totalPaid - currentMethodValue;
        const maxAllowed = total - otherMethodsTotal;

        if (maxAllowed < 0) {
            input.value = 0;
        } else {
            input.value = maxAllowed.toFixed(2);
        }

        toastMixin.fire({
            icon: 'warning',
            title: 'El monto excede el total a pagar',
            text: `Máximo permitido: $${maxAllowed.toFixed(2)}`
        });
    }

    updateRemainingAmount(methodKey);
    calculateChange();
}

function calculateTotalPaid() {
    let total = 0;
    selectedPaymentMethods.forEach(methodKey => {
        const method = paymentMethodsData[methodKey];
        const input = document.getElementById(method.id);
        if (input) {
            total += parseFloat(input.value) || 0;
        }
    });
    return total;
}

function updateRemainingAmount(methodKey) {
    const totalText = document.getElementById('grandTotal').textContent
        .replace(/[$,]/g, '')
        .trim();
    const total = parseFloat(totalText) || 0;
    const totalPaid = calculateTotalPaid();
    const remaining = total - totalPaid;

    const remainingElement = document.getElementById(`remaining_${methodKey}`);
    if (remainingElement) {
        remainingElement.textContent = `$${Math.max(0, remaining).toFixed(2)}`;

        // Cambiar color según estado
        remainingElement.classList.remove('complete', 'exceeded');
        if (remaining === 0) {
            remainingElement.classList.add('complete');
        } else if (remaining < 0) {
            remainingElement.classList.add('exceeded');
        }
    }
}

function fillMaxAmount(inputId) {
    const totalText = document.getElementById('grandTotal').textContent
        .replace(/[$,]/g, '')
        .trim();
    const total = parseFloat(totalText) || 0;

    // Calcular cuánto falta por pagar
    const currentValue = parseFloat(document.getElementById(inputId).value) || 0;
    const totalPaid = calculateTotalPaid();
    const remaining = total - (totalPaid - currentValue);

    if (remaining > 0) {
        document.getElementById(inputId).value = remaining.toFixed(2);

        // Actualizar displays
        const methodKey = Object.keys(paymentMethodsData).find(
            key => paymentMethodsData[key].id === inputId
        );
        if (methodKey) {
            handlePaymentInputChange(methodKey);
        }

        toastMixin.fire({
            icon: 'success',
            title: `Monto máximo aplicado: $${remaining.toFixed(2)}`
        });
    } else {
        toastMixin.fire({
            icon: 'info',
            title: 'El total ya está cubierto'
        });
    }
}

function removePaymentMethod(methodKey) {
    // Limpiar valor del input antes de remover
    const method = paymentMethodsData[methodKey];
    const input = document.getElementById(method.id);
    if (input) {
        input.value = 0;
    }

    // Remover de la selección
    selectedPaymentMethods = selectedPaymentMethods.filter(m => m !== methodKey);

    // Re-renderizar
    renderPaymentInputs();

    toastMixin.fire({
        icon: 'info',
        title: `${method.name} eliminado`
    });
}

// Modificar la función getPaymentMethods existente
function getPaymentMethods() {
    const paymentMethods = [];

    selectedPaymentMethods.forEach(methodKey => {
        const method = paymentMethodsData[methodKey];
        const input = document.getElementById(method.id);
        const amount = input ? parseFloat(input.value) || 0 : 0;

        if (amount > 0) {
            let code = method.code;
            let methodName = method.name;

            // Caso especial para tarjetas
            if (methodKey === 'card') {
                const cardType = getSelectedCardType();
                code = cardType === 'credit' ? '04' : '28';
                methodName = cardType === 'credit' ? 'Tarjeta Crédito' : 'Tarjeta Débito';
            }

            paymentMethods.push({
                method: code,
                methodName: methodName,
                amount: amount.toFixed(2)
            });
        }
    });

    return paymentMethods;
}

// Actualizar la función de atajos de teclado
document.addEventListener('keydown', function (e) {
    // ✅ F7 - Solo abrir selector de pago con tarjeta preseleccionada
    if (e.key === 'F7') {
        e.preventDefault();
        const totalText = document.getElementById('grandTotal').textContent.replace(/[$,]/g, '').trim();
        const total = parseFloat(totalText) || 0;

        if (total > 0) {
            // Abrir selector con tarjeta
            if (!selectedPaymentMethods.includes('card')) {
                selectedPaymentMethods = ['card'];
                renderPaymentInputs();

                // Enfocar el input de tarjeta
                setTimeout(() => {
                    const cardInput = document.getElementById('payCard');
                    if (cardInput) cardInput.focus();
                }, 100);
            }

            toastMixin.fire({
                icon: 'info',
                title: 'Forma de pago: Tarjeta'
            });
        } else {
            toastMixin.fire({
                icon: 'error',
                title: 'No hay monto para pagar'
            });
        }
    }

    // ✅ F8 - Solo abrir selector de pago con efectivo preseleccionado
    if (e.key === 'F8') {
        e.preventDefault();
        const totalText = document.getElementById('grandTotal').textContent.replace(/[$,]/g, '').trim();
        const total = parseFloat(totalText) || 0;

        if (total > 0) {
            // Abrir selector con efectivo
            if (!selectedPaymentMethods.includes('cash')) {
                selectedPaymentMethods = ['cash'];
                renderPaymentInputs();

                // Enfocar el input de efectivo
                setTimeout(() => {
                    const cashInput = document.getElementById('payCash');
                    if (cashInput) cashInput.focus();
                }, 100);
            }

            toastMixin.fire({
                icon: 'info',
                title: 'Forma de pago: Efectivo'
            });
        } else {
            toastMixin.fire({
                icon: 'error',
                title: 'No hay monto para pagar'
            });
        }
    }

    // F10 - Confirmar venta
    if (e.key === 'F10') {
        e.preventDefault();
        saveTransaction();
    }

    // ESC - Cancelar
    if (e.key === 'Escape') {
        e.preventDefault();
        if (confirm('¿Desea cancelar la venta actual?')) {
            cancelTransaction();
        }
    }
});



// ==================== SISTEMA DE TOOLTIPS ====================

function initializeTooltips() {
    const helpIcons = document.querySelectorAll('.help-icon');

    helpIcons.forEach(icon => {
        // Crear tooltip card
        const tooltipCard = createTooltipCard(icon);
        icon.parentElement.style.position = 'relative';
        icon.parentElement.appendChild(tooltipCard);

        // Evento hover en el icono
        icon.addEventListener('mouseenter', function () {
            showTooltip(icon, tooltipCard);
        });

        icon.addEventListener('mouseleave', function (e) {
            // Verificar si el mouse se movió al tooltip
            if (!tooltipCard.contains(e.relatedTarget)) {
                hideTooltip(tooltipCard);
            }
        });

        // Mantener tooltip visible cuando el mouse está sobre él
        tooltipCard.addEventListener('mouseenter', function () {
            tooltipCard.classList.add('active');
        });

        tooltipCard.addEventListener('mouseleave', function () {
            hideTooltip(tooltipCard);
        });
    });
}

function createTooltipCard(icon) {
    const card = document.createElement('div');
    card.className = 'tooltip-card';

    const title = icon.dataset.title || 'Información';
    const description = icon.dataset.description || '';
    const example = icon.dataset.example || '';
    const tips = icon.dataset.tips ? icon.dataset.tips.split('|') : [];
    const targetId = icon.dataset.tooltip;

    let html = `
        <div class="tooltip-title">
            <i class="fas fa-info-circle"></i>
            ${title}
        </div>
    `;

    if (description) {
        html += `<div class="tooltip-description">${description}</div>`;
    }

    if (example) {
        html += `
            <div class="tooltip-example">
                <strong><i class="fas fa-lightbulb"></i> ${example}</strong>
            </div>
        `;
    }

    if (tips.length > 0) {
        html += `
            <div class="tooltip-tips">
                <div class="tooltip-tips-title">
                    <i class="fas fa-star"></i> Consejos:
                </div>
        `;

        tips.forEach(tip => {
            html += `
                <div class="tooltip-tip">
                    <i class="fas fa-check-circle"></i>
                    <span>${tip}</span>
                </div>
            `;
        });

        html += `</div>`;
    }

    card.innerHTML = html;

    return card;
}

function showTooltip(icon, tooltipCard) {
    // Ocultar cualquier tooltip activo
    if (activeTooltip && activeTooltip !== tooltipCard) {
        hideTooltip(activeTooltip);
    }

    // Mostrar tooltip
    tooltipCard.classList.add('active');
    activeTooltip = tooltipCard;

    // Resaltar el input asociado
    const targetId = icon.dataset.tooltip;
    if (targetId) {
        const targetInput = document.getElementById(targetId);
        if (targetInput) {
            highlightInput(targetInput);
        }
    }
}

function hideTooltip(tooltipCard) {
    tooltipCard.classList.remove('active');

    // Remover resaltado de todos los inputs
    document.querySelectorAll('.form-control.highlighted, .form-select.highlighted').forEach(input => {
        removeHighlight(input);
    });

    if (activeTooltip === tooltipCard) {
        activeTooltip = null;
    }
}

function highlightInput(input) {
    input.classList.add('highlighted');
}

function removeHighlight(input) {
    input.classList.remove('highlighted');
}
// ==================== MANEJO DE FILAS ====================

function addRow() {
    const table = document.getElementById('itemsTable');

    // ✅ Si la tabla tiene el mensaje vacío, limpiarlo primero
    const emptyMessage = table.querySelector('td[colspan="8"]');
    if (emptyMessage) {
        table.innerHTML = '';
    }

    // ✅ VERIFICAR SI YA EXISTE UNA FILA VACÍA AL FINAL
    const rows = table.querySelectorAll('tr');
    if (rows.length > 0) {
        const lastRow = rows[rows.length - 1];
        const lastCode = lastRow.querySelector('.item-code').value.trim();
        const lastDesc = lastRow.querySelector('.item-desc').value.trim();

        // Si la última fila está vacía, no agregar otra
        if (lastCode === '' && lastDesc === '') {
            return;
        }
    }

    const row = document.createElement('tr');
    row.className = 'empty-row';
    row.innerHTML = `
        <td><input type="text" readonly class="item-code" placeholder="Código" onchange="markRowFilled(this)"></td>
        <td><input type="text" readonly class="item-desc" placeholder="Descripción del artículo"></td>
        <td><input type="number" readonly class="item-qty" value="0" min="0" onchange="calculateRowTotal(this)"></td>
        <td><input type="number" readonly class="item-price" value="0" min="0" step="0.01" onchange="calculateRowTotal(this)"></td>
        <td><input type="number" readonly class="item-discount" value="0" min="0" max="100" onchange="calculateRowTotal(this)"></td>
        <td><input type="number" class="item-stock" value="0" readonly></td>
        <td><input type="number" class="item-total" value="0.00" readonly style="font-weight: 600;"></td>
        <td><button class="delete-btn" readonly onclick="deleteRow(this)" title="Eliminar artículo"><i class="fas fa-trash"></i></button></td>
    `;
    table.appendChild(row);

    // Auto-focus en el código del artículo solo si es la primera fila
    if (table.querySelectorAll('tr').length === 1) {
        const codeInput = row.querySelector('.item-code');
        codeInput.focus();
    }

    // Agregar listener para Enter
    const inputs = row.querySelectorAll('input:not([readonly])');
    inputs.forEach((input, index) => {
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                if (index === inputs.length - 1) {
                    if (isRowFilled(row)) {
                        addRow();
                    }
                } else {
                    inputs[index + 1].focus();
                }
            }
        });
    });
}


function markRowFilled(input) {
    const row = input.closest('tr');
    if (input.value.trim() !== '') {
        row.classList.remove('empty-row');
    }
}

function isRowFilled(row) {
    const code = row.querySelector('.item-code').value.trim();
    const desc = row.querySelector('.item-desc').value.trim();
    const qty = parseFloat(row.querySelector('.item-qty').value) || 0;
    const price = parseFloat(row.querySelector('.item-price').value) || 0;

    return code !== '' && desc !== '' && qty > 0 && price > 0;
}

function deleteRow(btn) {
    const table = document.getElementById('itemsTable');
    const row = btn.closest('tr');
    const allRows = table.querySelectorAll('tr');

    // Contar filas con datos (excluyendo mensaje vacío)
    let filledRows = 0;
    allRows.forEach(r => {
        if (!r.querySelector('td[colspan="8"]')) { // No es mensaje vacío
            const code = r.querySelector('.item-code').value.trim();
            if (code !== '') {
                filledRows++;
            }
        }
    });

    // Si solo hay una fila con datos, no permitir eliminarla
    if (filledRows === 1 && row.querySelector('.item-code').value.trim() !== '') {
        toastMixin.fire({
            icon: 'error',
            title: 'Debe mantener al menos un artículo'
        });
        return;
    }

    // Eliminar fila
    row.remove();

    // ✅ Si no quedan filas, mostrar mensaje vacío
    if (table.querySelectorAll('tr').length === 0) {
        mostrarTablaVacia();
    }

    calculateTotals();

    toastMixin.fire({
        icon: 'warning',
        title: 'Artículo eliminado'
    });
}


function calculateRowTotal(input) {
    const row = input.closest('tr');
    const qty = parseFloat(row.querySelector('.item-qty').value) || 0;
    const price = parseFloat(row.querySelector('.item-price').value) || 0;
    const discount = parseFloat(row.querySelector('.item-discount').value) || 0;

    // Validar descuento
    if (discount > 100) {
        row.querySelector('.item-discount').value = 100;
        toastMixin.fire({
            icon: 'error',
            title: 'El descuento no puede ser mayor a 100%'
        })
        return;
    }

    let subtotal = qty * price;
    let discountAmount = (subtotal * discount) / 100;
    let total = subtotal;

    row.querySelector('.item-total').value = total.toFixed(2);
    calculateTotals();
}

// ==================== CÁLCULOS PRINCIPALES ====================

function calculateTotals() {
    let subtotal = 0;
    let totalDiscount = 0;
    let itemCount = 0;

    document.querySelectorAll('#itemsTable tr').forEach(row => {

        const qtyInput = row.querySelector('.item-qty');
        const priceInput = row.querySelector('.item-price');
        const discountInput = row.querySelector('.item-discount');

        // ❌ Si falta qty o price, no se procesa la fila
        if (!qtyInput || !priceInput) return;

        const qty = parseFloat(qtyInput.value) || 0;
        const price = parseFloat(priceInput.value) || 0;
        const discount = parseFloat(discountInput?.value) || 0;

        if (qty > 0 && price > 0) {
            itemCount++;

            const rowSubtotal = qty * price;
            const rowDiscount = (rowSubtotal * discount) / 100;

            subtotal += rowSubtotal;
            totalDiscount += rowDiscount;
        }
    });


    const subtotalAfterDiscount = subtotal - totalDiscount;
    const iva = subtotalAfterDiscount * 0.16;

    const grandTotal = subtotalAfterDiscount + iva;

    // Actualizar totales
    document.getElementById('subtotal').textContent = `$${subtotal.toFixed(2)}`;
    document.getElementById('discount').textContent = `-$${totalDiscount.toFixed(2)}`;
    document.getElementById('iva').textContent = `$${iva.toFixed(2)}`;
    document.getElementById('grandTotal').textContent = `$${grandTotal.toFixed(2)}`;
    document.getElementById('totalDisplay').textContent = `$${grandTotal.toFixed(2)}`;
    document.getElementById('savingsDisplay').textContent = `$${totalDiscount.toFixed(2)}`;

    calculateChange();
}


function calculateChange() {
    const totalText = document.getElementById('grandTotal').textContent
        .replace(/[$,]/g, '')
        .trim();
    const total = parseFloat(totalText) || 0;

    const totalPaid = calculateTotalPaid();
    const change = totalPaid - total;

    const changeRow = document.getElementById('changeRow');
    const changeElement = document.getElementById('change');

    if (change > 0.01) {
        changeRow.style.display = 'flex';
        changeRow.style.background = 'linear-gradient(135deg, #d1fae5 0%, #a7f3d0 100%)';
        changeElement.style.color = '#065f46';
        changeElement.textContent = `$${change.toFixed(2)}`;
    } else if (change < -0.01) {
        changeRow.style.display = 'flex';
        changeRow.style.background = 'linear-gradient(135deg, #fee2e2 0%, #fecaca 100%)';
        changeElement.style.color = '#991b1b';
        changeElement.textContent = `Falta: $${Math.abs(change).toFixed(2)}`;
    } else {
        changeRow.style.display = 'none';
    }

    // Actualizar displays de restante en cada método
    selectedPaymentMethods.forEach(methodKey => {
        updateRemainingAmount(methodKey);
    });
}

// ==================== VALIDACIONES ====================

function validateTransaction() {
    const clientName = document.getElementById('clientName').value.trim();
    if (!clientName) {
        toastMixin.fire({
            icon: 'error',
            title: 'Ingrese el nombre del cliente'
        })
        document.getElementById('clientName').focus();
        return false;
    }

    const totalText = document.getElementById('grandTotal').textContent.replace(',', '') || 0;
    const total = parseFloat(totalText) || 0;

    if (total <= 0) {
        toastMixin.fire({
            icon: 'error',
            title: 'Agregue al menos un artículo a la venta'
        })
        return false;
    }

    // Validar que haya al menos un artículo válido
    let hasValidItems = false;
    let invalidStructure = false;

    document.querySelectorAll('#itemsTable tr').forEach(row => {

        const codeInput = row.querySelector('.item-code');
        const qtyInput = row.querySelector('.item-qty');
        const priceInput = row.querySelector('.item-price');

        // ❌ Si falta algún input, marcamos error
        if (!codeInput || !qtyInput || !priceInput) {
            invalidStructure = true;
            return;
        }

        const code = codeInput.value.trim();
        const qty = parseFloat(qtyInput.value) || 0;
        const price = parseFloat(priceInput.value) || 0;

        if (code && qty > 0 && price > 0) {
            hasValidItems = true;
        }
    });

    if (invalidStructure || !hasValidItems) {
        toastMixin.fire({
            icon: 'error',
            title: 'Agregue artículos válidos con código, cantidad y precio'
        });
        return false;
    }


    // Validar pago
    const cash = parseFloat(document.getElementById('payCash').value) || 0;
    const card = parseFloat(document.getElementById('payCard').value) || 0;
    const transfer = parseFloat(document.getElementById('payTransfer').value) || 0;
    const check = parseFloat(document.getElementById('payCheck').value) || 0;
    const creditNote = parseFloat(document.getElementById('payCreditNote').value) || 0;
    const totalPaid = cash + card + transfer + check + creditNote;

    if (totalPaid < total) {
        const difference = (total - totalPaid).toFixed(2);
        toastMixin.fire({
            icon: 'error',
            title: `Falta pagar ${difference}`
        })
        return false;
    }

    return true;
}

// ==================== TRANSACCIONES ====================
function printReceipt() {
    toastMixin.fire({
        icon: 'success',
        title: 'Generando ticket de venta...'
    })
    // Aquí iría la lógica de impresión
}

function cancelTransaction() {
    const totalText = document.getElementById('grandTotal').textContent.replace(',', '') || 0;
    const total = parseFloat(totalText) || 0;

    if (total > 0) {
        if (!confirm('¿Está seguro de cancelar esta venta? Se perderán todos los datos.')) {
            return;
        }
    }

    resetForm();
    toastMixin.fire({
        icon: 'warning',
        title: 'Venta cancelada'
    })
}

function resetForm() {
    // Limpiar cliente
    document.getElementById('clientName').value = '';
    document.getElementById('clientRFC').value = '';

    // ✅ Limpiar tabla y mostrar mensaje vacío
    const table = document.getElementById('itemsTable');
    table.innerHTML = '';
    mostrarTablaVacia();

    // ✅ Limpiar métodos de pago seleccionados
    selectedPaymentMethods = [];
    renderPaymentInputs();

    // Recalcular
    calculateTotals();

    // Ocultar cambio
    document.getElementById('changeRow').style.display = 'none';

    // Resetear variables de edición
    isEditMode = false;
    loadedFolio = null;
    documentID = 0;
    cve_cli = "";
}

// ==================== ATAJOS DE TECLADO ====================

document.addEventListener('keydown', function (e) {
    // F7 - Total en tarjeta
    if (e.key === 'F7') {
        e.preventDefault();
        const totalText = document.getElementById('grandTotal').textContent.replace(',', '') || 0;
        const total = parseFloat(totalText) || 0;

        if (total > 0) {
            document.getElementById('payCard').value = total.toFixed(2);
            document.getElementById('payCash').value = '0';
            document.getElementById('payTransfer').value = '0';
            document.getElementById('payCheck').value = '0';
            document.getElementById('payCreditNote').value = '0';
            toastMixin.fire({
                icon: 'success',
                title: 'Total aplicado a tarjeta'
            })
        } else {
            toastMixin.fire({
                icon: 'error',
                title: 'No hay monto para pagar'
            })
        }
    }

    // F8 - Total en efectivo
    if (e.key === 'F8') {
        e.preventDefault();
        const totalText = document.getElementById('grandTotal').textContent.replace(',', '') || 0;
        const total = parseFloat(totalText) || 0;

        if (total > 0) {
            document.getElementById('payCash').value = total.toFixed(2);
            document.getElementById('payCard').value = '0';
            document.getElementById('payTransfer').value = '0';
            document.getElementById('payCheck').value = '0';
            document.getElementById('payCreditNote').value = '0';
            calculateChange();
            toastMixin.fire({
                icon: 'success',
                title: 'Total aplicado a efectivo'
            })
        } else {
            toastMixin.fire({
                icon: 'error',
                title: 'No hay monto para pagar'
            })
        }
    }

    // F10 - Confirmar venta
    if (e.key === 'F10') {
        e.preventDefault();
        saveTransaction();
    }

    // ESC - Cancelar
    if (e.key === 'Escape') {
        e.preventDefault();
        if (confirm('¿Desea cancelar la venta actual?')) {
            cancelTransaction();
        }
    }
});

// Cerrar tooltips al hacer click fuera
document.addEventListener('click', function (e) {
    if (activeTooltip && !e.target.closest('.help-icon') && !e.target.closest('.tooltip-card')) {
        hideTooltip(activeTooltip);
    }
});

// Formatear inputs de dinero al perder el foco
document.addEventListener('focusout', function (e) {
    if (e.target.type === 'number' && e.target.step === '0.01') {
        const value = parseFloat(e.target.value) || 0;
        e.target.value = value.toFixed(2);
    }
});

// Auto-guardar cada 30 segundos (opcional)
let autoSaveInterval = setInterval(() => {
    const totalText = document.getElementById('grandTotal').textContent.replace(',', '') || 0;
    const total = parseFloat(totalText) || 0;

    if (total > 0) {
        // Aquí podrías guardar un borrador
        console.log('Auto-guardado de borrador...');
    }
}, 30000);


async function saveTransaction() {
    // SI HAY UN FOLIO CARGADO, PREGUNTAR SI DESEA ACTUALIZAR O CREAR NUEVA VENTA
    if (isEditMode && loadedFolio) {
        const result = await Swal.fire({
            title: '¿Crear nueva venta?',
            text: `Está usando la cotización ${loadedFolio}.`,
            icon: 'question',
            showCancelButton: true,
            showDenyButton: true,
            denyButtonText: 'Crear nueva venta',
            cancelButtonText: 'Cancelar',
            showConfirmButton: false
        });

        if (result.isConfirmed) {
            await updateExistingSale(loadedFolio);
            return;
        } else if (result.isDenied) {
            isEditMode = false;
            loadedFolio = null;
        } else {
            return;
        }
    }

    console.log("Guardando nueva venta...");

    const validationResult = validateTransactionData();
    if (!validationResult.valid) {
        toastMixin.fire({
            icon: 'error',
            title: validationResult.message
        });
        return;
    }

    const formData = new FormData();

    // Datos del cliente
    formData.append('clientName', document.getElementById('clientName').value.trim());
    formData.append('documentID', documentID);
    formData.append('cve_cli', cve_cli);
    formData.append('clientRFC', document.getElementById('clientRFC').value.trim());

    // Datos del vendedor
    formData.append('seller', document.getElementById('seller').value.trim());

    // Totales
    const subtotal = parseFloat(document.getElementById('subtotal').textContent.replace(',', '')) || 0;
    const discount = Math.abs(parseFloat(document.getElementById('discount').textContent.replace(',', '').replace('-', ''))) || 0;
    const iva = parseFloat(document.getElementById('iva').textContent.replace(',', '')) || 0;
    const grandTotal = parseFloat(
        document.getElementById('grandTotal')
            .textContent
            .replace('$', '')   // quitar el símbolo de pesos
            .replace(',', '')   // quitar comas
    ) || 0;

    formData.append('subtotal', subtotal.toFixed(2));
    formData.append('discount', discount.toFixed(2));
    formData.append('iva', iva.toFixed(2));
    formData.append('grandTotal', grandTotal.toFixed(2));

    // **CAMBIO PRINCIPAL: Obtener lista de formas de pago**
    const paymentMethods = getPaymentMethods();

    // Validar que haya al menos una forma de pago
    if (paymentMethods.length === 0) {
        toastMixin.fire({
            icon: 'error',
            title: 'Debe especificar al menos una forma de pago'
        });
        return;
    }

    // Enviar las formas de pago como JSON
    formData.append('paymentMethodsJson', JSON.stringify(paymentMethods));

    // Calcular totales de pago

    const payCash = getInputValue('payCash');
    const payCard = getInputValue('payCard');
    const payTransfer = getInputValue('payTransfer');
    const payCheck = getInputValue('payCheck');
    const payCreditNote = getInputValue('payCreditNote');

    const totalPaid = payCash + payCard + payTransfer + payCheck + payCreditNote;
    const change = totalPaid - grandTotal;

    formData.append('totalPaid', totalPaid.toFixed(2));
    formData.append('change', change > 0 ? change.toFixed(2) : '0.00');

    // Recolectar artículos
    const items = [];
    document.querySelectorAll('#itemsTable tr').forEach((row) => {
        const code = row.querySelector('.item-code')?.value.trim();
        const qty = parseFloat(row.querySelector('.item-qty')?.value) || 0;
        const price = parseFloat(row.querySelector('.item-price')?.value) || 0;

        if (code && qty > 0 && price > 0) {
            items.push({
                code,
                description: row.querySelector('.item-desc')?.value.trim(),
                quantity: qty,
                quantityOriginal: parseFloat(row.querySelector('.item-qty-original')?.value) || qty,
                idPendiente: parseInt(row.querySelector('.item-id-pendiente')?.value) || 0,
                unitPrice: price,
                discountPercent: parseFloat(row.querySelector('.item-discount')?.value) || 0,
                stock: parseFloat(row.querySelector('.item-stock')?.value) || 0,
                unidad: 'PZA'
            });
        }
    });


    formData.append('itemsJson', JSON.stringify(items));

    // Fecha y hora
    const now = new Date();
    formData.append('transactionDate', now.toISOString().split('T')[0]);
    formData.append('transactionTime', now.toTimeString().split(' ')[0]);
    formData.append('timestamp', now.toISOString());

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    if (token) {
        formData.append('__RequestVerificationToken', token);
    }

    if (window.modoComplemento) {
        formData.append('encabezadoOrigenId', documentID);
    }

    // Mostrar loading
    const confirmButton = event.target;
    const originalText = confirmButton.innerHTML;
    confirmButton.disabled = true;
    confirmButton.innerHTML = '<span class="loading"></span> Guardando...';

    const endpoint = window.modoComplemento
        ? '/PuntoDeVenta/EntregarPendientes'
        : '/PuntoDeVenta/Guardar';

    try {
        const response = await fetch(endpoint, { method: 'POST', body: formData });

        if (!response.ok) {
            throw new Error(`Error HTTP: ${response.status}`);
        }

        const result = await response.json();

        if (result.success) {
            if (result.esParcial) {
                // Venta inicial con pendientes
                await Swal.fire({
                    icon: 'warning',
                    title: 'Venta parcial registrada',
                    html: `
                <b>Folio: ${result.folio_generado}</b><br>
                <span style="color: #d97706;">
                    ⚠ Quedan <b>${result.pendientes}</b> artículo(s) pendiente(s).
                </span><br>
                <small>Cuando llegue el material, cargue este folio
                y el sistema le mostrará los artículos por completar.</small>
            `,
                    confirmButtonText: 'Entendido'
                });
            } else if (result.todosCompletos !== undefined) {
                // Entrega complementaria
                await Swal.fire({
                    icon: result.todosCompletos ? 'success' : 'info',
                    title: result.todosCompletos
                        ? '✅ Entrega completa'
                        : '📦 Entrega parcial registrada',
                    html: result.todosCompletos
                        ? 'Todos los artículos fueron entregados. <b>Cotización cerrada.</b>'
                        : `Entrega registrada. Aún quedan <b>${result.restantes}</b> artículo(s) pendiente(s).`
                });
            } else {
                await Swal.fire({
                    icon: 'success',
                    title: 'Venta guardada',
                    html: `Folio: <b>${result.folio_generado}</b>`
                });
            }
            window.location.reload();
        }
        else {
            throw new Error(result.message || 'Error al guardar la venta');
        }

    } catch (error) {
        toastMixin.fire({
            icon: 'error',
            title: `Error al guardar: ${error.message}`
        })
    } finally {
        confirmButton.disabled = false;
        confirmButton.innerHTML = originalText;
    }
}

// ==================== MODIFICAR validateTransactionData() ====================

function validateTransactionData() {
    // ... (mantener validaciones anteriores de cliente, vendedor, artículos)

    const clientName = document.getElementById('clientName').value.trim();
    if (!clientName) {
        return { valid: false, message: 'El nombre del cliente es requerido' };
    }

    if (clientName.length < 3) {
        return { valid: false, message: 'El nombre del cliente debe tener al menos 3 caracteres' };
    }

    const rfc = document.getElementById('clientRFC').value.trim();
    if (rfc && rfc.length < 12) {
        return { valid: false, message: 'El RFC debe tener al menos 12 caracteres' };
    }

    const seller = document.getElementById('seller').value.trim();
    if (!seller) {
        return { valid: false, message: 'El vendedor es requerido' };
    }

    const items = [];
    let hasValidItems = false;
    let invalidStructure = false;

    document.querySelectorAll('#itemsTable tr').forEach(row => {

        const codeInput = row.querySelector('.item-code');
        const descInput = row.querySelector('.item-desc');
        const qtyInput = row.querySelector('.item-qty');
        const priceInput = row.querySelector('.item-price');
        const stockInput = row.querySelector('.item-stock');

        // ❌ Si falta algún input requerido
        if (!codeInput || !descInput || !qtyInput || !priceInput) {
            invalidStructure = true;
            return;
        }

        const code = codeInput.value.trim();
        const description = descInput.value.trim();
        const qty = parseFloat(qtyInput.value) || 0;
        const price = parseFloat(priceInput.value) || 0;
        const stock = parseFloat(stockInput?.value) || 0;

        if (code && description && qty > 0 && price > 0) {
            hasValidItems = true;

            if (stock < qty) {
                items.push({
                    valid: false,
                    message: `Artículo "${description}": Stock insuficiente (disponible: ${stock}, solicitado: ${qty})`
                });
            }

            if (price < 0) {
                items.push({
                    valid: false,
                    message: `Artículo "${description}": El precio no puede ser negativo`
                });
            }

            if (qty <= 0) {
                items.push({
                    valid: false,
                    message: `Artículo "${description}": La cantidad debe ser mayor a 0`
                });
            }
        }
    });

    // ❌ Error estructural o sin artículos válidos
    if (invalidStructure || !hasValidItems) {
        return {
            valid: false,
            message: 'Debe agregar al menos un artículo válido'
        };
    }


    const invalidItems = items.filter(item => !item.valid);
    if (invalidItems.length > 0) {
        return { valid: false, message: invalidItems[0].message };
    }

    const grandTotal = parseFloat(
        document.getElementById('grandTotal')
            .textContent
            .replace('$', '')   // quitar el símbolo de pesos
            .replace(',', '')   // quitar comas
    ) || 0;


    if (grandTotal <= 0) {
        return { valid: false, message: 'El total debe ser mayor a 0' };
    }

    // **CAMBIO: Validar formas de pago múltiples**
    const payCash = getInputValue('payCash');
    const payCard = getInputValue('payCard');
    const payTransfer = getInputValue('payTransfer');
    const payCheck = getInputValue('payCheck');
    const payCreditNote = getInputValue('payCreditNote');


    const totalPaid = payCash + payCard + payTransfer + payCheck + payCreditNote;

    if (totalPaid < grandTotal) {
        const difference = (grandTotal - totalPaid).toFixed(2);
        return { valid: false, message: `Falta pagar $${difference}` };
    }

    if (totalPaid === 0) {
        return { valid: false, message: 'Debe especificar al menos una forma de pago' };
    }

    if (payCash < 0 || payCard < 0 || payTransfer < 0 || payCheck < 0 || payCreditNote < 0) {
        return { valid: false, message: 'Las formas de pago no pueden ser negativas' };
    }

    return { valid: true, message: 'Datos válidos' };
}

function validateTransactionData() {
    // Validar cliente
    const clientName = document.getElementById('clientName').value.trim();
    if (!clientName) {
        return { valid: false, message: 'El nombre del cliente es requerido' };
    }

    if (clientName.length < 3) {
        return { valid: false, message: 'El nombre del cliente debe tener al menos 3 caracteres' };
    }

    // Validar RFC (si se proporcionó)
    const rfc = document.getElementById('clientRFC').value.trim();
    if (rfc && rfc.length < 12) {
        return { valid: false, message: 'El RFC debe tener al menos 12 caracteres' };
    }

    // Validar vendedor
    const seller = document.getElementById('seller').value.trim();
    if (!seller) {
        return { valid: false, message: 'El vendedor es requerido' };
    }

    // Validar artículos
    const items = [];
    let hasValidItems = false;
    let invalidStructure = false;

    document.querySelectorAll('#itemsTable tr').forEach(row => {

        const codeInput = row.querySelector('.item-code');
        const descInput = row.querySelector('.item-desc');
        const qtyInput = row.querySelector('.item-qty');
        const priceInput = row.querySelector('.item-price');
        const stockInput = row.querySelector('.item-stock');

        // ❌ Si falta algún input requerido
        if (!codeInput || !descInput || !qtyInput || !priceInput) {
            invalidStructure = true;
            return;
        }

        const code = codeInput.value.trim();
        const description = descInput.value.trim();
        const qty = parseFloat(qtyInput.value) || 0;
        const price = parseFloat(priceInput.value) || 0;
        const stock = parseFloat(stockInput?.value) || 0;

        if (code && description && qty > 0 && price > 0) {
            hasValidItems = true;

            if (stock < qty) {
                items.push({
                    valid: false,
                    message: `Artículo "${description}": Stock insuficiente (disponible: ${stock}, solicitado: ${qty})`
                });
            }

            if (price < 0) {
                items.push({
                    valid: false,
                    message: `Artículo "${description}": El precio no puede ser negativo`
                });
            }

            if (qty <= 0) {
                items.push({
                    valid: false,
                    message: `Artículo "${description}": La cantidad debe ser mayor a 0`
                });
            }
        }
    });

    // ❌ Error estructural o sin artículos válidos
    if (invalidStructure || !hasValidItems) {
        return {
            valid: false,
            message: 'Debe agregar al menos un artículo válido'
        };
    }


    // Reportar errores de artículos
    const invalidItems = items.filter(item => !item.valid);
    if (invalidItems.length > 0) {
        return { valid: false, message: invalidItems[0].message };
    }

    // Validar totales
    const grandTotal = parseFloat(
        document.getElementById('grandTotal')
            .textContent
            .replace('$', '')   // quitar el símbolo de pesos
            .replace(',', '')   // quitar comas
    ) || 0;


    if (grandTotal <= 0) {
        return { valid: false, message: 'El total debe ser mayor a 0' };
    }

    // Validar formas de pago
    const payCash = getInputValue('payCash');
    const payCard = getInputValue('payCard');
    const payTransfer = getInputValue('payTransfer');
    const payCheck = getInputValue('payCheck');
    const payCreditNote = getInputValue('payCreditNote');


    const totalPaid = payCash + payCard + payTransfer + payCheck + payCreditNote;

    if (totalPaid < grandTotal) {
        const difference = (grandTotal - totalPaid).toFixed(2);
        return { valid: false, message: `Falta pagar $${difference}` };
    }

    // Validar que al menos haya una forma de pago
    if (totalPaid === 0) {
        return { valid: false, message: 'Debe especificar al menos una forma de pago' };
    }

    // Validar valores negativos en pagos
    if (payCash < 0 || payCard < 0 || payTransfer < 0 || payCheck < 0 || payCreditNote < 0) {
        return { valid: false, message: 'Las formas de pago no pueden ser negativas' };
    }

    return { valid: true, message: 'Datos válidos' };
}

function handlePaymentInput(input) {
    const currentValue = parseFloat(input.value) || 0;

    // Mostrar/ocultar toggle de tarjeta
    if (input.id === 'payCard') {
        if (currentValue > 0) {
            document.getElementById('cardTypeToggle').style.display = 'flex';
        } else {
            document.getElementById('cardTypeToggle').style.display = 'none';
            calculateTotals();
        }
    }
    calculateChange();
}

function getPaymentMethods() {
    const paymentMethods = [];
    const payCash = getInputValue('payCash');
    const payCard = getInputValue('payCard');
    const payTransfer = getInputValue('payTransfer');
    const payCheck = getInputValue('payCheck');
    const payCreditNote = getInputValue('payCreditNote');


    // Agregar cada método de pago que tenga valor > 0
    if (payCash > 0) {
        paymentMethods.push({
            method: '01', // Código SAT para efectivo
            methodName: 'Efectivo',
            amount: payCash.toFixed(2)
        });
    }

    if (payCard > 0) {
        const cardType = getSelectedCardType();
        paymentMethods.push({
            method: cardType === 'credit' ? '04' : '28', // 04: Tarjeta Crédito, 28: Tarjeta Débito
            methodName: cardType === 'credit' ? 'Tarjeta Crédito' : 'Tarjeta Débito',
            amount: payCard.toFixed(2)
        });
    }

    if (payTransfer > 0) {
        paymentMethods.push({
            method: '03', // Código SAT para transferencia
            methodName: 'Transferencia',
            amount: payTransfer.toFixed(2)
        });
    }

    if (payCheck > 0) {
        paymentMethods.push({
            method: '02', // Código SAT para cheque
            methodName: 'Cheque',
            amount: payCheck.toFixed(2)
        });
    }

    if (payCreditNote > 0) {
        paymentMethods.push({
            method: '99', // Código personalizado para nota de crédito
            methodName: 'Nota de Crédito',
            amount: payCreditNote.toFixed(2)
        });
    }

    return paymentMethods;
}

function updateCardTypeLabel() {
    const cardType = document.querySelector('input[name="cardType"]:checked').value;
    const cardTypeText = cardType === 'debit' ? 'Débito' : 'Crédito';

    toastMixin.fire({
        icon: 'success',
        title: `Tarjeta de ${cardTypeText} seleccionada`
    });
}

function getSelectedCardType() {
    const cardTypeRadio = document.querySelector('input[name="cardType"]:checked');
    return cardTypeRadio ? cardTypeRadio.value : 'debit';
}


// Agregar esto en window.onload o al final del script
document.addEventListener('DOMContentLoaded', function () {
    const searchFolioInput = document.getElementById('searchFolio');
    if (searchFolioInput) {
        searchFolioInput.addEventListener('keypress', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                searchAndLoadFolio();
            }
        });
    }
});

// ==================== PASO 5: FUNCIONES PRINCIPALES DE BÚSQUEDA Y CARGA ==================== -->

async function searchAndLoadFolio() {
    const folioInput = document.getElementById('searchFolio');
    const folio = folioInput.value.trim();

    if (!folio) {
        toastMixin.fire({
            icon: 'error',
            title: 'Por favor ingrese un folio'
        });
        folioInput.focus();
        return;
    }

    const btnSearch = document.getElementById('btnSearchFolio');
    const originalHTML = btnSearch.innerHTML;

    btnSearch.disabled = true;
    btnSearch.innerHTML = '<span class="loading"></span> Buscando...';

    try {
        const result = await fetchSaleByFolio(folio);

        // ❌ Error (del servidor o red)
        if (!result.success) {
            toastMixin.fire({
                icon: 'warning',
                title: result.message || 'No se pudo cargar el folio'
            });
            return;
        }

        // ✅ Éxito
        loadSaleDataToForm(result.data);
        showFolioLoadedBadge(folio);

        toastMixin.fire({
            icon: 'success',
            title: `Venta ${folio} cargada exitosamente`
        });

    } catch (error) {
        toastMixin.fire({
            icon: 'error',
            title: `Error inesperado: ${error.message}`
        });
    } finally {
        btnSearch.disabled = false;
        btnSearch.innerHTML = originalHTML;
    }
}


async function fetchSaleByFolio(folio) {
    try {
        const formData = new FormData();
        formData.append("folio", folio);

        const response = await fetch("/PuntoDeVenta/BuscarDocumento", {
            method: "POST",
            body: formData
        });

        if (!response.ok) {
            return {
                success: false,
                message: "Error al conectar con el servidor"
            };
        }

        const result = await response.json();

        // Error funcional desde backend
        if (!result.success) {
            return {
                success: false,
                message: result.message
            };
        }

        // Éxito
        return {
            success: true,
            data: result.data
        };

    } catch (err) {
        return {
            success: false,
            message: err.message
        };
    }
}

function loadSaleDataToForm(saleData) {
    try {
        const itemsTable = document.getElementById('itemsTable');
        itemsTable.innerHTML = '';

        document.getElementById('clientName').value = saleData.client.name || 'Mostrador';
        document.getElementById('clientRFC').value = saleData.client.rfc || '';
        documentID = saleData.id;
        cve_cli = saleData.client.cve;
        document.getElementById('seller').value = saleData.seller || '';

        if (saleData.items && saleData.items.length > 0) {
            saleData.items.forEach((item) => {
                const stockDisponible = parseFloat(item.stock) || 0;
                const cantidadPedida = parseFloat(item.quantity) || 0; // pendiente o total según modo
                const cantidadSugerida = Math.min(stockDisponible, cantidadPedida);
                const hayProblema = stockDisponible < cantidadPedida;
                const precio = parseFloat(item.unitPrice) || 0;

                const row = document.createElement('tr');

                if (saleData.esParcial) {
                    // ── Modo complemento: mostrar contexto completo del pendiente ──
                    if (hayProblema) row.style.background = '#fff7ed'; // naranja muy suave

                    row.innerHTML = `
                        <td>
                            <input type="text" readonly class="item-code" value="${item.code || ''}">
                        </td>
                        <td>
                            <input type="text" readonly class="item-desc" value="${item.description || ''}">
                            <div style="font-size:11px;margin-top:3px;display:flex;gap:8px;flex-wrap:wrap">
                                <span style="color:#059669">✓ Entregado: <b>${parseFloat(item.quantityDelivered)}</b></span>
                                <span style="color:#d97706">⏳ Pendiente: <b>${cantidadPedida}</b></span>
                                ${hayProblema
                            ? `<span style="color:#dc2626">⚠ Stock: <b>${stockDisponible}</b></span>`
                            : `<span style="color:#6b7280">Stock: <b>${stockDisponible}</b></span>`
                        }
                            </div>
                        </td>
                        <td>
                            <input type="number" class="item-qty"
                                   value="${cantidadSugerida}"
                                   min="0" max="${stockDisponible}"
                                   onchange="calcularParcial(this, ${cantidadPedida}, ${stockDisponible})">
                        </td>
                        <td>
                            <input type="number" readonly class="item-price" value="${precio}">
                        </td>
                        <td>
                            <input type="number" readonly class="item-discount" value="${parseFloat(item.discountPercent) || 0}">
                        </td>
                        <td>
                            <input type="number" class="item-stock" value="${stockDisponible}" readonly
                                   style="color:${hayProblema ? '#dc2626' : 'inherit'};font-weight:${hayProblema ? '600' : 'normal'}">
                        </td>
                        <td>
                            <input type="hidden" class="item-qty-original"  value="${cantidadPedida}">
                            <input type="hidden" class="item-id-pendiente"  value="${item.idPendiente || 0}">
                            <input type="number" class="item-total"
                                   value="${(cantidadSugerida * precio).toFixed(2)}"
                                   readonly style="font-weight:600">
                        </td>
                        <td></td>
                    `;
                } else {
                    // ── Modo venta nueva: comportamiento original ──
                    if (hayProblema) row.style.background = '#fff7ed';

                    row.innerHTML = `
                        <td>
                            <input type="text" readonly class="item-code" value="${item.code || ''}">
                        </td>
                        <td>
                            <input type="text" readonly class="item-desc" value="${item.description || ''}">
                            ${hayProblema ? `
                            <div style="font-size:11px;color:#d97706;margin-top:3px">
                                ⚠ Stock insuficiente — pedido: ${cantidadPedida}, disponible: ${stockDisponible}
                            </div>` : ''}
                        </td>
                        <td>
                            <input type="number" class="item-qty"
                                   value="${cantidadSugerida}"
                                   min="0" max="${stockDisponible}"
                                   onchange="calcularParcial(this, ${cantidadPedida}, ${stockDisponible})">
                        </td>
                        <td>
                            <input type="number" readonly class="item-price" value="${precio}">
                        </td>
                        <td>
                            <input type="number" readonly class="item-discount" value="${parseFloat(item.discountPercent) || 0}">
                        </td>
                        <td>
                            <input type="number" class="item-stock" value="${stockDisponible}" readonly
                                   style="color:${hayProblema ? '#dc2626' : 'inherit'};font-weight:${hayProblema ? '600' : 'normal'}">
                        </td>
                        <td>
                            <input type="hidden" class="item-qty-original" value="${cantidadPedida}">
                            <input type="hidden" class="item-id-pendiente" value="0">
                            <input type="number" class="item-total"
                                   value="${(cantidadSugerida * precio).toFixed(2)}"
                                   readonly style="font-weight:600">
                        </td>
                        <td></td>
                    `;
                }

                itemsTable.appendChild(row);
                addRowListeners(row);
            });

            // Banner informativo para modo parcial
            if (saleData.esParcial) {
                mostrarBannerParcial(saleData.items.length);
            }

        } else {
            mostrarTablaVacia();
        }

        setTimeout(() => calculateTotals(), 150);
        loadedFolio = saleData.folio;
        window.modoComplemento = saleData.esParcial; // bandera global
        window.scrollTo({ top: 0, behavior: 'smooth' });

    } catch (error) {
        console.error('Error al cargar datos:', error);
        toastMixin.fire({ icon: 'error', title: 'Error al cargar los datos de la venta' });
    }
}

function mostrarBannerParcial(cantidadPendientes) {
    // Remover banner anterior si existe
    const bannerExistente = document.getElementById('banner-parcial');
    if (bannerExistente) bannerExistente.remove();

    const banner = document.createElement('div');
    banner.id = 'banner-parcial';
    banner.style.cssText = `
        background: linear-gradient(135deg, #fef3c7 0%, #fde68a 100%);
        border-left: 4px solid #f59e0b;
        border-radius: 8px;
        padding: 1rem 1.25rem;
        margin-bottom: 1rem;
        display: flex;
        align-items: center;
        gap: 1rem;
        font-size: 0.9rem;
        color: #78350f;
    `;
    banner.innerHTML = `
        <i class="fas fa-clock" style="font-size:1.5rem;color:#d97706"></i>
        <div>
            <strong style="font-size:1rem">Entrega complementaria</strong><br>
            Hay <b>${cantidadPendientes}</b> artículo(s) pendiente(s) de entrega.
            Ajuste las cantidades según el stock disponible y registre la entrega parcial o completa.
        </div>
    `;

    // Insertar antes de la tabla de artículos
    const tablaCard = document.querySelector('#itemsTable').closest('.modern-card');
    tablaCard.insertAdjacentElement('beforebegin', banner);
}

function calcularParcial(input, cantidadPendiente, stockDisponible) {
    let valor = parseFloat(input.value) || 0;

    if (valor > stockDisponible) {
        input.value = stockDisponible;
        valor = stockDisponible;
        toastMixin.fire({ icon: 'warning', title: `Máximo disponible: ${stockDisponible}` });
    }

    if (valor > cantidadPendiente) {
        input.value = cantidadPendiente;
        valor = cantidadPendiente;
        toastMixin.fire({ icon: 'warning', title: `No puede superar el pendiente: ${cantidadPendiente}` });
    }

    const row = input.closest('tr');
    const precio = parseFloat(row.querySelector('.item-price').value) || 0;
    const restante = cantidadPendiente - valor;

    row.querySelector('.item-total').value = (valor * precio).toFixed(2);

    // Actualizar badge de pendiente restante en la descripción
    let badge = row.querySelector('.pendiente-badge');
    if (restante > 0) {
        if (!badge) {
            badge = document.createElement('span');
            badge.className = 'pendiente-badge';
            badge.style.cssText = 'font-size:11px;color:#d97706;margin-left:8px';
            row.querySelector('.item-desc').after(badge);
        }
        badge.textContent = `→ Quedará pendiente: ${restante}`;
    } else if (badge) {
        badge.textContent = '→ Entrega completa ✓';
        badge.style.color = '#059669';
    }

    calculateTotals();
}


function showFolioLoadedBadge(folio) {
    const badge = document.getElementById('folioLoadedBadge');
    const badgeText = document.getElementById('folioLoadedText');
    const btnClear = document.getElementById('btnClearFolio');

    badgeText.textContent = `Folio: ${folio}`;
    badge.style.display = 'inline-flex';
    btnClear.style.display = 'inline-flex';
}

function clearLoadedFolio() {
    if (!confirm('¿Está seguro de limpiar el folio cargado? Se perderán todos los datos.')) {
        return;
    }

    // Limpiar todo
    resetForm();

    // Ocultar badge
    document.getElementById('folioLoadedBadge').style.display = 'none';
    document.getElementById('btnClearFolio').style.display = 'none';
    document.getElementById('searchFolio').value = '';

    // Resetear variables
    isEditMode = false;
    loadedFolio = null;

    toastMixin.fire({
        icon: 'info',
        title: 'Formulario limpiado'
    });
}

function addRowListeners(row) {
    const inputs = row.querySelectorAll('input:not([readonly])');
    inputs.forEach((input, index) => {
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                if (index === inputs.length - 1) {
                    if (isRowFilled(row)) {
                        addRow();
                    }
                } else {
                    inputs[index + 1].focus();
                }
            }
        });
    });
}

// ==================== 5. AGREGAR ESTAS FUNCIONES AL FINAL DEL SCRIPT ====================

async function startQRScanner() {
    const modal = document.getElementById('qrModal');
    const video = document.getElementById('qr-video');

    // Verificar si jsQR está disponible
    if (typeof jsQR === 'undefined') {
        toastMixin.fire({
            icon: 'error',
            title: 'Error: Librería QR no cargada. Recargue la página.'
        });
        return;
    }

    try {
        // Solicitar acceso a la cámara
        videoStream = await navigator.mediaDevices.getUserMedia({
            video: {
                facingMode: 'environment', // Usar cámara trasera en móviles
                width: { ideal: 1280 },
                height: { ideal: 720 }
            }
        });

        video.srcObject = videoStream;
        await video.play();

        // Mostrar modal
        modal.classList.add('active');

        // Iniciar escaneo
        scanQRCode();

        toastMixin.fire({
            icon: 'info',
            title: 'Cámara activada - Escanee el código QR'
        });

    } catch (error) {
        console.error('Error al acceder a la cámara:', error);

        let errorMessage = 'No se pudo acceder a la cámara.';

        if (error.name === 'NotAllowedError') {
            errorMessage = 'Permiso de cámara denegado. Active los permisos en su navegador.';
        } else if (error.name === 'NotFoundError') {
            errorMessage = 'No se encontró ninguna cámara en el dispositivo.';
        } else if (error.name === 'NotReadableError') {
            errorMessage = 'La cámara está siendo usada por otra aplicación.';
        }

        toastMixin.fire({
            icon: 'error',
            title: errorMessage
        });
    }
}

function stopQRScanner() {
    const modal = document.getElementById('qrModal');
    const video = document.getElementById('qr-video');

    modal.style.zIndex = '1000';

    // Detener cualquier stream activo (principal o factura)
    const streamActivo = videoStream || facturaVideoStream;
    if (streamActivo) {
        streamActivo.getTracks().forEach(t => t.stop());
        videoStream = null;
        facturaVideoStream = null;
    }

    video.pause();
    video.srcObject = null;
    modal.classList.remove('active');

    // Cancelar cualquier scanner activo
    if (qrScanner) { cancelAnimationFrame(qrScanner); qrScanner = null; }
    if (facturaQRScanner) { cancelAnimationFrame(facturaQRScanner); facturaQRScanner = null; }
}

function scanQRCode() {
    const video = document.getElementById('qr-video');
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d');

    function tick() {
        if (video.readyState === video.HAVE_ENOUGH_DATA) {
            canvas.height = video.videoHeight;
            canvas.width = video.videoWidth;
            context.drawImage(video, 0, 0, canvas.width, canvas.height);

            const imageData = context.getImageData(0, 0, canvas.width, canvas.height);

            try {
                const code = jsQR(imageData.data, imageData.width, imageData.height, {
                    inversionAttempts: "dontInvert",
                });

                if (code && code.data) {
                    // QR Code detectado!
                    handleQRCodeDetected(code.data);
                    return; // Detener el escaneo
                }
            } catch (error) {
                console.error('Error al escanear QR:', error);
            }
        }

        qrScanner = requestAnimationFrame(tick);
    }

    tick();
}

function handleQRCodeDetected(qrData) {
    // Detener el escáner
    stopQRScanner();

    // Extraer el folio del QR
    const folio = qrData.trim();

    // Colocar el folio en el input
    document.getElementById('searchFolio').value = folio;

    // Realizar búsqueda automática
    toastMixin.fire({
        icon: 'success',
        title: `Código QR detectado: ${folio}`
    });

    // Buscar automáticamente después de un pequeño delay
    setTimeout(() => {
        searchAndLoadFolio();
    }, 500);
}

// ==================== FUNCIONES DE MODAL DE FACTURACIÓN ====================

async function openFacturaModal() {
    const modal = document.getElementById('facturaModal');
    modal.classList.add('active');

    // Limpiar búsqueda y formulario
    document.getElementById('searchFacturaFolio').value = '';
    document.getElementById('facturaFormSection').style.display = 'none';

    // Cargar todos los documentos inmediatamente
    await loadAllInvoicableDocuments();
}

function closeFacturaModal() {

    // Volver a mostrar el grid
    document.getElementById('facturaDocumentsSection').style.display = 'block';
    document.getElementById('facturaFormSection').style.display = 'none';

    clearFacturaForm();
    selectedDocumentForInvoice = null;

    // Scroll al inicio
    document.querySelector('.qr-modal-content').scrollTop = 0;

    toastMixin.fire({
        icon: 'info',
        title: 'Facturación cancelada'
    });

    const modal = document.getElementById('facturaModal');
    modal.classList.remove('active');
    selectedDocumentForInvoice = null;
    clearFacturaForm();

}

async function loadAllInvoicableDocuments() {
    const loadingDiv = document.getElementById('loadingDocuments');
    const gridDiv = document.getElementById('documentsGrid');
    const noDocsDiv = document.getElementById('noDocumentsMessage');

    // Mostrar loading
    loadingDiv.style.display = 'block';
    gridDiv.innerHTML = '';
    noDocsDiv.style.display = 'none';

    try {
        const response = await fetch('/PuntoDeVenta/ObtenerTodosDocumentosFacturables', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
            },
            body: '__RequestVerificationToken=' + document.querySelector('input[name="__RequestVerificationToken"]')?.value
        });

        if (!response.ok) {
            throw new Error('Error al cargar documentos');
        }

        const result = await response.json();

        loadingDiv.style.display = 'none';

        if (result.success && result.data.length > 0) {
            window.allInvoicableDocuments = result.data;
            displayDocumentCards(result.data);
            updateDocumentCount(result.data.length, result.data.length);
        } else {
            noDocsDiv.style.display = 'block';
            updateDocumentCount(0, 0);
        }

    } catch (error) {
        loadingDiv.style.display = 'none';
        toastMixin.fire({
            icon: 'error',
            title: `Error: ${error.message}`
        });
    }
}

function displayDocumentCards(documents) {
    const grid = document.getElementById('documentsGrid');
    grid.innerHTML = '';

    documents.forEach((doc, index) => {
        const card = createDocumentCard(doc, index);
        grid.appendChild(card);
    });
}

function createDocumentCard(doc, index) {
    const isInvoiced = doc.invoiced || false;
    const cardDiv = document.createElement('div');
    cardDiv.className = `document-card ${isInvoiced ? 'invoiced' : ''}`;
    cardDiv.dataset.folio = doc.folio.toLowerCase();
    cardDiv.dataset.client = (doc.clientName || '').toLowerCase();
    cardDiv.dataset.rfc = (doc.rfc || '').toLowerCase();

    const statusBadgeHTML = isInvoiced
        ? '<span class="status-badge invoiced"><i class="fas fa-check-circle"></i> Facturado</span>'
        : '<span class="status-badge pending"><i class="fas fa-clock"></i> Pendiente</span>';

    cardDiv.innerHTML = `
        <div class="document-card-header">
            <div class="document-folio">
                <i class="fas fa-file-invoice"></i>
                ${doc.folio}
            </div>
            ${statusBadgeHTML}
        </div>

        <div class="document-card-body">
            <div class="document-info-row">
                <i class="fas fa-user"></i>
                <strong>Cliente:</strong> ${doc.n_cli || 'Sin nombre'}
            </div>

            <div class="document-info-row">
                <i class="fas fa-id-card"></i>
                <strong>RFC:</strong> ${doc.rfc || 'Sin RFC'}
            </div>

            <div class="document-info-row">
                <i class="fas fa-calendar"></i>
                <strong>Fecha:</strong> ${dateFormatter(doc.fch1)}
            </div>
        </div>

        <div class="document-total">
            <div class="document-total-label">Total de la Venta</div>
            <div class="document-total-amount">$${parseFloat(doc.imp).toFixed(2)}</div>
        </div>

        <button class="btn-facturar"
                onclick="selectDocumentForInvoice(${index})"
                ${isInvoiced ? 'disabled' : ''}>
            <i class="fas ${isInvoiced ? 'fa-check' : 'fa-file-invoice-dollar'}"></i>
            ${isInvoiced ? 'Ya Facturado' : 'Facturar Ahora'}
        </button>
    `;

    // Solo agregar evento de click si no está facturado
    if (!isInvoiced) {
        cardDiv.style.cursor = 'pointer';
        cardDiv.addEventListener('click', (e) => {
            // No activar si se hizo click en el botón
            if (!e.target.closest('.btn-facturar')) {
                selectDocumentForInvoice(index);
            }
        });
    }

    return cardDiv;
}

function filterDocuments() {
    const searchTerm = document.getElementById('searchFacturaFolio').value.toLowerCase().trim();
    const allDocs = window.allInvoicableDocuments || [];

    if (!searchTerm) {
        // Mostrar todos
        displayDocumentCards(allDocs);
        updateDocumentCount(allDocs.length, allDocs.length);
        return;
    }

    // Filtrar documentos
    const filtered = allDocs.filter(doc => {
        const folio = (doc.folio || '').toLowerCase();
        const client = (doc.clientName || '').toLowerCase();
        const rfc = (doc.rfc || '').toLowerCase();

        return folio.includes(searchTerm) ||
            client.includes(searchTerm) ||
            rfc.includes(searchTerm);
    });

    displayDocumentCards(filtered);
    updateDocumentCount(filtered.length, allDocs.length);

    // Mostrar mensaje si no hay resultados
    const noDocsDiv = document.getElementById('noDocumentsMessage');
    if (filtered.length === 0) {
        noDocsDiv.innerHTML = `
            <i class="fas fa-search"></i>
            <h3>No se encontraron resultados</h3>
            <p>No hay documentos que coincidan con "${searchTerm}"</p>
        `;
        noDocsDiv.style.display = 'block';
    } else {
        noDocsDiv.style.display = 'none';
    }
}

function updateDocumentCount(showing, total) {
    const countText = document.getElementById('documentCountText');
    if (showing === total) {
        countText.textContent = `${total} documento${total !== 1 ? 's' : ''}`;
    } else {
        countText.textContent = `${showing} de ${total} documentos`;
    }
}

function selectDocumentForInvoice(index) {
    const documents = window.allInvoicableDocuments;
    const doc = documents[index]; // ⚠️ CAMBIO: usar 'doc' en lugar de 'document'

    if (!doc || doc.invoiced) {
        toastMixin.fire({
            icon: 'error',
            title: 'Documento no disponible para facturación'
        });
        return;
    }

    selectedDocumentForInvoice = doc;

    // Ocultar grid y mostrar formulario
    document.getElementById('facturaDocumentsSection').style.display = 'none';
    document.getElementById('facturaFormSection').style.display = 'block';

    // Llenar información del documento
    document.getElementById('facturaFolioLabel').textContent = doc.folio;
    document.getElementById('facturaTotalLabel').textContent = parseFloat(doc.imp).toFixed(2);
    document.getElementById('facturaClientLabel').textContent = doc.razon_social;
    document.getElementById('facturaDateLabel').textContent = dateFormatter(doc.fch1);

    // Pre-llenar campos si hay datos disponibles
    if (doc.rfc && doc.rfc !== 'Sin RFC') {
        document.getElementById('facturaRFC').value = doc.rfc;
    }

    if (doc.cli_prov) {
        document.getElementById('facturaRazonSocial').value = doc.razon_social;
    }
    if (doc.codigo_postal) {
        document.getElementById('facturaCP').value = doc.codigo_postal;
    }
    if (doc.correo) {
        document.getElementById('facturaEmail').value = doc.correo;
    }
    if (doc.uso_sugerido) {
        document.getElementById('facturaUsoCFDI').value = doc.uso_sugerido;
    }
    if (doc.regimen_fiscal) {
        document.getElementById('facturaRegimenFiscal').value = doc.regimen_fiscal;
    }
    if (doc.f_pago) {
        document.getElementById('facturaFormaPago').value = doc.f_pago;
    }

    // Scroll al inicio del modal
    document.querySelector('.qr-modal-content').scrollTop = 0;

    toastMixin.fire({
        icon: 'success',
        title: `Documento ${doc.folio} seleccionado`
    });
}

async function generarFactura() {
    if (!selectedDocumentForInvoice) {
        toastMixin.fire({
            icon: 'error',
            title: 'No hay documento seleccionado'
        });
        return;
    }

    // Validar campos requeridos
    const rfc = document.getElementById('facturaRFC').value.trim();
    const razonSocial = document.getElementById('facturaRazonSocial').value.trim();
    const email = document.getElementById('facturaEmail').value.trim();
    const cp = document.getElementById('facturaCP').value.trim();
    const usoCFDI = document.getElementById('facturaUsoCFDI').value;
    const metodoPago = document.getElementById('facturaMetodoPago').value;
    const banco = document.getElementById('facturaCuentaBanco').value;

    if (!rfc || !razonSocial || !email || !cp || !usoCFDI || !metodoPago) {
        toastMixin.fire({
            icon: 'error',
            title: 'Complete todos los campos requeridos'
        });
        return;
    }

    // Validar RFC
    if (rfc.length < 12 || rfc.length > 13) {
        toastMixin.fire({
            icon: 'error',
            title: 'RFC inválido (debe tener 12 o 13 caracteres)'
        });
        document.getElementById('facturaRFC').focus();
        return;
    }

    // Validar CP
    if (!/^\d{5}$/.test(cp)) {
        toastMixin.fire({
            icon: 'error',
            title: 'Código Postal inválido (5 dígitos)'
        });
        document.getElementById('facturaCP').focus();
        return;
    }

    // Validar Email
    const emailRegex = /^[^\s@@]+@@[^\s@@]+\.[^\s@@]+$/;
    if (!emailRegex.test(email)) {
        toastMixin.fire({
            icon: 'error',
            title: 'Email inválido'
        });
        document.getElementById('facturaEmail').focus();
        return;
    }

    // Confirmar
    const confirmResult = await Swal.fire({
        title: '¿Generar Factura?',
        html: `
            <div style="text-align: left; padding: 1rem;">
                <p><strong>Folio:</strong> ${selectedDocumentForInvoice.folio}</p>
                <p><strong>RFC:</strong> ${rfc}</p>
                <p><strong>Razón Social:</strong> ${razonSocial}</p>
                <p><strong>Total:</strong> $${parseFloat(selectedDocumentForInvoice.imp).toFixed(2)}</p>
            </div>
        `,
        icon: 'question',
        showCancelButton: true,
        confirmButtonText: 'Sí, generar',
        cancelButtonText: 'Cancelar',
        confirmButtonColor: '#059669'
    });

    if (!confirmResult.isConfirmed) {
        return;
    }

    // Preparar datos
    const formData = new FormData();
    formData.append('idsDocumentos', selectedDocumentForInvoice.id_encabezado);
    formData.append('folio', selectedDocumentForInvoice.folio);
    formData.append('rfc', rfc.toUpperCase());
    formData.append('razonSocial', razonSocial);
    formData.append('tipo', "contado");
    formData.append('email', email);
    formData.append('codigoPostal', cp);
    formData.append('cfdi', usoCFDI);
    formData.append('mdp', metodoPago);
    formData.append('banco', banco);
    formData.append('observaciones', document.getElementById('facturaObservaciones').value.trim());
    formData.append('rFiscal', document.getElementById('facturaRegimenFiscal').value);

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    if (token) {
        formData.append('__RequestVerificationToken', token);
    }

    // ⭐ MOSTRAR LOADER
    const loadingOverlay = mostrarLoaderFacturacion();

    try {
        const response = await fetch('/PuntoDeVenta/ProcesarDocumentosAsync', {
            method: 'POST',
            body: formData
        });

        if (!response.ok) {
            throw new Error('Error al generar factura');
        }

        const result = await response.json();

        // ⭐ CERRAR LOADER
        cerrarLoaderFacturacion(loadingOverlay);

        if (result.success) {
            mostrarResumenExitoso(result, 0);
            //closeFacturaModal();
        } else {
            mostrarErrorPorPaso(result, 1);
        }

    } catch (error) {
        // ⭐ CERRAR LOADER EN CASO DE ERROR
        cerrarLoaderFacturacion(loadingOverlay);

        toastMixin.fire({
            icon: 'error',
            title: `Error: ${error.message}`
        });
    }
}

function cancelFactura() {
    if (confirm('¿Desea volver a la lista de documentos?')) {
        // Volver a mostrar el grid
        document.getElementById('facturaDocumentsSection').style.display = 'block';
        document.getElementById('facturaFormSection').style.display = 'none';

        clearFacturaForm();
        selectedDocumentForInvoice = null;

        // Scroll al inicio
        document.querySelector('.qr-modal-content').scrollTop = 0;

        toastMixin.fire({
            icon: 'info',
            title: 'Facturación cancelada'
        });
    }
}

function clearFacturaForm() {
    document.getElementById('facturaRFC').value = '';
    document.getElementById('facturaRazonSocial').value = '';
    document.getElementById('facturaEmail').value = '';
    document.getElementById('facturaCP').value = '';
    document.getElementById('facturaUsoCFDI').value = '';
    document.getElementById('facturaMetodoPago').value = '';
    document.getElementById('facturaObservaciones').value = '';
}

// Event listener para filtro en tiempo real
document.addEventListener('DOMContentLoaded', function () {
    const searchFacturaInput = document.getElementById('searchFacturaFolio');
    if (searchFacturaInput) {
        // El filtro se aplica automáticamente con oninput en el HTML
        searchFacturaInput.addEventListener('keypress', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
            }
        });
    }
});



// ============================================
// FUNCIÓN PARA MOSTRAR ÉXITO
// ============================================
function mostrarResumenExitoso(result, totalDocumentos, esRetorno = false) {
    const { resumen, detalles } = result;
    const montoFormateado = detalles.total ? parseFloat(detalles.total).toLocaleString('es-MX', {
        style: 'currency',
        currency: 'MXN'
    }) : 'N/A';

    // Guardar estado del modal para poder volver (solo si no es un retorno)
    if (!esRetorno) {
        guardarModalEnStack({ result, totalDocumentos });
    }

    Swal.fire({
        title: '✅ Proceso Completado con Éxito',
        html: `
            <div style="text-align: left; padding: 1rem;">
                <!-- Banner de Éxito -->
                <div style="
                    background: linear-gradient(135deg, #d1fae5 0%, #a7f3d0 100%);
                    padding: 1.5rem;
                    border-radius: 12px;
                    margin-bottom: 1.5rem;
                    border-left: 4px solid #059669;
                    animation: slideIn 0.5s ease-out;
                ">
                    <div style="font-size: 1.4rem; font-weight: 700; color: #047857; margin-bottom: 0.5rem;">
                        <i class="fas fa-check-circle"></i>
                        ${resumen.documentosProcesados} Documento(s) Facturado(s)
                    </div>
                    <div style="color: #065f46; font-size: 0.95rem;">
                        <i class="fas fa-clock"></i> ${resumen.tiempoFinalizacion}
                    </div>
                </div>

                <!-- Información de la Factura -->
                <div style="background: #f8fafc; padding: 1.25rem; border-radius: 10px; margin-bottom: 1rem; border: 1px solid #e2e8f0;">
                    <h4 style="color: #1e293b; margin-bottom: 1rem; display: flex; align-items: center; gap: 0.5rem;">
                        <i class="fas fa-file-invoice-dollar" style="color: #7c3aed;"></i>
                        Comprobante Fiscal
                    </h4>

                    <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; font-size: 0.9rem;">
                        <div>
                            <strong style="color: #64748b;">UUID:</strong><br>
                            <span style="color: #1e293b; font-family: monospace; font-size: 0.85rem;">${detalles.uuid || 'N/A'}</span>
                        </div>
                        <div>
                            <strong style="color: #64748b;">Serie-Folio:</strong><br>
                            <span style="color: #1e293b; font-weight: 600;">${detalles.serie || ''}-${detalles.folio || ''}</span>
                        </div>
                        <div>
                            <strong style="color: #64748b;">Total:</strong><br>
                            <span style="color: #059669; font-weight: 700; font-size: 1.1rem;">${montoFormateado}</span>
                        </div>
                        <div>
                            <strong style="color: #64748b;">Productos:</strong><br>
                            <span style="color: #1e293b; font-weight: 600;">${detalles.cantidadProductos || 0} partidas</span>
                        </div>
                    </div>
                </div>

                <!-- Información del Cliente -->
                <div style="background: #fefce8; padding: 1rem; border-radius: 8px; margin-bottom: 1rem; border-left: 3px solid #eab308;">
                    <h5 style="color: #854d0e; margin-bottom: 0.5rem; font-size: 0.95rem;">
                        <i class="fas fa-user-tie"></i> Cliente
                    </h5>
                    <div style="color: #713f12; font-size: 0.85rem;">
                        <strong>${detalles.razonSocial || 'N/A'}</strong><br>
                        RFC: ${detalles.rfcCliente || 'N/A'}
                    </div>
                </div>

                <!-- Detalles del Proceso -->
                <div style="background: #ede9fe; padding: 1rem; border-radius: 8px; margin-bottom: 1rem;">
                    <h5 style="color: #5b21b6; margin-bottom: 0.75rem; font-size: 0.95rem;">
                        <i class="fas fa-tasks"></i> IDs Generados
                    </h5>
                    <ul style="color: #6b21a8; margin: 0; padding-left: 1.5rem; font-size: 0.85rem; line-height: 1.8;">
                        <li>Pedido: <strong>#${detalles.pedidoId}</strong></li>
                        <li>Remisión: <strong>#${detalles.remisionId}</strong></li>
                        <li>Factura: <strong>#${detalles.facturaId}</strong></li>
                    </ul>
                </div>
            </div>
        `,
        icon: 'success',
        showCancelButton: true,
        showDenyButton: true,
        showCloseButton: true,
        confirmButtonText: '<i class="fas fa-download"></i> Descargar',
        denyButtonText: '<i class="fas fa-envelope"></i> Enviar correo',
        cancelButtonText: '<i class="fas fa-times"></i> Cerrar',
        confirmButtonColor: '#7c3aed',
        denyButtonColor: '#3b82f6',
        cancelButtonColor: '#6b7280',
        width: '700px',
        allowOutsideClick: false,
        allowEscapeKey: false,
    }).then((modalResult) => {
        if (modalResult.isConfirmed) {
            mostrarOpcionesDescarga(detalles, `${detalles.serie}-${detalles.folio}`, montoFormateado, result, totalDocumentos);
        } else if (modalResult.isDenied) {
            mostrarModalEnvioCorreo(detalles, result, totalDocumentos);
        } else {
            // Al cerrar, limpiar el stack y recargar
            limpiarStackModales();
            setTimeout(() => {
                recargarVista();
            }, 500);
        }
    });
}
// ============================================
// FUNCIÓN PARA MOSTRAR OPCIONES DE DESCARGA
// ============================================
function mostrarOpcionesDescarga(detalles, seriefolio, montoFormateado, resultOriginal, totalDocumentos) {
    Swal.fire({
        title: '📥 Descargar Comprobante',
        html: `
            <div style="padding: 1rem;">
                <p style="color: #6b7280; margin-bottom: 1.5rem; font-size: 0.95rem;">
                    Selecciona el formato que deseas descargar:
                </p>

                <div style="display: grid; gap: 1rem;">
                    <!-- Opción PDF -->
                    <a href="${detalles.pdfUrl}" download target="_blank" style="
                        display: flex;
                        align-items: center;
                        gap: 1rem;
                        padding: 1.25rem;
                        background: linear-gradient(135deg, #dc2626 0%, #ef4444 100%);
                        color: white;
                        text-decoration: none;
                        border-radius: 12px;
                        font-weight: 600;
                        font-size: 1rem;
                        transition: all 0.3s;
                        box-shadow: 0 4px 6px rgba(220, 38, 38, 0.3);
                    " onmouseover="this.style.transform='translateY(-2px)'"
                       onmouseout="this.style.transform='translateY(0)'">
                        <i class="fas fa-file-pdf" style="font-size: 2rem;"></i>
                        <div style="text-align: left; flex: 1;">
                            <div style="font-size: 1.1rem;">Descargar PDF</div>
                            <div style="font-size: 0.8rem; opacity: 0.9;">Versión imprimible</div>
                        </div>
                        <i class="fas fa-download"></i>
                    </a>

                    <!-- Opción XML -->
                    <a href="${detalles.xmlUrl}" download target="_blank" style="
                        display: flex;
                        align-items: center;
                        gap: 1rem;
                        padding: 1.25rem;
                        background: linear-gradient(135deg, #059669 0%, #10b981 100%);
                        color: white;
                        text-decoration: none;
                        border-radius: 12px;
                        font-weight: 600;
                        font-size: 1rem;
                        transition: all 0.3s;
                        box-shadow: 0 4px 6px rgba(5, 150, 105, 0.3);
                    " onmouseover="this.style.transform='translateY(-2px)'"
                       onmouseout="this.style.transform='translateY(0)'">
                        <i class="fas fa-file-code" style="font-size: 2rem;"></i>
                        <div style="text-align: left; flex: 1;">
                            <div style="font-size: 1.1rem;">Descargar XML</div>
                            <div style="font-size: 0.8rem; opacity: 0.9;">Archivo oficial del SAT</div>
                        </div>
                        <i class="fas fa-download"></i>
                    </a>

                    <!-- Opción Ambos -->
                    <button onclick="descargarAmbosArchivos('${detalles.pdfUrl}', '${detalles.xmlUrl}')" style="
                        display: flex;
                        align-items: center;
                        gap: 1rem;
                        padding: 1.25rem;
                        background: linear-gradient(135deg, #7c3aed 0%, #a855f7 100%);
                        color: white;
                        border: none;
                        border-radius: 12px;
                        font-weight: 600;
                        font-size: 1rem;
                        cursor: pointer;
                        transition: all 0.3s;
                        box-shadow: 0 4px 6px rgba(124, 58, 237, 0.3);
                    " onmouseover="this.style.transform='translateY(-2px)'"
                       onmouseout="this.style.transform='translateY(0)'">
                        <i class="fas fa-file-archive" style="font-size: 2rem;"></i>
                        <div style="text-align: left; flex: 1;">
                            <div style="font-size: 1.1rem;">Descargar Ambos</div>
                            <div style="font-size: 0.8rem; opacity: 0.9;">PDF + XML</div>
                        </div>
                        <i class="fas fa-download"></i>
                    </button>
                </div>
            </div>
        `,
        showCancelButton: true,
        showDenyButton: true,
        confirmButtonText: '<i class="fas fa-check"></i> Finalizar',
        denyButtonText: '<i class="fas fa-arrow-left"></i> Volver',
        cancelButtonText: '<i class="fas fa-times"></i> Cerrar',
        confirmButtonColor: '#059669',
        denyButtonColor: '#6b7280',
        cancelButtonColor: '#dc2626',
        width: '600px',
        allowOutsideClick: false,
    }).then((result) => {
        if (result.isDenied) {
            // Volver al modal de resumen
            modalStack.pop(); // Remover el estado actual antes de volver
            mostrarResumenExitoso(resultOriginal, totalDocumentos, true);
        } else if (result.isConfirmed || result.isDismissed) {
            limpiarStackModales();
            setTimeout(() => {
                recargarVista();
            }, 500);
        }
    });
}

// ============================================
// FUNCIÓN PARA DESCARGAR AMBOS ARCHIVOS
// ============================================
function descargarAmbosArchivos(pdfUrl, xmlUrl) {
    descargarArchivo(pdfUrl, "archivo.pdf");
    descargarArchivo(xmlUrl, "archivo.xml");

    //Swal.fire({
    //    icon: 'info',
    //    title: 'Descargando archivos...',
    //    text: 'PDF y XML',
    //    timer: 2000,
    //    toast: true,
    //    position: 'top-end',
    //    showConfirmButton: false
    //});
}

function descargarArchivo(url, nombre) {
    const a = document.createElement("a");
    a.href = url;
    a.download = nombre;   // <-- fuerza descarga
    document.body.appendChild(a);
    a.click();
    a.remove();
}


// ============================================
// FUNCIÓN CENTRALIZADA PARA RECARGAR LA VISTA
// ============================================
function recargarVista() {
    if (typeof cargarDocumentos === 'function') {
        cargarDocumentos();
    } else {
        location.reload();
    }
}

// ============================================
// FUNCIÓN PARA MOSTRAR ERRORES POR PASO (MEJORADA)
// ============================================
function mostrarErrorPorPaso(result, totalDocumentos) {
    const pasosInfo = {
        'GuardarDesdeDocumentos': {
            nombre: 'Guardar Documentos Base',
            icono: '📄',
            color: '#3b82f6',
            descripcion: 'Error al crear los documentos base en el sistema'
        },
        'GuardarRemisionDesdePedidos': {
            nombre: 'Crear Remisiones',
            icono: '📦',
            color: '#8b5cf6',
            descripcion: 'Error al generar las remisiones desde los pedidos'
        },
        'GuardarFacturaDesdeDocs': {
            nombre: 'Crear Facturas',
            icono: '🧾',
            color: '#ec4899',
            descripcion: 'Error al crear las facturas desde los documentos'
        },
        'GenerarFacturaDesdeDocs': {
            nombre: 'Generar Timbrado',
            icono: '✉️',
            color: '#f59e0b',
            descripcion: 'Error al timbrar la factura con el PAC'
        }
    };

    const pasoInfo = pasosInfo[result.step] || {
        nombre: result.step,
        icono: '⚠️',
        color: '#dc2626',
        descripcion: 'Error en el proceso'
    };

    // Extraer el mensaje de error completo
    const errorCompleto = result.error || result.message || 'Error desconocido';

    // Intentar extraer el código de error del SAT si existe
    let codigoSAT = '';
    let mensajeSAT = '';
    let detallesTecnicos = errorCompleto;

    const matchCFDI = errorCompleto.match(/CFDI\d+/);
    if (matchCFDI) {
        codigoSAT = matchCFDI[0];
    }

    // Extraer el mensaje principal del SAT
    const matchMensaje = errorCompleto.match(/CFDI\d+\s*-\s*([^-]+)/);
    if (matchMensaje) {
        mensajeSAT = matchMensaje[1].trim();
    }

    Swal.fire({
        title: '❌ Error en el Proceso de Facturación',
        html: `
        <div style="text-align: left; padding: 1rem;">
            <!-- Header del Error -->
            <div style="
                background: linear-gradient(135deg, #fee2e2 0%, #fecaca 100%);
                padding: 1.5rem;
                border-radius: 12px;
                margin-bottom: 1.5rem;
                border-left: 4px solid ${pasoInfo.color};
            ">
                <div style="
                    font-size: 2rem;
                    text-align: center;
                    margin-bottom: 0.75rem;
                ">
                    ${pasoInfo.icono}
                </div>
                <div style="
                    font-size: 1.2rem;
                    font-weight: 700;
                    color: #dc2626;
                    margin-bottom: 0.5rem;
                    text-align: center;
                ">
                    Error en: ${pasoInfo.nombre}
                </div>
                <div style="
                    color: #991b1b;
                    font-size: 0.9rem;
                    text-align: center;
                    font-style: italic;
                ">
                    ${pasoInfo.descripcion}
                </div>
            </div>

            ${codigoSAT ? `
            <!-- Código de Error SAT -->
            <div style="
                background: linear-gradient(135deg, #fef3c7 0%, #fde68a 100%);
                padding: 1.25rem;
                border-radius: 10px;
                margin-bottom: 1rem;
                border-left: 4px solid #f59e0b;
                text-align: center;
            ">
                <div style="
                    font-size: 1.5rem;
                    font-weight: 800;
                    color: #92400e;
                    margin-bottom: 0.5rem;
                    font-family: 'Courier New', monospace;
                ">
                    ${codigoSAT}
                </div>
                <div style="
                    color: #78350f;
                    font-size: 0.85rem;
                    font-weight: 600;
                ">
                    Código de error del SAT
                </div>
            </div>
            ` : ''}

            ${mensajeSAT ? `
            <!-- Mensaje Principal del SAT -->
            <div style="
                background: #fff1f2;
                padding: 1.25rem;
                border-radius: 10px;
                margin-bottom: 1rem;
                border: 2px solid #fecdd3;
            ">
                <div style="
                    display: flex;
                    align-items: start;
                    gap: 0.75rem;
                ">
                    <i class="fas fa-times-circle" style="
                        color: #dc2626;
                        font-size: 1.5rem;
                        margin-top: 0.125rem;
                    "></i>
                    <div style="flex: 1;">
                        <strong style="
                            color: #991b1b;
                            display: block;
                            margin-bottom: 0.75rem;
                            font-size: 1rem;
                        ">
                            Descripción del error:
                        </strong>
                        <div style="
                            color: #7f1d1d;
                            font-size: 1.05rem;
                            line-height: 1.6;
                            background: white;
                            padding: 1rem;
                            border-radius: 8px;
                            font-weight: 600;
                        ">
                            ${mensajeSAT}
                        </div>
                    </div>
                </div>
            </div>
            ` : ''}

            <!-- Detalles Completos del Error -->
            <div style="
                background: #f9fafb;
                padding: 1.25rem;
                border-radius: 10px;
                margin-bottom: 1rem;
                border: 1px solid #e5e7eb;
            ">
                <div style="
                    display: flex;
                    align-items: center;
                    gap: 0.5rem;
                    margin-bottom: 0.75rem;
                ">
                    <i class="fas fa-info-circle" style="color: #6b7280; font-size: 1.1rem;"></i>
                    <strong style="color: #374151; font-size: 0.95rem;">
                        Detalles completos del error:
                    </strong>
                </div>
                <div style="
                    color: #4b5563;
                    font-size: 0.9rem;
                    line-height: 1.8;
                    background: #1f2937;
                    color: #f3f4f6;
                    padding: 1rem;
                    border-radius: 8px;
                    font-family: 'Courier New', monospace;
                    max-height: 200px;
                    overflow-y: auto;
                    white-space: pre-wrap;
                    word-break: break-word;
                ">
${detallesTecnicos}
                </div>
            </div>

            <!-- Información de Rollback -->
            <div style="
                background: linear-gradient(135deg, #fef3c7 0%, #fde68a 100%);
                padding: 1.25rem;
                border-radius: 10px;
                margin-bottom: 1rem;
                border-left: 3px solid #f59e0b;
            ">
                <div style="display: flex; align-items: center; gap: 0.75rem; margin-bottom: 0.75rem;">
                    <i class="fas fa-undo" style="color: #d97706; font-size: 1.25rem;"></i>
                    <h4 style="color: #92400e; margin: 0; font-size: 1rem;">
                        Reversión Automática
                    </h4>
                </div>
                <p style="color: #78350f; font-size: 0.9rem; margin: 0; line-height: 1.6;">
                    Los registros creados antes del error fueron <strong>eliminados automáticamente</strong>
                    para mantener la integridad de los datos. No se generaron documentos incompletos.
                </p>
            </div>

            <!-- Documentos Afectados -->
            <div style="
                background: #f3f4f6;
                padding: 1.25rem;
                border-radius: 10px;
                margin-bottom: 1rem;
            ">
                <div style="display: flex; align-items: center; gap: 0.75rem; margin-bottom: 0.75rem;">
                    <i class="fas fa-file-invoice" style="color: #6b7280; font-size: 1.25rem;"></i>
                    <h4 style="color: #374151; margin: 0; font-size: 1rem;">
                        Documentos Afectados
                    </h4>
                </div>
                <div style="
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                    background: white;
                    padding: 1rem;
                    border-radius: 8px;
                ">
                    <span style="color: #6b7280; font-size: 0.95rem;">
                        Total de documentos no procesados:
                    </span>
                    <span style="
                        color: #dc2626;
                        font-size: 1.5rem;
                        font-weight: 700;
                    ">
                        ${totalDocumentos}
                    </span>
                </div>
            </div>

            <!-- Recomendaciones -->
            <div style="
                background: #dbeafe;
                padding: 1.25rem;
                border-radius: 10px;
                border-left: 3px solid #3b82f6;
            ">
                <div style="display: flex; align-items: center; gap: 0.75rem; margin-bottom: 0.75rem;">
                    <i class="fas fa-lightbulb" style="color: #2563eb; font-size: 1.25rem;"></i>
                    <h4 style="color: #1e3a8a; margin: 0; font-size: 1rem;">
                        ¿Qué hacer ahora?
                    </h4>
                </div>
                <ul style="
                    color: #1e40af;
                    font-size: 0.9rem;
                    margin: 0;
                    padding-left: 1.5rem;
                    line-height: 1.8;
                ">
                    <li>Revise el mensaje de error para identificar el problema específico</li>
                    <li>Verifique que los datos fiscales del receptor sean correctos</li>
                    ${codigoSAT === 'CFDI40159' ? '<li style="font-weight: 600; color: #dc2626;">Verifique que el Régimen Fiscal del receptor coincida con su tipo de RFC</li>' : ''}
                    <li>Corrija los datos necesarios e intente procesar nuevamente</li>
                    <li>Si el error persiste, contacte al administrador del sistema</li>
                </ul>
            </div>
        </div>
    `,
        icon: 'error',
        confirmButtonText: '<i class="fas fa-check"></i> Entendido',
        confirmButtonColor: '#7c3aed',
        width: '750px',
        showCancelButton: true,
        cancelButtonText: '<i class="fas fa-copy"></i> Copiar error',
        cancelButtonColor: '#6b7280',
        customClass: {
            popup: 'animated-popup'
        },
        allowOutsideClick: false
    }).then((modalResult) => {
        if (modalResult.dismiss === Swal.DismissReason.cancel) {
            // Copiar error al portapapeles
            const textoError = `
=== ERROR EN FACTURACIÓN ===
Paso: ${pasoInfo.nombre} (${result.step})
${codigoSAT ? `Código SAT: ${codigoSAT}` : ''}
${mensajeSAT ? `Mensaje: ${mensajeSAT}` : ''}

Detalles completos:
${detallesTecnicos}

Timestamp: ${new Date().toISOString()}
Documentos afectados: ${totalDocumentos}
            `.trim();

            navigator.clipboard.writeText(textoError).then(() => {
                Swal.fire({
                    icon: 'success',
                    title: 'Error copiado',
                    text: 'El detalle del error ha sido copiado al portapapeles',
                    timer: 2000,
                    showConfirmButton: false,
                    toast: true,
                    position: 'top-end'
                });
            }).catch(() => {
                Swal.fire({
                    icon: 'error',
                    title: 'Error al copiar',
                    text: 'No se pudo copiar al portapapeles',
                    timer: 2000,
                    showConfirmButton: false,
                    toast: true,
                    position: 'top-end'
                });
            });
        }
    });
}

// ============================================
// OBTENER DOCUMENTOS PENDIENTES
// ============================================
//async function obtenerDocumentosPendientes() {
//    try {
//        const response = await fetch('/PuntoDeVenta/ObtenerTodosDocumentosFacturables', {
//            method: 'POST',
//            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
//            body: '__RequestVerificationToken=' + document.querySelector('input[name="__RequestVerificationToken"]')?.value
//        });

//        if (!response.ok) {
//            throw new Error(`Error HTTP ${response.status}`);
//        }

//        const result = await response.json();

//        if (!result.success) {
//            Swal.fire({
//                icon: 'error',
//                title: 'Error al obtener documentos',
//                text: result.message || 'No se pudieron obtener los documentos'
//            });
//            return [];
//        }

//        return result.data ? result.data.filter(d => !d.invoiced) : [];

//    } catch (error) {
//        console.error('Error al obtener documentos:', error);
//        Swal.fire({
//            icon: 'error',
//            title: 'Error de conexión',
//            text: error.message
//        });
//        return [];
//    }
//}

// ============================================
// CERRAR MODAL DE PROGRESO
// ============================================
function cerrarModalProgreso(modal) {
    if (modal) {
        modal.style.animation = 'fadeOut 0.3s ease-in-out';
        setTimeout(() => {
            modal.remove();
        }, 300);
    }
}
// ============================================
// LOADER ESPECÍFICO PARA FACTURACIÓN INDIVIDUAL
// ============================================
function mostrarLoaderFacturacion() {
    const loaderHTML = `
        <div id="facturacionLoader" style="
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            background: rgba(0, 0, 0, 0.9);
            backdrop-filter: blur(8px);
            z-index: 99999;
            display: flex;
            justify-content: center;
            align-items: center;
            animation: fadeIn 0.3s ease-in-out;
        ">
            <div style="
                background: var(--bg-gray);
                border-radius: 20px;
                padding: 3rem 2.5rem;
                max-width: 450px;
                width: 90%;
                box-shadow: 0 25px 80px rgba(0, 0, 0, 0.5);
                animation: slideUp 0.4s ease-out;
                text-align: center;
            ">
                <!-- Spinner Animado -->
                <div style="
                    width: 100px;
                    height: 100px;
                    margin: 0 auto 2rem;
                    position: relative;
                ">
                    <!-- Círculo exterior rotando -->
                    <div style="
                        position: absolute;
                        width: 100%;
                        height: 100%;
                        border: 4px solid #e5e7eb;
                        border-top-color: #059669;
                        border-right-color: #7c3aed;
                        border-radius: 50%;
                        animation: spin 1s linear infinite;
                    "></div>

                    <!-- Círculo interior -->
                    <div style="
                        position: absolute;
                        width: 70%;
                        height: 70%;
                        top: 15%;
                        left: 15%;
                        border: 4px solid #e5e7eb;
                        border-bottom-color: #f59e0b;
                        border-left-color: #3b82f6;
                        border-radius: 50%;
                        animation: spinReverse 1.5s linear infinite;
                    "></div>

                    <!-- Icono central -->
                    <div style="
                        position: absolute;
                        width: 100%;
                        height: 100%;
                        display: flex;
                        align-items: center;
                        justify-content: center;
                    ">
                        <i class="fas fa-file-invoice-dollar" style="
                            font-size: 2.5rem;
                            color: #059669;
                            animation: pulse 1.5s ease-in-out infinite;
                        "></i>
                    </div>
                </div>

                <!-- Título -->
                <h3 style="
                    color: #1e293b;
                    margin: 0 0 1rem 0;
                    font-size: 1.5rem;
                    font-weight: 700;
                ">
                    Generando Factura
                </h3>

                <!-- Descripción -->
                <p style="
                    color: #64748b;
                    font-size: 0.95rem;
                    margin: 0 0 1.5rem 0;
                    line-height: 1.6;
                ">
                    Estamos procesando su factura electrónica.
                    <br>
                    <strong style="color: #059669;">Por favor espere...</strong>
                </p>

                <!-- Progress Steps -->
                <div style="
                    background: linear-gradient(135deg, var(--dark-blue) 0%, #f1f5f9 100%);
                    border-radius: 12px;
                    padding: 1.25rem;
                    margin-bottom: 1.5rem;
                ">
                    <div class="loader-step" style="
                        display: flex;
                        align-items: center;
                        gap: 0.75rem;
                        padding: 0.5rem 0;
                        animation: fadeInStep 0.5s ease-out 0.2s both;
                    ">
                        <div style="
                            width: 8px;
                            height: 8px;
                            background: #3b82f6;
                            border-radius: 50%;
                            animation: pulse 1.5s ease-in-out infinite;
                        "></div>
                        <span style="color: #475569; font-size: 0.9rem;">
                            Validando información fiscal
                        </span>
                    </div>

                    <div class="loader-step" style="
                        display: flex;
                        align-items: center;
                        gap: 0.75rem;
                        padding: 0.5rem 0;
                        animation: fadeInStep 0.5s ease-out 0.4s both;
                    ">
                        <div style="
                            width: 8px;
                            height: 8px;
                            background: #7c3aed;
                            border-radius: 50%;
                            animation: pulse 1.5s ease-in-out 0.3s infinite;
                        "></div>
                        <span style="color: #475569; font-size: 0.9rem;">
                            Generando comprobante fiscal
                        </span>
                    </div>

                    <div class="loader-step" style="
                        display: flex;
                        align-items: center;
                        gap: 0.75rem;
                        padding: 0.5rem 0;
                        animation: fadeInStep 0.5s ease-out 0.6s both;
                    ">
                        <div style="
                            width: 8px;
                            height: 8px;
                            background: #059669;
                            border-radius: 50%;
                            animation: pulse 1.5s ease-in-out 0.6s infinite;
                        "></div>
                        <span style="color: #475569; font-size: 0.9rem;">
                            Timbrado con el SAT
                        </span>
                    </div>
                </div>

                <!-- Información del documento -->
                <div style="
                    background: linear-gradient(135deg, #ede9fe 0%, #ddd6fe 100%);
                    border-radius: 10px;
                    padding: 1rem;
                    border-left: 4px solid #7c3aed;
                ">
                    <div style="
                        display: flex;
                        justify-content: space-between;
                        align-items: center;
                        font-size: 0.85rem;
                        color: #5b21b6;
                    ">
                        <span><strong>Folio:</strong></span>
                        <span style="font-weight: 700;">${selectedDocumentForInvoice.folio}</span>
                    </div>
                    <div style="
                        display: flex;
                        justify-content: space-between;
                        align-items: center;
                        font-size: 0.85rem;
                        color: #5b21b6;
                        margin-top: 0.5rem;
                    ">
                        <span><strong>Total:</strong></span>
                        <span style="font-weight: 700; font-size: 1.1rem;">
                            $${parseFloat(selectedDocumentForInvoice.imp).toFixed(2)}
                        </span>
                    </div>
                </div>

                <!-- Warning -->
                <p style="
                    margin-top: 1.5rem;
                    color: #f59e0b;
                    font-size: 0.85rem;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    gap: 0.5rem;
                ">
                    <i class="fas fa-exclamation-triangle"></i>
                    No cierre esta ventana
                </p>
            </div>
        </div>

        <style>
            @keyframes fadeIn {
                from { opacity: 0; }
                to { opacity: 1; }
            }

            @keyframes slideUp {
                from {
                    transform: translateY(30px);
                    opacity: 0;
                }
                to {
                    transform: translateY(0);
                    opacity: 1;
                }
            }

            @keyframes spin {
                from { transform: rotate(0deg); }
                to { transform: rotate(360deg); }
            }

            @keyframes spinReverse {
                from { transform: rotate(360deg); }
                to { transform: rotate(0deg); }
            }

            @keyframes pulse {
                0%, 100% {
                    opacity: 1;
                    transform: scale(1);
                }
                50% {
                    opacity: 0.5;
                    transform: scale(0.9);
                }
            }

            @keyframes fadeInStep {
                from {
                    opacity: 0;
                    transform: translateX(-10px);
                }
                to {
                    opacity: 1;
                    transform: translateX(0);
                }
            }
        </style>
    `;

    document.body.insertAdjacentHTML('beforeend', loaderHTML);
    return document.getElementById('facturacionLoader');
}

function cerrarLoaderFacturacion(loader) {
    if (loader) {
        loader.style.animation = 'fadeOut 0.3s ease-in-out';
        setTimeout(() => {
            loader.remove();
        }, 300);
    }
}

// ============================================
// MODAL PARA ENVÍO POR CORREO
// ============================================
function mostrarModalEnvioCorreo(detalles, resultOriginal, totalDocumentos) {
    Swal.fire({
        title: '📧 Enviar Factura por Correo',
        html: `
            <div style="text-align: left; padding: 1rem;">
                <!-- Información del documento -->
                <div style="
                    background: linear-gradient(135deg, #ede9fe 0%, #ddd6fe 100%);
                    padding: 1.25rem;
                    border-radius: 12px;
                    margin-bottom: 1.5rem;
                    border-left: 4px solid #7c3aed;
                ">
                    <div style="font-size: 1.1rem; font-weight: 700; color: #5b21b6; margin-bottom: 0.5rem;">
                        ${detalles.serie}-${detalles.folio}
                    </div>
                    <div style="color: #6b21a8; font-size: 0.9rem;">
                        <strong>Total:</strong> ${parseFloat(detalles.total).toLocaleString('es-MX', { style: 'currency', currency: 'MXN' })}
                    </div>
                </div>

                <!-- Campo de correo -->
                <div style="margin-bottom: 1.5rem;">
                    <label style="
                        display: block;
                        color: #374151;
                        font-weight: 600;
                        margin-bottom: 0.5rem;
                        font-size: 0.95rem;
                    ">
                        <i class="fas fa-envelope"></i> Correo electrónico *
                    </label>
                    <input
                        type="email"
                        id="emailDestino"
                        class="swal2-input"
                        placeholder="ejemplo@correo.com"
                        value="${detalles.emailCliente || ''}"
                        style="
                            width: 100%;
                            padding: 0.75rem;
                            border: 2px solid #e5e7eb;
                            border-radius: 8px;
                            font-size: 1rem;
                            margin: 0;
                        "
                    >
                </div>

                <!-- Opciones de archivos -->
                <div style="
                    background: #f8fafc;
                    padding: 1rem;
                    border-radius: 10px;
                    margin-bottom: 1rem;
                    border: 1px solid #e2e8f0;
                ">
                    <div style="color: #374151; font-weight: 600; margin-bottom: 0.75rem; font-size: 0.9rem;">
                        <i class="fas fa-paperclip"></i> Archivos adjuntos:
                    </div>
                    <label style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem; cursor: pointer;">
                        <input type="checkbox" id="incluirPDF" checked style="width: 18px; height: 18px;">
                        <span style="color: #6b7280; font-size: 0.9rem;">
                            <i class="fas fa-file-pdf" style="color: #dc2626;"></i> PDF
                        </span>
                    </label>
                    <label style="display: flex; align-items: center; gap: 0.5rem; cursor: pointer;">
                        <input type="checkbox" id="incluirXML" checked style="width: 18px; height: 18px;">
                        <span style="color: #6b7280; font-size: 0.9rem;">
                            <i class="fas fa-file-code" style="color: #059669;"></i> XML
                        </span>
                    </label>
                </div>

                <!-- Mensaje adicional -->
                <div style="margin-bottom: 1rem;">
                    <label style="
                        display: block;
                        color: #374151;
                        font-weight: 600;
                        margin-bottom: 0.5rem;
                        font-size: 0.95rem;
                    ">
                        <i class="fas fa-comment"></i> Mensaje adicional (opcional)
                    </label>
                    <textarea
                        id="mensajeAdicional"
                        rows="3"
                        placeholder="Agregue un mensaje personalizado..."
                        style="
                            width: 100%;
                            padding: 0.75rem;
                            border: 2px solid #e5e7eb;
                            border-radius: 8px;
                            font-size: 0.9rem;
                            resize: vertical;
                            font-family: inherit;
                        "
                    ></textarea>
                </div>
            </div>
        `,
        showCancelButton: true,
        showDenyButton: true,
        confirmButtonText: '<i class="fas fa-paper-plane"></i> Enviar',
        denyButtonText: '<i class="fas fa-arrow-left"></i> Volver',
        cancelButtonText: '<i class="fas fa-times"></i> Cancelar',
        confirmButtonColor: '#3b82f6',
        denyButtonColor: '#6b7280',
        cancelButtonColor: '#dc2626',
        width: '600px',
        focusConfirm: false,
        preConfirm: () => {
            const email = document.getElementById('emailDestino').value.trim();
            const incluirPDF = document.getElementById('incluirPDF').checked;
            const incluirXML = document.getElementById('incluirXML').checked;
            const mensaje = document.getElementById('mensajeAdicional').value.trim();

            if (!email) {
                Swal.showValidationMessage('Ingrese un correo electrónico');
                return false;
            }

            const emailRegex = /^[^\s@@]+@@[^\s@@]+\.[^\s@@]+$/;
            if (!emailRegex.test(email)) {
                Swal.showValidationMessage('Ingrese un correo válido');
                return false;
            }

            if (!incluirPDF && !incluirXML) {
                Swal.showValidationMessage('Seleccione al menos un archivo');
                return false;
            }

            return { email, incluirPDF, incluirXML, mensaje };
        }
    }).then((result) => {
        if (result.isConfirmed && result.value) {
            enviarFacturaPorCorreo(detalles, result.value, resultOriginal, totalDocumentos);
        } else if (result.isDenied) {
            // Volver al modal de resumen
            modalStack.pop();
            mostrarResumenExitoso(resultOriginal, totalDocumentos, true);
        } else if (result.isDismissed) {
            limpiarStackModales();
            setTimeout(() => {
                recargarVista();
            }, 500);
        }
    });
}

// ============================================
// ENVIAR FACTURA POR CORREO
// ============================================
async function enviarFacturaPorCorreo(detalles, opciones, resultOriginal, totalDocumentos) {
    const { email, incluirPDF, incluirXML, mensaje } = opciones;

    Swal.fire({
        title: 'Enviando correo...',
        html: `
            <div style="text-align: center; padding: 1rem;">
                <div style="
                    width: 60px;
                    height: 60px;
                    margin: 0 auto 1rem;
                    border: 4px solid #e5e7eb;
                    border-top-color: #3b82f6;
                    border-radius: 50%;
                    animation: spin 1s linear infinite;
                "></div>
                <p style="color: #6b7280;">Enviando a <strong>${email}</strong></p>
            </div>
        `,
        showConfirmButton: false,
        allowOutsideClick: false,
    });

    try {
        const formData = new FormData();
        formData.append('email', email);
        formData.append('serie', detalles.serie);
        formData.append('folio', detalles.folio);
        formData.append('facturaId', detalles.uuid);
        formData.append('pdfUrl', detalles.pdfUrl);
        formData.append('xmlUrl', detalles.xmlUrl);
        formData.append('incluirPDF', incluirPDF);
        formData.append('incluirXML', incluirXML);
        formData.append('mensaje', mensaje);
        formData.append('razonSocial', detalles.razonSocial);
        formData.append('total', detalles.total);
        formData.append('asunto', "Factura");

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        const response = await fetch('/FacturaConsulta/EnviarEmail', {
            method: 'POST',
            body: formData
        });

        if (!response.ok) throw new Error(`Error HTTP: ${response.status}`);

        const result = await response.json();

        if (result.success) {
            Swal.fire({
                icon: 'success',
                title: '✅ Correo enviado',
                html: `
                    <div style="text-align: center; padding: 1rem;">
                        <p style="color: #374151; margin-bottom: 0.5rem;">
                            Factura enviada exitosamente a:
                        </p>
                        <p style="color: #3b82f6; font-weight: 700; font-size: 1.1rem;">
                            ${email}
                        </p>
                    </div>
                `,
                showCancelButton: true,
                confirmButtonText: '<i class="fas fa-check"></i> Finalizar',
                cancelButtonText: '<i class="fas fa-arrow-left"></i> Volver al resumen',
                confirmButtonColor: '#059669',
                cancelButtonColor: '#6b7280'
            }).then((finalResult) => {
                if (finalResult.isDismissed || finalResult.isConfirmed) {
                    if (finalResult.isDismissed) {
                        // Volver al modal de resumen
                        modalStack.pop();
                        mostrarResumenExitoso(resultOriginal, totalDocumentos, true);
                    } else {
                        limpiarStackModales();
                        recargarVista();
                    }
                }
            });
        } else {
            throw new Error(result.message || 'Error al enviar');
        }

    } catch (error) {
        Swal.fire({
            icon: 'error',
            title: 'Error al enviar',
            text: error.message,
            showCancelButton: true,
            confirmButtonText: 'Reintentar',
            cancelButtonText: 'Volver',
            confirmButtonColor: '#3b82f6'
        }).then((retryResult) => {
            if (retryResult.isConfirmed) {
                mostrarModalEnvioCorreo(detalles, resultOriginal, totalDocumentos);
            } else {
                modalStack.pop();
                mostrarResumenExitoso(resultOriginal, totalDocumentos, true);
            }
        });
    }
}
// ==================== VALIDACIÓN DE APERTURA DE CAJA ====================
(function validarAperturaCaja() {
    const tieneApertura = viewBagApertura;

    if (!tieneApertura) {
        // Mostrar alerta inmediatamente
        Swal.fire({
            icon: 'error',
            title: '⚠️ Apertura de Caja Requerida',
            html: `
                <div style="text-align: center; padding: 1.5rem;">
                    <div style="
                        font-size: 4rem;
                        color: #dc2626;
                        margin-bottom: 1rem;
                        animation: shake 0.5s ease-in-out;
                    ">
                        <i class="fas fa-cash-register"></i>
                    </div>

                    <h3 style="
                        color: #991b1b;
                        margin-bottom: 1rem;
                        font-size: 1.3rem;
                        font-weight: 700;
                    ">
                        No hay apertura de caja registrada
                    </h3>

                    <p style="
                        color: #6b7280;
                        font-size: 1rem;
                        line-height: 1.6;
                        margin-bottom: 1.5rem;
                    ">
                        Debe realizar la <strong style="color: #dc2626;">apertura de caja</strong>
                        del día de hoy antes de poder registrar ventas.
                    </p>

                    <div style="
                        background: linear-gradient(135deg, #fef3c7 0%, #fde68a 100%);
                        padding: 1.25rem;
                        border-radius: 10px;
                        border-left: 4px solid #f59e0b;
                        text-align: left;
                        margin-top: 1rem;
                    ">
                        <div style="
                            display: flex;
                            align-items: center;
                            gap: 0.75rem;
                            margin-bottom: 0.75rem;
                        ">
                            <i class="fas fa-info-circle" style="color: #d97706; font-size: 1.2rem;"></i>
                            <strong style="color: #92400e; font-size: 0.95rem;">¿Qué hacer?</strong>
                        </div>
                        <ol style="
                            color: #78350f;
                            margin: 0;
                            padding-left: 1.5rem;
                            font-size: 0.9rem;
                            line-height: 1.8;
                        ">
                            <li>Vaya al módulo de <strong>Caja</strong></li>
                            <li>Registre la <strong>apertura del día</strong></li>
                            <li>Regrese a este módulo para continuar</li>
                        </ol>
                    </div>
                </div>

                <style>
                    @keyframes shake {
                        0%, 100% { transform: translateX(0); }
                        25% { transform: translateX(-10px); }
                        75% { transform: translateX(10px); }
                    }
                </style>
            `,
            confirmButtonText: '<i class="fas fa-arrow-left"></i> Ir a Apertura de Caja',
            confirmButtonColor: '#dc2626',
            allowOutsideClick: false,
            allowEscapeKey: false,
            showCloseButton: false,
            width: '650px',
            customClass: {
                popup: 'animated-popup'
            }
        }).then((result) => {
            if (result.isConfirmed) {
                // Redirigir al módulo de caja (ajusta la URL según tu aplicación)
                window.location.href = '/Caja/Apertura'; // Cambia esta URL
            }
        });

        // Bloquear TODA la interfaz
        bloquearInterfazCompleta();
    }
})();

function bloquearInterfazCompleta() {
    // Deshabilitar todos los inputs
    document.querySelectorAll('input, textarea, select').forEach(input => {
        input.disabled = true;
        input.style.cursor = 'not-allowed';
        input.style.opacity = '0.5';
    });

    // Deshabilitar todos los botones
    document.querySelectorAll('button, .btn').forEach(button => {
        button.disabled = true;
        button.style.cursor = 'not-allowed';
        button.style.opacity = '0.5';
        button.style.pointerEvents = 'none';
    });

    // Deshabilitar la tabla de artículos
    const itemsTable = document.getElementById('itemsTable');
    if (itemsTable) {
        itemsTable.style.pointerEvents = 'none';
        itemsTable.style.opacity = '0.5';
    }

    // Deshabilitar el FAB menu
    const fabHub = document.querySelector('.fab-hub');
    if (fabHub) {
        fabHub.style.display = 'none';
    }

    // Agregar overlay bloqueante sobre todo el contenido
    const overlay = document.createElement('div');
    overlay.id = 'bloqueo-apertura-overlay';
    overlay.style.cssText = `
        position: fixed;
        top: 0;
        left: 0;
        width: 100%;
        height: 100%;
        background: rgba(0, 0, 0, 0.3);
        backdrop-filter: blur(3px);
        z-index: 998;
        cursor: not-allowed;
        display: flex;
        justify-content: center;
        align-items: center;
    `;

    overlay.innerHTML = `
        <div style="
            background: white;
            padding: 2rem;
            border-radius: 16px;
            box-shadow: 0 10px 40px rgba(0,0,0,0.3);
            text-align: center;
            max-width: 400px;
            animation: pulse 2s ease-in-out infinite;
        ">
            <i class="fas fa-lock" style="
                font-size: 3rem;
                color: #dc2626;
                margin-bottom: 1rem;
                display: block;
            "></i>
            <h3 style="color: #991b1b; margin-bottom: 0.5rem;">
                Módulo Bloqueado
            </h3>
            <p style="color: #6b7280; font-size: 0.95rem;">
                Realice la apertura de caja para continuar
            </p>
        </div>

        <style>
            @keyframes pulse {
                0%, 100% { transform: scale(1); }
                50% { transform: scale(1.05); }
            }
        </style>
    `;

    document.body.appendChild(overlay);

    // Mensaje en consola
    console.error('%c⚠️ PUNTO DE VENTA BLOQUEADO', 'color: #dc2626; font-size: 20px; font-weight: bold;');
    console.error('Razón: No hay apertura de caja registrada para el día de hoy.');

    // Mostrar toast de recordatorio cada 30 segundos
    let toastInterval = setInterval(() => {
        toastMixin.fire({
            icon: 'warning',
            title: 'Apertura de caja pendiente',
            timer: 3000
        });
    }, 30000); // Cada 30 segundos

    // Guardar el intervalo para limpiarlo si se desbloquea
    window.toastAperturaInterval = toastInterval;
}

// Función para desbloquear (por si se implementa recarga automática)
function desbloquearInterfaz() {
    // Habilitar todos los inputs
    document.querySelectorAll('input, textarea, select').forEach(input => {
        input.disabled = false;
        input.style.cursor = '';
        input.style.opacity = '';
    });

    // Habilitar todos los botones
    document.querySelectorAll('button, .btn').forEach(button => {
        button.disabled = false;
        button.style.cursor = '';
        button.style.opacity = '';
        button.style.pointerEvents = '';
    });

    // Habilitar la tabla
    const itemsTable = document.getElementById('itemsTable');
    if (itemsTable) {
        itemsTable.style.pointerEvents = '';
        itemsTable.style.opacity = '';
    }

    // Mostrar el FAB menu
    const fabHub = document.querySelector('.fab-hub');
    if (fabHub) {
        fabHub.style.display = '';
    }

    // Remover overlay
    const overlay = document.getElementById('bloqueo-apertura-overlay');
    if (overlay) {
        overlay.remove();
    }

    // Limpiar intervalo de toast
    if (window.toastAperturaInterval) {
        clearInterval(window.toastAperturaInterval);
    }

    toastMixin.fire({
        icon: 'success',
        title: 'Interfaz desbloqueada correctamente'
    });
}




function startFacturaQRScanner() {
    // Reusar el mismo modal QR pero con callback diferente
    const modal = document.getElementById('qrModal');
    const video = document.getElementById('qr-video');

    modal.style.zIndex = '99999';

    if (typeof jsQR === 'undefined') {
        toastMixin.fire({ icon: 'error', title: 'Librería QR no cargada. Recargue la página.' });
        return;
    }

    navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'environment', width: { ideal: 1280 }, height: { ideal: 720 } }
    }).then(stream => {
        facturaVideoStream = stream;
        video.srcObject = stream;
        video.play();
        modal.classList.add('active');
        scanFacturaQRCode();
        toastMixin.fire({ icon: 'info', title: 'Escanee el QR del documento' });
    }).catch(error => {
        let msg = 'No se pudo acceder a la cámara.';
        if (error.name === 'NotAllowedError') msg = 'Permiso de cámara denegado.';
        else if (error.name === 'NotFoundError') msg = 'No se encontró cámara.';
        toastMixin.fire({ icon: 'error', title: msg });
    });
}

function stopFacturaQRScanner() {
    const modal = document.getElementById('qrModal');
    const video = document.getElementById('qr-video');

    modal.style.zIndex = '1000';

    if (facturaVideoStream) {
        facturaVideoStream.getTracks().forEach(t => t.stop());
        facturaVideoStream = null;
    }
    video.pause();
    video.srcObject = null;
    modal.classList.remove('active');

    if (facturaQRScanner) {
        cancelAnimationFrame(facturaQRScanner);
        facturaQRScanner = null;
    }
}

function scanFacturaQRCode() {
    const video = document.getElementById('qr-video');
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d');

    function tick() {
        if (video.readyState === video.HAVE_ENOUGH_DATA) {
            canvas.height = video.videoHeight;
            canvas.width = video.videoWidth;
            context.drawImage(video, 0, 0, canvas.width, canvas.height);
            const imageData = context.getImageData(0, 0, canvas.width, canvas.height);

            try {
                const code = jsQR(imageData.data, imageData.width, imageData.height, {
                    inversionAttempts: "dontInvert"
                });
                if (code && code.data) {
                    handleFacturaQRDetected(code.data);
                    return;
                }
            } catch (e) {
                console.error('Error QR:', e);
            }
        }
        facturaQRScanner = requestAnimationFrame(tick);
    }
    tick();
}

async function handleFacturaQRDetected(qrData) {
    stopFacturaQRScanner();

    const folio = qrData.trim();
    toastMixin.fire({ icon: 'info', title: `QR detectado: ${folio}` });

    // Poner el folio en el buscador del modal
    document.getElementById('searchFacturaFolio').value = folio;

    // Mostrar loading en el grid
    const loadingDiv = document.getElementById('loadingDocuments');
    const gridDiv = document.getElementById('documentsGrid');
    const noDocsDiv = document.getElementById('noDocumentsMessage');

    loadingDiv.style.display = 'block';
    gridDiv.innerHTML = '';
    noDocsDiv.style.display = 'none';

    try {
        const response = await fetch(
            `/PuntoDeVenta/BuscarDocumentoFacturablePorFolio?folio=${encodeURIComponent(folio)}`,
            { method: 'GET' }
        );

        if (!response.ok) throw new Error(`Error HTTP: ${response.status}`);

        const result = await response.json();
        loadingDiv.style.display = 'none';

        if (result.success && result.data) {
            // Agregar al array global y mostrar como card
            const doc = result.data;
            window.allInvoicableDocuments = [doc];
            displayDocumentCards([doc]);
            updateDocumentCount(1, 1);

            toastMixin.fire({
                icon: 'success',
                title: `Documento ${doc.folio} encontrado`
            });
        } else {
            noDocsDiv.innerHTML = `
                <i class="fas fa-search" style="font-size:3rem;color:#d1d5db;display:block;margin-bottom:1rem;"></i>
                <h3>No se encontró resultado</h3>
                <p>${result.message || 'No hay documentos facturables para ese folio'}</p>
                <button class="btn btn-outline" onclick="loadAllInvoicableDocuments()" style="margin-top:1rem;">
                    <i class="fas fa-list"></i> Ver todos los documentos
                </button>
            `;
            noDocsDiv.style.display = 'block';
        }

    } catch (error) {
        loadingDiv.style.display = 'none';
        toastMixin.fire({ icon: 'error', title: `Error: ${error.message}` });
    }
}
window.RecepcionModule = {
    init() {
        var html5QrcodeScanner;
        var currentDocument = null;
        var productStates = {};
        var documentsData;

        // Initialize Tom Select
        var tomS = new TomSelect("#documentSearch", {
            create: false,
            sortField: {
                field: "text",
                direction: "asc"
            },
            render: {
                option: function (data, escape) {
                    return `<div style="color: var(--text-dark);">${escape(data.text)}</div>`;
                }
            }
        });

        // Load document and its products
        function loadDocument(documentId) {
            if (documentId == 0) {
                toastMixin.fire({
                    icon: "warning",
                    title: "Selecciona un folio para poder filtrar."
                })

                return;
            }
            GetData({
                path: "/Inventario/GetOrdenesCompra",
                data: {
                    uuid: documentId,
                }
            }).then((docData) => {
                if (docData.ordenCompra.length === 0) {
                    console.error("Document not found");
                    toastMixin.fire({
                        icon: "error",
                        title: "Documento no encontrado",
                    })
                    return;
                }

                documentsData = docData;
                document.getElementById("documentTitle").textContent = `${docData.ordenCompra.folio}`;

                // Generate products list
                const productsList = document.getElementById("productsList");
                productsList.innerHTML = "";

                productStates = {};
                docData.partidas.forEach((product, index) => {
                    const productElement = createProductElement(product, index);
                    productsList.appendChild(productElement);
                    productStates[product.id_partidas] = { checked: false, hasError: false };
                });

                // Show document card
                document.getElementById("documentCard").classList.add("show");

                // Update statistics
                updateStatistics();

                // Scroll to document card
                document.getElementById("documentCard").scrollIntoView({
                    behavior: "smooth",
                    block: "start"
                });
            })
        }

        // Create product element
        function createProductElement(product, index) {
            const div = document.createElement("div");
            div.className = "product-item";
            div.innerHTML = `
                <div class="row align-items-center">
                    <div class="col-md-1">
                        <input class="btn-check product-checkbox" type="checkbox" id="product_${product.id_partidas}" data-product-id="${product.id_partidas}">
                        <label class="btn btn-outline-success" for="product_${product.id_partidas}" title="Marcar como correcto"><i class="fa-solid fa-badge-check"></i></label>
                    </div>
                    <div class="col-md-5 col-sm-12">
                        <h6 class="mb-1">${product.descripcion}</h6>
                        <small class="text-muted">Codigo: ${product.codigo}</small>
                    </div>
                    <div class="col-md-2 col-sm-12">
                        <span class="badge bg-secondary">Cant: ${product.cantidad}</span>
                    </div>
                    <div class="col-md-2 col-sm-12">
                        <strong>$${product.total.toFixed(2)}</strong>
                    </div>
                    <div class="col-md-2 text-end">
                        <button class="btn btn-outline-danger mark-error-btn"
                                data-product-id="${product.id_partidas}" title="Marcar como discrepancia">
                            <i class="fas fa-exclamation-triangle"></i>
                        </button>
                    </div>
                </div>
            `;

            // Add event listeners
            const checkbox = div.querySelector(".product-checkbox");
            const errorBtn = div.querySelector(".mark-error-btn");

            checkbox.addEventListener("change", (e) => {
                handleProductCheck(product.id_partidas, e.target.checked, div);
            });

            errorBtn.addEventListener("click", (e) => {
                handleProductError(product.id_partidas, div);
            });

            return div;
        }

        // Handle product checkbox change
        function handleProductCheck(productId, isChecked, element) {
            productStates[productId].checked = isChecked;
            productStates[productId].hasError = false;

            const product = documentsData.partidas.find(p => p.id_partidas === productId);
            productStates[productId].quantity = product.cantidad;

            updateProductElementState(element, isChecked, false);
            updateStatistics();
        }

        // Handle product error marking
        function handleProductError(productId, element) {
            const hasError = !productStates[productId].hasError;
            productStates[productId].hasError = hasError;
            productStates[productId].checked = false;

            // Uncheck checkbox
            const checkbox = element.querySelector(".product-checkbox");
            checkbox.checked = false;

            const product = documentsData.partidas.find(p => p.id_partidas === productId);

            if (!product) return;

            if (hasError) {
                // Ocultamos el original visualmente (para no confundir)
                element.style.display = "none";

                // Crear contenedor de discrepancias
                const wrapper = document.createElement("div");
                wrapper.className = "product-discrepancy-wrapper product-item";

                // Partida "correcta"
                const correctDiv = createEditableRow(product, "Cantidad correcta", product.cantidad);
                // Partida "discrepancia"
                const errorDiv = createEditableRow(product, "Cantidad en discrepancia", product.cantidad);

                wrapper.appendChild(correctDiv);
                wrapper.appendChild(errorDiv);

                // Insertar después del original
                element.insertAdjacentElement("afterend", wrapper);

                // Guardar en estado
                productStates[productId].discrepancyWrapper = wrapper;
                productStates[productId].cantidadCorrecta = product.cantidad;
                productStates[productId].cantidadError = product.cantidad;

                // Listeners para inputs
                correctDiv.querySelector("input").addEventListener("input", e => {
                    productStates[productId].cantidadCorrecta = parseFloat(e.target.value) || 0;
                });

                errorDiv.querySelector("input").addEventListener("input", e => {
                    productStates[productId].cantidadError = parseFloat(e.target.value) || 0;
                });

                const select = correctDiv.querySelector(".discrepancy-reason");
                select.addEventListener("change", e => {
                    productStates[productId].motivoCorrecta = e.target.value;
                });

                const selectError = errorDiv.querySelector(".discrepancy-reason");
                selectError.addEventListener("change", e => {
                    productStates[productId].motivoError = e.target.value;
                });

                // Botón para quitar discrepancia
                const removeBtn = document.createElement("button");
                removeBtn.className = "btn btn-sm orange-500 mt-2 remove_discrepancy";
                removeBtn.textContent = "Quitar discrepancia";
                removeBtn.addEventListener("click", () => {
                    wrapper.remove();
                    element.style.display = ""; // volvemos a mostrar original
                    element.classList.remove("error");
                    productStates[productId].hasError = false;
                    productStates[productId].cantidadCorrecta = null;
                    productStates[productId].cantidadError = null;
                    updateStatistics();
                });

                wrapper.appendChild(removeBtn);

            } else {
                // Si desmarcamos error, quitamos duplicados y mostramos el original
                if (productStates[productId].discrepancyWrapper) {
                    productStates[productId].discrepancyWrapper.remove();
                    productStates[productId].discrepancyWrapper = null;
                }
                element.style.display = "";
            }

            updateProductElementState(element, false, hasError);
            updateStatistics();
        }

        // 🔧 Función auxiliar para crear un renglón editable
        function createEditableRow(product, label, cantidadInicial) {
            const row = document.createElement("div");
            row.className = "row align-items-center discrepancia";
            row.innerHTML = `
        <div class="col-md-5 mb-1">
            <h6 class="mb-1">${product.descripcion}</h6>
            <small class="text-muted">Código: ${product.codigo}</small>
            <span class="badge bg-danger ms-2">${label}</span>
        </div>
        <div class="col-md-2 mb-1">
            <input type="number" class="form-control form-control-sm lock-min"
                   value="${cantidadInicial}" min="0" />
        </div>
        <div class="col-md-2 mb-1">
            <strong>$${product.total.toFixed(2)}</strong>
        </div>
        <div class="col-md-3 mb-1">
            <select class="form-select form-select-sm discrepancy-reason">
                <option value="">Seleccione motivo</option>
                <option value="Material dañado">Material dañado</option>
                <option value="Material diferente">Material diferente</option>
                <option value="Material faltante">Material faltante</option>
            </select>
        </div>
    `;
            return row;
        }

        // Update product element visual state
        function updateProductElementState(element, isChecked, hasError) {
            element.classList.remove("checked", "error");

            if (hasError) {
                element.classList.add("error");
            } else if (isChecked) {
                element.classList.add("checked");
            }
        }

        // Update statistics
        function updateStatistics() {
            const total = documentsData.partidas.length;
            let checked = 0;
            let errors = 0;

            Object.values(productStates).forEach(state => {
                if (state.hasError) errors++;
                else if (state.checked) checked++;
            });

            const pending = total - checked - errors;

            document.getElementById("totalProducts").textContent = total;
            document.getElementById("checkedProducts").textContent = checked;
            document.getElementById("uncheckedProducts").textContent = pending;
            document.getElementById("errorProducts").textContent = errors;
        }

        // QR Scanner functionality
        function initializeQRScanner() {
            const qrScanBtn = document.getElementById("qrScanBtn");
            const qrModal = new bootstrap.Modal(document.getElementById("qrModal"));

            qrScanBtn.addEventListener("click", () => {
                qrModal.show();
                startQRScanner();
            });

            // Stop scanner when modal is hidden
            document.getElementById("qrModal").addEventListener("hidden.bs.modal", () => {
                stopQRScanner();
            });
        }

        function startQRScanner() {
            html5QrcodeScanner = new Html5Qrcode("qrReader");

            html5QrcodeScanner.start(
                { facingMode: "environment" },
                {
                    fps: 10,
                    qrbox: { width: 300, height: 300 }
                },
                (decodedText, decodedResult) => {
                    // Handle successful scan
                    console.log(`QR Code detected: ${decodedText}`);

                    // Try to find document by QR code
                    const documentKey = Object.keys(documentsData).find(key =>
                        decodedText.includes(key) || key === decodedText
                    );

                    if (documentKey) {
                        // Set the document in Tom Select
                        tomS.setValue(documentKey);
                        loadDocument(documentKey);

                        // Close modal
                        bootstrap.Modal.getInstance(document.getElementById("qrModal")).hide();

                        // Show success message
                        toastMixin.fire({
                            icon: "success",
                            title: "Documento encontrado y cargado exitosamente",
                        });
                    } else {
                        toastMixin.fire({ title: "No se encontró un documento válido con este código QR", icon: "warning" });
                    }
                },
                (errorMessage) => {
                    // Handle scan error (can be ignored)
                }
            );
        }

        function stopQRScanner() {
            if (html5QrcodeScanner) {
                html5QrcodeScanner.stop().then(() => {
                    html5QrcodeScanner.clear();
                }).catch(err => {
                    console.log("Error stopping QR scanner:", err);
                });
            }
        }

        // Event listeners
        document.addEventListener("DOMContentLoaded", () => {
            initializeQRScanner();

            // Search button
            document.getElementById("searchBtn").addEventListener("click", () => {
                const selectedDoc = tomS.getValue();
                if (selectedDoc !== "0") {
                    loadDocument(selectedDoc);
                } else {
                    toastMixin.fire({ title: "Por favor selecciona un documento", icon: "warning" });
                }
            });

            // Check all button
            document.getElementById("checkAllBtn").addEventListener("click", () => {
                const selectedDoc = tomS.getValue();
                if (selectedDoc === "0") {
                    toastMixin.fire({ title: "Por favor selecciona un documento", icon: "warning" });
                    return;
                }

                Object.keys(productStates).forEach(productId => {
                    if (!productStates[productId].hasError) {   // 👈 se ignoran discrepancias
                        const product = documentsData.partidas.find(p => p.id_partidas === parseInt(productId));
                        productStates[productId].quantity = product.cantidad;
                        productStates[productId].checked = true;

                        const checkbox = document.getElementById(`product_${productId}`);
                        const productElement = checkbox.closest(".product-item");
                        checkbox.checked = true;
                        updateProductElementState(productElement, true, false);
                    }
                });

                updateStatistics();
                toastMixin.fire({ title: "Todos los productos han sido verificados (excepto con discrepancias)", icon: "info" });
            });

            // Uncheck all button
            document.getElementById("uncheckAllBtn").addEventListener("click", () => {
                const selectedDoc = tomS.getValue();
                if (selectedDoc === "0") {
                    toastMixin.fire({ title: "Por favor selecciona un documento", icon: "warning" });
                    return;
                }

                Object.keys(productStates).forEach(productId => {
                    productStates[productId].checked = false;
                    productStates[productId].hasError = false;

                    const checkbox = document.getElementById(`product_${productId}`);
                    const productElement = checkbox.closest(".product-item");
                    checkbox.checked = false;

                    $(".remove_discrepancy").click()

                    updateProductElementState(productElement, false, false);
                });

                updateStatistics();
                toastMixin.fire({ title: "Se han desmarcado todos los productos", icon: "info" });
            });

            // Save changes button
            document.getElementById("saveChangesBtn").addEventListener("click", () => {
                const selectedDoc = tomS.getValue();
                if (selectedDoc === "0") {
                    toastMixin.fire({ title: "Por favor selecciona un documento", icon: "warning" });
                    return;
                }

                const checkedCount = Object.values(productStates).filter(s => s.checked).length;
                const errorCount = Object.values(productStates).filter(s => s.hasError).length;

                if (checkedCount == 0 && errorCount == 0) {
                    toastMixin.fire({
                        icon: "warning",
                        title: "Debes seleccionar al menos un elemento para guardar."
                    })
                    return;
                }

                if (documentsData.partidas.length > (checkedCount + errorCount)) {
                    toastMixin.fire({
                        icon: "warning",
                        title: "Hay campos sin seleccionar, revisa antes de continuar."
                    })
                    return;
                }

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
                    title: "Seguro?",
                    html: `¿Está seguro que desea proceder con la recepcion del material con el folio ${documentsData.ordenCompra.folio}? <br>` +
                        `Correctos: ${checkedCount} -- Discrepancias: ${errorCount}`,
                    footer: "<b style='color: red;'>ADVERTENCIA - Este cambio no se puede revertir</b>",
                    customClass: {
                        icon: "p-0 bg-transparent border-0 shadow-none"
                    },
                    preConfirm: () => {
                        let payload = [];

                        Object.entries(productStates).forEach(([productId, state]) => {
                            if (state.hasError) {
                                payload.push({
                                    productId,
                                    status: "ok",
                                    quantity: state.cantidadCorrecta,
                                    motivo: null
                                });

                                payload.push({
                                    productId,
                                    status: "error",
                                    quantity: state.cantidadError ?? 0, // cantidad en discrepancia
                                    motivo: state.motivoError || "Sin motivo"
                                });
                            } else if (state.checked) {
                                payload.push({
                                    productId,
                                    status: "ok",
                                    quantity: state.quantity,
                                    motivo: null
                                });
                            }
                        });

                        GetData({
                            path: "/Inventario/RecibirMaterial",
                            data: {
                                productos: JSON.stringify(payload)
                            },
                            swalResponse: true,
                            swalPath: "/Inventario/Recepcion"
                        })
                    }
                })

            });


            // Reset button
            document.getElementById("resetBtn").addEventListener("click", () => {
                const selectedDoc = tomS.getValue();
                if (selectedDoc === "0") {
                    toastMixin.fire({ title: "Por favor selecciona un documento", icon: "warning" });
                    return;
                }

                _Swal.fire({
                    title: "¿Estás seguro de que quieres resetear todos los cambios?",
                    preConfirm: () => {
                        Object.keys(productStates).forEach(productId => {
                            productStates[productId].checked = false;
                            productStates[productId].hasError = false;

                            const checkbox = document.getElementById(`product_${productId}`);
                            const productElement = checkbox.closest(".product-item");
                            checkbox.checked = false;
                            updateProductElementState(productElement, false, false);
                        });
                        $(".remove_discrepancy").click()
                        updateStatistics();
                        toastMixin.fire({ title: "Se han reseteado todos los cambios", icon: "info" });
                    }
                })
            });

            // Tom Select change event
            tomS.on("change", (value) => {
                if (value) {
                    loadDocument(value);
                }
            });
        });

        document.addEventListener("keydown", (e) => {
            if (e.ctrlKey || e.metaKey) {
                switch (e.key.toLowerCase()) {
                    case "a": // Ctrl/Cmd + A → check all
                        e.preventDefault();
                        e.stopPropagation();
                        document.getElementById("checkAllBtn")?.click();
                        break;

                    case "r": // Ctrl/Cmd + R → reset
                        e.preventDefault();
                        e.stopPropagation();
                        document.getElementById("resetBtn")?.click();
                        break;

                    case "s": // Ctrl/Cmd + S → save
                        e.preventDefault();
                        e.stopPropagation();
                        document.getElementById("saveChangesBtn")?.click();
                        break;
                }
            }
        });


        // Add some sample QR codes mapping for demonstration
        var qrCodeMappings = {
            "DOC-001": "QR_CODE_DOC_001",
            "DOC-002": "QR_CODE_DOC_002",
            "DOC-003": "QR_CODE_DOC_003",
            "DOC-004": "QR_CODE_DOC_004",
            "DOC-005": "QR_CODE_DOC_005",
            "DOC-006": "QR_CODE_DOC_006",
            "DOC-007": "QR_CODE_DOC_007",
            "DOC-008": "QR_CODE_DOC_008"
        };

        // Enhanced QR detection to handle various QR formats
        function findDocumentByQR(qrText) {
            // Direct match
            if (documentsData[qrText]) {
                return qrText;
            }

            // Search in QR mappings
            const docKey = Object.keys(qrCodeMappings).find(key =>
                qrCodeMappings[key] === qrText
            );
            if (docKey) {
                return docKey;
            }

            // Partial match in document titles or content
            const foundKey = Object.keys(documentsData).find(key => {
                const doc = documentsData[key];
                return doc.title.toLowerCase().includes(qrText.toLowerCase()) ||
                    qrText.toLowerCase().includes(key.toLowerCase());
            });

            return foundKey || null;
        }

        // Add loading states
        function showLoading(element, text = "Cargando...") {
            const spinner = document.createElement("span");
            spinner.className = "spinner-border spinner-border-sm me-2";
            spinner.setAttribute("role", "status");

            const originalContent = element.innerHTML;
            element.innerHTML = "";
            element.appendChild(spinner);
            element.insertAdjacentText("beforeend", text);
            element.disabled = true;

            return () => {
                element.innerHTML = originalContent;
                element.disabled = false;
            };
        }
    },
    destroy() { }
}
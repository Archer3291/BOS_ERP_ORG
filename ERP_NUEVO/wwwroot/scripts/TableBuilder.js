function createTable(options) {
    const {
        selector,
        path,
        columns,
        pageSizeOptions = [10, 25, 50, 100],
        defaultPageSize = 10,
        searchPlaceholder = 'Buscar...',
        actions = [],
        RowsEvents = {},
        GlobalEvents = {},
        searchable = true,
        data = {},
        exports = [],
        selectable = { enabled: false },
        rowKey = 'id',
        childRows = null,
    } = options;

    if (!options.hasOwnProperty('path')) {
        _Swal.fire({ icon: 'error', title: `Propiedad faltante: 'path'`, text: `Debes especificar la propiedad 'path'.` });
        console.error(`Falta la propiedad 'path'.`);
        return;
    }
    if (!options.hasOwnProperty('selector')) {
        _Swal.fire({ icon: 'error', title: `Propiedad faltante: 'selector'`, text: `La propiedad 'selector' es obligatoria.` });
        console.error(`Falta la propiedad 'selector'.`);
        return;
    }

    const container = document.querySelector(selector);
    if (!container) {
        _Swal.fire({ icon: 'error', title: `Contenedor no encontrado`, text: `No se encontró el selector '${selector}'.` });
        console.error(`No se encontró el contenedor con el selector: ${selector}`);
        return;
    }

    let currentPage = 1,
        totalRecords = 0,
        pageSizeValue = defaultPageSize;

    let selectedRows = new Map();
    let currentRows = [];
    let sortColumn = null;
    let sortDir = options.sortDir ?? 'asc';

    // Set de filas hijo actualmente expandidas (por id de fila padre)
    const expandedRows = new Set();

    const tableId = selector.replace('#', '');

    // ── Resolución de la llave de identidad de cada fila ────────────────────
    // rowKey puede ser:
    //   - un string: nombre de la propiedad a usar como id (comportamiento original)
    //   - una función: (row) => string, para construir una llave compuesta
    //     cuando un solo campo no identifica de forma única a la fila
    //     (ej: (row) => `${row.codigo}|${row.pedimento}`)
    const getRowKey = typeof rowKey === 'function' ? rowKey : (row) => row[rowKey];

    // Número de columnas totales (para colspan de la fila hija)
    const totalCols = () =>
        columns.length +
        (actions.length ? 1 : 0) +
        (selectable.enabled ? 1 : 0) +
        (childRows ? 1 : 0); // columna del toggle

    container.innerHTML = `
        <div class="custom-table-container mt-3">
            <div class="d-flex justify-content-between align-items-center flex-wrap mb-3">
                <div class="d-flex align-items-center gap-2">
                    <select id="${tableId}-pageSize" class="page-size-selector form-control">
                        ${pageSizeOptions.map(size => `<option value="${size}" ${size === defaultPageSize ? 'selected' : ''}>${size} por página</option>`).join('')}
                    </select>
                </div>
                <span id="${tableId}-selected" class="text-sm text-muted"></span>
                ${renderExportButtons(exports, tableId)}
                ${searchable ? `
                <div class="input-group" style="max-width: 300px;">
                    <input type="text" id="${tableId}-search" class="form-control search-input" placeholder="${searchPlaceholder}" />
                    <button class="btn btn-outline-secondary clear-search" id="${tableId}-clear"><i class="fa-solid fa-eraser"></i></button>
                </div>` : ''}
            </div>

            <div class="table-responsive">
                <table class="modern-table w-100">
                    <thead>
                        <tr>
                            ${childRows ? `<th style="width:36px"></th>` : ''}
                            ${selectable.enabled ? `<th><input type="checkbox" id="${tableId}-select-all" class="form-check-input" title="Seleccionar todos" /></th>` : ''}
                            ${columns.map(c => `
                                <th class="${c.className ?? ''} ${c.sortable !== false ? 'ct-sortable' : ''}"
                                    data-col="${c.data ?? ''}"
                                    style="${c.sortable !== false ? 'cursor:pointer;user-select:none;white-space:nowrap' : ''}">
                                        ${c.title}
                                        ${c.sortable !== false ? `<span class="ct-sort-icon" data-col="${c.data ?? ''}">⇅</span>` : ''}
                                </th>`).join('')}
                            ${actions.length ? '<th>Acciones</th>' : ''}
                        </tr>
                    </thead>
                    <tbody id="${tableId}-tbody"></tbody>
                </table>
            </div>

            <div class="custom-pagination mt-3 d-flex justify-content-between align-items-center flex-wrap">
                <div class="pagination-info">
                    Mostrando <strong id="${tableId}-from">0</strong> a <strong id="${tableId}-to">0</strong> de <strong id="${tableId}-total">0</strong> registros
                </div>
                <div id="${tableId}-pagination" class="d-flex flex-wrap gap-1 pagination-controls"></div>
            </div>
        </div>`;

    // ── Referencias ──────────────────────────────────────────────────────────
    const tableBody = container.querySelector(`#${tableId}-tbody`);
    const searchInput = container.querySelector(`#${tableId}-search`);
    const clearBtn = container.querySelector(`#${tableId}-clear`);
    const pageSizeSelect = container.querySelector(`#${tableId}-pageSize`);
    const paginationInfo = container.querySelector('.pagination-info');
    const paginationControls = container.querySelector(`#${tableId}-pagination`);
    const tableResponsive = container.querySelector('.table-responsive');

    // ── Asignar onclick a botones de export ────────────────────────────────
    // bindExportButtons está declarada más abajo pero en el mismo closure,
    // el setTimeout(0) asegura que se llame tras la declaración completa.
    setTimeout(() => bindExportButtons(), 0);

    if (searchable && searchInput) {
        searchInput.addEventListener('input', () => fetchData(searchInput.value, 1));
        clearBtn.addEventListener('click', () => { searchInput.value = ''; fetchData('', 1); });
    }

    pageSizeSelect.addEventListener('change', () => {
        pageSizeValue = parseInt(pageSizeSelect.value);
        fetchData(searchInput?.value ?? '', 1);
    });

    if (selectable.enabled) {
        const selectAllCheckbox = container.querySelector(`#${tableId}-select-all`);
        if (selectAllCheckbox) {
            selectAllCheckbox.addEventListener('change', () => {
                const shouldSelect = selectAllCheckbox.checked;
                currentRows.forEach(row => {
                    const id = getRowKey(row);
                    const canSelect = selectable.condition ? selectable.condition(row) : true;
                    if (!canSelect) return;
                    if (shouldSelect) selectedRows.set(id, row);
                    else selectedRows.delete(id);
                });
                renderRows(currentRows);
                updateSelectedLabel();
                if (selectable.onChange) selectable.onChange(Array.from(selectedRows.values()));
            });
        }
    }

    container.querySelectorAll('th.ct-sortable').forEach(th => {
        th.addEventListener('click', () => {
            const col = th.dataset.col;
            if (!col) return;
            if (sortColumn === col) {
                sortDir = sortDir === 'asc' ? 'desc' : 'asc';
            } else {
                sortColumn = col;
                sortDir = 'asc';
            }
            fetchData(searchInput?.value ?? '', 1);
        });
    });

    const urlParams = new URLSearchParams(window.location.search);
    const queryValue = urlParams.get('q') || urlParams.get('buscar') || urlParams.get('search') || urlParams.get('folio') || '';
    if (queryValue && searchInput) { searchInput.value = queryValue; fetchData(queryValue, 1); }
    else { fetchData(''); }

    // =========================================================================
    // FETCH PRINCIPAL
    // =========================================================================
    function fetchData(search, page = 1) {
        currentPage = page;
        if (GlobalEvents.onLoading) GlobalEvents.onLoading(true);

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const formData = new FormData();
        formData.append('__RequestVerificationToken', token || '');
        formData.append('page', String(page).trim());
        formData.append('pageSize', String(pageSizeValue).trim());
        formData.append('nombre', (search || '').toString().trim());
        //if (sortColumn) {
        formData.append('sortColumn', sortColumn);
        formData.append('sortDir', sortDir);
        //}

        const extraData = typeof data === 'function' ? data() : data;
        for (const [key, value] of Object.entries(extraData)) {
            if (value !== undefined && value !== null) formData.append(key, value);
        }

        fetch(path, { method: 'POST', body: formData })
            .then(res => res.json())
            .then(response => {
                const rows = response.data || response;
                currentRows = rows;
                totalRecords = Number(response.total ?? rows.length);
                expandedRows.clear(); // al recargar, colapsar todo
                renderRows(rows);
                renderPagination();
                updateSortIcons();
                if (GlobalEvents.onDataLoaded) GlobalEvents.onDataLoaded(response);
            })
            .catch(err => {
                console.error('Error al cargar datos:', err);
                if (GlobalEvents.onError) GlobalEvents.onError(err);
            })
            .finally(() => {
                if (GlobalEvents.onLoading) GlobalEvents.onLoading(false);
            });
    }

    // =========================================================================
    // RENDER FILAS
    // =========================================================================
    function renderRows(rows) {
        tableBody.innerHTML = '';

        if (!rows || !rows.length) {
            tableBody.innerHTML = `
                <tr>
                    <td colspan="${totalCols()}" class="text-center">
                        <div class="flex flex-col items-center justify-center text-gray-400">
                            <svg xmlns="http://www.w3.org/2000/svg" width="60" height="60" viewBox="0 0 24 24"><g fill="none" stroke="#999999" stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5"><path d="M20.5 5a1.5 1.5 0 1 0 0-3a1.5 1.5 0 0 0 0 3m-17 17a1.5 1.5 0 1 0 0-3a1.5 1.5 0 0 0 0 3m17.539-8.938c.569-.135.961-.569.961-1.062s-.392-.927-.962-1.062l-4.517-1.076a5 5 0 0 0-9.042 0l-4.517 1.076C2.392 11.073 2 11.507 2 12s.392.927.962 1.062l4.517 1.076a5 5 0 0 0 9.042 0z"/><path d="M12 14a2 2 0 1 0 0-4a2 2 0 0 0 0 4m3-11.542A10 10 0 0 0 12 2a9.99 9.99 0 0 0-8 4m5 15.542A10 10 0 0 0 12 22a9.99 9.99 0 0 0 8-3.999"/></g></svg>
                            <p class="text-sm text-gray-400 mt-2">No hay datos para mostrar</p>
                        </div>
                    </td>
                </tr>`;
            paginationInfo.innerHTML = `Mostrando 0 de 0 registros`;
            return;
        }

        const fragment = document.createDocumentFragment();

        rows.forEach(row => {
            const id = getRowKey(row);
            const tr = document.createElement('tr');
            tr.dataset.rowId = id;

            // ── Botón toggle hijo ────────────────────────────────────────────
            if (childRows) {
                const tdToggle = document.createElement('td');
                tdToggle.style.cssText = 'width:36px;text-align:center;cursor:pointer;';
                const isOpen = expandedRows.has(id);
                tdToggle.innerHTML = `
                    <button class="btn btn-sm ct-toggle-btn" data-id="${id}" title="Ver detalle"
                        style="padding:2px 6px;border:none;background:transparent;color:var(--accent-blue, #3b82f6);transition:transform .2s;${isOpen ? 'transform:rotate(90deg)' : ''}">
                        <i class="fa-solid fa-chevron-right" style="font-size:.75rem"></i>
                    </button>`;
                tdToggle.addEventListener('click', e => {
                    e.stopPropagation();
                    handleToggleChild(id, row, tr);
                });
                tr.appendChild(tdToggle);
            }

            // ── Selectable ───────────────────────────────────────────────────
            if (selectable.enabled) {
                const canSelect = selectable.condition ? selectable.condition(row) : true;
                const isSelected = selectedRows.has(id);
                const td = document.createElement('td');
                td.innerHTML = `<input type="checkbox" class="form-check-input" ${isSelected ? 'checked' : ''} ${!canSelect ? 'disabled' : ''} />`;
                const checkbox = td.querySelector('input');
                if (canSelect) {
                    checkbox.addEventListener('click', e => { e.stopPropagation(); toggleRow(row); });
                    tr.addEventListener('click', () => toggleRow(row));
                }
                if (selectable.initialSelected && selectable.initialSelected(row)) selectedRows.set(id, row);
                tr.appendChild(td);
            }

            // ── Columnas ─────────────────────────────────────────────────────
            columns.forEach(col => {
                const td = document.createElement('td');
                const raw = col.data ? row[col.data] : row;
                const rowIndex = (currentPage - 1) * pageSizeValue + rows.indexOf(row) + 1;
                const value = col.formatter ? col.formatter(raw, row, rowIndex) : raw ?? '';
                let className = '';
                if (typeof col.className === 'function') className = col.className(row);
                else if (typeof col.className === 'string') className = col.className.trim();
                td.innerHTML = value ?? '';
                if (className) td.classList.add(...className.split(/\s+/));
                tr.appendChild(td);
            });

            // ── Acciones ─────────────────────────────────────────────────────
            if (actions.length) {
                const td = document.createElement('td');

                actions.forEach(act => {
                    if (act.visible && !act.visible(row)) return;

                    const btn = document.createElement('button');

                    const icon = typeof act.icon === 'function'
                        ? act.icon(row)
                        : act.icon;

                    const color = typeof act.color === 'function'
                        ? act.color(row)
                        : act.color;

                    const title = typeof act.title === 'function'
                        ? act.title(row)
                        : act.title;

                    const data = typeof act.data === 'function'
                        ? act.data(row)
                        : act.data;

                    if (data && typeof data === 'object') {
                        Object.entries(data).forEach(([key, value]) => {
                            btn.dataset[key] = value ?? '';
                        });
                    }

                    btn.className = `btn btn-sm ${color} me-1`;
                    btn.innerHTML = `<i class="fa-solid ${icon}"></i>`;
                    btn.title = title;
                    btn.dataset = data;
                    btn.addEventListener('click', e => {
                        e.stopPropagation();
                        act.onClick && act.onClick(row);
                    });

                    td.appendChild(btn);
                });

                tr.appendChild(td);
            }

            // ── Row events ───────────────────────────────────────────────────
            if (RowsEvents.onRowClick) tr.addEventListener('click', () => RowsEvents.onRowClick(row));
            if (RowsEvents.onRowDblClick) tr.addEventListener('dblclick', () => RowsEvents.onRowDblClick(row));
            if (RowsEvents.onKeyPress) tr.addEventListener('keydown', e => RowsEvents.onKeyPress(e, row));
            if (selectable.enabled && selectedRows.has(id)) tr.classList.add('table-active');

            tr.tabIndex = 0;
            fragment.appendChild(tr);

            // ── Si estaba expandida, re-insertar la fila hija ────────────────
            if (childRows && expandedRows.has(id)) {
                const existingChild = buildChildPlaceholder(id);
                fragment.appendChild(existingChild);
                loadChildRows(id, row, existingChild);
            }
        });

        tableBody.appendChild(fragment);
        updateSelectAllState();
        updateSelectedLabel();
        if (tableResponsive?._updateDragCursor) tableResponsive._updateDragCursor();
    }

    // =========================================================================
    // CHILD ROWS — toggle
    // =========================================================================
    function handleToggleChild(id, row, parentTr) {
        const toggleBtn = parentTr.querySelector('.ct-toggle-btn');

        if (expandedRows.has(id)) {
            // Cerrar: quitar fila hija del DOM
            expandedRows.delete(id);
            const childTr = tableBody.querySelector(`tr[data-child-of="${id}"]`);
            if (childTr) childTr.remove();
            if (toggleBtn) toggleBtn.style.transform = '';
        } else {
            // Abrir: insertar fila hija después del padre
            expandedRows.add(id);
            if (toggleBtn) toggleBtn.style.transform = 'rotate(90deg)';
            const childTr = buildChildPlaceholder(id);
            parentTr.insertAdjacentElement('afterend', childTr);
            loadChildRows(id, row, childTr);
        }
    }

    // Crea el <tr> contenedor de la sub-tabla (con spinner mientras carga)
    function buildChildPlaceholder(parentId) {
        const tr = document.createElement('tr');
        tr.dataset.childOf = parentId;
        tr.classList.add('ct-child-row');
        tr.innerHTML = `
            <td colspan="${totalCols()}" style="padding:0;border-top:none;">
                <div class="ct-child-container">
                    <div class="ct-child-loading">
                        <span class="ct-child-spinner"></span> Cargando...
                    </div>
                </div>
            </td>`;
        return tr;
    }

    // Carga los datos hijos y renderiza la sub-tabla
    async function loadChildRows(parentId, parentRow, childTr) {
        const container = childTr.querySelector('.ct-child-container');

        try {
            let childData = [];

            if (childRows.data) {
                // Fuente: función sobre el row padre (sin petición HTTP)
                childData = await Promise.resolve(childRows.data(parentRow));
            } else if (childRows.path) {
                // Fuente: endpoint propio
                const paramKey = childRows.paramName ?? 'parentId';
                // Nota: si rowKey es una función (llave compuesta), el valor real que se
                // envía al servidor debe indicarse explícitamente con childRows.pathKey,
                // ya que una llave compuesta no corresponde a una propiedad real de la fila.
                const paramVal = childRows.pathKey
                    ? parentRow[childRows.pathKey]
                    : (typeof rowKey === 'string' ? parentRow[rowKey] : parentId);
                const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                const formData = new FormData();
                formData.append('__RequestVerificationToken', token || '');
                formData.append(paramKey, paramVal);

                // Filtros extra opcionales
                if (childRows.extraData) {
                    const extra = typeof childRows.extraData === 'function'
                        ? childRows.extraData(parentRow)
                        : childRows.extraData;
                    for (const [k, v] of Object.entries(extra)) {
                        if (v !== undefined && v !== null) formData.append(k, v);
                    }
                }

                const res = await fetch(childRows.path, { method: 'POST', body: formData });
                const json = await res.json();
                childData = json.data ?? json;
            }

            container.innerHTML = buildChildTable(childData);

        } catch (err) {
            console.error('Error al cargar filas hijas:', err);
            container.innerHTML = `<div class="ct-child-error"><i class="fa-solid fa-triangle-exclamation"></i> Error al cargar el detalle.</div>`;
        }
    }

    // Construye el HTML de la sub-tabla
    function buildChildTable(rows) {
        const cols = childRows.columns;
        const empty = childRows.emptyText ?? 'Sin registros';
        const indent = childRows.indent !== false; // sangría por defecto activa

        if (!rows || !rows.length) {
            return `<div class="ct-child-empty">${empty}</div>`;
        }

        const thead = cols.map(c => `<th class="${c.className ?? ''}">${c.title}</th>`).join('');
        const tbody = rows.map(row => {
            const cells = cols.map(col => {
                const raw = col.data ? row[col.data] : row;
                const value = col.formatter ? col.formatter(raw, row) : raw ?? '';
                let cls = '';
                if (typeof col.className === 'function') cls = col.className(row);
                else if (typeof col.className === 'string') cls = col.className.trim();
                return `<td class="${cls}">${value ?? ''}</td>`;
            }).join('');
            return `<tr class="ct-child-data-row">${cells}</tr>`;
        }).join('');

        return `
            <table class="ct-child-table${indent ? ' ct-child-indent' : ''}">
                <thead><tr>${thead}</tr></thead>
                <tbody>${tbody}</tbody>
            </table>`;
    }

    // =========================================================================
    // SELECTABLE
    // =========================================================================
    function toggleRow(row) {
        const id = getRowKey(row);
        if (selectedRows.has(id)) selectedRows.delete(id);
        else selectedRows.set(id, row);
        renderRows(currentRows);
        updateSelectedLabel();
        if (selectable.onChange) selectable.onChange(Array.from(selectedRows.values()));
    }

    function updateSelectedLabel() {
        const label = container.querySelector(`#${tableId}-selected`);
        if (!label) return;
        const count = selectedRows.size;
        label.textContent = count ? `${count} seleccionados` : '';
    }

    // =========================================================================
    // ICONOS DE SORTEO POR COLUMNA
    // =========================================================================
    function updateSortIcons() {
        container.querySelectorAll('.ct-sort-icon').forEach(icon => {
            const col = icon.dataset.col;
            if (col === sortColumn) {
                icon.textContent = sortDir === 'asc' ? '↑' : '↓';
                icon.style.color = 'var(--accent-blue, #3b82f6)';
                icon.style.opacity = '1';
            } else {
                icon.textContent = '⇅';
                icon.style.color = '';
                icon.style.opacity = '.35';
            }
        });
    }

    // =========================================================================
    // PAGINATION
    // =========================================================================
    function renderPagination() {
        const totalPages = Math.ceil(totalRecords / pageSizeValue);
        paginationControls.innerHTML = '';

        const from = (currentPage - 1) * pageSizeValue + 1;
        const to = Math.min(currentPage * pageSizeValue, totalRecords);
        paginationInfo.innerHTML = `Mostrando <strong>${from}</strong> a <strong>${to}</strong> de <strong>${totalRecords}</strong> registros`;

        if (totalPages <= 1) return;

        const createBtn = (label, page, disabled = false, active = false, isIcon = false) => {
            const btn = document.createElement('button');
            btn.className = `pagination-btn btn btn-sm ${active ? 'btn-primary active' : 'btn-light'} ${active ? 'disabled' : ''}`;
            btn.innerHTML = isIcon ? `<i class="fas ${label}"></i>` : label;
            btn.disabled = disabled;
            btn.onclick = () => !disabled && fetchData(searchInput?.value ?? '', page);
            paginationControls.appendChild(btn);
        };

        createBtn('fa-angle-left', currentPage - 1, currentPage === 1, false, true);

        const range = 2;
        let start = Math.max(2, currentPage - range);
        let end = Math.min(totalPages - 1, currentPage + range);

        createBtn(1, 1, false, currentPage === 1);
        if (start > 2) { const d = document.createElement('span'); d.className = 'pagination-dots'; d.textContent = '…'; paginationControls.appendChild(d); }
        for (let i = start; i <= end; i++) createBtn(i, i, i === currentPage, i === currentPage);
        if (end < totalPages - 1) { const d = document.createElement('span'); d.className = 'pagination-dots'; d.textContent = '…'; paginationControls.appendChild(d); }
        if (totalPages > 1) createBtn(totalPages, totalPages, false, currentPage === totalPages);

        createBtn('fa-angle-right', currentPage + 1, currentPage === totalPages, false, true);
    }

    // =========================================================================
    // EXPORTS
    // =========================================================================

    // ── Helper: strip HTML tags → plain text ────────────────────────────────
    function stripHtml(html) {
        if (html === null || html === undefined) return '';
        const div = document.createElement('div');
        div.innerHTML = String(html);
        return div.innerText.trim();
    }

    // ── Helper: fetch datos desde endpoint de export ─────────────────────────
    async function fetchExportData(expConfig) {
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const formData = new FormData();
        formData.append('__RequestVerificationToken', token || '');

        const extraData = typeof expConfig.data === 'function'
            ? expConfig.data()
            : (expConfig.data ?? {});
        for (const [k, v] of Object.entries(extraData)) {
            if (v !== undefined && v !== null) formData.append(k, v);
        }

        const res = await fetch(expConfig.path, { method: 'POST', body: formData });
        const json = await res.json();
        return json; // devolvemos el JSON completo (puede ser objeto multi-hoja o array)
    }

    // ── Helper: convertir array de objetos → matriz [header, ...rows] ────────
    // sheetColumns: columnas custom para esta hoja (opcional)
    // Si no se pasan, usa las keys del primer objeto como headers.
    // Para cada key, si coincide con el `data` de alguna columna del createTable
    // aplica su formatter y usa su title; si no, usa la key y el valor crudo.
    function buildExportMatrix(data, sheetColumns = null) {
        if (!data || !data.length) return [[]];

        if (sheetColumns && sheetColumns.length) {
            // ── Con columnas definidas por hoja ──────────────────────────
            const header = sheetColumns.map(c => c.title);
            const body = data.map(row =>
                sheetColumns.map(col => {
                    const raw = col.data ? row[col.data] : row;
                    if (col.formatter) return stripHtml(col.formatter(raw, row));
                    return raw ?? '';
                })
            );
            return [header, ...body];
        } else {
            // ── Sin columnas: auto desde keys del JSON ───────────────────
            // Construir mapa key → columna padre para reutilizar formatters y titles
            const colMap = {};
            columns.forEach(col => { if (col.data) colMap[col.data] = col; });

            const keys = Object.keys(data[0]);
            // Header: si existe columna padre con ese key usa su title; si no, la key tal cual
            const header = keys.map(k => colMap[k]?.title ?? k);
            const body = data.map(row =>
                keys.map(k => {
                    const col = colMap[k];
                    if (col?.formatter) return stripHtml(col.formatter(row[k], row));
                    return row[k] ?? '';
                })
            );
            return [header, ...body];
        }
    }

    async function exportToExcel(expConfig = null) {
        const fileName = expConfig?.fileName ?? `export_${tableId}`;
        const wb = XLSX.utils.book_new();

        if (expConfig?.path) {
            const json = await fetchExportData(expConfig);

            if (expConfig.sheets && expConfig.sheets.length) {
                // ── Modo multi-hoja ──────────────────────────────────────
                // El endpoint devuelve un objeto: { facturas: [...], complementos: [...], ... }
                expConfig.sheets.forEach(sheetDef => {
                    const sheetData = json[sheetDef.key];
                    if (!sheetData) {
                        console.warn(`createTable export: no se encontró la key '${sheetDef.key}' en la respuesta`);
                        return;
                    }
                    const matrix = buildExportMatrix(sheetData, sheetDef.columns ?? null);
                    const ws = XLSX.utils.aoa_to_sheet(matrix);
                    XLSX.utils.book_append_sheet(wb, ws, sheetDef.sheetName ?? sheetDef.key);
                });
            } else {
                // ── Modo hoja única con endpoint ─────────────────────────
                // Acepta { data: [...] } o [...] directo
                const data = json.data ?? json;
                const matrix = buildExportMatrix(data, null);
                const ws = XLSX.utils.aoa_to_sheet(matrix);
                XLSX.utils.book_append_sheet(wb, ws, expConfig.sheetName ?? 'Datos');
            }
        } else {
            // ── Exportar la tabla visible (comportamiento original) ───────
            const table = container.querySelector('table');
            const rows = Array.from(table.querySelectorAll('tr')).map(tr =>
                Array.from(tr.querySelectorAll('th, td')).map(td => {
                    let text = td.innerText.trim();
                    if (/^\$[\d,]+(\.\d+)?$/.test(text)) return { v: parseFloat(text.replace(/[$,]/g, '')), t: 'n' };
                    if (/^-?\d+(\.\d+)?$/.test(text)) return { v: parseFloat(text), t: 'n' };
                    if (/^[0-9]+([\-\/][0-9]+)*$/.test(text)) return { v: text, t: 's' };
                    return { v: text };
                })
            );
            const ws = XLSX.utils.aoa_to_sheet(rows);
            XLSX.utils.book_append_sheet(wb, ws, expConfig?.sheetName ?? 'Datos');
        }

        XLSX.writeFile(wb, `${fileName}.xlsx`);
    }

    async function exportToPDF(expConfig = null) {
        const fileName = expConfig?.fileName ?? `export_${tableId}`;
        const { jsPDF } = window.jspdf;
        const doc = new jsPDF('l', 'pt', 'a4');

        // ── Helper interno: agrega una tabla al doc con título ───────────
        const addPdfTable = (title, matrix, isFirstPage) => {
            if (!isFirstPage) doc.addPage();
            doc.setFontSize(13);
            doc.setFont(undefined, 'bold');
            doc.text(title, 40, 30);
            doc.setFont(undefined, 'normal');
            doc.autoTable({
                startY: 50,
                head: [matrix[0]],
                body: matrix.slice(1),
                styles: { fontSize: 8, cellPadding: 4, overflow: 'linebreak' },
                headStyles: { fillColor: [41, 128, 185], textColor: 255, halign: 'center' },
                alternateRowStyles: { fillColor: [245, 245, 245] },
                theme: 'striped',
                didDrawPage: d => {
                    doc.setFontSize(8);
                    doc.text(
                        `Página ${d.pageNumber} de ${doc.internal.getNumberOfPages()}`,
                        d.settings.margin.left,
                        doc.internal.pageSize.height - 10
                    );
                },
            });
        };

        if (expConfig?.path) {
            const json = await fetchExportData(expConfig);

            if (expConfig.sheets && expConfig.sheets.length) {
                // ── Modo multi-sección: una página por hoja ──────────────
                expConfig.sheets.forEach((sheetDef, i) => {
                    const sheetData = json[sheetDef.key];
                    if (!sheetData) {
                        console.warn(`createTable export PDF: no se encontró la key '${sheetDef.key}'`);
                        return;
                    }
                    const matrix = buildExportMatrix(sheetData, sheetDef.columns ?? null);
                    addPdfTable(sheetDef.sheetName ?? sheetDef.key, matrix, i === 0);
                });
            } else {
                // ── Hoja única con endpoint ──────────────────────────────
                const data = json.data ?? json;
                const matrix = buildExportMatrix(data, null);
                addPdfTable(expConfig.sheetName ?? fileName, matrix, true);
            }
        } else {
            // ── Exportar la tabla visible (comportamiento original) ───────
            const headers = columns.map(c => c.title);
            const pdfRows = Array.from(container.querySelectorAll('tbody tr:not(.ct-child-row)')).map(tr =>
                Array.from(tr.querySelectorAll('td')).map(td => td.innerText.trim())
            );
            addPdfTable(fileName, [headers, ...pdfRows], true);
        }

        doc.save(`${fileName}.pdf`);
    }

    // Genera solo el HTML de los botones (sin onclick — se asignan en bindExportButtons)
    function renderExportButtons(exports, tableId) {
        if (!exports || !exports.length) return '';
        return `
        <div class="export-buttons d-flex align-items-center gap-1" id="${tableId}-export-btns">
            ${exports.map((exp, idx) => {
            if (typeof exp === 'string') {
                if (exp === 'excel') return `<button class="btn btn-success btn-sm export-excel" data-export-idx="${idx}"><i class="fa-solid fa-file-excel"></i> Exportar a Excel</button>`;
                if (exp === 'pdf') return `<button class="btn btn-danger  btn-sm export-pdf"   data-export-idx="${idx}"><i class="fa-solid fa-file-pdf"></i> Exportar a PDF</button>`;
                return '';
            }
            if (typeof exp === 'object' && exp.type) {
                const isExcel = exp.type === 'excel';
                const isPdf = exp.type === 'pdf';
                if (!isExcel && !isPdf) return '';
                const label = isExcel
                    ? `<i class="fa-solid fa-file-excel"></i> ${exp.label ?? 'Exportar a Excel'}`
                    : `<i class="fa-solid fa-file-pdf"></i>   ${exp.label ?? 'Exportar a PDF'}`;
                const btnClass = isExcel ? 'btn btn-success btn-sm export-excel' : 'btn btn-danger btn-sm export-pdf';
                return `<button class="${btnClass}" data-export-idx="${idx}">${label}</button>`;
            }
            if (typeof exp === 'object' && exp.html) {
                const temp = document.createElement('div');
                temp.innerHTML = exp.html.trim();
                const btn = temp.firstElementChild;
                if (!btn) return '';
                if (exp.type === 'excel') btn.classList.add('export-excel');
                if (exp.type === 'pdf') btn.classList.add('export-pdf');
                btn.dataset.exportIdx = idx;
                if (!btn.classList.contains('btn')) btn.classList.add('btn', 'btn-sm', 'btn-outline-secondary');
                return btn.outerHTML;
            }
            return '';
        }).join('')}
        </div>`;
    }

    function bindExportButtons() {
        const wrapper = container.querySelector(`#${tableId}-export-btns`);
        if (!wrapper) return;
        wrapper.querySelectorAll('button[data-export-idx]').forEach(btn => {
            const idx = parseInt(btn.dataset.exportIdx);
            const exp = exports[idx];
            const expConfig = (typeof exp === 'object' && exp.type) ? exp : null;
            const type = expConfig?.type ?? (btn.classList.contains('export-excel') ? 'excel' : 'pdf');
            btn.onclick = () => {
                if (type === 'excel') exportToExcel(expConfig);
                if (type === 'pdf') exportToPDF(expConfig);
            };
        });
    }

    function updateSelectAllState() {
        if (!selectable.enabled) return;
        const selectAllCheckbox = container.querySelector(`#${tableId}-select-all`);
        if (!selectAllCheckbox) return;

        const selectableRows = currentRows.filter(row =>
            selectable.condition ? selectable.condition(row) : true
        );
        const selectedCount = selectableRows.filter(row => selectedRows.has(getRowKey(row))).length;

        if (selectedCount === 0) {
            selectAllCheckbox.checked = false;
            selectAllCheckbox.indeterminate = false;
        } else if (selectedCount === selectableRows.length) {
            selectAllCheckbox.checked = true;
            selectAllCheckbox.indeterminate = false;
        } else {
            selectAllCheckbox.checked = false;
            selectAllCheckbox.indeterminate = true; // estado "parcial"
        }
    }

    // =========================================================================
    // DRAG TO SCROLL (horizontal)
    // =========================================================================
    initDragScroll(tableResponsive);

    function initDragScroll(el) {
        if (!el) return;

        let isDown = false;
        let startX = 0;
        let scrollLeftStart = 0;
        let moved = false;

        const updateCursorState = () => {
            const hasOverflow = el.scrollWidth > el.clientWidth;
            el.classList.toggle('ct-scrollable', hasOverflow); // en vez de el.style.cursor
        };

        updateCursorState();
        window.addEventListener('resize', updateCursorState);
        el._updateDragCursor = updateCursorState;

        el.addEventListener('mousedown', (e) => {
            // No iniciar drag si el click viene del header (para no chocar con el sort)
            if (e.target.closest('thead')) return;
            if (e.target.closest('button, a, input, select, .ct-toggle-btn')) return;
            const hasOverflow = el.scrollWidth > el.clientWidth;
            if (!hasOverflow) return;

            isDown = true;
            moved = false;
            el.classList.add('ct-dragging');
            startX = e.pageX - el.getBoundingClientRect().left;
            scrollLeftStart = el.scrollLeft;
        });

        el.addEventListener('mouseleave', () => {
            if (!isDown) return;
            isDown = false;
            el.classList.remove('ct-dragging');
        });

        el.addEventListener('mouseup', () => {
            if (!isDown) return;
            isDown = false;
            el.classList.remove('ct-dragging');
        });

        el.addEventListener('mousemove', (e) => {
            if (!isDown) return;
            e.preventDefault();
            const x = e.pageX - el.getBoundingClientRect().left;
            const walk = x - startX;
            if (Math.abs(walk) > 3) moved = true;
            el.scrollLeft = scrollLeftStart - walk;
        });

        el.addEventListener('click', (e) => {
            if (moved) {
                e.stopPropagation();
                e.preventDefault();
                moved = false;
            }
        }, true);
    }

    // =========================================================================
    // API PÚBLICA
    // =========================================================================
    return {
        reload: (search = '') => fetchData(search, 1),
        refreshPage: () => fetchData(searchInput?.value ?? '', currentPage),
        getCurrentPage: () => currentPage,
        clearSelection: () => { selectedRows.clear(); updateSelectedLabel(); fetchData(searchInput?.value ?? '', currentPage); },
        getSelectedRows: () => { const rows = Array.from(selectedRows.values()); return selectable.map ? rows.map(r => selectable.map(r)) : rows; },
        collapseAll: () => { expandedRows.clear(); renderRows(currentRows); },
        removeSelectedRow: (id) => {
            selectedRows.delete(id);
            renderRows(currentRows);
            updateSelectedLabel();

            if (selectable.onChange) {
                selectable.onChange(Array.from(selectedRows.values()));
            }
        },
    };
}
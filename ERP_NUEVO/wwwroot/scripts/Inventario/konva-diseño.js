
let stage = null;
let konvaLayer = null;

// Función para inicializar Konva cuando sea necesario
let isDraggable = true;

function initKonva() {
    const content = document.getElementById('contentDisplay');
    const toolbar = document.getElementById('konvaToolbar');
    toolbar.style.display = 'flex';
    if (!document.getElementById('konvaContainer')) {
        content.innerHTML = `<div id="konvaContainer" style="width:100%; height:100%;"></div>`;
    }

    if (stage) stage.destroy();

    const container = document.getElementById('konvaContainer');
    stage = new Konva.Stage({
        container: 'konvaContainer',
        width: 1000,
        height: window.innerHeight * 0.8,
        draggable: isDraggable
    });

    konvaLayer = new Konva.Layer();
    stage.add(konvaLayer);

    window.addEventListener('resize', () => {
        const width = container.offsetWidth;
        const height = window.innerHeight * 0.8;
        stage.width(width);
        stage.height(height);
    });
}
function adjustStageHeight() {
    if (!konvaLayer) return;

    const rect = konvaLayer.getClientRect({ skipTransform: false });
    const minHeight = window.innerHeight * 0.8;
    const newHeight = Math.max(rect.height + rect.y, minHeight);

    stage.height(newHeight);

    // 🔥 Actualizar el fondo
    const bgDoc = konvaLayer.findOne('#bgDoc');
    if (bgDoc) {
        bgDoc.height(newHeight);
        bgDoc.width(stage.width());
    }

    konvaLayer.batchDraw();
}
// 🔍 Zoom
function zoomIn() {
    const scale = stage.scaleX() * 1.2;
    stage.scale({ x: scale, y: scale });
    stage.batchDraw();
}
function zoomOut() {
    const scale = stage.scaleX() / 1.2;
    stage.scale({ x: scale, y: scale });
    stage.batchDraw();
}

// Reset vista
function resetView() {
    stage.scale({ x: 1, y: 1 });
    stage.position({ x: 0, y: 0 });
    stage.batchDraw();
}

// Alternar modo drag
function toggleDrag() {
    isDraggable = !isDraggable;
    stage.draggable(isDraggable);
}

// Función auxiliar para crear gradientes y sombras modernas
function createModernRect(config) {
    const group = new Konva.Group();

    // Sombra
    const shadow = new Konva.Rect({
        x: config.x + 4,
        y: config.y + 4,
        width: config.width,
        height: config.height,
        fill: 'rgba(0, 0, 0, 0.15)',
        cornerRadius: config.cornerRadius || 12,
        blur: 8
    });
    group.add(shadow);

    // Rect principal
    const rect = new Konva.Rect({
        x: config.x,
        y: config.y,
        width: config.width,
        height: config.height,
        fill: config.fill,
        cornerRadius: config.cornerRadius || 12,
        stroke: config.stroke || 'rgba(255, 255, 255, 0.2)',
        strokeWidth: config.strokeWidth || 1
    });
    group.add(rect);

    // Brillo superior
    const highlight = new Konva.Rect({
        x: config.x,
        y: config.y,
        width: config.width,
        height: config.height / 3,
        fill: 'rgba(255, 255, 255, 0.1)',
        cornerRadius: config.cornerRadius || 12
    });
    group.add(highlight);

    return { group, rect, shadow, highlight };
}
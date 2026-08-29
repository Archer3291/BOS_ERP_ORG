function initNormalToggles() {
    document.querySelectorAll('.bootstrap-toggle:not(.disabled)').forEach(toggleContainer => {
        const input = toggleContainer.querySelector('input[type="checkbox"]');
        const toggleGroup = toggleContainer.querySelector('.toggle-group');
        const toggleOn = toggleContainer.querySelector('.toggle-on');
        const toggleOff = toggleContainer.querySelector('.toggle-off');
        const handle = toggleContainer.querySelector('.toggle-handle');

        if (!input || !toggleGroup || !toggleOn || !toggleOff || !handle) return;

        // Función para posicionar el handle dinámicamente
        function positionNormalHandle() {
            const isChecked = input.checked;
            const activeElement = isChecked ? toggleOn : toggleOff;

            if (activeElement) {
                const left = activeElement.offsetLeft;
                const width = activeElement.offsetWidth;
                handle.style.left = left + 'px';
                handle.style.width = width + 'px';
            }
        }

        // Posicionar handle inicial
        positionNormalHandle();

        // Agregar event listener al cambio
        input.addEventListener('change', positionNormalHandle);

        // Reposicionar handle cuando cambie el tamaño de la ventana
        window.addEventListener('resize', positionNormalHandle);
    });
}

// Función para inicializar los toggles de 3 estados
function initTriStateToggles() {
    document.querySelectorAll('.bootstrap-toggle-triple').forEach(toggleContainer => {
        const input = toggleContainer.querySelector('input[type="hidden"]');
        const toggleGroup = toggleContainer.querySelector('.toggle-group-triple');
        const states = toggleContainer.querySelectorAll('.toggle-state');
        const handle = toggleContainer.querySelector('.toggle-handle-triple');

        if (!input || !toggleGroup || !states.length || !handle) return;

        function positionHandle(value) {
            const activeState = Array.from(states).find(s => s.getAttribute('data-value') === value);
            if (!activeState) return;
            handle.style.left = activeState.offsetLeft + 'px';
            handle.style.width = activeState.offsetWidth + 'px';
        }

        function init() {
            const initialValue = input.value;
            updateTriStateDisplay(toggleGroup, states, initialValue);
            // Esperar un frame para que el DOM tenga dimensiones reales
            requestAnimationFrame(() => positionHandle(initialValue));
        }

        states.forEach(state => {
            state.addEventListener('click', function () {
                const newValue = this.getAttribute('data-value');
                input.value = newValue;
                updateTriStateDisplay(toggleGroup, states, newValue);
                positionHandle(newValue);
                input.dispatchEvent(new Event('change', { bubbles: true }));
            });
        });

        window.addEventListener('resize', () => positionHandle(input.value));

        init();
    });
}

// Función para actualizar la visualización del tristate
function updateTriStateDisplay(toggleGroup, states, value) {
    toggleGroup.setAttribute('data-state', value);

    states.forEach(state => {
        if (state.getAttribute('data-value') === value) {
            state.classList.add('active');
        } else {
            state.classList.remove('active');
        }
    });
}

document.addEventListener('DOMContentLoaded', function () {
    initNormalToggles();
    initTriStateToggles();
});
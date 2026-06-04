let dotNetRef = null;
let isListening = false;

function onMouseMove(e) {
    if (!dotNetRef) return;

    // Clamp coordinates to viewport to keep the modal inside the visible area
    const clampX = Math.round(Math.max(0, Math.min(window.innerWidth - 1, e.clientX)));
    const clampY = Math.round(Math.max(0, Math.min(window.innerHeight - 1, e.clientY)));

    dotNetRef.invokeMethodAsync('OnMouseMove', clampX, clampY);
}

function onMouseUp(e) {
    if (dotNetRef) {
        dotNetRef.invokeMethodAsync('OnMouseUp');
    }
}

function onTouchMove(e) {
    if (!dotNetRef) return;
    if (!e.touches || e.touches.length === 0) return;
    const t = e.touches[0];
    const clampX = Math.round(Math.max(0, Math.min(window.innerWidth - 1, t.clientX)));
    const clampY = Math.round(Math.max(0, Math.min(window.innerHeight - 1, t.clientY)));
    dotNetRef.invokeMethodAsync('OnMouseMove', clampX, clampY);
}

export function initialize(dotNetReference) {
    dotNetRef = dotNetReference;
}

export function startDrag() {
    if (!isListening) {
        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup', onMouseUp);
        // support touch-driven dragging
        document.addEventListener('touchmove', onTouchMove, { passive: false });
        document.addEventListener('touchend', onMouseUp);
        isListening = true;
    }
}

export function stopDrag() {
    if (isListening) {
        document.removeEventListener('mousemove', onMouseMove);
        document.removeEventListener('mouseup', onMouseUp);
        isListening = false;
    }
}

export function cleanup() {
    stopDrag();
    dotNetRef = null;
}

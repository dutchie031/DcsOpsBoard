let dotNetRef = null;
let isListening = false;

function onMouseMove(e) {
    if (dotNetRef) {
        dotNetRef.invokeMethodAsync('OnMouseMove', e.clientX, e.clientY);
    }
}

function onMouseUp(e) {
    if (dotNetRef) {
        dotNetRef.invokeMethodAsync('OnMouseUp');
    }
}

export function initialize(dotNetReference) {
    dotNetRef = dotNetReference;
}

export function startDrag() {
    if (!isListening) {
        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup', onMouseUp);
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

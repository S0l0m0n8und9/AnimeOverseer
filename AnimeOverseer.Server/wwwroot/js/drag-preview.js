// Give native HTML drag-and-drop a compact, reliable visual preview of an anime card.
document.addEventListener('dragstart', event => {
    const card = event.target instanceof Element ? event.target.closest('.anime-card-draggable') : null;
    if (!card || !event.dataTransfer) return;

    const preview = card.cloneNode(true);
    preview.classList.add('anime-card-drag-preview');
    document.body.appendChild(preview);
    event.dataTransfer.setData('text/plain', 'anime-card');
    event.dataTransfer.effectAllowed = 'copy';
    event.dataTransfer.setDragImage(preview, 58, 80);
    requestAnimationFrame(() => preview.remove());
}, true);

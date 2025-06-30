// Global functions for Blazor interop
window.addKeyboardListeners = (dotNetRef) => {
    document.addEventListener('keydown', (e) => {
        // Movement keys
        if (e.key === 'w' || e.key === 'a' || e.key === 's' || e.key === 'd' || e.key === ' ') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('OnKeyDown', e.key);
        }
        // Speed keys (0-9)
        else if (e.key >= '0' && e.key <= '9') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('OnSpeedKey', e.key);
        }
    });
    
    document.addEventListener('keyup', (e) => {
        if (e.key === 'w' || e.key === 'a' || e.key === 's' || e.key === 'd' || e.key === ' ') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('OnKeyUp', e.key);
        }
    });
    
    console.log('Keyboard listeners added');
};

// Add mouse listeners for the game canvas
window.addMouseListeners = (canvasElement, dotNetRef) => {
    if (!canvasElement) return;
    
    // Right click for setting heading
    canvasElement.addEventListener('contextmenu', (e) => {
        e.preventDefault();
        const rect = canvasElement.getBoundingClientRect();
        const x = e.clientX - rect.left;
        const y = e.clientY - rect.top;
        dotNetRef.invokeMethodAsync('OnRightClick', x, y);
    });
    
    // Left click for shooting
    canvasElement.addEventListener('mousedown', (e) => {
        if (e.button === 0) { // Left button
            const rect = canvasElement.getBoundingClientRect();
            const x = e.clientX - rect.left;
            const y = e.clientY - rect.top;
            dotNetRef.invokeMethodAsync('OnLeftClick', x, y);
        }
    });
    
    canvasElement.addEventListener('mouseup', (e) => {
        if (e.button === 0) { // Left button
            dotNetRef.invokeMethodAsync('OnLeftRelease');
        }
    });
    
    console.log('Mouse listeners added');
};
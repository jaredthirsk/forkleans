// Global functions for Blazor interop
window.addKeyboardListeners = (dotNetRef) => {
    document.addEventListener('keydown', (e) => {
        if (e.key === 'w' || e.key === 'a' || e.key === 's' || e.key === 'd' || e.key === ' ') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('OnKeyDown', e.key);
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
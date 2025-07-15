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

// Draw network statistics graph
window.drawNetworkGraph = (canvas, clientSent, clientRecv, serverSent, serverRecv) => {
    if (!canvas || !canvas.getContext) return;
    
    const ctx = canvas.getContext('2d');
    const width = canvas.width;
    const height = canvas.height;
    const padding = 30; // Increased for right axis labels
    const graphWidth = width - padding * 2;
    const graphHeight = height - padding * 2;
    
    // Clear canvas
    ctx.fillStyle = '#1a1a1a';
    ctx.fillRect(0, 0, width, height);
    
    // Find max values for left and right axes
    // Left axis: client sent & server received
    const leftValues = [...clientSent, ...serverRecv];
    const maxLeftValue = Math.max(...leftValues, 10); // At least 10 for scale
    
    // Right axis: client received & server sent
    const rightValues = [...clientRecv, ...serverSent];
    const maxRightValue = Math.max(...rightValues, 10); // At least 10 for scale
    
    // Draw grid lines
    ctx.strokeStyle = '#333';
    ctx.lineWidth = 1;
    
    // Horizontal grid lines
    for (let i = 0; i <= 5; i++) {
        const y = padding + (graphHeight / 5) * i;
        ctx.beginPath();
        ctx.moveTo(padding, y);
        ctx.lineTo(width - padding, y);
        ctx.stroke();
        
        // Left Y-axis labels
        const leftValue = Math.round((maxLeftValue * (5 - i)) / 5);
        ctx.fillStyle = '#666';
        ctx.font = '10px monospace';
        ctx.textAlign = 'right';
        ctx.fillText(leftValue.toString(), padding - 5, y + 3);
        
        // Right Y-axis labels
        const rightValue = Math.round((maxRightValue * (5 - i)) / 5);
        ctx.textAlign = 'left';
        ctx.fillText(rightValue.toString(), width - padding + 5, y + 3);
    }
    
    // Draw data lines
    const dataArrays = [
        { data: clientSent, color: '#2196F3', label: 'Client Sent', axis: 'left', maxValue: maxLeftValue },
        { data: clientRecv, color: '#4CAF50', label: 'Client Recv', axis: 'right', maxValue: maxRightValue },
        { data: serverSent, color: '#FF9800', label: 'Server Sent', axis: 'right', maxValue: maxRightValue },
        { data: serverRecv, color: '#F44336', label: 'Server Recv', axis: 'left', maxValue: maxLeftValue }
    ];
    
    dataArrays.forEach(({ data, color, maxValue }) => {
        if (data.length < 2) return;
        
        ctx.strokeStyle = color;
        ctx.lineWidth = 2;
        ctx.beginPath();
        
        const xStep = graphWidth / (data.length - 1);
        
        for (let i = 0; i < data.length; i++) {
            const x = padding + xStep * i;
            const y = padding + graphHeight - (data[i] / maxValue) * graphHeight;
            
            if (i === 0) {
                ctx.moveTo(x, y);
            } else {
                ctx.lineTo(x, y);
            }
        }
        
        ctx.stroke();
    });
    
    // Draw axes
    ctx.strokeStyle = '#666';
    ctx.lineWidth = 2;
    ctx.beginPath();
    // Left axis
    ctx.moveTo(padding, padding);
    ctx.lineTo(padding, height - padding);
    // Bottom axis
    ctx.lineTo(width - padding, height - padding);
    // Right axis
    ctx.lineTo(width - padding, padding);
    ctx.stroke();
    
    // X-axis label
    ctx.fillStyle = '#666';
    ctx.font = '12px monospace';
    ctx.textAlign = 'center';
    ctx.fillText('Last 60 seconds', width / 2, height - 5);
    
    // Left Y-axis label (rotated)
    ctx.save();
    ctx.translate(10, height / 2);
    ctx.rotate(-Math.PI / 2);
    ctx.fillStyle = '#2196F3';
    ctx.fillText('Sent', 20, 0);
    ctx.fillStyle = '#F44336';
    ctx.fillText('Recv', -20, 0);
    ctx.restore();
    
    // Right Y-axis label (rotated)
    ctx.save();
    ctx.translate(width - 10, height / 2);
    ctx.rotate(Math.PI / 2);
    ctx.fillStyle = '#4CAF50';
    ctx.fillText('Recv', 20, 0);
    ctx.fillStyle = '#FF9800';
    ctx.fillText('Sent', -20, 0);
    ctx.restore();
};
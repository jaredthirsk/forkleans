// Triple canvas rendering context wrapper that rotates between three canvases for reduced flicker
class TripleCanvasContext {
    constructor(canvasA, canvasB, canvasC, dotNetRef) {
        this.canvases = [canvasA, canvasB, canvasC];
        this.contexts = [
            canvasA.getContext('2d'),
            canvasB.getContext('2d'),
            canvasC.getContext('2d')
        ];
        this.dotNetRef = dotNetRef;
        
        // Initialize buffer indices
        this.displayIndex = 0;  // Currently displayed canvas
        this.drawIndex = 1;     // Canvas being drawn to
        this.readyIndex = 2;    // Canvas ready to be displayed
        
        // Start with first canvas visible, others hidden
        this.canvases[0].style.display = 'block';
        this.canvases[1].style.display = 'none';
        this.canvases[2].style.display = 'none';
        
        this.explosionParticles = [];
        
        // Add mouse listeners to all canvases
        this.canvases.forEach(canvas => this.addMouseListeners(canvas));
    }
    
    addMouseListeners(canvas) {
        // Right click for setting heading
        canvas.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            const rect = canvas.getBoundingClientRect();
            const x = e.clientX - rect.left;
            const y = e.clientY - rect.top;
            if (this.dotNetRef) {
                this.dotNetRef.invokeMethodAsync('OnRightClick', x, y);
            }
        });
        
        // Left click for shooting
        canvas.addEventListener('mousedown', (e) => {
            if (e.button === 0) { // Left button
                const rect = canvas.getBoundingClientRect();
                const x = e.clientX - rect.left;
                const y = e.clientY - rect.top;
                if (this.dotNetRef) {
                    this.dotNetRef.invokeMethodAsync('OnLeftClick', x, y);
                }
            }
        });
        
        canvas.addEventListener('mouseup', (e) => {
            if (e.button === 0) { // Left button
                if (this.dotNetRef) {
                    this.dotNetRef.invokeMethodAsync('OnLeftRelease');
                }
            }
        });
    }
    
    // Start a new frame - prepare the draw buffer
    beginFrame() {
        // Get the current drawing context
        this.currentCtx = this.contexts[this.drawIndex];
        this.currentCanvas = this.canvases[this.drawIndex];
        
        // Clear the draw buffer
        this.currentCtx.clearRect(0, 0, this.currentCanvas.width, this.currentCanvas.height);
    }
    
    // End frame - rotate buffers
    endFrame() {
        // The draw buffer becomes ready
        const oldReadyIndex = this.readyIndex;
        this.readyIndex = this.drawIndex;
        
        // The ready buffer becomes displayed
        const oldDisplayIndex = this.displayIndex;
        this.displayIndex = oldReadyIndex;
        
        // The old display buffer becomes the new draw buffer
        this.drawIndex = oldDisplayIndex;
        
        // Update visibility - only the display buffer is visible
        this.canvases.forEach((canvas, index) => {
            canvas.style.display = index === this.displayIndex ? 'block' : 'none';
        });
    }
    
    clearCanvas(width, height, cameraOffset = { x: 0, y: 0 }) {
        this.currentCtx.fillStyle = '#000033';
        this.currentCtx.fillRect(0, 0, width, height);
        
        // Draw small grid
        this.currentCtx.strokeStyle = '#003366';
        this.currentCtx.lineWidth = 0.5;
        const gridSize = 50;
        
        const offsetX = -cameraOffset.x % gridSize;
        const offsetY = -cameraOffset.y % gridSize;
        
        for (let x = offsetX; x < width; x += gridSize) {
            this.currentCtx.beginPath();
            this.currentCtx.moveTo(x, 0);
            this.currentCtx.lineTo(x, height);
            this.currentCtx.stroke();
        }
        
        for (let y = offsetY; y < height; y += gridSize) {
            this.currentCtx.beginPath();
            this.currentCtx.moveTo(0, y);
            this.currentCtx.lineTo(width, y);
            this.currentCtx.stroke();
        }
    }
    
    drawZoneBoundaries(width, height, cameraOffset = { x: 0, y: 0 }, availableZones = []) {
        // Draw zone boundaries (500x500 units each)
        const zoneSize = 500;
        
        // Create a set of available zones for quick lookup
        const availableZoneSet = new Set(availableZones.map(z => `${z.x},${z.y}`));
        
        // Calculate visible zone range
        const startX = Math.floor(cameraOffset.x / zoneSize) * zoneSize;
        const startY = Math.floor(cameraOffset.y / zoneSize) * zoneSize;
        const endX = startX + width + zoneSize * 2;
        const endY = startY + height + zoneSize * 2;
        
        // Draw zones
        for (let x = startX; x < endX; x += zoneSize) {
            for (let y = startY; y < endY; y += zoneSize) {
                const zoneX = Math.floor(x / zoneSize);
                const zoneY = Math.floor(y / zoneSize);
                const isAvailable = availableZoneSet.has(`${zoneX},${zoneY}`);
                
                const screenX = x - cameraOffset.x;
                const screenY = y - cameraOffset.y;
                
                if (isAvailable) {
                    // Draw available zone with normal styling
                    this.currentCtx.strokeStyle = '#666666';
                    this.currentCtx.lineWidth = 2;
                    
                    // Draw zone border
                    this.currentCtx.strokeRect(screenX, screenY, zoneSize, zoneSize);
                    
                    // Draw zone label
                    this.currentCtx.fillStyle = '#888888';
                    this.currentCtx.font = '14px monospace';
                    if (screenX + 10 > 0 && screenX + 10 < width && screenY + 20 > 0 && screenY + 20 < height) {
                        this.currentCtx.fillText(`Zone ${zoneX},${zoneY}`, screenX + 10, screenY + 20);
                    }
                } else {
                    // Draw unavailable zone with red tint and blocking pattern
                    this.currentCtx.fillStyle = 'rgba(128, 0, 0, 0.3)';
                    this.currentCtx.fillRect(screenX, screenY, zoneSize, zoneSize);
                    
                    this.currentCtx.strokeStyle = '#ff0000';
                    this.currentCtx.lineWidth = 3;
                    this.currentCtx.strokeRect(screenX, screenY, zoneSize, zoneSize);
                    
                    // Draw X pattern to indicate blocked zone
                    this.currentCtx.beginPath();
                    this.currentCtx.moveTo(screenX, screenY);
                    this.currentCtx.lineTo(screenX + zoneSize, screenY + zoneSize);
                    this.currentCtx.moveTo(screenX + zoneSize, screenY);
                    this.currentCtx.lineTo(screenX, screenY + zoneSize);
                    this.currentCtx.stroke();
                    
                    // Draw "UNAVAILABLE" text
                    this.currentCtx.fillStyle = '#ff6666';
                    this.currentCtx.font = 'bold 16px monospace';
                    const text = 'UNAVAILABLE';
                    const textWidth = this.currentCtx.measureText(text).width;
                    if (screenX + zoneSize/2 - textWidth/2 > 0 && screenX + zoneSize/2 + textWidth/2 < width) {
                        this.currentCtx.fillText(text, screenX + zoneSize/2 - textWidth/2, screenY + zoneSize/2);
                    }
                }
            }
        }
    }
    
    drawEntity(x, y, size, color, rotation, entityType = 'default', subType = 0, state = 'Active', stateTimer = 0) {
        this.currentCtx.save();
        this.currentCtx.translate(x, y);
        
        // Handle different entity types
        switch (entityType) {
            case 'Player':
                this.drawPlayer(size, color, rotation, state, stateTimer);
                break;
            case 'Enemy':
                this.drawEnemy(size, color, rotation, subType);
                break;
            case 'Bullet':
                this.drawBullet(size, color, rotation);
                break;
            case 'Explosion':
                this.drawExplosion(size, stateTimer, subType);
                break;
            default:
                this.drawDefault(size, color, rotation);
                break;
        }
        
        this.currentCtx.restore();
    }
    
    drawPlayer(size, color, rotation, state, stateTimer) {
        this.currentCtx.rotate(rotation);
        
        if (state === 'Respawning') {
            // Flashing effect for respawning
            const alpha = Math.sin(stateTimer * 10) * 0.5 + 0.5;
            this.currentCtx.globalAlpha = alpha;
        } else if (state === 'Dying' || state === 'Dead') {
            this.currentCtx.globalAlpha = 0.3;
        }
        
        // Draw ship shape
        this.currentCtx.fillStyle = color;
        this.currentCtx.beginPath();
        this.currentCtx.moveTo(size, 0);
        this.currentCtx.lineTo(-size * 0.7, -size * 0.7);
        this.currentCtx.lineTo(-size * 0.3, 0);
        this.currentCtx.lineTo(-size * 0.7, size * 0.7);
        this.currentCtx.closePath();
        this.currentCtx.fill();
        
        // Draw cockpit
        this.currentCtx.fillStyle = '#003366';
        this.currentCtx.beginPath();
        this.currentCtx.arc(0, 0, size * 0.3, 0, Math.PI * 2);
        this.currentCtx.fill();
    }
    
    drawEnemy(size, color, rotation, subType) {
        this.currentCtx.rotate(rotation);
        
        switch (subType) {
            case 1: // Kamikaze
                // Spiky aggressive shape
                this.currentCtx.fillStyle = '#ff4444';
                this.currentCtx.beginPath();
                for (let i = 0; i < 8; i++) {
                    const angle = (i / 8) * Math.PI * 2;
                    const radius = i % 2 === 0 ? size : size * 0.6;
                    const x = Math.cos(angle) * radius;
                    const y = Math.sin(angle) * radius;
                    if (i === 0) this.currentCtx.moveTo(x, y);
                    else this.currentCtx.lineTo(x, y);
                }
                this.currentCtx.closePath();
                this.currentCtx.fill();
                break;
                
            case 2: // Sniper
                // Diamond shape with scope
                this.currentCtx.fillStyle = '#44ff44';
                this.currentCtx.beginPath();
                this.currentCtx.moveTo(size, 0);
                this.currentCtx.lineTo(0, -size);
                this.currentCtx.lineTo(-size, 0);
                this.currentCtx.lineTo(0, size);
                this.currentCtx.closePath();
                this.currentCtx.fill();
                
                // Scope line
                this.currentCtx.strokeStyle = '#88ff88';
                this.currentCtx.lineWidth = 2;
                this.currentCtx.beginPath();
                this.currentCtx.moveTo(size * 1.5, 0);
                this.currentCtx.lineTo(size * 2.5, 0);
                this.currentCtx.stroke();
                break;
                
            case 3: // Strafing
                // Curved aggressive shape
                this.currentCtx.fillStyle = '#ffaa44';
                this.currentCtx.beginPath();
                this.currentCtx.arc(0, 0, size, -Math.PI * 0.3, Math.PI * 0.3);
                this.currentCtx.lineTo(-size * 0.5, size * 0.5);
                this.currentCtx.lineTo(-size * 0.5, -size * 0.5);
                this.currentCtx.closePath();
                this.currentCtx.fill();
                break;
                
            default:
                // Default enemy
                this.currentCtx.fillStyle = color;
                this.currentCtx.beginPath();
                this.currentCtx.arc(0, 0, size, 0, Math.PI * 2);
                this.currentCtx.fill();
                break;
        }
    }
    
    drawBullet(size, color, rotation) {
        this.currentCtx.rotate(rotation);
        
        // Draw bullet trail
        const gradient = this.currentCtx.createLinearGradient(-size * 3, 0, size, 0);
        gradient.addColorStop(0, 'transparent');
        gradient.addColorStop(0.5, '#ffff0066');
        gradient.addColorStop(1, '#ffff00');
        
        this.currentCtx.fillStyle = gradient;
        this.currentCtx.fillRect(-size * 3, -size * 0.5, size * 4, size);
        
        // Draw bullet
        this.currentCtx.fillStyle = '#ffff00';
        this.currentCtx.beginPath();
        this.currentCtx.arc(0, 0, size, 0, Math.PI * 2);
        this.currentCtx.fill();
    }
    
    drawExplosion(size, timer, subType) {
        const maxRadius = subType === 1 ? size * 3 : size * 5; // Small or large explosion
        const progress = timer / 0.5; // 0.5 second explosion duration
        const currentRadius = maxRadius * progress;
        const alpha = 1 - progress;
        
        // Outer blast
        this.currentCtx.fillStyle = `rgba(255, 200, 0, ${alpha * 0.3})`;
        this.currentCtx.beginPath();
        this.currentCtx.arc(0, 0, currentRadius, 0, Math.PI * 2);
        this.currentCtx.fill();
        
        // Inner blast
        this.currentCtx.fillStyle = `rgba(255, 255, 0, ${alpha * 0.6})`;
        this.currentCtx.beginPath();
        this.currentCtx.arc(0, 0, currentRadius * 0.6, 0, Math.PI * 2);
        this.currentCtx.fill();
        
        // Core
        this.currentCtx.fillStyle = `rgba(255, 255, 255, ${alpha})`;
        this.currentCtx.beginPath();
        this.currentCtx.arc(0, 0, currentRadius * 0.3, 0, Math.PI * 2);
        this.currentCtx.fill();
    }
    
    drawDefault(size, color, rotation) {
        this.currentCtx.rotate(rotation);
        
        this.currentCtx.fillStyle = color;
        this.currentCtx.beginPath();
        this.currentCtx.arc(0, 0, size, 0, Math.PI * 2);
        this.currentCtx.fill();
        
        // Draw direction indicator
        this.currentCtx.strokeStyle = color;
        this.currentCtx.lineWidth = 2;
        this.currentCtx.beginPath();
        this.currentCtx.moveTo(0, 0);
        this.currentCtx.lineTo(size, 0);
        this.currentCtx.stroke();
    }
    
    drawHealthBar(x, y, width, healthPercent) {
        const height = 4;
        
        // Background
        this.currentCtx.fillStyle = '#333';
        this.currentCtx.fillRect(x - width/2, y, width, height);
        
        // Health
        this.currentCtx.fillStyle = healthPercent > 0.5 ? '#00ff00' : 
                         healthPercent > 0.25 ? '#ffff00' : '#ff0000';
        this.currentCtx.fillRect(x - width/2, y, width * healthPercent, height);
        
        // Border
        this.currentCtx.strokeStyle = '#666';
        this.currentCtx.lineWidth = 1;
        this.currentCtx.strokeRect(x - width/2, y, width, height);
    }
    
    drawDeathMessage(width, height, respawnTimer) {
        this.currentCtx.fillStyle = 'rgba(0, 0, 0, 0.7)';
        this.currentCtx.fillRect(0, height/2 - 50, width, 100);
        
        this.currentCtx.fillStyle = '#ff0000';
        this.currentCtx.font = 'bold 36px monospace';
        this.currentCtx.textAlign = 'center';
        this.currentCtx.fillText('YOU DIED', width/2, height/2);
        
        this.currentCtx.fillStyle = '#ffffff';
        this.currentCtx.font = '20px monospace';
        this.currentCtx.fillText(`Respawning in ${Math.ceil(respawnTimer)} seconds...`, width/2, height/2 + 30);
        
        this.currentCtx.textAlign = 'left';
    }
}

// ES6 Module exports for the canvas component
export function initCanvas(canvas) {
    // Legacy single canvas support
    return new CanvasContext(canvas);
}

export function initDualCanvas(canvasA, canvasB, dotNetRef) {
    // For backward compatibility - create a triple canvas with only two canvases
    // Third canvas will be created dynamically
    const canvasC = canvasA.cloneNode(false);
    canvasC.style.display = 'none';
    canvasA.parentNode.appendChild(canvasC);
    return new TripleCanvasContext(canvasA, canvasB, canvasC, dotNetRef);
}

export function initTripleCanvas(canvasA, canvasB, canvasC, dotNetRef) {
    return new TripleCanvasContext(canvasA, canvasB, canvasC, dotNetRef);
}
// Double buffered canvas rendering context for smooth animation
class DoubleBufferedCanvasContext {
    constructor(visibleCanvas, bufferCanvas, dotNetRef) {
        this.visibleCanvas = visibleCanvas;
        this.bufferCanvas = bufferCanvas;
        this.visibleCtx = visibleCanvas.getContext('2d');
        this.bufferCtx = bufferCanvas.getContext('2d');
        this.dotNetRef = dotNetRef;
        
        // Current drawing context (always the buffer)
        this.currentCtx = this.bufferCtx;
        this.currentCanvas = this.bufferCanvas;
        
        this.explosionParticles = [];
        
        // Add mouse listeners to visible canvas only
        this.addMouseListeners(this.visibleCanvas);
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
    
    // Flip buffers - copy buffer to visible canvas
    flipBuffers() {
        // Copy entire buffer to visible canvas in one operation
        this.visibleCtx.drawImage(this.bufferCanvas, 0, 0);
        
        // Clear buffer for next frame
        this.bufferCtx.clearRect(0, 0, this.bufferCanvas.width, this.bufferCanvas.height);
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
                this.drawEnemy(size, color, rotation, subType, state, stateTimer);
                break;
            case 'Bullet':
                this.drawBullet(size, color, rotation);
                break;
            case 'Explosion':
                this.drawExplosion(size, stateTimer, subType);
                break;
            case 'Factory':
                this.drawFactory(size, color, rotation);
                break;
            case 'Asteroid':
                this.drawAsteroid(size, color, rotation, subType);
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
    
    drawEnemy(size, color, rotation, subType, state = 'Active', stateTimer = 0) {
        this.currentCtx.rotate(rotation);
        
        // Draw Scout alert indicator if in Alerting state
        if (subType === 4 && state === 'Alerting') {
            this.drawScoutAlertIndicator(size, stateTimer, rotation);
        }
        
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
                
            case 4: // Scout
                // Purple hexagon shape
                this.currentCtx.fillStyle = '#aa44ff';
                this.currentCtx.beginPath();
                for (let i = 0; i < 6; i++) {
                    const angle = (i / 6) * Math.PI * 2 - Math.PI / 2;
                    const x = Math.cos(angle) * size;
                    const y = Math.sin(angle) * size;
                    if (i === 0) this.currentCtx.moveTo(x, y);
                    else this.currentCtx.lineTo(x, y);
                }
                this.currentCtx.closePath();
                this.currentCtx.fill();
                
                // Eye
                this.currentCtx.fillStyle = '#ffff00';
                this.currentCtx.beginPath();
                this.currentCtx.arc(0, 0, size * 0.3, 0, Math.PI * 2);
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
    
    drawScoutAlertIndicator(size, stateTimer, rotation) {
        // Get progress (0-3 seconds)
        const progress = Math.min(stateTimer / 3, 1);
        
        // Determine how many bars to show
        let barsToShow = 1;
        if (progress > 0.33) barsToShow = 2;
        if (progress > 0.66) barsToShow = 3;
        
        // Color based on progress
        const isReady = progress >= 1;
        const color = isReady ? '#00ff00' : '#888888';
        
        // Flash when ready
        const flash = isReady ? (Math.sin(Date.now() * 0.01) + 1) * 0.5 : 1;
        const alpha = isReady ? (0.4 + flash * 0.6) : 0.8;
        
        this.currentCtx.save();
        this.currentCtx.rotate(-rotation); // Unrotate to draw in world space
        
        // Draw wifi arcs
        this.currentCtx.strokeStyle = color;
        this.currentCtx.globalAlpha = alpha;
        this.currentCtx.lineWidth = 2;
        
        // Draw concentric arcs based on progress
        for (let i = 1; i <= barsToShow; i++) {
            const radius = size * 1.5 + i * 10;
            this.currentCtx.beginPath();
            this.currentCtx.arc(0, 0, radius, rotation - Math.PI/6, rotation + Math.PI/6);
            this.currentCtx.stroke();
        }
        
        this.currentCtx.restore();
    }
    
    drawFactory(size, color, rotation) {
        // Draw factory building (large square structure)
        const buildingSize = size * 2;
        
        // Base building
        this.currentCtx.fillStyle = '#444444';
        this.currentCtx.fillRect(-buildingSize, -buildingSize, buildingSize * 2, buildingSize * 2);
        
        // Building details
        this.currentCtx.fillStyle = '#666666';
        this.currentCtx.fillRect(-buildingSize * 0.8, -buildingSize * 0.8, buildingSize * 1.6, buildingSize * 1.6);
        
        // Factory symbol (gear)
        this.currentCtx.fillStyle = '#888888';
        this.currentCtx.beginPath();
        const teeth = 8;
        for (let i = 0; i < teeth * 2; i++) {
            const angle = (i / (teeth * 2)) * Math.PI * 2;
            const radius = i % 2 === 0 ? size * 0.8 : size * 0.5;
            const x = Math.cos(angle) * radius;
            const y = Math.sin(angle) * radius;
            if (i === 0) this.currentCtx.moveTo(x, y);
            else this.currentCtx.lineTo(x, y);
        }
        this.currentCtx.closePath();
        this.currentCtx.fill();
        
        // Center hole
        this.currentCtx.fillStyle = '#666666';
        this.currentCtx.beginPath();
        this.currentCtx.arc(0, 0, size * 0.3, 0, Math.PI * 2);
        this.currentCtx.fill();
    }
    
    drawAsteroid(size, color, rotation, subType) {
        // Rotate for visual variety
        this.currentCtx.rotate(rotation);
        
        // Draw asteroid as an irregular rocky shape
        this.currentCtx.fillStyle = '#8B7355'; // Brownish rock color
        this.currentCtx.strokeStyle = '#5C4A3B';
        this.currentCtx.lineWidth = 2;
        
        // Create irregular shape with multiple points
        const points = 8;
        const angleStep = (Math.PI * 2) / points;
        
        this.currentCtx.beginPath();
        for (let i = 0; i < points; i++) {
            const angle = i * angleStep;
            // Add some randomness to radius for irregular shape
            const radiusVariation = 0.7 + (Math.sin(i * 1.7) * 0.3);
            const radius = size * radiusVariation;
            const x = Math.cos(angle) * radius;
            const y = Math.sin(angle) * radius;
            
            if (i === 0) {
                this.currentCtx.moveTo(x, y);
            } else {
                this.currentCtx.lineTo(x, y);
            }
        }
        this.currentCtx.closePath();
        this.currentCtx.fill();
        this.currentCtx.stroke();
        
        // Add some crater details
        this.currentCtx.fillStyle = '#6B5A45';
        for (let i = 0; i < 3; i++) {
            const craterAngle = (i * 2.3);
            const craterDist = size * 0.5;
            const craterX = Math.cos(craterAngle) * craterDist;
            const craterY = Math.sin(craterAngle) * craterDist;
            const craterSize = size * 0.15;
            
            this.currentCtx.beginPath();
            this.currentCtx.arc(craterX, craterY, craterSize, 0, Math.PI * 2);
            this.currentCtx.fill();
        }
        
        // Add movement indicator for moving asteroids
        if (subType === 2) { // Moving asteroid
            this.currentCtx.strokeStyle = 'rgba(255, 255, 255, 0.3)';
            this.currentCtx.lineWidth = 1;
            this.currentCtx.setLineDash([5, 5]);
            this.currentCtx.beginPath();
            this.currentCtx.moveTo(-size * 1.5, 0);
            this.currentCtx.lineTo(-size * 0.8, 0);
            this.currentCtx.stroke();
            this.currentCtx.setLineDash([]);
        }
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

// Canvas initialization functions
function initCanvas(canvas) {
    // Legacy single canvas support
    return new CanvasContext(canvas);
}

function initDualCanvas(canvasA, canvasB, dotNetRef) {
    // For backward compatibility - use double buffering
    return new DoubleBufferedCanvasContext(canvasA, canvasB, dotNetRef);
}

// Make functions available globally
window.initCanvas = initCanvas;
window.initDualCanvas = initDualCanvas;

function initDoubleBufferedCanvas(visibleCanvas, bufferCanvas, dotNetRef) {
    return new DoubleBufferedCanvasContext(visibleCanvas, bufferCanvas, dotNetRef);
}

// Make it available globally
window.initDoubleBufferedCanvas = initDoubleBufferedCanvas;

// Helper for requestAnimationFrame callback
window.requestAnimationFrame = window.requestAnimationFrame || 
    window.webkitRequestAnimationFrame || 
    window.mozRequestAnimationFrame || 
    function(callback) { return setTimeout(callback, 16); };

// Animation loop helper for Blazor
window.startCanvasAnimationLoop = function(canvasContext, dotNetRef) {
    let animationId = null;
    let isRunning = true;
    
    async function animate() {
        if (!isRunning) return;
        
        try {
            // Tell Blazor to render a frame
            await dotNetRef.invokeMethodAsync('OnAnimationFrame');
            // Flip the buffers
            canvasContext.flipBuffers();
        } catch (error) {
            console.error('Animation frame error:', error);
        }
        
        // Schedule next frame
        animationId = requestAnimationFrame(animate);
    }
    
    // Start the loop
    animate();
    
    // Return a handle to stop the animation
    return {
        stop: function() {
            isRunning = false;
            if (animationId) {
                cancelAnimationFrame(animationId);
            }
        }
    };
};
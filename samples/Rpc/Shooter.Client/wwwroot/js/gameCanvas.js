// Canvas rendering context wrapper that provides the methods expected by Blazor
class CanvasContext {
    constructor(canvas) {
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d');
    }
    
    clearCanvas(width, height) {
        this.ctx.fillStyle = '#000033';
        this.ctx.fillRect(0, 0, width, height);
        
        // Draw grid
        this.ctx.strokeStyle = '#003366';
        this.ctx.lineWidth = 0.5;
        const gridSize = 50;
        
        for (let x = 0; x < width; x += gridSize) {
            this.ctx.beginPath();
            this.ctx.moveTo(x, 0);
            this.ctx.lineTo(x, height);
            this.ctx.stroke();
        }
        
        for (let y = 0; y < height; y += gridSize) {
            this.ctx.beginPath();
            this.ctx.moveTo(0, y);
            this.ctx.lineTo(width, y);
            this.ctx.stroke();
        }
    }
    
    drawEntity(x, y, size, color, rotation) {
        this.ctx.save();
        this.ctx.translate(x, y);
        this.ctx.rotate(rotation);
        
        this.ctx.fillStyle = color;
        this.ctx.beginPath();
        this.ctx.arc(0, 0, size, 0, Math.PI * 2);
        this.ctx.fill();
        
        // Draw direction indicator
        this.ctx.strokeStyle = color;
        this.ctx.lineWidth = 2;
        this.ctx.beginPath();
        this.ctx.moveTo(0, 0);
        this.ctx.lineTo(size, 0);
        this.ctx.stroke();
        
        this.ctx.restore();
    }
    
    drawHealthBar(x, y, width, healthPercent) {
        const height = 4;
        
        // Background
        this.ctx.fillStyle = '#333';
        this.ctx.fillRect(x - width/2, y, width, height);
        
        // Health
        this.ctx.fillStyle = healthPercent > 0.5 ? '#00ff00' : 
                         healthPercent > 0.25 ? '#ffff00' : '#ff0000';
        this.ctx.fillRect(x - width/2, y, width * healthPercent, height);
    }
}

// ES6 Module exports for the canvas component
export function initCanvas(canvas) {
    return new CanvasContext(canvas);
}
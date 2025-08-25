// ActionServer Phaser View - Read-only visualization of server state
let game;
let connection;
let serverConfig = {};
let viewSettings = {
    showLocal: true,
    showAdjacent: false,
    selectedZones: []
};
let adjacentZones = [];
let currentZone = null;
let updateCounter = 0;
let lastUpdateTime = Date.now();

// Entity sprites
let entitySprites = new Map();
let zoneGraphics = null;
let gridGraphics = null;

// Colors for different entity types and zones
const COLORS = {
    PLAYER_LOCAL: 0x4CAF50,
    PLAYER_ADJACENT: 0x81C784,
    ENEMY: 0xf44336,
    FACTORY: 0xFF9800,
    ASTEROID: 0x9E9E9E,
    PROJECTILE: 0xFFEB3B,
    ZONE_BORDER: 0x444444,
    ZONE_CURRENT: 0x9C27B0,
    ZONE_ADJACENT: 0x2196F3,
    GRID: 0x222222
};

// Initialize SignalR connection
async function initConnection() {
    connection = new signalR.HubConnectionBuilder()
        .withUrl("/worldStateHub")
        .withAutomaticReconnect()
        .build();
    
    connection.on("serverConfig", (config) => {
        serverConfig = config;
        currentZone = config.AssignedZone;
        document.getElementById('zoneInfo').textContent = `(${config.AssignedZone.x}, ${config.AssignedZone.y})`;
        document.getElementById('instanceInfo').textContent = config.ConnectionId.substring(0, 8);
        console.log("Server config received:", config);
        
        // Load adjacent zones
        loadAdjacentZones();
    });
    
    connection.on("worldStateUpdate", (worldState) => {
        if (game && game.scene.keys.main) {
            game.scene.keys.main.updateWorldState(worldState);
        }
        updateStats(worldState);
        updateCounter++;
    });
    
    connection.onreconnecting(() => {
        document.getElementById('connectionStatus').className = 'status-indicator status-disconnected';
        console.log("Reconnecting to server...");
    });
    
    connection.onreconnected(() => {
        document.getElementById('connectionStatus').className = 'status-indicator status-connected';
        console.log("Reconnected to server");
    });
    
    try {
        await connection.start();
        document.getElementById('connectionStatus').className = 'status-indicator status-connected';
        console.log("Connected to WorldStateHub");
    } catch (err) {
        console.error("Failed to connect:", err);
        document.getElementById('connectionStatus').className = 'status-indicator status-disconnected';
    }
}

// Load adjacent zones from server
async function loadAdjacentZones() {
    try {
        const response = await fetch('/api/phaser/adjacent-zones');
        const zones = await response.json();
        adjacentZones = zones;
        
        // Update zone selector UI
        updateZoneSelector();
    } catch (err) {
        console.error("Failed to load adjacent zones:", err);
    }
}

// Update zone selector buttons
function updateZoneSelector() {
    const selector = document.getElementById('zoneSelector');
    selector.innerHTML = '';
    
    adjacentZones.forEach(zone => {
        const btn = document.createElement('button');
        btn.className = 'zone-btn';
        btn.textContent = `(${zone.zone.x},${zone.zone.y})`;
        
        // Check if this is the current zone
        if (currentZone && zone.zone.x === currentZone.x && zone.zone.y === currentZone.y) {
            btn.className += ' current';
            btn.disabled = true;
            btn.title = 'Current Zone';
        } else {
            // Check if this zone is selected
            if (viewSettings.selectedZones.some(z => z.x === zone.zone.x && z.y === zone.zone.y)) {
                btn.className += ' selected';
            }
            
            btn.onclick = () => toggleZoneSelection(zone.zone);
        }
        
        selector.appendChild(btn);
    });
}

// Toggle zone selection
function toggleZoneSelection(zone) {
    const index = viewSettings.selectedZones.findIndex(z => z.x === zone.x && z.y === zone.y);
    if (index >= 0) {
        viewSettings.selectedZones.splice(index, 1);
    } else {
        viewSettings.selectedZones.push(zone);
    }
    
    updateZoneSelector();
    updateViewSettings();
}

// Update view settings on server
async function updateViewSettings() {
    if (connection && connection.state === signalR.HubConnectionState.Connected) {
        try {
            await connection.invoke("UpdateViewSettings", 
                viewSettings.showLocal, 
                viewSettings.showAdjacent, 
                viewSettings.selectedZones);
        } catch (err) {
            console.error("Failed to update view settings:", err);
        }
    }
}

// Toggle local entities view
function toggleLocal() {
    viewSettings.showLocal = !viewSettings.showLocal;
    document.getElementById('localBtn').classList.toggle('active');
    updateViewSettings();
}

// Toggle adjacent zones view
function toggleAdjacent() {
    viewSettings.showAdjacent = !viewSettings.showAdjacent;
    document.getElementById('adjacentBtn').classList.toggle('active');
    document.getElementById('zoneSelectGroup').style.display = 
        viewSettings.showAdjacent ? 'block' : 'none';
    updateViewSettings();
}

// Update statistics display
function updateStats(worldState) {
    let localCount = 0;
    let adjacentCount = 0;
    let playerCount = 0;
    let enemyCount = 0;
    let factoryCount = 0;
    
    if (worldState.localEntities) {
        localCount = worldState.localEntities.length;
        worldState.localEntities.forEach(entity => {
            if (entity.type === 0) playerCount++; // Player
            else if (entity.type === 1) enemyCount++; // Enemy
            else if (entity.type === 2) factoryCount++; // Factory
        });
    }
    
    if (worldState.adjacentZoneEntities) {
        Object.values(worldState.adjacentZoneEntities).forEach(entities => {
            adjacentCount += entities.length;
            entities.forEach(entity => {
                if (entity.type === 0) playerCount++; // Player
                else if (entity.type === 1) enemyCount++; // Enemy
                else if (entity.type === 2) factoryCount++; // Factory
            });
        });
    }
    
    document.getElementById('localEntityCount').textContent = localCount;
    document.getElementById('adjacentEntityCount').textContent = adjacentCount;
    document.getElementById('playerCount').textContent = playerCount;
    document.getElementById('enemyCount').textContent = enemyCount;
    document.getElementById('factoryCount').textContent = factoryCount;
    
    // Calculate update rate
    const now = Date.now();
    if (now - lastUpdateTime > 1000) {
        const rate = Math.round(updateCounter / ((now - lastUpdateTime) / 1000));
        document.getElementById('updateRate').textContent = rate + '/s';
        updateCounter = 0;
        lastUpdateTime = now;
    }
}

// Phaser game configuration
const config = {
    type: Phaser.AUTO,
    width: 800,
    height: 600,
    parent: 'phaser-container',
    backgroundColor: '#000000',
    scene: {
        preload: preload,
        create: create,
        update: update
    },
    physics: {
        default: 'arcade',
        arcade: {
            gravity: { y: 0 },
            debug: false
        }
    }
};

function preload() {
    // Create simple colored rectangles for entities
    this.load.image('player', 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==');
}

function create() {
    // Store scene reference
    this.worldScale = 0.8; // Scale to fit world in view
    this.worldOffset = { x: 400, y: 300 }; // Center of screen
    
    // Create grid graphics
    gridGraphics = this.add.graphics();
    drawGrid.call(this);
    
    // Create zone boundary graphics
    zoneGraphics = this.add.graphics();
    
    // Create entity container
    this.entityContainer = this.add.container();
    
    // FPS display
    this.fpsText = this.add.text(10, 10, 'FPS: 0', { 
        fontSize: '14px', 
        fill: '#00ff00' 
    });
    
    // Zone info text
    this.zoneText = this.add.text(10, 30, '', { 
        fontSize: '12px', 
        fill: '#ffffff' 
    });
    
    // Store update function
    this.updateWorldState = updateWorldState;
}

function update(time, delta) {
    // Update FPS display
    const fps = Math.round(this.game.loop.actualFps);
    this.fpsText.setText(`FPS: ${fps}`);
    document.getElementById('fpsInfo').textContent = fps;
}

function drawGrid() {
    gridGraphics.clear();
    gridGraphics.lineStyle(1, COLORS.GRID, 0.3);
    
    // Draw grid lines every 100 units
    const gridSize = 100;
    const gridCount = 20;
    
    for (let i = 0; i <= gridCount; i++) {
        const x = i * gridSize * this.worldScale;
        const y = i * gridSize * this.worldScale;
        
        // Vertical lines
        gridGraphics.moveTo(x, 0);
        gridGraphics.lineTo(x, 600);
        
        // Horizontal lines
        gridGraphics.moveTo(0, y);
        gridGraphics.lineTo(800, y);
    }
    
    gridGraphics.strokePath();
}

function updateWorldState(worldState) {
    const scene = this;
    
    // Clear existing sprites
    entitySprites.forEach(sprite => sprite.destroy());
    entitySprites.clear();
    
    // Draw zone boundaries
    drawZoneBoundaries.call(scene, worldState);
    
    // Draw local entities
    if (worldState.localEntities && viewSettings.showLocal) {
        worldState.localEntities.forEach(entity => {
            drawEntity.call(scene, entity, true);
        });
    }
    
    // Draw adjacent zone entities
    if (worldState.adjacentZoneEntities && viewSettings.showAdjacent) {
        Object.entries(worldState.adjacentZoneEntities).forEach(([zoneKey, entities]) => {
            entities.forEach(entity => {
                drawEntity.call(scene, entity, false);
            });
        });
    }
    
    // Update zone text
    if (worldState.localZone) {
        scene.zoneText.setText(`Zone: (${worldState.localZone.x}, ${worldState.localZone.y})`);
    }
}

function drawZoneBoundaries(worldState) {
    zoneGraphics.clear();
    
    // Draw current zone boundary
    if (worldState.localZone) {
        zoneGraphics.lineStyle(2, COLORS.ZONE_CURRENT, 1);
        const x = worldState.localZone.x * 1000 * this.worldScale;
        const y = worldState.localZone.y * 1000 * this.worldScale;
        zoneGraphics.strokeRect(x, y, 1000 * this.worldScale, 1000 * this.worldScale);
    }
    
    // Draw selected adjacent zones
    if (viewSettings.showAdjacent) {
        zoneGraphics.lineStyle(1, COLORS.ZONE_ADJACENT, 0.5);
        viewSettings.selectedZones.forEach(zone => {
            const x = zone.x * 1000 * this.worldScale;
            const y = zone.y * 1000 * this.worldScale;
            zoneGraphics.strokeRect(x, y, 1000 * this.worldScale, 1000 * this.worldScale);
        });
    }
}

function drawEntity(entity, isLocal) {
    const scene = this;
    
    // Calculate screen position
    const screenX = entity.position.x * scene.worldScale;
    const screenY = entity.position.y * scene.worldScale;
    
    // Skip if outside view
    if (screenX < 0 || screenX > 800 || screenY < 0 || screenY > 600) {
        return;
    }
    
    // Determine entity color and size
    let color = COLORS.ASTEROID;
    let size = 5;
    
    switch (entity.type) {
        case 0: // Player
            color = isLocal ? COLORS.PLAYER_LOCAL : COLORS.PLAYER_ADJACENT;
            size = 8;
            break;
        case 1: // Enemy
            color = COLORS.ENEMY;
            size = 6;
            break;
        case 2: // Factory
            color = COLORS.FACTORY;
            size = 10;
            break;
        case 3: // Projectile
            color = COLORS.PROJECTILE;
            size = 3;
            break;
        case 4: // Asteroid
            color = COLORS.ASTEROID;
            size = 7;
            break;
    }
    
    // Create sprite
    const graphics = scene.add.graphics();
    
    // Draw entity based on type
    if (entity.type === 2) { // Factory - draw as square
        graphics.fillStyle(color, entity.state === 2 ? 0.3 : 1); // Fade if dead
        graphics.fillRect(screenX - size/2, screenY - size/2, size, size);
    } else { // Others - draw as circle
        graphics.fillStyle(color, entity.state === 2 ? 0.3 : 1); // Fade if dead
        graphics.fillCircle(screenX, screenY, size);
    }
    
    // Draw health bar for entities with health
    if (entity.health > 0 && entity.maxHealth > 0 && entity.type !== 3) {
        const healthPercent = entity.health / entity.maxHealth;
        const barWidth = 20;
        const barHeight = 3;
        
        graphics.fillStyle(0x000000, 0.5);
        graphics.fillRect(screenX - barWidth/2, screenY - size - 8, barWidth, barHeight);
        
        graphics.fillStyle(healthPercent > 0.5 ? 0x00ff00 : healthPercent > 0.25 ? 0xffff00 : 0xff0000, 1);
        graphics.fillRect(screenX - barWidth/2, screenY - size - 8, barWidth * healthPercent, barHeight);
    }
    
    // Draw name for players
    if (entity.type === 0 && entity.playerName) {
        const text = scene.add.text(screenX, screenY + size + 5, entity.playerName, {
            fontSize: '10px',
            fill: '#ffffff',
            align: 'center'
        });
        text.setOrigin(0.5, 0);
        entitySprites.set(entity.entityId + '_name', text);
    }
    
    // Store sprite
    entitySprites.set(entity.entityId, graphics);
}

// Initialize when page loads
window.addEventListener('load', async () => {
    // Initialize SignalR connection
    await initConnection();
    
    // Initialize Phaser game
    game = new Phaser.Game(config);
    
    // Load initial configuration
    try {
        // Check if we have a target zone from the URL
        const targetZone = window.phaserViewConfig?.targetZone;
        let configUrl = '/api/phaser/config';
        
        if (targetZone) {
            configUrl += `?x=${targetZone.x}&y=${targetZone.y}`;
        }
        
        const response = await fetch(configUrl);
        const config = await response.json();
        
        // Update UI to show the target zone instead of assigned zone if viewing a specific zone
        const displayZone = config.TargetZone || config.AssignedZone;
        if (displayZone) {
            document.getElementById('zoneInfo').textContent = `(${displayZone.X || displayZone.x}, ${displayZone.Y || displayZone.y})`;
        } else {
            document.getElementById('zoneInfo').textContent = 'Zone: Unknown';
            console.warn('No zone information received from server:', config);
        }
        document.getElementById('instanceInfo').textContent = config.ServerInstanceId || 'Unknown';
        
        // Store the full config for later use
        serverConfig = config;
        currentZone = displayZone;
        
        console.log("Server config received:", config);
        if (targetZone) {
            console.log(`Viewing zone (${targetZone.x}, ${targetZone.y}) - may be different from server's assigned zone`);
        }
    } catch (err) {
        console.error("Failed to load config:", err);
    }
});
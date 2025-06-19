class GamePhaser {
    constructor() {
        this.game = null;
        this.scene = null;
        this.worldState = null;
        this.sprites = new Map();
        this.playerId = null;
        this.dotNetReference = null;
        this.mousePosition = { x: 0, y: 0 };
        this.availableZones = [];
        this.currentZone = null;
        this.currentServer = null;
        this.serverZone = null;
        this.playerZone = null;
        this.preEstablishedConnections = {}; // Track pre-established connections
        this.lastZoneMismatchLog = null; // Track last time we logged zone mismatch
    }

    init(dotNetReference, containerId, playerId) {
        this.dotNetReference = dotNetReference;
        this.playerId = playerId;

        const config = {
            type: Phaser.AUTO,
            parent: containerId,
            width: 800,
            height: 600,
            backgroundColor: '#000033',
            scene: {
                preload: this.preload.bind(this),
                create: this.create.bind(this),
                update: this.update.bind(this)
            },
            physics: {
                default: 'arcade',
                arcade: {
                    debug: false
                }
            }
        };

        this.game = new Phaser.Game(config);
        return true;
    }

    preload() {
        // Get the current scene
        this.scene = this.game.scene.scenes[0];
        
        // Create sprites with different shapes
        this.createPlayerSprite();
        this.createEnemySprites();
        this.createBulletSprites();
        this.createExplosionSprites();
    }
    
    createPlayerSprite() {
        console.log('Creating player sprite texture');
        const graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        // Player - bright green circle with direction indicator
        const size = 15;
        graphics.fillStyle(0x00ff00, 1);
        graphics.fillCircle(size, size, size);
        
        // Add darker green border
        graphics.lineStyle(2, 0x008800, 1);
        graphics.strokeCircle(size, size, size);
        
        // Add direction indicator (small triangle pointing up)
        graphics.fillStyle(0xffffff, 1);
        graphics.beginPath();
        graphics.moveTo(size, 5);
        graphics.lineTo(size - 3, 10);
        graphics.lineTo(size + 3, 10);
        graphics.closePath();
        graphics.fillPath();
        
        graphics.generateTexture('player', size * 2, size * 2);
        graphics.destroy();
        console.log('Player texture created');
    }
    
    createEnemySprites() {
        console.log('Creating enemy sprite textures');
        
        // Kamikaze - small red triangle (aggressive shape)
        let graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        const kamSize = 10;
        graphics.fillStyle(0xff4444, 1);
        graphics.beginPath();
        graphics.moveTo(kamSize, 2);
        graphics.lineTo(2, kamSize * 2 - 2);
        graphics.lineTo(kamSize * 2 - 2, kamSize * 2 - 2);
        graphics.closePath();
        graphics.fillPath();
        
        // Add dark red border
        graphics.lineStyle(1, 0x880000, 1);
        graphics.strokePath();
        
        graphics.generateTexture('enemy-kamikaze', kamSize * 2, kamSize * 2);
        graphics.destroy();
        console.log('Kamikaze texture created');
        
        // Sniper - green square with crosshair
        graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        const snipSize = 12;
        graphics.fillStyle(0x44ff44, 1);
        graphics.fillRect(0, 0, snipSize * 2, snipSize * 2);
        
        // Dark green border
        graphics.lineStyle(2, 0x008800, 1);
        graphics.strokeRect(0, 0, snipSize * 2, snipSize * 2);
        
        // Crosshair in center
        graphics.lineStyle(1, 0x000000, 0.8);
        graphics.moveTo(snipSize, 4);
        graphics.lineTo(snipSize, snipSize * 2 - 4);
        graphics.moveTo(4, snipSize);
        graphics.lineTo(snipSize * 2 - 4, snipSize);
        graphics.strokePath();
        
        graphics.generateTexture('enemy-sniper', snipSize * 2, snipSize * 2);
        graphics.destroy();
        
        // Strafing - orange diamond (nimble shape)
        graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        const strafSize = 12;
        graphics.fillStyle(0xffaa44, 1);
        graphics.beginPath();
        graphics.moveTo(strafSize, 2);
        graphics.lineTo(strafSize * 2 - 2, strafSize);
        graphics.lineTo(strafSize, strafSize * 2 - 2);
        graphics.lineTo(2, strafSize);
        graphics.closePath();
        graphics.fillPath();
        
        // Orange border
        graphics.lineStyle(1, 0xff6600, 1);
        graphics.strokePath();
        
        graphics.generateTexture('enemy-strafing', strafSize * 2, strafSize * 2);
        graphics.destroy();
    }
    
    createBulletSprites() {
        // Player bullet - yellow
        let graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        graphics.fillStyle(0xffff00, 1);
        graphics.fillCircle(2, 4, 2);
        graphics.generateTexture('bullet', 4, 8);
        graphics.destroy();
        
        // Enemy bullet - purple
        graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        graphics.fillStyle(0xff00ff, 1);
        graphics.fillCircle(2, 4, 2);
        graphics.generateTexture('enemy-bullet', 4, 8);
        graphics.destroy();
    }
    
    createExplosionSprites() {
        // Large explosion - multi-layered burst
        let graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        const largeSize = 25;
        
        // Outer orange layer
        graphics.fillStyle(0xff8800, 0.6);
        graphics.fillCircle(largeSize, largeSize, largeSize);
        
        // Middle yellow layer
        graphics.fillStyle(0xffff00, 0.8);
        graphics.fillCircle(largeSize, largeSize, largeSize * 0.7);
        
        // Inner white core
        graphics.fillStyle(0xffffff, 0.9);
        graphics.fillCircle(largeSize, largeSize, largeSize * 0.4);
        
        // Add some spikes for explosion effect
        graphics.fillStyle(0xff8800, 0.7);
        for (let i = 0; i < 8; i++) {
            const angle = (i / 8) * Math.PI * 2;
            const x = largeSize + Math.cos(angle) * largeSize * 0.8;
            const y = largeSize + Math.sin(angle) * largeSize * 0.8;
            graphics.fillCircle(x, y, 5);
        }
        
        graphics.generateTexture('explosion', largeSize * 2, largeSize * 2);
        graphics.destroy();
        
        // Small explosion
        graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        const smallSize = 15;
        
        graphics.fillStyle(0xff8800, 0.7);
        graphics.fillCircle(smallSize, smallSize, smallSize);
        graphics.fillStyle(0xffff00, 0.9);
        graphics.fillCircle(smallSize, smallSize, smallSize * 0.6);
        
        graphics.generateTexture('explosion-small', smallSize * 2, smallSize * 2);
        graphics.destroy();
    }

    createColoredSprite(key, color, width, height) {
        // Create a graphics object
        const graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        
        // Draw the shape
        graphics.fillStyle(color, 1);
        graphics.fillRect(0, 0, width, height);
        
        // Add a border for better visibility
        graphics.lineStyle(1, 0xffffff, 0.5);
        graphics.strokeRect(0, 0, width, height);
        
        // Generate texture
        graphics.generateTexture(key, width, height);
        graphics.destroy();
    }

    create() {
        // Create grid lines (scrollFactor 1 means they move with the world)
        this.gridGraphics = this.scene.add.graphics();
        this.gridGraphics.setScrollFactor(1);
        this.drawGrid();

        // Create UI text (scrollFactor 0 makes them stay fixed on screen)
        this.zoneText = this.scene.add.text(10, 10, '', { 
            fontSize: '16px', 
            fill: '#ffffff' 
        }).setScrollFactor(0);
        this.serverText = this.scene.add.text(10, 30, '', { 
            fontSize: '16px', 
            fill: '#ffffff' 
        }).setScrollFactor(0);
        this.playerText = this.scene.add.text(10, 50, '', { 
            fontSize: '16px', 
            fill: '#ffffff' 
        }).setScrollFactor(0);
        
        // Log camera bounds
        console.log('Phaser camera bounds:', {
            x: this.scene.cameras.main.x,
            y: this.scene.cameras.main.y,
            width: this.scene.cameras.main.width,
            height: this.scene.cameras.main.height
        });

        // Mouse input
        this.scene.input.on('pointermove', (pointer) => {
            this.mousePosition = { x: pointer.x, y: pointer.y };
        });

        this.scene.input.on('pointerdown', (pointer) => {
            if (pointer.leftButtonDown()) {
                this.handleLeftClick(pointer.x, pointer.y);
            } else if (pointer.rightButtonDown()) {
                this.handleRightClick(pointer.x, pointer.y);
            }
        });

        // Keyboard input
        this.keys = this.scene.input.keyboard.addKeys({
            'W': Phaser.Input.Keyboard.KeyCodes.W,
            'A': Phaser.Input.Keyboard.KeyCodes.A,
            'S': Phaser.Input.Keyboard.KeyCodes.S,
            'D': Phaser.Input.Keyboard.KeyCodes.D,
            'ZERO': Phaser.Input.Keyboard.KeyCodes.ZERO,
            'ONE': Phaser.Input.Keyboard.KeyCodes.ONE,
            'TWO': Phaser.Input.Keyboard.KeyCodes.TWO,
            'THREE': Phaser.Input.Keyboard.KeyCodes.THREE,
            'FOUR': Phaser.Input.Keyboard.KeyCodes.FOUR,
            'FIVE': Phaser.Input.Keyboard.KeyCodes.FIVE,
            'SIX': Phaser.Input.Keyboard.KeyCodes.SIX,
            'SEVEN': Phaser.Input.Keyboard.KeyCodes.SEVEN,
            'EIGHT': Phaser.Input.Keyboard.KeyCodes.EIGHT,
            'NINE': Phaser.Input.Keyboard.KeyCodes.NINE
        });

        // Prevent right-click context menu
        this.scene.game.canvas.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            return false;
        });
    }

    update() {
        // Handle keyboard input
        let moveX = 0;
        let moveY = 0;
        let speed = 1;

        // Check number keys for speed
        if (this.keys.ZERO.isDown) speed = 0;
        else if (this.keys.ONE.isDown) speed = 0.1;
        else if (this.keys.TWO.isDown) speed = 0.2;
        else if (this.keys.THREE.isDown) speed = 0.3;
        else if (this.keys.FOUR.isDown) speed = 0.4;
        else if (this.keys.FIVE.isDown) speed = 0.5;
        else if (this.keys.SIX.isDown) speed = 0.6;
        else if (this.keys.SEVEN.isDown) speed = 0.7;
        else if (this.keys.EIGHT.isDown) speed = 0.8;
        else if (this.keys.NINE.isDown) speed = 0.9;

        // Check WASD for movement
        if (this.keys.W.isDown) moveY = -1;
        if (this.keys.S.isDown) moveY = 1;
        if (this.keys.A.isDown) moveX = -1;
        if (this.keys.D.isDown) moveX = 1;

        // Normalize diagonal movement
        if (moveX !== 0 && moveY !== 0) {
            moveX *= 0.707;
            moveY *= 0.707;
        }

        // Apply speed modifier and scale for proper movement
        moveX *= speed * 100; // Scale up for world units
        moveY *= speed * 100; // Scale up for world units

        // Send movement to server
        if (this.dotNetReference && (moveX !== 0 || moveY !== 0)) {
            this.dotNetReference.invokeMethodAsync('OnMovementInput', moveX, moveY);
        }

        // Update sprites based on world state
        if (this.worldState) {
            this.updateEntities();
        }
        
        // Redraw grid as camera moves
        this.drawGrid();
    }

    updateWorldState(worldState) {
        this.worldState = worldState;
    }

    updateEntities() {
        if (!this.worldState || !this.worldState.entities) return;

        // Log entity count periodically
        if (Math.random() < 0.05) { // 5% chance each frame
            //console.log(`Updating ${this.worldState.entities.length} entities`);
            const entityTypes = {};
            for (const entity of this.worldState.entities) {
                const typeName = this.getEntityTypeName(entity.type);
                entityTypes[typeName] = (entityTypes[typeName] || 0) + 1;
            }
            //console.log('Entity breakdown:', entityTypes);
        }

        // Track existing entity IDs
        const currentEntityIds = new Set();

        // Update or create sprites for each entity
        for (const entity of this.worldState.entities) {
            currentEntityIds.add(entity.entityId);
            
            let sprite = this.sprites.get(entity.entityId);
            
            if (!sprite) {
                // Create new sprite
                const spriteKey = this.getSpriteKey(entity);
                console.log(`Creating sprite with key '${spriteKey}' for entity type ${entity.type}, subType ${entity.subType}`);
                
                // Check if texture exists
                if (!this.scene.textures.exists(spriteKey)) {
                    console.error(`Texture '${spriteKey}' does not exist! Available textures:`, this.scene.textures.list);
                    // Fallback to creating a simple colored sprite
                    this.createColoredSprite(spriteKey, this.getEntityColor(entity), 20, 20);
                }
                
                sprite = this.scene.add.sprite(entity.position.x, entity.position.y, spriteKey);
                sprite.setOrigin(0.5, 0.5);
                this.sprites.set(entity.entityId, sprite);
                
                console.log(`Created sprite for ${this.getEntityTypeName(entity.type)} at (${entity.position.x}, ${entity.position.y})`);
                
                // Make camera follow the player
                const entityType = typeof entity.type === 'string' ? this.parseEntityType(entity.type) : entity.type;
                if (entityType === 0 && entity.entityId === this.playerId) {
                    this.scene.cameras.main.startFollow(sprite, true, 0.1, 0.1);
                    console.log('Camera now following player sprite');
                }
            }

            // Update position and rotation
            sprite.x = entity.position.x;
            sprite.y = entity.position.y;
            sprite.rotation = entity.rotation;

            // Update visibility based on state
            if (entity.state === 2) { // Dying
                sprite.alpha = 0.5;
            } else if (entity.state === 3) { // Dead
                sprite.visible = false;
            } else if (entity.state === 4) { // Respawning
                sprite.alpha = 0.3;
            } else {
                sprite.visible = true;
                sprite.alpha = 1;
            }

            // Update player info
            if (entity.entityId === this.playerId) {
                const zone = this.getGridSquare(entity.position.x, entity.position.y);
                this.playerZone = zone;
                this.currentZone = `${zone.x}, ${zone.y}`;
                
                // Check for zone mismatch and notify .NET for logging
                if (this.serverZone && this.playerZone && 
                    (this.serverZone.x !== this.playerZone.x || this.serverZone.y !== this.playerZone.y)) {
                    // Only log once per second to avoid spam
                    const now = Date.now();
                    if (!this.lastZoneMismatchLog || now - this.lastZoneMismatchLog > 1000) {
                        this.lastZoneMismatchLog = now;
                        if (this.dotNetReference) {
                            this.dotNetReference.invokeMethodAsync('OnZoneMismatch', 
                                entity.position.x, entity.position.y,
                                this.playerZone.x, this.playerZone.y,
                                this.serverZone.x, this.serverZone.y);
                        }
                    }
                }
                
                const playerInfo = `Player: ${entity.entityId.substring(0, 8)}... Health: ${Math.round(entity.health)}`;
                this.playerText.setText(playerInfo);
                
                // Ensure camera is following player sprite
                if (!this.scene.cameras.main.target && sprite) {
                    this.scene.cameras.main.startFollow(sprite, true, 0.1, 0.1);
                }
            }
        }

        // Remove sprites for entities that no longer exist
        for (const [entityId, sprite] of this.sprites) {
            if (!currentEntityIds.has(entityId)) {
                sprite.destroy();
                this.sprites.delete(entityId);
            }
        }
    }

    getSpriteKey(entity) {
        // Log the actual values to debug
        console.log(`getSpriteKey: type=${entity.type}, subType=${entity.subType}, typeString=${typeof entity.type}`);
        
        // Check if type is a string and needs conversion
        const entityType = typeof entity.type === 'string' ? this.parseEntityType(entity.type) : entity.type;
        
        switch (entityType) {
            case 0: // Player
                return 'player';
            case 1: // Enemy
                switch (entity.subType) {
                    case 1: return 'enemy-kamikaze';
                    case 2: return 'enemy-sniper';
                    case 3: return 'enemy-strafing';
                    default: return 'enemy-kamikaze';
                }
            case 2: // Bullet
                return entity.subType === 1 ? 'enemy-bullet' : 'bullet';
            case 3: // Explosion
                return entity.subType === 1 ? 'explosion-small' : 'explosion';
            default:
                console.warn(`Unknown entity type: ${entityType}`);
                return 'player';
        }
    }
    
    parseEntityType(typeString) {
        // Handle string enum values
        const typeMap = {
            'Player': 0,
            'Enemy': 1,
            'Bullet': 2,
            'Explosion': 3
        };
        return typeMap[typeString] ?? 0;
    }

    getEntityTypeName(type) {
        // Handle both string and numeric types
        if (typeof type === 'string') return type;
        
        switch (type) {
            case 0: return 'Player';
            case 1: return 'Enemy';
            case 2: return 'Bullet';
            case 3: return 'Explosion';
            default: return 'Unknown';
        }
    }
    
    getEntityColor(entity) {
        if (entity.type === 0) return 0x00ff00; // Player - green
        if (entity.type === 1) { // Enemy
            switch (entity.subType) {
                case 1: return 0xff4444; // Kamikaze - red
                case 2: return 0x44ff44; // Sniper - light green
                case 3: return 0xffaa44; // Strafing - orange
                default: return 0xff0000;
            }
        }
        if (entity.type === 2) return entity.subType === 1 ? 0xff00ff : 0xffff00; // Bullets
        if (entity.type === 3) return 0xff8800; // Explosions
        return 0xffffff; // Default white
    }

    drawGrid() {
        this.gridGraphics.clear();
        
        // Get camera position to draw grid relative to world coordinates
        const cam = this.scene.cameras.main;
        const worldView = {
            left: cam.worldView.x,
            right: cam.worldView.x + cam.worldView.width,
            top: cam.worldView.y,
            bottom: cam.worldView.y + cam.worldView.height
        };
        
        //console.log('Camera worldView:', worldView);
        
        // Calculate which zones are visible
        const startZoneX = Math.floor(worldView.left / 500);
        const endZoneX = Math.ceil(worldView.right / 500);
        const startZoneY = Math.floor(worldView.top / 500);
        const endZoneY = Math.ceil(worldView.bottom / 500);
        
        // Determine if player is in the correct zone
        const isPlayerInCorrectZone = this.serverZone && this.playerZone && 
            this.serverZone.x === this.playerZone.x && this.serverZone.y === this.playerZone.y;
        
        // First, draw all zone boundaries with a consistent color
        this.gridGraphics.lineStyle(2, 0x666666, 1.0);
        
        // Draw vertical zone boundaries
        for (let zx = startZoneX; zx <= endZoneX; zx++) {
            const x = zx * 500;
            this.gridGraphics.lineBetween(x, worldView.top, x, worldView.bottom);
        }
        
        // Draw horizontal zone boundaries
        for (let zy = startZoneY; zy <= endZoneY; zy++) {
            const y = zy * 500;
            this.gridGraphics.lineBetween(worldView.left, y, worldView.right, y);
        }
        
        // Now draw colored hollow boxes around each zone
        for (let zx = startZoneX; zx <= endZoneX; zx++) {
            for (let zy = startZoneY; zy <= endZoneY; zy++) {
                const x = zx * 500;
                const y = zy * 500;
                const zoneSize = 500;
                const borderWidth = 20; // Width of the hollow box border
                
                const isServerZone = this.serverZone && this.serverZone.x === zx && this.serverZone.y === zy;
                
                let boxColor;
                const zoneKey = `${zx},${zy}`;
                
                if (isServerZone) {
                    // Current server's zone
                    if (!isPlayerInCorrectZone && this.playerZone) {
                        // Player is not in the correct zone - use red
                        boxColor = 0x990000; // #900
                    } else {
                        // Normal server zone - light gray
                        boxColor = 0x999999; // #999
                    }
                } else if (this.preEstablishedConnections[zoneKey]) {
                    // Pre-established connection - blue
                    boxColor = 0x3399ff; // Nice blue color
                } else {
                    // Other zones - darker gray
                    boxColor = 0x555555; // #555
                }
                
                this.gridGraphics.fillStyle(boxColor, 0.3);
                
                // Draw top rectangle
                const topX = Math.max(x, worldView.left);
                const topY = Math.max(y, worldView.top);
                const topWidth = Math.min(x + zoneSize, worldView.right) - topX;
                const topHeight = Math.min(borderWidth, Math.min(y + zoneSize, worldView.bottom) - topY);
                if (topWidth > 0 && topHeight > 0) {
                    this.gridGraphics.fillRect(topX, topY, topWidth, topHeight);
                }
                
                // Draw bottom rectangle
                const bottomY = Math.max(y + zoneSize - borderWidth, worldView.top);
                const bottomHeight = Math.min(y + zoneSize, worldView.bottom) - bottomY;
                if (topWidth > 0 && bottomHeight > 0 && bottomY < worldView.bottom) {
                    this.gridGraphics.fillRect(topX, bottomY, topWidth, bottomHeight);
                }
                
                // Draw left rectangle (full height minus corners to avoid overlap)
                const leftX = Math.max(x, worldView.left);
                const leftY = Math.max(y + borderWidth, worldView.top);
                const leftWidth = Math.min(borderWidth, Math.min(x + zoneSize, worldView.right) - leftX);
                const leftHeight = Math.min(y + zoneSize - borderWidth, worldView.bottom) - leftY;
                if (leftWidth > 0 && leftHeight > 0) {
                    this.gridGraphics.fillRect(leftX, leftY, leftWidth, leftHeight);
                }
                
                // Draw right rectangle (full height minus corners to avoid overlap)
                const rightX = Math.max(x + zoneSize - borderWidth, worldView.left);
                const rightWidth = Math.min(x + zoneSize, worldView.right) - rightX;
                if (rightWidth > 0 && leftHeight > 0 && rightX < worldView.right) {
                    this.gridGraphics.fillRect(rightX, leftY, rightWidth, leftHeight);
                }
            }
        }
        
        // Draw finer grid lines (100 unit spacing)
        this.gridGraphics.lineStyle(1, 0x444444, 0.3);
        
        // Vertical lines
        const startX = Math.floor(worldView.left / 100) * 100;
        const endX = Math.ceil(worldView.right / 100) * 100;
        for (let x = startX; x <= endX; x += 100) {
            if (x % 500 !== 0) { // Skip zone boundaries
                this.gridGraphics.lineBetween(x, worldView.top, x, worldView.bottom);
            }
        }
        
        // Horizontal lines
        const startY = Math.floor(worldView.top / 100) * 100;
        const endY = Math.ceil(worldView.bottom / 100) * 100;
        for (let y = startY; y <= endY; y += 100) {
            if (y % 500 !== 0) { // Skip zone boundaries
                this.gridGraphics.lineBetween(worldView.left, y, worldView.right, y);
            }
        }
        
        // Draw unavailable zones (grayed out)
        if (this.availableZones && this.availableZones.length > 0) {
            // Create a set of available zones for quick lookup
            const availableSet = new Set(this.availableZones.map(z => `${z.x},${z.y}`));
            
            // Draw gray overlay for unavailable zones
            this.gridGraphics.fillStyle(0x000000, 0.5);
            
            for (let zx = startZoneX; zx <= endZoneX; zx++) {
                for (let zy = startZoneY; zy <= endZoneY; zy++) {
                    if (!availableSet.has(`${zx},${zy}`)) {
                        // This zone is not available, gray it out
                        const x = zx * 500;
                        const y = zy * 500;
                        this.gridGraphics.fillRect(x, y, 500, 500);
                    }
                }
            }
            
            // Note: Zone borders are now drawn with custom colors in the main zone drawing loop above
        }
    }

    getGridSquare(x, y) {
        return {
            x: Math.floor(x / 500),
            y: Math.floor(y / 500)
        };
    }

    updateZoneInfo(zones) {
        this.availableZones = zones || [];
        this.drawGrid();
        
        if (this.currentZone) {
            this.zoneText.setText(`Current Zone: ${this.currentZone}`);
        }
    }

    updateServerInfo(serverId, serverZone) {
        this.currentServer = serverId;
        this.serverZone = serverZone;
        this.serverText.setText(`Server: ${serverId || 'Unknown'}`);
        
        // Redraw grid with new server zone info
        this.drawGrid();
    }

    handleLeftClick(x, y) {
        if (!this.dotNetReference || !this.playerId) return;

        // Find player position
        const playerSprite = this.sprites.get(this.playerId);
        if (!playerSprite) return;

        // Convert screen coordinates to world coordinates
        const worldX = x + this.scene.cameras.main.scrollX;
        const worldY = y + this.scene.cameras.main.scrollY;

        // Calculate shoot direction
        const dx = worldX - playerSprite.x;
        const dy = worldY - playerSprite.y;
        const length = Math.sqrt(dx * dx + dy * dy);
        
        if (length > 0) {
            const shootX = dx / length;
            const shootY = dy / length;
            this.dotNetReference.invokeMethodAsync('OnShootInput', shootX, shootY);
        }
    }

    handleRightClick(x, y) {
        if (!this.dotNetReference || !this.playerId) return;

        // Find player position
        const playerSprite = this.sprites.get(this.playerId);
        if (!playerSprite) return;

        // Convert screen coordinates to world coordinates
        const worldX = x + this.scene.cameras.main.scrollX;
        const worldY = y + this.scene.cameras.main.scrollY;

        // Calculate heading direction
        const dx = worldX - playerSprite.x;
        const dy = worldY - playerSprite.y;
        const length = Math.sqrt(dx * dx + dy * dy);
        
        if (length > 0) {
            const headingX = dx / length;
            const headingY = dy / length;
            this.dotNetReference.invokeMethodAsync('OnMovementInput', headingX, headingY);
        }
    }

    updateZoneInfo(zones) {
        this.availableZones = zones;
        if (this.gridGraphics) {
            this.drawGrid();
        }
    }

    updateServerInfo(serverId, serverZone) {
        this.currentServer = serverId;
        this.serverZone = serverZone;
    }

    updatePreEstablishedConnections(connections) {
        // The connections dictionary now has string keys like "0,1" directly
        this.preEstablishedConnections = connections || {};
        
        // Redraw the grid to show pre-established connections
        if (this.gridGraphics) {
            this.drawGrid();
        }
    }

    destroy() {
        if (this.game) {
            this.game.destroy(true);
            this.game = null;
        }
        this.sprites.clear();
        this.worldState = null;
        this.dotNetReference = null;
    }
}

// Create a single instance
window.gamePhaser = new GamePhaser();

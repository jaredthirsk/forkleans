export class GamePhaser {
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
        const graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        // Draw a triangle for player
        graphics.fillStyle(0x00ff00, 1);
        graphics.fillTriangle(10, 0, 0, 20, 20, 20);
        graphics.lineStyle(1, 0xffffff, 0.8);
        graphics.strokeTriangle(10, 0, 0, 20, 20, 20);
        graphics.generateTexture('player', 20, 20);
        graphics.destroy();
    }
    
    createEnemySprites() {
        // Kamikaze - small red diamond
        let graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        graphics.fillStyle(0xff0000, 1);
        graphics.fillPolygon([7.5, 0, 15, 7.5, 7.5, 15, 0, 7.5]);
        graphics.generateTexture('enemy-kamikaze', 15, 15);
        graphics.destroy();
        
        // Sniper - green square
        graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        graphics.fillStyle(0x00ff00, 1);
        graphics.fillRect(0, 0, 18, 18);
        graphics.lineStyle(2, 0x004400, 1);
        graphics.strokeRect(0, 0, 18, 18);
        graphics.generateTexture('enemy-sniper', 18, 18);
        graphics.destroy();
        
        // Strafing - orange hexagon
        graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        graphics.fillStyle(0xff8800, 1);
        graphics.fillCircle(8, 8, 8);
        graphics.lineStyle(1, 0xffff00, 0.8);
        graphics.strokeCircle(8, 8, 8);
        graphics.generateTexture('enemy-strafing', 16, 16);
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
        // Large explosion
        let graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        graphics.fillStyle(0xffaa00, 0.8);
        graphics.fillCircle(15, 15, 15);
        graphics.fillStyle(0xffff00, 0.6);
        graphics.fillCircle(15, 15, 10);
        graphics.generateTexture('explosion', 30, 30);
        graphics.destroy();
        
        // Small explosion
        graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        graphics.fillStyle(0xffaa00, 0.8);
        graphics.fillCircle(10, 10, 10);
        graphics.fillStyle(0xffff00, 0.6);
        graphics.fillCircle(10, 10, 6);
        graphics.generateTexture('explosion-small', 20, 20);
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
            console.log(`Updating ${this.worldState.entities.length} entities`);
            const entityTypes = {};
            for (const entity of this.worldState.entities) {
                const typeName = this.getEntityTypeName(entity.type);
                entityTypes[typeName] = (entityTypes[typeName] || 0) + 1;
            }
            console.log('Entity breakdown:', entityTypes);
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
                sprite = this.scene.add.sprite(entity.position.x, entity.position.y, spriteKey);
                sprite.setOrigin(0.5, 0.5);
                this.sprites.set(entity.entityId, sprite);
                
                console.log(`Created sprite for ${this.getEntityTypeName(entity.type)} at (${entity.position.x}, ${entity.position.y})`);
                
                // Add glow effect for player
                if (entity.type === 0 && entity.entityId === this.playerId) {
                    sprite.setTint(0x00ff00);
                    // Make camera follow the player
                    this.scene.cameras.main.startFollow(sprite, true, 0.1, 0.1);
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
                this.currentZone = `${zone.x}, ${zone.y}`;
                
                const playerInfo = `Player: ${entity.entityId.substring(0, 8)}... Health: ${Math.round(entity.health)}`;
                this.playerText.setText(playerInfo);
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
        switch (entity.type) {
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
                return 'player';
        }
    }

    getEntityTypeName(type) {
        switch (type) {
            case 0: return 'Player';
            case 1: return 'Enemy';
            case 2: return 'Bullet';
            case 3: return 'Explosion';
            default: return 'Unknown';
        }
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
        
        // Draw zone boundaries (1000x1000 unit zones)
        this.gridGraphics.lineStyle(3, 0x00ff00, 0.8);
        
        // Calculate which zones are visible
        const startZoneX = Math.floor(worldView.left / 1000);
        const endZoneX = Math.ceil(worldView.right / 1000);
        const startZoneY = Math.floor(worldView.top / 1000);
        const endZoneY = Math.ceil(worldView.bottom / 1000);
        
        // Draw vertical zone boundaries
        for (let zx = startZoneX; zx <= endZoneX; zx++) {
            const x = zx * 1000;
            this.gridGraphics.moveTo(x, worldView.top);
            this.gridGraphics.lineTo(x, worldView.bottom);
        }
        
        // Draw horizontal zone boundaries
        for (let zy = startZoneY; zy <= endZoneY; zy++) {
            const y = zy * 1000;
            this.gridGraphics.moveTo(worldView.left, y);
            this.gridGraphics.lineTo(worldView.right, y);
        }
        
        // Draw finer grid lines (100 unit spacing)
        this.gridGraphics.lineStyle(1, 0x444444, 0.3);
        
        // Vertical lines
        const startX = Math.floor(worldView.left / 100) * 100;
        const endX = Math.ceil(worldView.right / 100) * 100;
        for (let x = startX; x <= endX; x += 100) {
            if (x % 1000 !== 0) { // Skip zone boundaries
                this.gridGraphics.moveTo(x, worldView.top);
                this.gridGraphics.lineTo(x, worldView.bottom);
            }
        }
        
        // Horizontal lines
        const startY = Math.floor(worldView.top / 100) * 100;
        const endY = Math.ceil(worldView.bottom / 100) * 100;
        for (let y = startY; y <= endY; y += 100) {
            if (y % 1000 !== 0) { // Skip zone boundaries
                this.gridGraphics.moveTo(worldView.left, y);
                this.gridGraphics.lineTo(worldView.right, y);
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
                        const x = zx * 1000;
                        const y = zy * 1000;
                        this.gridGraphics.fillRect(x, y, 1000, 1000);
                    }
                }
            }
            
            // Draw a border around available zones
            this.gridGraphics.lineStyle(2, 0x00ff00, 0.6);
            for (const zone of this.availableZones) {
                const x = zone.x * 1000;
                const y = zone.y * 1000;
                this.gridGraphics.strokeRect(x, y, 1000, 1000);
            }
        }
    }

    getGridSquare(x, y) {
        return {
            x: Math.floor(x / 1000),
            y: Math.floor(y / 1000)
        };
    }

    updateZoneInfo(zones) {
        this.availableZones = zones || [];
        this.drawGrid();
        
        if (this.currentZone) {
            this.zoneText.setText(`Current Zone: ${this.currentZone}`);
        }
    }

    updateServerInfo(serverId) {
        this.currentServer = serverId;
        this.serverText.setText(`Server: ${serverId || 'Unknown'}`);
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
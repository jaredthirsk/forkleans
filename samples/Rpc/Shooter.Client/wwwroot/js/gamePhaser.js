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
        this.createFactorySprite();
    }
    
    createPlayerSprite() {
        console.log('Creating player sprite texture');
        const graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        const size = 15;
        const centerX = size;
        const centerY = size;
        
        // Draw ship shape (similar to Canvas version)
        graphics.fillStyle(0x00ff00, 1); // Bright green
        graphics.beginPath();
        graphics.moveTo(centerX + size, centerY); // Nose of ship
        graphics.lineTo(centerX - size * 0.7, centerY - size * 0.7); // Left wing
        graphics.lineTo(centerX - size * 0.3, centerY); // Back indent
        graphics.lineTo(centerX - size * 0.7, centerY + size * 0.7); // Right wing
        graphics.closePath();
        graphics.fillPath();
        
        // Add darker green border
        graphics.lineStyle(2, 0x008800, 1);
        graphics.strokePath();
        
        // Draw cockpit (blue circle in center)
        graphics.fillStyle(0x003366, 1);
        graphics.fillCircle(centerX, centerY, size * 0.3);
        
        // Add bright accent
        graphics.fillStyle(0x00ffff, 0.8);
        graphics.fillCircle(centerX + size * 0.1, centerY, size * 0.15);
        
        graphics.generateTexture('player', size * 2, size * 2);
        graphics.destroy();
        console.log('Player spaceship texture created');
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
        
        // Scout - purple hexagon (watchful shape)
        graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        const scoutSize = 14;
        graphics.fillStyle(0x9966cc, 1);
        graphics.beginPath();
        
        // Draw hexagon
        for (let i = 0; i < 6; i++) {
            const angle = (i / 6) * Math.PI * 2;
            const x = scoutSize + Math.cos(angle) * scoutSize * 0.8;
            const y = scoutSize + Math.sin(angle) * scoutSize * 0.8;
            if (i === 0) {
                graphics.moveTo(x, y);
            } else {
                graphics.lineTo(x, y);
            }
        }
        graphics.closePath();
        graphics.fillPath();
        
        // Purple border
        graphics.lineStyle(2, 0x663399, 1);
        graphics.strokePath();
        
        // Eye in center (scanner)
        graphics.fillStyle(0xffff00, 1);
        graphics.fillCircle(scoutSize, scoutSize, scoutSize * 0.3);
        
        graphics.generateTexture('enemy-scout', scoutSize * 2, scoutSize * 2);
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
    
    createFactorySprite() {
        console.log('Creating factory sprite texture');
        const graphics = this.scene.make.graphics({ x: 0, y: 0, add: false });
        const size = 30; // Factories are larger
        
        // Base building (dark gray)
        graphics.fillStyle(0x444444, 1);
        graphics.fillRect(0, 0, size * 2, size * 2);
        
        // Inner building (lighter gray)
        graphics.fillStyle(0x666666, 1);
        graphics.fillRect(4, 4, size * 2 - 8, size * 2 - 8);
        
        // Factory gear symbol
        graphics.fillStyle(0x888888, 1);
        const centerX = size;
        const centerY = size;
        const teeth = 8;
        
        graphics.beginPath();
        for (let i = 0; i < teeth * 2; i++) {
            const angle = (i / (teeth * 2)) * Math.PI * 2;
            const radius = i % 2 === 0 ? size * 0.6 : size * 0.4;
            const x = centerX + Math.cos(angle) * radius;
            const y = centerY + Math.sin(angle) * radius;
            if (i === 0) graphics.moveTo(x, y);
            else graphics.lineTo(x, y);
        }
        graphics.closePath();
        graphics.fillPath();
        
        // Center hole
        graphics.fillStyle(0x666666, 1);
        graphics.fillCircle(centerX, centerY, size * 0.2);
        
        // Dark border
        graphics.lineStyle(2, 0x222222, 1);
        graphics.strokeRect(0, 0, size * 2, size * 2);
        
        graphics.generateTexture('factory', size * 2, size * 2);
        graphics.destroy();
        console.log('Factory texture created');
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
        
        // Add special key handlers for automove and test mode
        this.automoveKey = this.scene.input.keyboard.addKey('V'); // V for automove (A is already used for left)
        this.testModeKey = this.scene.input.keyboard.addKey('T');
        
        // Track key state to detect press events
        this.keyStates = {
            automove: false,
            testMode: false
        };

        // Prevent right-click context menu
        this.scene.game.canvas.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            return false;
        });
    }

    update() {
        // Handle special key press events
        if (this.automoveKey.isDown && !this.keyStates.automove) {
            this.keyStates.automove = true;
            if (this.dotNetReference) {
                this.dotNetReference.invokeMethodAsync('OnKeyPress', 'v');
            }
        } else if (!this.automoveKey.isDown && this.keyStates.automove) {
            this.keyStates.automove = false;
        }
        
        if (this.testModeKey.isDown && !this.keyStates.testMode) {
            this.keyStates.testMode = true;
            if (this.dotNetReference) {
                this.dotNetReference.invokeMethodAsync('OnKeyPress', 't');
            }
        } else if (!this.testModeKey.isDown && this.keyStates.testMode) {
            this.keyStates.testMode = false;
        }
        
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

            // Parse entity state consistently (handle string/number)
            const entityState = typeof entity.state === 'string' ? this.parseEntityState(entity.state) : entity.state;
            
            // Update visibility based on state
            if (entityState === 2) { // Dying
                sprite.alpha = 0.5;
            } else if (entityState === 3) { // Dead
                sprite.visible = false;
            } else if (entityState === 4) { // Respawning
                sprite.alpha = 0.3;
            } else if (entityState === 5) { // Alerting (Scout)
                sprite.visible = true;
                sprite.alpha = 1;
                this.updateScoutAlertIndicator(sprite, entity);
            } else {
                sprite.visible = true;
                sprite.alpha = 1;
                this.clearScoutAlertIndicator(sprite);
            }

            // Update health bar for living entities
            this.updateHealthBar(sprite, entity);

            // Update player name for other players
            this.updatePlayerName(sprite, entity);

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
                this.clearScoutAlertIndicator(sprite);
                this.clearHealthBar(sprite);
                this.clearPlayerName(sprite);
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
                    case 4: return 'enemy-scout';
                    default: return 'enemy-kamikaze';
                }
            case 2: // Bullet
                return entity.subType === 1 ? 'enemy-bullet' : 'bullet';
            case 3: // Explosion
                return entity.subType === 1 ? 'explosion-small' : 'explosion';
            case 4: // Factory
                return 'factory';
            case 5: // Asteroid
                return 'asteroid';
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
            'Explosion': 3,
            'Factory': 4,
            'Asteroid': 5
        };
        return typeMap[typeString] ?? 0;
    }

    parseEntityState(stateString) {
        // Handle string enum values
        const stateMap = {
            'Active': 0,
            'Dying': 2,
            'Dead': 3,
            'Respawning': 4,
            'Alerting': 5
        };
        return stateMap[stateString] ?? 0;
    }

    getEntityTypeName(type) {
        // Handle both string and numeric types
        if (typeof type === 'string') return type;
        
        switch (type) {
            case 0: return 'Player';
            case 1: return 'Enemy';
            case 2: return 'Bullet';
            case 3: return 'Explosion';
            case 4: return 'Factory';
            case 5: return 'Asteroid';
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
                case 4: return 0x9966cc; // Scout - purple
                default: return 0xff0000;
            }
        }
        if (entity.type === 2) return entity.subType === 1 ? 0xff00ff : 0xffff00; // Bullets
        if (entity.type === 3) return 0xff8800; // Explosions
        if (entity.type === 4) return 0x666666; // Factory - gray
        if (entity.type === 5) return 0x8B4513; // Asteroid - brown
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
        
        // Zone peek distance constant - how far into adjacent zones we can see
        const ZONE_PEEK_DISTANCE = 200; // This should match GameConstants.ZonePeekDistance
        
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
                        boxColor = 0xFF0000;
                    } else {
                        // Normal server zone - brighter (closer to white)
                        boxColor = 0xdddddd; // Much brighter gray, closer to white
                    }
                } else if (this.preEstablishedConnections[zoneKey]) {
                    // Pre-established connection
                    const connectionInfo = this.preEstablishedConnections[zoneKey];
                    if (connectionInfo.isConnecting) {
                        // Currently connecting - green
                        boxColor = 0x22ff22; 
                    } else if (connectionInfo.isConnected) {
                        // Connected zone - blue
                        boxColor = 0x3399ff;
                    } else {
                        // Failed connection - yellow
                        boxColor = 0xFFFF00; 
                    }
                } else {
                    // Other zones - transparent black (no color, just rely on fillStyle alpha)
                    boxColor = 0x000000; // Black (will be transparent)
                }
                
                // Use lower alpha for unconnected zones
                const alpha = (this.preEstablishedConnections[zoneKey] || isServerZone) ? 0.3 : 0.1;
                this.gridGraphics.fillStyle(boxColor, alpha);
                
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
        }
        
        // Draw gray overlays for adjacent zones
        // Strategy: First gray out all adjacent zones, then clear the visible peek areas
        if (this.playerZone) {
            // Get player's position in world units
            let playerX = 0;
            let playerY = 0;
            
            if (this.worldState && this.worldState.entities) {
                const playerEntity = this.worldState.entities.find(e => e.entityId === this.playerId);
                if (playerEntity) {
                    playerX = playerEntity.position.x;
                    playerY = playerEntity.position.y;
                }
            }
            
            // Calculate distances to zone edges
            const zoneMinX = this.playerZone.x * 500;
            const zoneMaxX = (this.playerZone.x + 1) * 500;
            const zoneMinY = this.playerZone.y * 500;
            const zoneMaxY = (this.playerZone.y + 1) * 500;
            
            const distToLeft = playerX - zoneMinX;
            const distToRight = zoneMaxX - playerX;
            const distToTop = playerY - zoneMinY;
            const distToBottom = zoneMaxY - playerY;
            
            // Draw gray overlay for adjacent zones, but exclude peek areas
            this.gridGraphics.fillStyle(0x000000, 0.3); // 30% opacity gray
            
            // Process each adjacent zone
            for (let dx = -1; dx <= 1; dx++) {
                for (let dy = -1; dy <= 1; dy++) {
                    if (dx === 0 && dy === 0) continue; // Skip current zone
                    
                    const adjacentX = (this.playerZone.x + dx) * 500;
                    const adjacentY = (this.playerZone.y + dy) * 500;
                    
                    // Calculate the gray area for this zone
                    let grayX = adjacentX;
                    let grayY = adjacentY;
                    let grayWidth = 500;
                    let grayHeight = 500;
                    
                    // Adjust for peek areas (only for cardinal directions, not diagonals)
                    if (dx === -1 && dy === 0 && distToLeft <= ZONE_PEEK_DISTANCE) {
                        // Left zone - don't gray the rightmost ZONE_PEEK_DISTANCE pixels
                        grayWidth = 500 - ZONE_PEEK_DISTANCE;
                    } else if (dx === 1 && dy === 0 && distToRight <= ZONE_PEEK_DISTANCE) {
                        // Right zone - don't gray the leftmost ZONE_PEEK_DISTANCE pixels
                        grayX = adjacentX + ZONE_PEEK_DISTANCE;
                        grayWidth = 500 - ZONE_PEEK_DISTANCE;
                    } else if (dx === 0 && dy === -1 && distToTop <= ZONE_PEEK_DISTANCE) {
                        // Top zone - don't gray the bottommost ZONE_PEEK_DISTANCE pixels
                        grayHeight = 500 - ZONE_PEEK_DISTANCE;
                    } else if (dx === 0 && dy === 1 && distToBottom <= ZONE_PEEK_DISTANCE) {
                        // Bottom zone - don't gray the topmost ZONE_PEEK_DISTANCE pixels
                        grayY = adjacentY + ZONE_PEEK_DISTANCE;
                        grayHeight = 500 - ZONE_PEEK_DISTANCE;
                    }
                    
                    // Clip to viewport
                    const clippedRect = {
                        x: Math.max(grayX, worldView.left),
                        y: Math.max(grayY, worldView.top),
                        width: Math.min(grayX + grayWidth, worldView.right) - Math.max(grayX, worldView.left),
                        height: Math.min(grayY + grayHeight, worldView.bottom) - Math.max(grayY, worldView.top)
                    };
                    
                    if (clippedRect.width > 0 && clippedRect.height > 0) {
                        this.gridGraphics.fillRect(clippedRect.x, clippedRect.y, clippedRect.width, clippedRect.height);
                    }
                }
            }
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

    updateScoutAlertIndicator(sprite, entity) {
        // Create or update the alert indicator for scouts
        if (!sprite.alertIndicator) {
            sprite.alertIndicator = this.scene.add.graphics();
        }
        
        const graphics = sprite.alertIndicator;
        graphics.clear();
        graphics.x = sprite.x;
        graphics.y = sprite.y;
        
        // Get the state timer to determine progress (0-3 seconds)
        const stateTimer = entity.stateTimer || 0;
        const progress = Math.min(stateTimer / 3, 1); // 0 to 1 over 3 seconds
        
        // Determine how many bars to show (1, 2, or 3)
        let barsToShow = 1;
        if (progress > 0.33) barsToShow = 2;
        if (progress > 0.66) barsToShow = 3;
        
        // Color based on progress: gray while building up, green when ready
        const isReady = progress >= 1;
        const color = isReady ? 0x00ff00 : 0x888888; // Green when ready, gray otherwise
        
        // Flash when actively alerting
        const time = Date.now() * 0.01;
        const flash = isReady ? (Math.sin(time) + 1) * 0.5 : 1; // Only flash when ready
        const alpha = isReady ? (0.4 + flash * 0.6) : 0.8;
        
        graphics.lineStyle(3, color, alpha);
        
        // Draw wi-fi style arcs based on progress
        if (entity.rotation === 0 && entity.position) {
            // Center position - show 8-directional pattern
            this.drawEightDirectionalAlerts(graphics, alpha, barsToShow, color);
        } else {
            // Directional alert - show arcs in the alert direction
            this.drawDirectionalAlert(graphics, entity.rotation, alpha, barsToShow, color);
        }
    }
    
    drawEightDirectionalAlerts(graphics, alpha, barsToShow, color) {
        // Draw 8 small arcs pointing in all directions
        const directions = [0, Math.PI/4, Math.PI/2, 3*Math.PI/4, Math.PI, 5*Math.PI/4, 3*Math.PI/2, 7*Math.PI/4];
        
        directions.forEach(angle => {
            // Only draw the number of bars specified
            for (let i = 1; i <= barsToShow; i++) {
                this.drawWifiArc(graphics, angle, 15 + i * 5, alpha * (1.2 - i * 0.2), color);
            }
        });
    }
    
    drawDirectionalAlert(graphics, direction, alpha, barsToShow, color) {
        // Draw concentric arcs in the alert direction based on barsToShow
        for (let i = 1; i <= barsToShow; i++) {
            this.drawWifiArc(graphics, direction, 15 + i * 10, alpha * (1.2 - i * 0.2), color);
        }
    }
    
    drawWifiArc(graphics, direction, radius, alpha, color = 0xffff00) {
        // Draw a wi-fi style arc
        graphics.lineStyle(2, color, alpha);
        
        const startAngle = direction - Math.PI/6; // 30 degree arc
        const endAngle = direction + Math.PI/6;
        
        // Draw the arc
        graphics.beginPath();
        graphics.arc(0, 0, radius, startAngle, endAngle, false);
        graphics.strokePath();
        
        // Add small lines at the ends for wi-fi effect
        const startX = Math.cos(startAngle) * radius;
        const startY = Math.sin(startAngle) * radius;
        const endX = Math.cos(endAngle) * radius;
        const endY = Math.sin(endAngle) * radius;
        
        graphics.lineStyle(3, color, alpha);
        graphics.lineBetween(startX - 3, startY - 3, startX + 3, startY + 3);
        graphics.lineBetween(endX - 3, endY - 3, endX + 3, endY + 3);
    }

    clearScoutAlertIndicator(sprite) {
        // Remove the alert indicator if it exists
        if (sprite.alertIndicator) {
            sprite.alertIndicator.destroy();
            sprite.alertIndicator = null;
        }
    }

    updateHealthBar(sprite, entity) {
        // Parse entity type consistently
        const entityType = typeof entity.type === 'string' ? this.parseEntityType(entity.type) : entity.type;
        
        // Parse entity state consistently
        const entityState = typeof entity.state === 'string' ? this.parseEntityState(entity.state) : entity.state;
        
        // Only show health bars for living entities (not bullets/explosions)
        const shouldShowHealthBar = entityType !== 2 && entityType !== 3 && // Not bullet or explosion
                                   (entityState === 0 || entityState === 5) && entity.health > 0; // Active or Alerting and alive

        if (shouldShowHealthBar) {
            // Create or update the health bar
            if (!sprite.healthBar) {
                sprite.healthBar = this.scene.add.graphics();
            }

            const graphics = sprite.healthBar;
            graphics.clear();
            graphics.x = sprite.x;
            graphics.y = sprite.y;

            // Calculate max health based on entity type and subtype
            let maxHealth;
            if (entityType === 0) { // Player
                maxHealth = 1000;
            } else if (entityType === 1) { // Enemy
                maxHealth = entity.subType === 1 ? 30 : entity.subType === 4 ? 300 : 50; // Kamikaze: 30, Scout: 300, Others: 50
            } else {
                maxHealth = 100; // Default
            }

            const healthPercent = Math.max(0, Math.min(1, entity.health / maxHealth));
            const width = 30; // Health bar width
            const height = 4; // Health bar height
            const yOffset = -25; // Position above sprite

            // Background (dark gray)
            graphics.fillStyle(0x333333, 1);
            graphics.fillRect(-width/2, yOffset, width, height);

            // Health fill (color based on health percentage)
            let healthColor;
            if (healthPercent > 0.5) {
                healthColor = 0x00ff00; // Green
            } else if (healthPercent > 0.25) {
                healthColor = 0xffff00; // Yellow
            } else {
                healthColor = 0xff0000; // Red
            }

            graphics.fillStyle(healthColor, 1);
            graphics.fillRect(-width/2, yOffset, width * healthPercent, height);

            // Border (gray)
            graphics.lineStyle(1, 0x666666, 1);
            graphics.strokeRect(-width/2, yOffset, width, height);
        } else {
            // Remove health bar if it shouldn't be shown
            this.clearHealthBar(sprite);
        }
    }

    clearHealthBar(sprite) {
        // Remove the health bar if it exists
        if (sprite.healthBar) {
            sprite.healthBar.destroy();
            sprite.healthBar = null;
        }
    }

    updatePlayerName(sprite, entity) {
        // Parse entity type
        const entityType = typeof entity.type === 'string' ? this.parseEntityType(entity.type) : entity.type;
        
        // Only show names for other players (not current player, not enemies/bullets/etc)
        const shouldShowName = entityType === 0 && entity.entityId !== this.playerId;

        if (shouldShowName) {
            // Create or update the name text
            if (!sprite.nameText) {
                // Extract a readable name from the entity ID
                // Entity IDs are GUIDs, so we'll take the first 8 characters
                const displayName = entity.entityId.substring(0, 8) + '...';
                
                sprite.nameText = this.scene.add.text(0, 0, displayName, {
                    fontSize: '12px',
                    fill: '#ffffff',
                    stroke: '#000000',
                    strokeThickness: 2,
                    align: 'center'
                });
                sprite.nameText.setOrigin(0.5, 0);
            }

            // Update position to be below the sprite
            sprite.nameText.x = sprite.x;
            sprite.nameText.y = sprite.y + 20; // Position below sprite
        } else {
            // Remove name text if it shouldn't be shown
            this.clearPlayerName(sprite);
        }
    }

    clearPlayerName(sprite) {
        // Remove the name text if it exists
        if (sprite.nameText) {
            sprite.nameText.destroy();
            sprite.nameText = null;
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

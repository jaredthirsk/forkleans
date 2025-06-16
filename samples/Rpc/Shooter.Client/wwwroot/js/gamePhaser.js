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
        
        // Create simple colored rectangles for sprites
        this.createColoredSprite('player', 0x00ff00, 20, 20);
        this.createColoredSprite('enemy-kamikaze', 0xff0000, 15, 15);
        this.createColoredSprite('enemy-sniper', 0xff00ff, 18, 18);
        this.createColoredSprite('enemy-strafing', 0xff8800, 16, 16);
        this.createColoredSprite('bullet', 0xffff00, 4, 8);
        this.createColoredSprite('enemy-bullet', 0xff00ff, 4, 8);
        this.createColoredSprite('explosion', 0xffaa00, 30, 30);
        this.createColoredSprite('explosion-small', 0xffaa00, 20, 20);
    }

    createColoredSprite(key, color, width, height) {
        const graphics = this.scene.add.graphics();
        graphics.fillStyle(color, 1);
        graphics.fillRect(0, 0, width, height);
        graphics.generateTexture(key, width, height);
        graphics.destroy();
    }

    create() {
        // Create grid lines
        this.gridGraphics = this.scene.add.graphics();
        this.drawGrid();

        // Create UI text
        this.zoneText = this.scene.add.text(10, 10, '', { 
            fontSize: '16px', 
            fill: '#ffffff' 
        });
        this.serverText = this.scene.add.text(10, 30, '', { 
            fontSize: '16px', 
            fill: '#ffffff' 
        });
        this.playerText = this.scene.add.text(10, 50, '', { 
            fontSize: '16px', 
            fill: '#ffffff' 
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

        // Apply speed modifier
        moveX *= speed;
        moveY *= speed;

        // Send movement to server
        if (this.dotNetReference) {
            this.dotNetReference.invokeMethodAsync('OnMovementInput', moveX, moveY);
        }

        // Update sprites based on world state
        if (this.worldState) {
            this.updateEntities();
        }
    }

    updateWorldState(worldState) {
        this.worldState = worldState;
    }

    updateEntities() {
        if (!this.worldState || !this.worldState.entities) return;

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
                
                // Add glow effect for player
                if (entity.type === 0 && entity.entityId === this.playerId) {
                    sprite.setTint(0x00ff00);
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

    drawGrid() {
        this.gridGraphics.clear();
        this.gridGraphics.lineStyle(1, 0x444444, 0.5);

        // Draw grid lines every 100 units
        for (let x = 0; x <= 800; x += 100) {
            this.gridGraphics.moveTo(x, 0);
            this.gridGraphics.lineTo(x, 600);
        }
        
        for (let y = 0; y <= 600; y += 100) {
            this.gridGraphics.moveTo(0, y);
            this.gridGraphics.lineTo(800, y);
        }

        // Draw available zones
        if (this.availableZones && this.availableZones.length > 0) {
            this.gridGraphics.fillStyle(0x00ff00, 0.1);
            for (const zone of this.availableZones) {
                this.gridGraphics.fillRect(zone.x * 500, zone.y * 500, 500, 500);
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

    updateServerInfo(serverId) {
        this.currentServer = serverId;
        this.serverText.setText(`Server: ${serverId || 'Unknown'}`);
    }

    handleLeftClick(x, y) {
        if (!this.dotNetReference || !this.playerId) return;

        // Find player position
        const playerSprite = this.sprites.get(this.playerId);
        if (!playerSprite) return;

        // Calculate shoot direction
        const dx = x - playerSprite.x;
        const dy = y - playerSprite.y;
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

        // Calculate heading direction
        const dx = x - playerSprite.x;
        const dy = y - playerSprite.y;
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
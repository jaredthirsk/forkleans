namespace Shooter.ActionServer.Views;

public static class PhaserViewHtml
{
    public static string GetHtml()
    {
        return @"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>ActionServer</title>
    <script src='https://cdn.jsdelivr.net/npm/phaser@3.70.0/dist/phaser.min.js'></script>
    <script src='https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js'></script>
    <style>
        body {
            margin: 0;
            padding: 20px;
            background-color: #1a1a1a;
            color: #e0e0e0;
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
        }
        
        .container {
            max-width: 1200px;
            margin: 0 auto;
        }
        
        .header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 20px;
            padding: 15px;
            background-color: #2a2a2a;
            border-radius: 8px;
            border: 1px solid #444;
        }
        
        .title {
            font-size: 24px;
            font-weight: bold;
            color: #9C27B0;
        }
        
        .server-info {
            display: flex;
            gap: 20px;
            font-size: 14px;
        }
        
        .info-item {
            display: flex;
            align-items: center;
            gap: 5px;
        }
        
        .info-label {
            color: #888;
        }
        
        .info-value {
            color: #4CAF50;
            font-weight: bold;
        }
        
        .controls {
            display: flex;
            gap: 20px;
            margin-bottom: 20px;
            padding: 15px;
            background-color: #2a2a2a;
            border-radius: 8px;
            border: 1px solid #444;
        }
        
        .control-group {
            display: flex;
            flex-direction: column;
            gap: 10px;
        }
        
        .control-label {
            font-size: 14px;
            color: #aaa;
            font-weight: bold;
        }
        
        .toggle-group {
            display: flex;
            gap: 10px;
        }
        
        .toggle-btn {
            padding: 6px 12px;
            background-color: #333;
            border: 1px solid #555;
            border-radius: 4px;
            color: #e0e0e0;
            cursor: pointer;
            font-size: 14px;
            transition: all 0.2s;
        }
        
        .toggle-btn:hover {
            background-color: #444;
            border-color: #666;
        }
        
        .toggle-btn.active {
            background-color: #9C27B0;
            border-color: #9C27B0;
            color: white;
        }
        
        .zone-selector {
            display: flex;
            flex-wrap: wrap;
            gap: 5px;
            max-width: 400px;
        }
        
        .zone-btn {
            padding: 4px 8px;
            background-color: #333;
            border: 1px solid #555;
            border-radius: 4px;
            color: #e0e0e0;
            cursor: pointer;
            font-size: 12px;
            transition: all 0.2s;
        }
        
        .zone-btn:hover {
            background-color: #444;
            border-color: #666;
        }
        
        .zone-btn.selected {
            background-color: #4CAF50;
            border-color: #4CAF50;
            color: white;
        }
        
        .zone-btn.current {
            background-color: #9C27B0;
            border-color: #9C27B0;
            color: white;
            cursor: default;
        }
        
        .game-container {
            display: flex;
            justify-content: center;
            margin-bottom: 20px;
        }
        
        #phaser-container {
            border: 2px solid #444;
            border-radius: 8px;
            background-color: #000;
        }
        
        .stats {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 15px;
            padding: 15px;
            background-color: #2a2a2a;
            border-radius: 8px;
            border: 1px solid #444;
        }
        
        .stat-card {
            padding: 10px;
            background-color: #333;
            border-radius: 4px;
            border: 1px solid #555;
        }
        
        .stat-title {
            font-size: 12px;
            color: #888;
            margin-bottom: 5px;
        }
        
        .stat-value {
            font-size: 20px;
            font-weight: bold;
            color: #4CAF50;
        }
        
        .legend {
            display: flex;
            gap: 20px;
            padding: 10px;
            background-color: #333;
            border-radius: 4px;
            font-size: 12px;
        }
        
        .legend-item {
            display: flex;
            align-items: center;
            gap: 5px;
        }
        
        .legend-color {
            width: 12px;
            height: 12px;
            border-radius: 2px;
        }
        
        .status-indicator {
            display: inline-block;
            width: 8px;
            height: 8px;
            border-radius: 50%;
            margin-right: 5px;
            animation: pulse 2s infinite;
        }
        
        .status-connected {
            background-color: #4CAF50;
        }
        
        .status-disconnected {
            background-color: #f44336;
            animation: none;
        }
        
        @keyframes pulse {
            0% { opacity: 1; }
            50% { opacity: 0.5; }
            100% { opacity: 1; }
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <div class='title'>
                ActionServer
            </div>
            <div class='server-info'>
                <div class='info-item'>
                    <span class='status-indicator status-disconnected' id='connectionStatus'></span>
                    <span class='info-label'>Zone:</span>
                    <span class='info-value' id='zoneInfo'>Loading...</span>
                </div>
                <div class='info-item'>
                    <span class='info-label'>Instance:</span>
                    <span class='info-value' id='instanceInfo'>Loading...</span>
                </div>
                <div class='info-item'>
                    <span class='info-label'>FPS:</span>
                    <span class='info-value' id='fpsInfo'>0</span>
                </div>
            </div>
        </div>
        
        <div class='controls'>
            <div class='control-group'>
                <div class='control-label'>View Mode</div>
                <div class='toggle-group'>
                    <button class='toggle-btn active' id='localBtn' onclick='toggleLocal()'>Local Entities</button>
                    <button class='toggle-btn' id='adjacentBtn' onclick='toggleAdjacent()'>Adjacent Zones</button>
                </div>
            </div>
            
            <div class='control-group' id='zoneSelectGroup' style='display: none;'>
                <div class='control-label'>Select Adjacent Zones</div>
                <div class='zone-selector' id='zoneSelector'>
                    <!-- Zone buttons will be added dynamically -->
                </div>
            </div>
        </div>
        
        <div class='game-container'>
            <div id='phaser-container'></div>
        </div>
        
        <div class='stats'>
            <div class='stat-card'>
                <div class='stat-title'>Local Entities</div>
                <div class='stat-value' id='localEntityCount'>0</div>
            </div>
            <div class='stat-card'>
                <div class='stat-title'>Adjacent Entities</div>
                <div class='stat-value' id='adjacentEntityCount'>0</div>
            </div>
            <div class='stat-card'>
                <div class='stat-title'>Players</div>
                <div class='stat-value' id='playerCount'>0</div>
            </div>
            <div class='stat-card'>
                <div class='stat-title'>Enemies</div>
                <div class='stat-value' id='enemyCount'>0</div>
            </div>
            <div class='stat-card'>
                <div class='stat-title'>Factories</div>
                <div class='stat-value' id='factoryCount'>0</div>
            </div>
            <div class='stat-card'>
                <div class='stat-title'>Update Rate</div>
                <div class='stat-value' id='updateRate'>0/s</div>
            </div>
        </div>
        
        <div class='legend'>
            <div class='legend-item'>
                <div class='legend-color' style='background-color: #4CAF50;'></div>
                <span>Players (Local)</span>
            </div>
            <div class='legend-item'>
                <div class='legend-color' style='background-color: #81C784;'></div>
                <span>Players (Adjacent)</span>
            </div>
            <div class='legend-item'>
                <div class='legend-color' style='background-color: #f44336;'></div>
                <span>Enemies</span>
            </div>
            <div class='legend-item'>
                <div class='legend-color' style='background-color: #FF9800;'></div>
                <span>Factories</span>
            </div>
            <div class='legend-item'>
                <div class='legend-color' style='background-color: #9E9E9E;'></div>
                <span>Asteroids</span>
            </div>
            <div class='legend-item'>
                <div class='legend-color' style='background-color: #FFEB3B;'></div>
                <span>Projectiles</span>
            </div>
        </div>
    </div>
    
    <script>
        // Extract zone coordinates from URL path if present
        // URL format: /phaser/{x}/{y} or /phaser (default zone)
        const pathParts = window.location.pathname.split('/').filter(p => p);
        let targetZone = null;
        
        if (pathParts.length >= 3 && pathParts[0] === 'phaser') {
            const x = parseInt(pathParts[1]);
            const y = parseInt(pathParts[2]);
            if (!isNaN(x) && !isNaN(y)) {
                targetZone = { x: x, y: y };
            }
        }
        
        // Make zone coordinates available to the main script
        window.phaserViewConfig = {
            targetZone: targetZone
        };
    </script>
    <script src='/js/actionserver-phaser.js'></script>
</body>
</html>
";
    }
}
using System.Text.Json.Serialization;

namespace Shooter.Shared.Models;

[Orleans.GenerateSerializer]
public record GridSquare(int X, int Y)
{
    public const int Size = 500; // Size of each grid square in world units
    
    public static GridSquare FromPosition(Vector2 position)
    {
        var x = (int)Math.Floor(position.X / Size);
        var y = (int)Math.Floor(position.Y / Size);
        return new GridSquare(x, y);
    }
    
    public Vector2 GetCenter() => new Vector2(X * Size + Size / 2f, Y * Size + Size / 2f);
    public (Vector2 min, Vector2 max) GetBounds() => 
        (new Vector2(X * Size, Y * Size), new Vector2((X + 1) * Size, (Y + 1) * Size));
}

[Orleans.GenerateSerializer]
public record ActionServerInfo(
    string ServerId,
    string IpAddress,
    int UdpPort,
    string HttpEndpoint,
    GridSquare AssignedSquare,
    DateTime RegisteredAt,
    int RpcPort = 0);

[Orleans.GenerateSerializer]
public record PlayerInfo(
    string PlayerId,
    string Name,
    Vector2 Position,
    Vector2 Velocity,
    float Health);

[Orleans.GenerateSerializer]
public record EntityState(
    [property: JsonPropertyName("entityId")] string EntityId,
    [property: JsonPropertyName("type")] EntityType Type,
    [property: JsonPropertyName("position")] Vector2 Position,
    [property: JsonPropertyName("velocity")] Vector2 Velocity,
    [property: JsonPropertyName("health")] float Health,
    [property: JsonPropertyName("rotation")] float Rotation,
    [property: JsonPropertyName("subType")] int SubType = 0,
    [property: JsonPropertyName("state")] EntityStateType State = EntityStateType.Active,
    [property: JsonPropertyName("stateTimer")] float StateTimer = 0f,
    [property: JsonPropertyName("playerName")] string? PlayerName = null);

[Orleans.GenerateSerializer]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EntityType
{
    Player,
    Enemy,
    Bullet,
    Explosion,
    Factory,
    Asteroid
}

[Orleans.GenerateSerializer]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EntityStateType
{
    Active,
    Dying,
    Dead,
    Respawning,
    Alerting
}

[Orleans.GenerateSerializer]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EnemySubType
{
    Kamikaze = 1,
    Sniper = 2,
    Strafing = 3,
    Scout = 4
}

[Orleans.GenerateSerializer]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AsteroidSubType
{
    Stationary = 1,
    Moving = 2
}

[Orleans.GenerateSerializer]
public record WorldState(
    [property: JsonPropertyName("entities")] List<EntityState> Entities,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp,
    [property: JsonPropertyName("sequenceNumber")] long SequenceNumber = 0);

[Orleans.GenerateSerializer]
public record PlayerTransferInfo(
    string PlayerId,
    ActionServerInfo? NewServer,
    ActionServerInfo? OldServer,
    PlayerInfo PlayerState);

[Orleans.GenerateSerializer]
[Orleans.Alias("ZoneStats")]
public record ZoneStats
{
    [Orleans.Id(0)]
    public int FactoryCount { get; init; }
    
    [Orleans.Id(1)]
    public int EnemyCount { get; init; }
    
    [Orleans.Id(2)]
    public int PlayerCount { get; init; }
    
    [Orleans.Id(3)]
    public DateTime LastUpdate { get; init; } = DateTime.UtcNow;
    
    public ZoneStats(int factoryCount, int enemyCount, int playerCount = 0)
    {
        FactoryCount = factoryCount;
        EnemyCount = enemyCount;
        PlayerCount = playerCount;
        LastUpdate = DateTime.UtcNow;
    }
}

[Orleans.GenerateSerializer]
public record PlayerScore(
    string PlayerId,
    string PlayerName,
    int RespawnCount);

[Orleans.GenerateSerializer]
public record GameOverMessage(
    List<PlayerScore> PlayerScores,
    DateTime GameEndTime,
    int RestartDelaySeconds = 15);

[Orleans.GenerateSerializer]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GamePhase
{
    Playing,
    GameOver,
    Restarting
}

[Orleans.GenerateSerializer]
public record DamageEvent(
    string AttackerId,
    string TargetId,
    EntityType AttackerType,
    EntityType TargetType,
    int AttackerSubType,
    int TargetSubType,
    float Damage,
    string WeaponType,
    DateTime Timestamp);

[Orleans.GenerateSerializer]
public record PlayerDamageStats(
    string PlayerId,
    string PlayerName,
    Dictionary<string, float> DamageDealtByWeapon,
    Dictionary<string, float> DamageReceivedByEnemyType,
    Dictionary<string, float> DamageReceivedByWeapon,
    float TotalDamageDealt,
    float TotalDamageReceived);

[Orleans.GenerateSerializer]
public record ZoneDamageReport(
    GridSquare Zone,
    DateTime StartTime,
    DateTime EndTime,
    List<DamageEvent> DamageEvents,
    Dictionary<string, PlayerDamageStats> PlayerStats);

[Orleans.GenerateSerializer]
public record ChatMessage(
    string SenderId,
    string SenderName,
    string Message,
    DateTime Timestamp,
    bool IsSystemMessage = false);
using System.Text.Json.Serialization;

namespace Shooter.Shared.Models;

[Forkleans.GenerateSerializer]
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

[Forkleans.GenerateSerializer]
public record ActionServerInfo(
    string ServerId,
    string IpAddress,
    int UdpPort,
    string HttpEndpoint,
    GridSquare AssignedSquare,
    DateTime RegisteredAt,
    int RpcPort = 0);

[Forkleans.GenerateSerializer]
public record PlayerInfo(
    string PlayerId,
    string Name,
    Vector2 Position,
    Vector2 Velocity,
    float Health);

[Forkleans.GenerateSerializer]
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

[Forkleans.GenerateSerializer]
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

[Forkleans.GenerateSerializer]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EntityStateType
{
    Active,
    Dying,
    Dead,
    Respawning,
    Alerting
}

[Forkleans.GenerateSerializer]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EnemySubType
{
    Kamikaze = 1,
    Sniper = 2,
    Strafing = 3,
    Scout = 4
}

[Forkleans.GenerateSerializer]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AsteroidSubType
{
    Stationary = 1,
    Moving = 2
}

[Forkleans.GenerateSerializer]
public record WorldState(
    [property: JsonPropertyName("entities")] List<EntityState> Entities,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp,
    [property: JsonPropertyName("sequenceNumber")] long SequenceNumber = 0);

[Forkleans.GenerateSerializer]
public record PlayerTransferInfo(
    string PlayerId,
    ActionServerInfo? NewServer,
    ActionServerInfo? OldServer,
    PlayerInfo PlayerState);

[Forkleans.GenerateSerializer]
[Forkleans.Alias("ZoneStats")]
public record ZoneStats
{
    [Forkleans.Id(0)]
    public int FactoryCount { get; init; }
    
    [Forkleans.Id(1)]
    public int EnemyCount { get; init; }
    
    [Forkleans.Id(2)]
    public int PlayerCount { get; init; }
    
    public ZoneStats(int factoryCount, int enemyCount, int playerCount = 0)
    {
        FactoryCount = factoryCount;
        EnemyCount = enemyCount;
        PlayerCount = playerCount;
    }
}

[Forkleans.GenerateSerializer]
public record PlayerScore(
    string PlayerId,
    string PlayerName,
    int RespawnCount);

[Forkleans.GenerateSerializer]
public record GameOverMessage(
    List<PlayerScore> PlayerScores,
    DateTime GameEndTime,
    int RestartDelaySeconds = 15);

[Forkleans.GenerateSerializer]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GamePhase
{
    Playing,
    GameOver,
    Restarting
}

[Forkleans.GenerateSerializer]
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

[Forkleans.GenerateSerializer]
public record PlayerDamageStats(
    string PlayerId,
    string PlayerName,
    Dictionary<string, float> DamageDealtByWeapon,
    Dictionary<string, float> DamageReceivedByEnemyType,
    Dictionary<string, float> DamageReceivedByWeapon,
    float TotalDamageDealt,
    float TotalDamageReceived);

[Forkleans.GenerateSerializer]
public record ZoneDamageReport(
    GridSquare Zone,
    DateTime StartTime,
    DateTime EndTime,
    List<DamageEvent> DamageEvents,
    Dictionary<string, PlayerDamageStats> PlayerStats);
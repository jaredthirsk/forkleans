using System.Text.Json.Serialization;

namespace Shooter.Shared.Models;

[Orleans.GenerateSerializer]
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
    [property: JsonPropertyName("stateTimer")] float StateTimer = 0f);

[Orleans.GenerateSerializer]
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

[Orleans.GenerateSerializer]
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
[Forkleans.GenerateSerializer]
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

[Forkleans.GenerateSerializer]
[Forkleans.Alias("ZoneStats")]
public record ZoneStats
{
    [Forkleans.Id(0)]
    public int FactoryCount { get; init; }
    
    [Forkleans.Id(1)]
    public int EnemyCount { get; init; }
    
    public ZoneStats(int factoryCount, int enemyCount)
    {
        FactoryCount = factoryCount;
        EnemyCount = enemyCount;
    }
}
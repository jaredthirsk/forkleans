using System.Text.Json.Serialization;

namespace Shooter.Shared.Models;

[Forkleans.GenerateSerializer]
public record struct Vector2(
    [property: JsonPropertyName("x")] float X, 
    [property: JsonPropertyName("y")] float Y)
{
    public static Vector2 Zero => new(0, 0);
    
    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator *(Vector2 a, float scalar) => new(a.X * scalar, a.Y * scalar);
    
    public float Length() => MathF.Sqrt(X * X + Y * Y);
    public Vector2 Normalized()
    {
        var length = Length();
        return length > 0 ? new Vector2(X / length, Y / length) : Zero;
    }
    
    public float DistanceTo(Vector2 other) => (this - other).Length();
}
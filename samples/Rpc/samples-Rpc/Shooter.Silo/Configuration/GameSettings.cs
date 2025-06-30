namespace Shooter.Silo.Configuration;

public class GameSettings
{
    /// <summary>
    /// Number of rounds after which the silo should quit.
    /// 0 means never quit (default).
    /// </summary>
    public int QuitAfterNRounds { get; set; } = 0;

    /// <summary>
    /// Number of minutes after which the silo should quit.
    /// 0 means never quit (default).
    /// </summary>
    public int QuitAfterNMinutes { get; set; } = 0;
}
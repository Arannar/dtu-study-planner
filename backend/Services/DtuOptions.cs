namespace Planner.Backend.Services;

public sealed class DtuOptions
{
    public int CourseBatchSize { get; set; } = 25;
    public int CacheEntries { get; set; } = 2048;
    public int CacheMinutes { get; set; } = 60;
    public int MissingCacheMinutes { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 30;
}

namespace Planner.Backend.Services;

public sealed class VolumeResolver : IVolumeResolver
{
    private readonly ILogger<VolumeResolver> _logger;

    public VolumeResolver(ILogger<VolumeResolver> logger)
    {
        _logger = logger;
    }

    public Task<int> ResolveAsync(int? requestedVolume)
    {
        if (requestedVolume.HasValue)
        {
            return Task.FromResult(requestedVolume.Value);
        }

        // First simple version:
        // default to the calendar year
        var fallback = DateTime.Now.Year;
        _logger.LogInformation("No volume specified. Falling back to current year {Volume}", fallback);

        return Task.FromResult(fallback);
    }
}
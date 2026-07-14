namespace Planner.Backend.Services;

public interface IVolumeResolver
{
    Task<int> ResolveAsync(int? requestedVolume);
}
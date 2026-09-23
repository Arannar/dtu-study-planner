using System.Xml;
namespace Planner.Backend.Services;

public sealed class VolumeResolver(IDtuGateway gateway) : IVolumeResolver
{
    public async Task<int> ResolveAsync(int? requestedVolume)
    {
        if (requestedVolume is int explicitYear) return new AcademicYear(explicitYear).StartYear;
        var catalogues = await gateway.GetCatalogueVersionsAsync();
        var settings = catalogues.SelectNodes("descendant-or-self::*[local-name()='VolumeSetting']")!.OfType<XmlElement>()
            .Select(node => new { Text = node.GetAttribute("Volume"), Preferred = node.GetAttribute("Preferred") == "1" })
            .Where(item => System.Text.RegularExpressions.Regex.IsMatch(item.Text, @"^\d{4}/\d{4}$"))
            .Select(item => new { Year = int.Parse(item.Text[..4]), item.Preferred }).ToList();
        var volumes = await gateway.GetVolumesAsync();
        var available = settings.Where(item => volumes.Any(v => v.Year == item.Year && v.Active)).ToList();
        var preferred = available.OrderByDescending(item => item.Preferred)
            .ThenByDescending(item => volumes.Any(v => v.Year == item.Year && v.Current))
            .ThenByDescending(item => item.Year).FirstOrDefault();
        if (preferred is null)
            throw new DtuUnavailableException("default academic-year discovery", new InvalidOperationException("No shared published catalogue and active programme volume was found."));
        return new AcademicYear(preferred.Year).StartYear;
    }
}

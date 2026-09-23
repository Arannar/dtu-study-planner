using System.Xml;
using Planner.Backend.Soap.Courseblocks;
using Planner.Backend.Soap.Visualizations;
using Planner.Backend.Soap.Volumes;

namespace Planner.Backend.Services;

public interface IDtuGateway
{
    Task<XmlElement> GetCoursesAsync(AcademicYear year, IReadOnlyCollection<string> codes);
    Task<Education[]> GetEducationsAsync(int volume);
    Task<Volume[]> GetVolumesAsync();
    Task<XmlElement> GetCatalogueVersionsAsync();
    Task<StudyBoxInfo[]> GetStudyBoxesAsync(int volume);
    Task<Visualization[]> GetVisualizationsAsync(int educationId);
    Task<string> GetVisualizationAsync(int id, string code, int volume, string language);
}

public sealed class DtuUnavailableException(string operation, Exception inner)
    : Exception($"DTU could not complete {operation}. Please retry shortly.", inner);

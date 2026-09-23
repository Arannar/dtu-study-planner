using System.Diagnostics;
using System.ServiceModel;
using System.Xml;
using Microsoft.Extensions.Options;
using Planner.Backend.Soap.Courseblocks;
using Planner.Backend.Soap.Visualizations;
using Planner.Backend.Soap.Volumes;

namespace Planner.Backend.Services;

public sealed class DtuGateway(DtuCache cache, IOptions<DtuOptions> options, ILogger<DtuGateway> logger) : IDtuGateway
{
    public Task<XmlElement> GetCoursesAsync(AcademicYear year, IReadOnlyCollection<string> codes) =>
        CallAsync("SearchDtuShb_Full", () => Configure(new CourseSoapClient(CourseSoapClient.EndpointConfiguration.CourseSoap12)),
            client => client.SearchDtuShb_FullAsync(string.Join(',', codes), "", "", "", year.Catalogue,
                "", "", "", "FullXML", "", "", "", "", ""));

    public Task<Education[]> GetEducationsAsync(int volume) => cache.GetAsync($"educations:{volume}", () =>
        CallAsync("GetEducationsInVolume", VolumeClient, client => client.GetEducationsInVolumeAsync(volume)));

    public Task<Volume[]> GetVolumesAsync() => cache.GetAsync("volumes", () =>
        CallAsync("GetVolumes", VolumeClient, client => client.GetVolumesAsync()));

    public Task<XmlElement> GetCatalogueVersionsAsync() => cache.GetAsync("catalogues", () =>
        CallAsync("CourseCatalogVersions", () => Configure(new CourseSoapClient(CourseSoapClient.EndpointConfiguration.CourseSoap12)),
            client => client.CourseCatalogVersionsAsync()));

    public Task<StudyBoxInfo[]> GetStudyBoxesAsync(int volume) => cache.GetAsync($"studyboxes:{volume}", () =>
        CallAsync("GetStudyBoxInfoByVolumes", () => Configure(new CourseblockServiceClient(CourseblockServiceClient.EndpointConfiguration.BasicHttpBinding_ICourseblockService)),
            client => client.GetStudyBoxInfoByVolumesAsync([volume])));

    public Task<Visualization[]> GetVisualizationsAsync(int educationId) => cache.GetAsync($"views:{educationId}", () =>
        CallAsync("GetVisualizationsInEducation", VisualizationClient, client => client.GetVisualizationsInEducationAsync(educationId)));

    public Task<string> GetVisualizationAsync(int id, string code, int volume, string language) =>
        cache.GetAsync($"html:{id}:{code.ToUpperInvariant()}:{volume}:{language.ToLowerInvariant()}", () =>
            CallAsync("GetVisualization", VisualizationClient, client => client.GetVisualizationAsync(id, code, volume, language)));

    private VolumeServiceClient VolumeClient() => Configure(new VolumeServiceClient(VolumeServiceClient.EndpointConfiguration.BasicHttpBinding_IVolumeService));
    private VisualizationServiceClient VisualizationClient() => Configure(new VisualizationServiceClient(VisualizationServiceClient.EndpointConfiguration.BasicHttpBinding_IVisualizationService));

    private ClientBase<T> ConfigureBase<T>(ClientBase<T> client) where T : class
    {
        var timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
        client.Endpoint.Binding.OpenTimeout = timeout;
        client.Endpoint.Binding.CloseTimeout = TimeSpan.FromSeconds(5);
        client.Endpoint.Binding.SendTimeout = timeout;
        client.InnerChannel.OperationTimeout = timeout;
        return client;
    }

    private CourseSoapClient Configure(CourseSoapClient client) { ConfigureBase(client); return client; }
    private VolumeServiceClient Configure(VolumeServiceClient client) { ConfigureBase(client); return client; }
    private VisualizationServiceClient Configure(VisualizationServiceClient client) { ConfigureBase(client); return client; }
    private CourseblockServiceClient Configure(CourseblockServiceClient client) { ConfigureBase(client); return client; }

    private async Task<TResult> CallAsync<TClient, TResult>(string operation, Func<TClient> factory, Func<TClient, Task<TResult>> invoke)
        where TClient : ICommunicationObject
    {
        for (var attempt = 0; ; attempt++)
        {
            var client = factory();
            var timer = Stopwatch.StartNew();
            try
            {
                var result = await invoke(client);
                try { client.Close(); } catch { client.Abort(); }
                logger.LogInformation("DTU {Operation} completed in {ElapsedMs} ms (attempt {Attempt})", operation, timer.ElapsedMilliseconds, attempt + 1);
                return result;
            }
            catch (Exception ex)
            {
                client.Abort();
                logger.LogWarning(ex, "DTU {Operation} failed after {ElapsedMs} ms", operation, timer.ElapsedMilliseconds);
                if (attempt == 0 && ex is not FaultException && ex is TimeoutException or CommunicationException)
                {
                    await Task.Delay(200);
                    continue;
                }
                throw new DtuUnavailableException(operation, ex);
            }
        }
    }
}

using System.Diagnostics;
using Planner.Backend.Services;
using Planner.Backend.Soap.Courseblocks;
using Planner.Backend.Soap.Visualizations;
using Planner.Backend.Soap.Volumes;

var builder = WebApplication.CreateBuilder(args);

var localAppUrl = builder.Configuration["LocalApp:Url"] ?? "http://localhost:5140";
var cloudPort = Environment.GetEnvironmentVariable("PORT");
var isRunningInContainer = string.Equals(
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
    "true",
    StringComparison.OrdinalIgnoreCase);

if (!builder.Environment.IsDevelopment() &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_URLS")))
{
    var listenUrl = !string.IsNullOrWhiteSpace(cloudPort)
        ? $"http://0.0.0.0:{cloudPort}"
        : isRunningInContainer
            ? "http://0.0.0.0:8080"
            : localAppUrl;

    builder.WebHost.UseUrls(listenUrl);
}

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddScoped<CourseSoapClient>(sp =>
    new CourseSoapClient(CourseSoapClient.EndpointConfiguration.CourseSoap12));
builder.Services.AddScoped<VolumeServiceClient>(sp =>
    new VolumeServiceClient(VolumeServiceClient.EndpointConfiguration.BasicHttpBinding_IVolumeService));
builder.Services.AddScoped<VisualizationServiceClient>(sp =>
    new VisualizationServiceClient(VisualizationServiceClient.EndpointConfiguration.BasicHttpBinding_IVisualizationService));
builder.Services.AddScoped<CourseblockServiceClient>(sp =>
    new CourseblockServiceClient(CourseblockServiceClient.EndpointConfiguration.BasicHttpBinding_ICourseblockService));

builder.Services.AddScoped<ICourseCatalogService, CourseCatalogService>();
builder.Services.AddSingleton<IStudyPlanValidator, StudyPlanValidator>();
builder.Services.AddSingleton<IVolumeResolver, VolumeResolver>();
builder.Services.AddScoped<IProgrammeVisualizationService, ProgrammeVisualizationService>();
builder.Services.AddScoped<IGenericStudyFlowPresetLoader, GenericStudyFlowPresetLoader>();
builder.Services.AddScoped<IProgrammeService, ProgrammeService>();

var app = builder.Build();

app.UseCors("frontend");
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

var staticIndexPath = Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html");
if (File.Exists(staticIndexPath))
{
    app.MapFallbackToFile("index.html");
}

if (app.Configuration.GetValue("LocalApp:OpenBrowserOnStart", false))
{
    app.Lifetime.ApplicationStarted.Register(() => OpenBrowser(localAppUrl, app.Logger));
}

app.Run();

static void OpenBrowser(string url, ILogger logger)
{
    try
    {
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not open browser for {Url}", url);
    }
}

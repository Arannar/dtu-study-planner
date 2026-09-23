using System.Diagnostics;
using Planner.Backend.Services;

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

builder.Services.AddOptions<DtuOptions>().BindConfiguration("Dtu")
    .Validate(o => o.CourseBatchSize is >= 1 and <= 100 && o.CacheEntries >= 64 &&
        o.CacheMinutes > 0 && o.MissingCacheMinutes > 0 && o.TimeoutSeconds is >= 1 and <= 120,
        "Invalid DTU batch, cache, or timeout settings.").ValidateOnStart();
builder.Services.AddSingleton<DtuCache>();
builder.Services.AddScoped<IDtuGateway, DtuGateway>();
builder.Services.AddScoped<ICourseCatalogService, CourseCatalogService>();
builder.Services.AddSingleton<IStudyPlanValidator, StudyPlanValidator>();
builder.Services.AddScoped<IVolumeResolver, VolumeResolver>();
builder.Services.AddScoped<IProgrammeVisualizationService, ProgrammeVisualizationService>();
builder.Services.AddScoped<IGenericStudyFlowPresetLoader, GenericStudyFlowPresetLoader>();
builder.Services.AddScoped<IProgrammeService, ProgrammeService>();
builder.Services.AddScoped<IProgrammeClassificationService, ProgrammeClassificationService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (DtuUnavailableException ex)
    {
        await Results.Problem(statusCode: 503, title: "DTU is temporarily unavailable", detail: ex.Message).ExecuteAsync(context);
    }
    catch (ArgumentException ex)
    {
        await Results.Problem(statusCode: 400, title: "Invalid request", detail: ex.Message).ExecuteAsync(context);
    }
});
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

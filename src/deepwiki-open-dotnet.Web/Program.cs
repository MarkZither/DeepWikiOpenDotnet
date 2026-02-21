using deepwiki_open_dotnet.Web;
using deepwiki_open_dotnet.Web.Components;
using deepwiki_open_dotnet.Web.Services;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Http.Resilience;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.AddRedisOutputCache("cache");

// Aspire's AddStandardResilienceHandler() (registered via ConfigureHttpClientDefaults)
// sets a 10-second AttemptTimeout by default. Embedding a single file via Ollama
// typically takes 1-30 seconds, so the 10s limit causes every ingest request to be
// cancelled before the API can write anything to the database.
// The "-standard" key matches the pipeline name registered by ConfigureHttpClientDefaults
// (empty client-name prefix + "-standard" suffix).
builder.Services.Configure<HttpStandardResilienceOptions>("-standard", options =>
{
    // AttemptTimeout: how long a single HTTP attempt may take.
    // Ollama embedding can take 1–30s per file; 10 min gives plenty of headroom.
    options.AttemptTimeout.Timeout      = TimeSpan.FromMinutes(10);

    // TotalRequestTimeout must be >= AttemptTimeout * (MaxRetryAttempts + 1).
    options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(30);

    options.Retry.MaxRetryAttempts      = 2;
    options.Retry.Delay                 = TimeSpan.FromSeconds(2);

    // Polly validation: SamplingDuration must be >= 2 × AttemptTimeout (600 s).
    // Raise to 25 min so the circuit breaker still functions but doesn't block startup.
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(25);
    // Keep break duration reasonable so a flaky Ollama instance recovers.
    options.CircuitBreaker.BreakDuration    = TimeSpan.FromSeconds(30);
});

// Add services to the container.
// MaximumReceiveMessageSize: Blazor Server sends ALL selected file metadata
// (name, size, type) in a single SignalR message before any content streams.
// The default 32 KB is exceeded with more than ~300 files. 50 MB covers repos
// with tens of thousands of files. Content is still streamed separately.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.RootComponents.MaxJSRootComponents = 300;
    });

builder.Services.AddSignalR(hubOptions =>
{
    hubOptions.MaximumReceiveMessageSize = 50 * 1024 * 1024; // 50 MB
});

builder.Services.AddMudServices();

// UI/chat foundation services (Phase 2)
builder.Services.AddScoped<ChatStateService>();
builder.Services.AddSingleton<NdJsonStreamParser>();
builder.Services.AddHttpClient<ChatApiClient>(client => client.BaseAddress = new("https+http://apiservice"));
builder.Services.AddHttpClient<DocumentsApiClient>(client =>
{
    client.BaseAddress = new("https+http://apiservice");
    // Ingestion calls embed files and can take many minutes for large batches.
    // The default 30-second timeout is far too short — use infinite and rely
    // on the user's Cancel button (CancellationToken) to abort if needed.
    client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
});

// Logs circuit disconnections (e.g. SignalR message-size rejections) to the
// Aspire dashboard so silent WebSocket failures are always diagnosable.
builder.Services.AddScoped<CircuitHandler, CircuitErrorLogger>();

// Markdown rendering pipeline for ChatMessage (Markdig)
builder.Services.AddSingleton(new Markdig.MarkdownPipelineBuilder().Build());

builder.Services.AddHttpClient<WeatherApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://apiservice");
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

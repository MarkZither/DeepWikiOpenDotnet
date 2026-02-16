using deepwiki_open_dotnet.Web;
using deepwiki_open_dotnet.Web.Components;
using MudBlazor.Services;
using deepwiki_open_dotnet.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.AddRedisOutputCache("cache");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// UI/chat foundation services (Phase 2)
builder.Services.AddScoped<ChatStateService>();
builder.Services.AddSingleton<NdJsonStreamParser>();
builder.Services.AddHttpClient<ChatApiClient>(client => client.BaseAddress = new("https+http://apiservice"));

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

using BlazorApp4.Components;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor services
builder.Services.AddMudServices();

// Configure HttpClient for server-side Blazor. Use named client and register a default scoped client for injection.
var baseUrl = builder.Configuration["BaseUrl"] ?? "https://localhost:5001/";
builder.Services.AddHttpClient("ServerAPI", client =>
{
    // Use configured base address so relative URLs (e.g. "webapi/...") work
    client.BaseAddress = new Uri(baseUrl);
});
// Provide a scoped HttpClient that resolves to the named client
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("ServerAPI"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

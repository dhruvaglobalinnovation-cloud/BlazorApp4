using BlazorApp4.Components;
using MudBlazor.Services;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor services
builder.Services.AddMudServices();

// Add controllers for API endpoints
builder.Services.AddControllers();

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "BlazorApp4 API", Version = "v1" });
});

// Configure HttpClient for server-side Blazor. Use named client and register a default scoped client for injection.
var baseUrl = builder.Configuration["BaseUrl"] ?? "https://localhost:5001/";
builder.Services.AddHttpClient("ServerAPI", client =>
{
    client.BaseAddress = new Uri(baseUrl);
});
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("ServerAPI"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Enable Swagger for all environments (adjust if needed)
app.UseSwagger(c => c.RouteTemplate = "swagger/{documentName}/swagger.json");
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "BlazorApp4 API V1");
    c.RoutePrefix = "swagger"; // serve at /swagger
});

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Antiforgery middleware must be between UseRouting and endpoint mapping
app.UseAntiforgery();

// Map API controllers
app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

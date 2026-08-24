using System.Reflection;
using System.Text.Json.Serialization;
using ApiRouter.Actions;
using ApiRouter.Auth;
using ApiRouter.Data;
using ApiRouter.Dispatching;
using ApiRouter.Rules;
using ApiRouter.Scheduling;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// --- Persistence: a real, file-based SQLite database via EF Core. ---
var connectionString = builder.Configuration.GetConnectionString("Router")
                       ?? "Data Source=router.db";
builder.Services.AddDbContext<RouterDbContext>(options => options.UseSqlite(connectionString));

// --- MVC + JSON (enums serialized as strings for a friendlier API). ---
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// --- RFC 7807 problem responses; unhandled errors return JSON, never a stack trace. ---
builder.Services.AddProblemDetails();

// --- Outbound HTTP for forwarding to HTTP targets (size-capped to bound memory;
//     redirects disabled so a public target can't bounce a request to an internal host). ---
builder.Services.AddHttpClient();
builder.Services
    .AddHttpClient("forward", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.MaxResponseContentBufferSize = 10 * 1024 * 1024; // 10 MB
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

// --- Client for downloading client-supplied wallpaper URLs. Redirects are followed (many
//     image hosts, e.g. picsum.photos, 302 to a CDN), but the ConnectCallback validates the
//     IP of EVERY connection — the initial host and each redirect hop — so a public URL can't
//     bounce the fetch to an internal address, and DNS rebinding is closed because the address
//     connected to is the one validated. Body size is capped to bound memory. ---
builder.Services
    .AddHttpClient("wallpaper-download", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.MaxResponseContentBufferSize = 20 * 1024 * 1024; // 20 MB
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        ConnectCallback = NetworkGuard.SafeConnectAsync,
    });

// --- Routing domain services. ---
builder.Services.AddSingleton<IRateLimiter, InMemoryRateLimiter>();
builder.Services.AddScoped<IRuleEngine, RuleEngine>();
builder.Services.AddScoped<IDispatchExecutor, DispatchExecutor>();
builder.Services.AddSingleton<IActionHandler, SetWallpaperActionHandler>();
builder.Services.AddHostedService<SchedulerHostedService>();

// --- API-key authentication keeps the API stateless. ---
builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        ApiKeyAuthenticationHandler.AdminPolicy,
        policy => policy.RequireRole(ApiKeyAuthenticationHandler.AdminRole));
});

// --- OpenAPI / Swagger, wired so "Authorize" sends the X-Api-Key header. ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "API Router",
        Version = "v1",
        Description = "A RESTful router that receives requests, forwards or executes them " +
                      "against registered targets, and enforces per-user rules.",
    });

    var apiKeyScheme = new OpenApiSecurityScheme
    {
        Name = ApiKeyAuthenticationHandler.HeaderName,
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Description = "Paste an API key to authorize requests.",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = ApiKeyAuthenticationHandler.SchemeName,
        },
    };
    options.AddSecurityDefinition(ApiKeyAuthenticationHandler.SchemeName, apiKeyScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [apiKeyScheme] = Array.Empty<string>() });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// --- Create and seed the database on startup. ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RouterDbContext>();
    db.Database.Migrate();

    // Bootstrap admin key. Outside Development we fail closed: an operator must supply
    // Bootstrap:AdminApiKey rather than silently seeding the well-known demo key (which is
    // published in this repo) as an admin credential.
    var configuredKey = builder.Configuration["Bootstrap:AdminApiKey"];
    if (string.IsNullOrWhiteSpace(configuredKey) && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Bootstrap:AdminApiKey must be configured outside the Development environment; " +
            "refusing to seed the well-known demo admin key.");
    }

    DbSeeder.Seed(db, configuredKey ?? DbSeeder.DemoApiKey);
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // Return RFC 7807 problem responses for unhandled errors, and enforce HTTPS.
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseHttpsRedirection();

// Swagger is served in Development, or anywhere Swagger:Enabled is explicitly set true —
// so the full API surface isn't published by default in Production.
var swaggerEnabled = app.Environment.IsDevelopment()
                     || app.Configuration.GetValue<bool>("Swagger:Enabled");
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "API Router v1"));
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => swaggerEnabled
    ? Results.Redirect("/swagger")
    : Results.Ok(new { service = "api-router", status = "ok" }));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>Exposed so integration tests can reference the entry-point assembly.</summary>
public partial class Program { }

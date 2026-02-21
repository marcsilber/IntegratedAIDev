using AIDev.Api.Data;
using AIDev.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=aidev.db"));

// ── GitHub Integration ────────────────────────────────────────────────────
var gitHubToken = builder.Configuration["GitHub:PersonalAccessToken"];
if (!string.IsNullOrWhiteSpace(gitHubToken))
{
    builder.Services.AddSingleton<IGitHubService, GitHubService>();
}
else
{
    builder.Services.AddSingleton<IGitHubService, NullGitHubService>();
}

// ── Product Owner Agent ───────────────────────────────────────────────────
builder.Services.AddSingleton<IReferenceDocumentService, ReferenceDocumentService>();
if (!string.IsNullOrWhiteSpace(gitHubToken))
{
    builder.Services.AddSingleton<ILlmClientFactory, LlmClientFactory>();
    builder.Services.AddSingleton<ILlmService, LlmService>();
    builder.Services.AddHostedService<ProductOwnerAgentService>();

    // ── Architect Agent ───────────────────────────────────────────────────
    builder.Services.AddSingleton<ICodebaseService, CodebaseService>();
    builder.Services.AddSingleton<IArchitectLlmService, ArchitectLlmService>();
    builder.Services.AddHostedService<ArchitectAgentService>();

    // ── Copilot Implementation (Phase 4) ──────────────────────────────────
    builder.Services.AddHostedService<ImplementationTriggerService>();
    builder.Services.AddHostedService<PrMonitorService>();
    // ── Code Review Agent (Phase 6) ───────────────────────────────
    builder.Services.AddSingleton<ICodeReviewLlmService, CodeReviewLlmService>();
    builder.Services.AddHostedService<CodeReviewAgentService>();
    // ── Pipeline Orchestrator (Phase 5) ───────────────────────────────────
    builder.Services.AddHostedService<PipelineOrchestratorService>();
}

// ── Authentication (Entra ID) ─────────────────────────────────────────────
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd");

// ── API Services ──────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddOpenApi();

// ── CORS ──────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
            ?? builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:5173" };
        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// ── Auto-migrate database ─────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// ── Middleware Pipeline ───────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

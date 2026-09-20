using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using Zunavio.KdpFactory.Application;
using Zunavio.KdpFactory.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddKdpInfrastructure(builder.Configuration)
    .AddKdpApplication();

builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

// ---- REST API ----
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---- Health ----
builder.Services.AddHealthChecks();

// ---- Blazor Server dashboard ----
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "ZUNAVIO KDP Factory API v1"));
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var checks = string.Join(", ", report.Entries.Select(e => $"{e.Key}: {e.Value.Status}"));
        await ctx.Response.WriteAsync($"{{\"status\":\"{report.Status}\",\"checks\":[{string.Join(",", report.Entries.Select(e => $"\"{e.Key}\""))}],\"detail\":\"{checks}\"}}");
    },
});

app.MapControllers();

app.MapRazorComponents<Zunavio.KdpFactory.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
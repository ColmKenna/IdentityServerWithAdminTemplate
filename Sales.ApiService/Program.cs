using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using Sales.ApiService.Data;

const string ApiScope = "sales.api";
const string SysAdminRole = "SysAdmin";
const string ApiScopePolicy = "ApiScope";
const string SysAdminPolicy = "SysAdmin";

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

builder.AddSqlServerDbContext<SalesDbContext>("SalesDb");

var identityServerAuthority = builder.Configuration["services:identityserver:https:0"]
    ?? throw new InvalidOperationException("Configuration value 'services:identityserver:https:0' is required (Aspire service discovery reference to 'identityserver').");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = identityServerAuthority;
        options.TokenValidationParameters.ValidAudiences = ["sales"];
        options.TokenValidationParameters.ValidTypes = ["at+jwt"];
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ApiScopePolicy, policy => policy.RequireClaim("scope", ApiScope))
    .AddPolicy(SysAdminPolicy, policy => policy
        .RequireClaim("scope", ApiScope)
        .RequireRole(SysAdminRole));

builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Sales API", Version = "v1" });
    options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OAuth2,
        Flows = new OpenApiOAuthFlows
        {
            AuthorizationCode = new OpenApiOAuthFlow
            {
                AuthorizationUrl = new Uri($"{identityServerAuthority}/connect/authorize"),
                TokenUrl = new Uri($"{identityServerAuthority}/connect/token"),
                Scopes = new Dictionary<string, string> { [ApiScope] = "Access the Sales API" },
            },
        },
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2" } },
            [ApiScope]
        },
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Sales API v1");
        options.OAuthClientId("razorclient");
        options.OAuthUsePkce();
    });
}

app.UseAuthentication();
app.UseAuthorization();

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/", () => "API service is running. Navigate to /weatherforecast to see sample data.");

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.RequireAuthorization(ApiScopePolicy);

app.MapGet("/admin/ping", () => Results.Ok(new { message = "pong, admin" }))
    .WithName("AdminPing")
    .RequireAuthorization(SysAdminPolicy);

app.MapDefaultEndpoints();

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public partial class Program;

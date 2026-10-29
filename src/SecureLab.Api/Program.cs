using SecureLab.Api.Scaffolding;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SecureLab.Api.Application.Access;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Endpoints;
using SecureLab.Api.Security;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "Client"
});
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
// Зламаний JSON або дата в чужому форматі є помилкою клієнта: 400 замість 500.
// Тіло відповіді все одно формує стандартний Problem Details без деталей винятку.
builder.Services.AddExceptionHandler(options => options.StatusCodeSelector = exception =>
    exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError);

var connectionString = builder.Configuration.GetConnectionString("SecureLab")
    ?? throw new InvalidOperationException(
        "Connection string 'SecureLab' не налаштовано.");

builder.Services.AddDbContext<SecureLabDbContext>(options =>
    options.UseNpgsql(connectionString));
// ЛР 4: сторож правил доступу і обмеження переліків. Без стану, тому один екземпляр на застосунок.
builder.Services.AddSingleton<IncidentAccessGuard>();
builder.Services.AddSingleton<IncidentScope>();
builder.Services.AddScoped<IncidentQueries>();
builder.Services.AddScoped<SeverityBreakdownQuery>();
builder.Services.AddScoped<IncidentCreationService>();
builder.Services.AddScoped<IncidentSearchQuery>();
builder.Services.AddScoped<IncidentChangeService>();
builder.Services.AddSingleton(TimeProvider.System);

builder.AddLab03Identity();
// ЛР 3, добрий рівень: обмеження спроб входу (політика "login", фіксоване вікно, ключ IP).
builder.AddLoginThrottling();
// ЛР 3, відмінний рівень: строк дії сеансу 20 хв зі sliding і CSRF-токен для змінювальних запитів.
builder.AddSessionLifetime();
builder.AddForgeryGuard();

var app = builder.Build();

var resetRequested = args.Contains("--reset-database", StringComparer.Ordinal);
await DatabaseBootstrap.InitializeAsync(app.Services, app.Configuration, app.Environment, resetRequested);

if (resetRequested)
{
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", async (SecureLabDbContext dbContext, CancellationToken cancellationToken) =>
    await dbContext.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.Problem(
            title: "База даних недоступна",
            statusCode: StatusCodes.Status503ServiceUnavailable))
    .WithName("GetHealth")
    .Produces(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapIncidentEndpoints();
app.MapIncidentChangeEndpoints();
app.MapIncidentCommentEndpoints();
app.MapLab03Identity();
app.MapForgeryTokenEndpoint();
app.MapLab02Endpoints();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;

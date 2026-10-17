using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SecureLab.Api.Tests;

// Окремий host зі своїми лічильниками rate limiter і, за потреби, своїм журналом.
// Helper LoginAsync працює лише з SecureLabApiFactory, тому вхід тут робиться напряму.
internal static class TestHosts
{
    public static WebApplicationFactory<Program> Fresh(
        SecureLabApiFactory factory,
        int? permitLimit = null,
        int? windowSeconds = null,
        ILoggerProvider? logSink = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            var settings = new Dictionary<string, string?>();
            if (permitLimit is not null) settings["Auth:LoginThrottle:PermitLimit"] = permitLimit.ToString();
            if (windowSeconds is not null) settings["Auth:LoginThrottle:WindowSeconds"] = windowSeconds.ToString();
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            if (logSink is not null) builder.ConfigureLogging(logging => logging.AddProvider(logSink));
        });

    public static HttpClient HttpsClient(WebApplicationFactory<Program> host) =>
        host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string userName, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { userName, password });
}

// Збирає всі повідомлення журналу host у пам'яті, щоб тест міг перевірити їхній вміст.
internal sealed class MemoryLogSink : ILoggerProvider
{
    public ConcurrentQueue<(string Category, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new SinkLogger(categoryName, this);

    public void Dispose()
    {
    }

    private sealed class SinkLogger(string category, MemoryLogSink sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Entries.Enqueue((category, formatter(state, exception)));
    }
}

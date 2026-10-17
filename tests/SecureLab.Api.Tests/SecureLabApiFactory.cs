using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SecureLab.Api.Tests;

public sealed class SecureLabApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // У тестовому сервері в усіх клієнтів немає IP, тож усі входи класу потрапляють в один
        // лічильник. Звичайним класам ліміт входів піднято; справжні 4 за 30 с перевіряє
        // LoginThrottleTests на окремому host.
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Auth:LoginThrottle:PermitLimit"] = "1000" }));
    }
}

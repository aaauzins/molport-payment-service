using Microsoft.Extensions.Configuration;
using PaymentService;

namespace PaymentService.Tests;

public static class TestConfig
{
    public static AppSettings Load()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        return config.GetSection("AppSettings").Get<AppSettings>()
            ?? throw new InvalidOperationException(
                "AppSettings missing. Create PaymentService.Tests/appsettings.Test.json or set environment variables.");
    }
}

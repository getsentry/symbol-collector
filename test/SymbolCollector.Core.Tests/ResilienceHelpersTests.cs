using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Xunit;

namespace SymbolCollector.Core.Tests;

public class ResilienceHelpersTests
{
    [Fact]
    public async Task SentryRetryStrategy_ShouldRetryOnFailure()
    {
        // Arrange
        var attempts = 0;
        var options = ResilienceHelpers.SentryRetryStrategy();
        var handler = new TestMessageHandler(
            (_, _) =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            });

        var services = new ServiceCollection();
        services.AddHttpClient()
            .ConfigureHttpClientDefaults(configure =>
            {
                configure.ConfigurePrimaryHttpMessageHandler(() => handler);
                configure.AddResilienceHandler("retry", builder =>
                    builder.AddRetry(options));
            });

        var serviceProvider = services.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("retry");

        httpClient.BaseAddress = new Uri("https://example.com");

        // Act
        var response = await httpClient.GetAsync("/");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1 + options.MaxRetryAttempts, attempts); // 1 initial request + max retries

    }

    [Fact]
    public async Task SentryRetryStrategy_OnRetry_ShouldCallAddBreadcrumb()
    {
        // Arrange
        var attempts = 0;
        var breadcrumbs = new List<(string, Dictionary<string, string>)>();
        var options = ResilienceHelpers.SentryRetryStrategy((message, data) => breadcrumbs.Add((message, data)));
        var handler = new TestMessageHandler(
            (_, _) =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            });

        var services = new ServiceCollection();
        services.AddHttpClient("retry")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler("retry", builder => builder.AddRetry(options));

        var serviceProvider = services.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("retry");

        httpClient.BaseAddress = new Uri("https://example.com");

        // Act
        var response = await httpClient.GetAsync("/");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(options.MaxRetryAttempts, breadcrumbs.Count); // Ensure AddBreadcrumb was called for each retry
    }

}

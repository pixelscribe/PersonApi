using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.MySql;

namespace PersonApi.Tests;

public class PersonApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // When set, tests run as post-deploy regression tests against a real deployed
    // instance instead of an in-process TestServer + disposable Testcontainers DB.
    private static readonly string? RemoteBaseUrl = Environment.GetEnvironmentVariable("PERSON_API_BASE_URL");

    private readonly MySqlContainer? _mysql = RemoteBaseUrl is null
        ? new MySqlBuilder("mysql:8.0").WithDatabase("personapi_test").Build()
        : null;

    private readonly ConcurrentBag<ulong> _createdIds = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_mysql is null)
        {
            return;
        }

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _mysql.GetConnectionString(),
            });
        });
    }

    // Hides WebApplicationFactory.CreateClient(): the test methods were written assuming a
    // disposable per-run database and don't delete what they create, which is fine against
    // Testcontainers but not against a real persistent DB. In remote mode, every person the
    // tests create is tracked here so DisposeAsync can delete it afterward.
    public new HttpClient CreateClient()
    {
        if (RemoteBaseUrl is null)
        {
            return base.CreateClient();
        }

        var handler = new CreatedPersonTrackingHandler(_createdIds) { InnerHandler = new HttpClientHandler() };
        return new HttpClient(handler) { BaseAddress = new Uri(RemoteBaseUrl) };
    }

    public async Task InitializeAsync()
    {
        if (_mysql is not null)
        {
            await _mysql.StartAsync();
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (RemoteBaseUrl is not null && !_createdIds.IsEmpty)
        {
            using var cleanup = new HttpClient { BaseAddress = new Uri(RemoteBaseUrl) };
            foreach (var id in _createdIds.Distinct())
            {
                await cleanup.DeleteAsync($"/api/person/{id}");
            }
        }

        await base.DisposeAsync();

        if (_mysql is not null)
        {
            await _mysql.DisposeAsync();
        }
    }

    private sealed class CreatedPersonTrackingHandler(ConcurrentBag<ulong> createdIds) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);

            if (request.Method == HttpMethod.Post
                && response.StatusCode == HttpStatusCode.Created
                && response.Content.Headers.ContentType?.MediaType == "application/json")
            {
                // Buffer and replace the content so the caller can still read it downstream.
                var body = await response.Content.ReadAsStringAsync(ct);
                if (JsonDocument.Parse(body).RootElement.TryGetProperty("id", out var idProp))
                {
                    createdIds.Add(idProp.GetUInt64());
                }

                response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            return response;
        }
    }
}

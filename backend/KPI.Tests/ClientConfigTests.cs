using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.API.Controllers;
using Microsoft.AspNetCore.Hosting;

namespace KPI.Tests;

public class ClientConfigTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData(" v1.2 ", "1.2.0")]
    [InlineData("2", "2.0.0")]
    [InlineData("1.2.3-beta.1+build5", "1.2.3")]
    [InlineData("01.002.3", "1.2.3")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("latest", null)]
    [InlineData("1.2.3.4", null)]
    [InlineData("1..2", null)]
    [InlineData("<script>", null)]
    [InlineData("99999999.0.0", null)]
    public void Versions_are_normalised_and_garbage_is_ignored(string? raw, string? expected) =>
        Assert.Equal(expected, ClientConfigController.NormalizeVersion(raw));

    private static async Task<JsonElement> GetAsync(ApiFactory factory)
    {
        var res = await factory.CreateClient().GetAsync("/api/client-config");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private sealed class Configured(string min, string latest) : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Client:MinDesktopVersion", min);
            builder.UseSetting("Client:LatestDesktopVersion", latest);
        }
    }

    [Fact]
    public async Task Defaults_are_served_without_signing_in()
    {
        using var factory = new ApiFactory();
        var json = await GetAsync(factory);
        Assert.Equal("1.0.0", json.GetProperty("minDesktopVersion").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("latestDesktopVersion").ValueKind);
    }

    [Fact]
    public async Task Configured_values_are_normalised()
    {
        using var factory = new Configured("v1.4", "1.5.2");
        var json = await GetAsync(factory);
        Assert.Equal("1.4.0", json.GetProperty("minDesktopVersion").GetString());
        Assert.Equal("1.5.2", json.GetProperty("latestDesktopVersion").GetString());
    }

    [Fact]
    public async Task Garbage_falls_back_to_the_defaults()
    {
        using var factory = new Configured("not a version", "???");
        var json = await GetAsync(factory);
        Assert.Equal("1.0.0", json.GetProperty("minDesktopVersion").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("latestDesktopVersion").ValueKind);
    }
}

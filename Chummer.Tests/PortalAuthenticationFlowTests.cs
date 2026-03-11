#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class PortalAuthenticationFlowTests
{
    [TestMethod]
    public async Task Dev_login_populates_portal_user_and_me_reports_authenticated_owner()
    {
        await using WebApplication app = await CreateAppAsync(devAuthEnabled: true, requireAuth: false);
        using HttpClient client = app.GetTestClient();

        using HttpResponseMessage loginResponse = await client.PostAsJsonAsync("/auth/dev-login", new PortalDevLoginRequest("Alice@example.com"));
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        string cookieHeader = GetRequiredCookieHeader(loginResponse);

        JsonObject me = await GetRequiredJsonObject(client, "/auth/me", cookieHeader);
        bool? isAuthenticated = me["isAuthenticated"]?.GetValue<bool>();
        Assert.IsNotNull(isAuthenticated);
        Assert.IsTrue(isAuthenticated.Value);
        Assert.AreEqual("alice@example.com", me["owner"]?.GetValue<string>());
        Assert.AreEqual("portal-dev", me["authenticationType"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Protected_routes_return_unauthorized_when_portal_auth_is_required_and_cookie_is_missing()
    {
        await using WebApplication app = await CreateAppAsync(devAuthEnabled: true, requireAuth: true);
        using HttpClient client = app.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync("/api/protected");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Protected_routes_allow_authenticated_portal_cookie_when_auth_is_required()
    {
        await using WebApplication app = await CreateAppAsync(devAuthEnabled: true, requireAuth: true);
        using HttpClient client = app.GetTestClient();

        using HttpResponseMessage loginResponse = await client.PostAsJsonAsync("/auth/dev-login", new PortalDevLoginRequest("bob@example.com"));
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        string cookieHeader = GetRequiredCookieHeader(loginResponse);

        JsonObject response = await GetRequiredJsonObject(client, "/api/protected", cookieHeader);
        Assert.AreEqual("bob@example.com", response["owner"]?.GetValue<string>());
    }

    private static async Task<WebApplication> CreateAppAsync(bool devAuthEnabled, bool requireAuth)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "Chummer.Portal.Test";
                options.LoginPath = "/auth/login";
                options.LogoutPath = "/auth/logout";
            });
        builder.Services.AddAuthorization();

        PortalAuthenticationSettings settings = new(
            devAuthEnabled,
            requireAuth,
            "dev@example.com",
            CookieAuthenticationDefaults.AuthenticationScheme);

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.Use(async (context, next) =>
        {
            if (settings.RequireAuthenticatedUser
                && PortalProtectedRouteMatcher.RequiresAuthenticatedUser(context.Request.Path)
                && context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "portal_auth_required"
                });
                return;
            }

            await next();
        });

        PortalAuthenticationEndpoints.MapPortalAuthenticationEndpoints(app, settings);
        app.MapGet("/api/protected", (HttpContext context) =>
        {
            string owner = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            return Results.Json(new
            {
                owner
            });
        });

        await app.StartAsync();
        return app;
    }

    private static async Task<JsonObject> GetRequiredJsonObject(HttpClient client, string relativePath, string? cookieHeader = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, relativePath);
        if (!string.IsNullOrWhiteSpace(cookieHeader))
        {
            request.Headers.Add("Cookie", cookieHeader);
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"GET {relativePath} failed with {(int)response.StatusCode}: {content}");
        JsonNode? parsed = JsonNode.Parse(content);
        Assert.IsInstanceOfType<JsonObject>(parsed);
        return (JsonObject)parsed;
    }

    private static string GetRequiredCookieHeader(HttpResponseMessage response)
    {
        IEnumerable<string> cookieValues = response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
            ? values
            : [];
        string cookieHeader = string.Join("; ", cookieValues.Select(value => value.Split(';', 2)[0]));
        Assert.IsFalse(string.IsNullOrWhiteSpace(cookieHeader));
        return cookieHeader;
    }
}

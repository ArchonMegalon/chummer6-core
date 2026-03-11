#nullable enable annotations

using System.Security.Claims;
using Chummer.Contracts.Owners;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class PortalAuthenticatedOwnerPropagationTests
{
    private const string SharedKey = "portal-owner-test-key";

    [TestMethod]
    public void Apply_clears_spoofed_portal_headers_when_owner_forwarding_is_disabled()
    {
        DefaultHttpContext context = new();
        context.Request.Path = "/api/workspaces";
        context.Request.Headers[PortalOwnerPropagationContract.OwnerHeaderName] = "spoofed@example.com";
        context.Request.Headers[PortalOwnerPropagationContract.TimestampHeaderName] = "123";
        context.Request.Headers[PortalOwnerPropagationContract.SignatureHeaderName] = "bad";

        PortalAuthenticatedOwnerPropagation.Apply(context, null);

        Assert.IsFalse(context.Request.Headers.ContainsKey(PortalOwnerPropagationContract.OwnerHeaderName));
        Assert.IsFalse(context.Request.Headers.ContainsKey(PortalOwnerPropagationContract.TimestampHeaderName));
        Assert.IsFalse(context.Request.Headers.ContainsKey(PortalOwnerPropagationContract.SignatureHeaderName));
    }

    [TestMethod]
    public void Apply_sets_signed_owner_headers_for_authenticated_portal_user()
    {
        DefaultHttpContext context = new();
        context.Request.Path = "/api/workspaces";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "Alice@example.com")
        ], "portal-cookie"));

        PortalAuthenticatedOwnerPropagation.Apply(context, SharedKey);

        Assert.AreEqual("alice@example.com", context.Request.Headers[PortalOwnerPropagationContract.OwnerHeaderName].ToString());
        Assert.IsTrue(long.TryParse(context.Request.Headers[PortalOwnerPropagationContract.TimestampHeaderName], out _));
        Assert.AreEqual(
            PortalAuthenticatedOwnerPropagation.CreateSignature(
                "alice@example.com",
                context.Request.Headers[PortalOwnerPropagationContract.TimestampHeaderName].ToString(),
                SharedKey),
            context.Request.Headers[PortalOwnerPropagationContract.SignatureHeaderName].ToString());
    }

    [TestMethod]
    public void Apply_does_not_emit_headers_for_non_api_routes()
    {
        DefaultHttpContext context = new();
        context.Request.Path = "/blazor/";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "portal@example.com")
        ], "portal-cookie"));

        PortalAuthenticatedOwnerPropagation.Apply(context, SharedKey);

        Assert.IsFalse(context.Request.Headers.ContainsKey(PortalOwnerPropagationContract.OwnerHeaderName));
    }
}

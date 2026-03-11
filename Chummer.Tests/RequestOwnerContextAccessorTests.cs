#nullable enable annotations

using System;
using System.Security.Claims;
using Chummer.Api.Owners;
using Chummer.Contracts.Owners;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class RequestOwnerContextAccessorTests
{
    private const string SharedKey = "portal-owner-test-key";
    private const string OwnerHeaderName = "X-Chummer-Owner";

    [TestMethod]
    public void Current_defaults_to_local_single_user_when_no_http_context_exists()
    {
        RequestOwnerContextAccessor accessor = new(new HttpContextAccessor());

        Assert.AreEqual(OwnerScope.LocalSingleUser.NormalizedValue, accessor.Current.NormalizedValue);
    }

    [TestMethod]
    public void Current_uses_authenticated_nameidentifier_claim_when_present()
    {
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "Alice@example.com")
        ], "test"));

        RequestOwnerContextAccessor accessor = new(new HttpContextAccessor
        {
            HttpContext = context
        });

        Assert.AreEqual("alice@example.com", accessor.Current.NormalizedValue);
    }

    [TestMethod]
    public void Current_uses_forwarded_header_when_enabled_and_user_is_anonymous()
    {
        DefaultHttpContext context = new();
        context.Request.Headers[OwnerHeaderName] = "Bob@example.com";

        RequestOwnerContextAccessor accessor = new(
            new HttpContextAccessor
            {
                HttpContext = context
            },
            headerName: OwnerHeaderName);

        Assert.AreEqual("bob@example.com", accessor.Current.NormalizedValue);
    }

    [TestMethod]
    public void Current_uses_signed_portal_owner_when_present_and_valid()
    {
        DefaultHttpContext context = new();
        context.Request.Path = "/api/workspaces";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "PortalUser@example.com")
        ], "portal"));

        PortalAuthenticatedOwnerPropagation.Apply(context, SharedKey);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());

        RequestOwnerContextAccessor accessor = new(
            new HttpContextAccessor
            {
                HttpContext = context
            },
            headerName: OwnerHeaderName,
            portalOwnerSharedKey: SharedKey);

        Assert.AreEqual("portaluser@example.com", accessor.Current.NormalizedValue);
    }

    [TestMethod]
    public void Current_prefers_signed_portal_owner_over_dev_owner_header()
    {
        DefaultHttpContext context = new();
        context.Request.Path = "/api/workspaces";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "carol@example.com")
        ], "portal"));

        PortalAuthenticatedOwnerPropagation.Apply(context, SharedKey);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        context.Request.Headers[OwnerHeaderName] = "ignored@example.com";

        RequestOwnerContextAccessor accessor = new(
            new HttpContextAccessor
            {
                HttpContext = context
            },
            headerName: OwnerHeaderName,
            portalOwnerSharedKey: SharedKey);

        Assert.AreEqual("carol@example.com", accessor.Current.NormalizedValue);
    }

    [TestMethod]
    public void Current_ignores_invalid_signed_portal_owner_and_uses_dev_owner_header_when_enabled()
    {
        DefaultHttpContext context = new();
        context.Request.Headers[PortalOwnerPropagationContract.OwnerHeaderName] = "portal@example.com";
        context.Request.Headers[PortalOwnerPropagationContract.TimestampHeaderName] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        context.Request.Headers[PortalOwnerPropagationContract.SignatureHeaderName] = "bad-signature";
        context.Request.Headers[OwnerHeaderName] = "Bob@example.com";

        RequestOwnerContextAccessor accessor = new(
            new HttpContextAccessor
            {
                HttpContext = context
            },
            headerName: OwnerHeaderName,
            portalOwnerSharedKey: SharedKey);

        Assert.AreEqual("bob@example.com", accessor.Current.NormalizedValue);
    }

    [TestMethod]
    public void Current_prefers_authenticated_user_over_forwarded_header()
    {
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "carol@example.com")
        ], "test"));
        context.Request.Headers[OwnerHeaderName] = "ignored@example.com";

        RequestOwnerContextAccessor accessor = new(
            new HttpContextAccessor
            {
                HttpContext = context
            },
            headerName: OwnerHeaderName);

        Assert.AreEqual("carol@example.com", accessor.Current.NormalizedValue);
    }
}

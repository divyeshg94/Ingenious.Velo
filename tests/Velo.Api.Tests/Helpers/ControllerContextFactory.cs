using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Velo.Api.Tests.Helpers;

/// <summary>
/// Factory helpers for creating ControllerContext instances used in controller unit tests.
/// </summary>
public static class ControllerContextFactory
{
    /// <summary>Creates an empty context with no OrgId in HttpContext.Items.</summary>
    public static ControllerContext Empty()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        return new ControllerContext { HttpContext = ctx };
    }

    /// <summary>Creates a context with OrgId set in HttpContext.Items.</summary>
    public static ControllerContext WithOrgId(string orgId)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Items["OrgId"] = orgId;
        return new ControllerContext { HttpContext = ctx };
    }

    /// <summary>Creates a context with OrgId and an additional request header.</summary>
    public static ControllerContext WithOrgIdAndHeader(string orgId, string headerName, string headerValue)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Items["OrgId"] = orgId;
        ctx.Request.Headers[headerName] = headerValue;
        return new ControllerContext { HttpContext = ctx };
    }
}

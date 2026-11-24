namespace Attestia.Api.Infrastructure;

/// <summary>
/// Intestazioni di sicurezza applicate a tutte le risposte dell'API. Su IIS le stesse intestazioni sono
/// impostate anche in web.config (difesa in profondita'); qui servono per Kestrel e per i test.
/// </summary>
internal static class SecurityHeaders
{
    private const string ApiCsp = "default-src 'none'; frame-ancestors 'none'";
    private const string AdminCsp = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; form-action 'self'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var h = context.Response.Headers;
                h.XContentTypeOptions = "nosniff";
                h.XFrameOptions = "DENY";
                h["Referrer-Policy"] = "no-referrer";
                h["Cross-Origin-Opener-Policy"] = "same-origin";
                h.ContentSecurityPolicy = context.Request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase) ? AdminCsp : ApiCsp;
                h.Remove("X-Powered-By");
                h.Remove("Server");
                return Task.CompletedTask;
            });
            await next(context);
        });
}

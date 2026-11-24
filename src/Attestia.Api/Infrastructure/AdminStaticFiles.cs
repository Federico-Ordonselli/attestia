using Microsoft.Extensions.FileProviders;

namespace Attestia.Api.Infrastructure;

/// <summary>
/// Solo per lo sviluppo su Linux: serve la build Angular sotto /admin dalla stessa origine dell'API,
/// replicando il layout di IIS (un sito, /admin applicazione statica). In produzione lo fa IIS.
/// </summary>
internal static class AdminStaticFiles
{
    public static IApplicationBuilder UseAdminStaticFiles(this WebApplication app, IConfiguration configuration)
    {
        var configured = configuration["Admin:StaticPath"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return app;
        }

        var root = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, configured));
        if (!Directory.Exists(root))
        {
            return app;
        }

        var files = new PhysicalFileProvider(root);
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files, RequestPath = "/admin" });
        // Fallback SPA: le rotte Angular (es. /admin/scadenze) restituiscono index.html.
        app.MapFallbackToFile("/admin/{**path}", "index.html", new StaticFileOptions { FileProvider = files }).AllowAnonymous();
        return app;
    }
}

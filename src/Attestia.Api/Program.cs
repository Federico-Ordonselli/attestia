using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Attestia.Api.Auth;
using Attestia.Api.Endpoints;
using Attestia.Api.Infrastructure;
using Attestia.Core.Time;
using Attestia.Data.Infrastructure;
using Attestia.Data.Repositories;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

if (MigrateCommand.IsRequested(args))
{
    return MigrateCommand.Run(builder.Configuration, args);
}

// --- Logging strutturato: JSON fuori dallo sviluppo -------------------------------------------
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
}

// --- Servizi ------------------------------------------------------------------------------------
var services = builder.Services;

services.AddSingleton(TimeProvider.System);
services.AddSingleton<IBusinessClock>(sp => new BusinessClock(sp.GetRequiredService<TimeProvider>()));

services.AddSingleton<ISqlConnectionFactory>(sp =>
    new SqlConnectionFactory(sp.GetRequiredService<IConfiguration>().GetConnectionString("Attestia")
        ?? throw new InvalidOperationException("Connection string 'Attestia' mancante (ConnectionStrings__Attestia).")));
services.AddScoped<ICourseRepository, CourseRepository>();
services.AddScoped<IEmployeeRepository, EmployeeRepository>();
services.AddScoped<ICertificateRepository, CertificateRepository>();
services.AddScoped<IReportRepository, ReportRepository>();
services.AddHostedService<StartupMigrations>();

services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    // Numeri solo come numeri JSON: contratto OpenAPI piu' preciso ("integer", non "integer|string").
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
services.AddProblemDetails();
services.AddExceptionHandler<ExceptionToProblemDetailsHandler>();
services.AddValidation();
services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "Attestia API";
    doc.Info.Version = "v1";
    doc.Info.Description = "Registro della formazione obbligatoria con scadenze. Autenticazione: cookie attestia_sso emesso da /sso/acs.";
    return Task.CompletedTask;
}));
services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

services.AddAuthentication(AuthSchemes.SsoCookie)
    .AddScheme<AuthenticationSchemeOptions, NoSessionAuthenticationHandler>(AuthSchemes.SsoCookie, null);
services.AddAuthorization(Policies.Configure);

// CORS: in produzione tutto e' sulla stessa origine (un solo sito IIS); le origini extra vanno elencate esplicitamente.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowCredentials()
    .WithMethods("GET", "POST", "PUT", "DELETE").WithHeaders("Content-Type", "X-XSRF-TOKEN")));

services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(RateLimits.Upload, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// --- Pipeline -----------------------------------------------------------------------------------
var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSecurityHeaders();
app.UseAdminStaticFiles(builder.Configuration);
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();

var api = app.MapGroup("/api");
api.MapCourses();
api.MapEmployees();
api.MapCertificates();
api.MapReports();
api.MapMe();

app.MapGet("/", () => Results.Redirect("/portale/")).AllowAnonymous().ExcludeFromDescription();

await app.RunAsync();
return 0;

public partial class Program;

using Attestia.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

if (MigrateCommand.IsRequested(args))
{
    return MigrateCommand.Run(builder.Configuration, args);
}

builder.Services.AddOpenApi();

var app = builder.Build();
app.MapOpenApi();
app.MapGet("/health", () => "ok");
await app.RunAsync();
return 0;

public partial class Program;

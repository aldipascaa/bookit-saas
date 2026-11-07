using BookIt.Api.Infrastructure.Persistence;
using BookIt.Api.Infrastructure.Security;
using BookIt.Api.Web.Extensions;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddWebApi();

var app = builder.Build();

// Fail fast (before touching the database) when the JWT signing key is missing or weak.
_ = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
await DbInitializer.InitializeAsync(app.Services); // applies EF Core migrations, then seeds
app.UseBookItPipeline();
app.Run();

// Exposed for WebApplicationFactory<Program> in the test project.
public partial class Program { }

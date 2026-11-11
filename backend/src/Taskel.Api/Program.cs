using Microsoft.EntityFrameworkCore;
using Taskel.Api.Data;
using Taskel.Api.Services;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:5173"];

builder.Services.AddControllers();
builder.Services.AddOpenApi();
var connectionString = builder.Configuration.GetConnectionString("Taskel")
    ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Taskel' is not configured.");
builder.Services.AddDbContext<TaskelDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<ITaskRepository, EfTaskRepository>();
builder.Services.AddCors(options =>
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Apply pending migrations on startup when enabled (on by default in Development).
if (app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<TaskelDbContext>().Database.MigrateAsync();
}

app.UseCors(FrontendCorsPolicy);
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

await app.RunAsync();

// Exposed for integration tests (WebApplicationFactory).
public partial class Program;

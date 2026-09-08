using System.Reflection;
using System.Text.Json;

using DispatchService.Repositories;
using DispatchService.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("default")
    ?? throw new InvalidOperationException("Connection string 'default' was not found.");

// Absent configuration permits no browser origin. A fallback would turn a
// staging mistake into an accidental cross-origin policy.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// Add services to the container.

builder.Services.AddSingleton<IDbConnectionFactory>(new MySqlConnectionFactory(connectionString));
builder.Services.AddScoped<ITechnicianRepository, TechnicianRepository>();
builder.Services.AddScoped<TechnicianService>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // DataAnnotations and handwritten ProblemDetails must use the same
        // camel-cased field names so React can render every error in-place.
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Built from the assembly name so a project rename does not silently drop
    // the descriptions; the file sits next to the DLL in the output folder.
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.UseAuthorization();

app.MapControllers();

app.Run();

partial class Program
{
    private const string FrontendCorsPolicy = "Frontend";
}

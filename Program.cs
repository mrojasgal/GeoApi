using GeoApi.Services;
using DotNetEnv;
using OfficeOpenXml;

var builder = WebApplication.CreateBuilder(args);

// Cargar variables de entorno desde .env
Env.Load();

// EPPlus requiere definir el contexto de licencia antes de usarlo
var epplusContext = builder.Configuration["EPPlus:LicenseContext"] ??
                    Environment.GetEnvironmentVariable("EPPLUS_LICENSE_CONTEXT");
if (string.Equals(epplusContext, "Commercial", StringComparison.OrdinalIgnoreCase))
{
    ExcelPackage.LicenseContext = LicenseContext.Commercial;
}
else
{
    ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
}
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<CoordinateTransformService>();
builder.Services.AddSingleton<InventarioService>(sp =>
    new InventarioService(
        sp.GetRequiredService<ILogger<InventarioService>>(),
        sp.GetRequiredService<CoordinateTransformService>(),
        sp.GetRequiredService<IHostEnvironment>()
    )
);

builder.Services.AddHttpClient<OpenAiImageService>();

// Options
builder.Services.Configure<OpenStreetMapOptions>(
    builder.Configuration.GetSection("OpenStreetMap"));

// HttpClient + Service
builder.Services.AddHttpClient<OpenStreetMapService>(client =>
{
    // Puedes definir un timeout razonable desde el día 1
    client.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();
app.MapControllers();

app.Run();

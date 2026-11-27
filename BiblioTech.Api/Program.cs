using System.Text;
using BiblioTech.Api.Configuracion;
using BiblioTech.Api.Data;
using BiblioTech.Api.Middleware;
using BiblioTech.Api.Services;
using BiblioTech.Api.Services.Externos;
using BiblioTech.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddDbContext<BibliotecaContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BibliotecaDb")));

builder.Services.AddScoped<IAutorService, AutorService>();
builder.Services.AddScoped<ILibroService, LibroService>();
builder.Services.AddScoped<ISocioService, SocioService>();
builder.Services.AddScoped<IPrestamoService, PrestamoService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICalendarioService, CalendarioService>();


builder.Services.AddMemoryCache();
builder.Services.Configure<FestivosApiOpciones>(builder.Configuration.GetSection(FestivosApiOpciones.Seccion));
builder.Services.AddHttpClient<IFestivosClient, FestivosClient>((proveedor, cliente) =>
{
    var opciones = proveedor.GetRequiredService<IOptions<FestivosApiOpciones>>().Value;
    cliente.BaseAddress = new Uri(opciones.BaseUrl);
    cliente.Timeout = TimeSpan.FromSeconds(opciones.TimeoutSegundos);
});

var jwt = builder.Configuration.GetSection(JwtOpciones.Seccion).Get<JwtOpciones>() ?? new JwtOpciones();
if (Encoding.UTF8.GetByteCount(jwt.Key) < 32)
    throw new InvalidOperationException(
        "Falta la clave JWT (mínimo 32 caracteres). En la carpeta BiblioTech.Api ejecuta: " +
        "dotnet user-secrets set \"Jwt:Key\" \"<una clave larga y aleatoria>\"");

builder.Services.Configure<JwtOpciones>(builder.Configuration.GetSection(JwtOpciones.Seccion));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;   
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,                 
            ValidateIssuerSigningKey = true,         
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            NameClaimType = "email",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();


const string PoliticaFrontend = "Frontend";
var origenes = builder.Configuration.GetSection("Cors:OrigenesPermitidos").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy(PoliticaFrontend, policy =>
        policy.WithOrigins(origenes).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = contexto =>
    {
        var problema = new ValidationProblemDetails(contexto.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Hay datos inválidos en la solicitud."
        };
        return new BadRequestObjectResult(problema) { ContentTypes = { "application/problem+json" } };
    };
});


builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins("https://bibliotechweb-abckapa2anehahf5.brazilsouth-01.azurewebsites.net/")
    .AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((documento, contexto, cancelacion) =>
    {
        documento.Components ??= new OpenApiComponents();
        documento.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        documento.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Pega el token que devuelve POST /api/auth/login"
        };
        documento.Security =
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", documento)] = [] }
        ];
        return Task.CompletedTask;
    });
});

var app = builder.Build();
var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>();
Console.WriteLine("HASH_ADMIN123: " + hasher.HashPassword(null!, "Admin123"));

app.UseMiddleware<ErrorHandlingMiddleware>();   

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();               
    app.MapScalarApiReference();    
}

app.UseHttpsRedirection();
app.UseAuthentication();            
app.UseCors(PoliticaFrontend);      
app.UseAuthorization();             
app.MapControllers();

app.Run();

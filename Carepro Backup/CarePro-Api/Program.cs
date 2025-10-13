using Application.Interfaces.Authentication;
using Application.Interfaces.Email;
using Domain.Settings;
using Infrastructure.Content.Data;
using Infrastructure.Identity.Data;
using Infrastructure.Identity.Models;
using Infrastructure.Identity.Seeds;
using Infrastructure.Identity.Services;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();
var ev = Environment.GetEnvironmentVariable("CAREPROSQLAZURECONNSTR_DefaultConnection");
// 


// Injecting DbContext Class into our Service collections Starts 
var connectionString = ev != null ? ev.Replace("\"", "").Replace("\\\\", "\\") : "environment is null";// builder.Configuration.GetConnectionString("DefaultConnection");
var connectionString2 = ev != null ? ev.Replace("\"", "").Replace("\\\\", "\\") : "environment is null"; // builder.Configuration.GetConnectionString("IdentityConnection");
Console.WriteLine(connectionString);
Console.WriteLine(connectionString2);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddDbContext<AppIdentityContext>(options =>
    options.UseSqlServer(connectionString2));



/// Configure JWT

builder.Services.Configure<JWT>(builder.Configuration.GetSection("JWT"));
//Add support to logging with SERILOG
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));




builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromHours(2); // Set token lifespan here
});



/// Configuring ASP.Net Identity
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireDigit = true;
    options.SignIn.RequireConfirmedEmail = true;
    options.SignIn.RequireConfirmedAccount = true;
    options.Password.RequiredLength = 6;
    options.Password.RequiredUniqueChars = 1;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
    options.Tokens.EmailConfirmationTokenProvider = "CustomEmailConfirmation";
    options.Tokens.ProviderMap["CustomEmailConfirmation"] = new TokenProviderDescriptor(typeof(DataProtectorTokenProvider<AppUser>));

    // User settings.
    options.User.AllowedUserNameCharacters =
    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";

    options.User.RequireUniqueEmail = true;

}).AddEntityFrameworkStores<AppIdentityContext>()
    .AddDefaultTokenProviders()
.AddRoles<IdentityRole>();



/// Setting up Lifespan for our Services
/// Dependency injection, to enable us use the repository services in controller

builder.Services.AddScoped<IAuthResponseService, AuthResponseService>();
builder.Services.AddScoped<IEmailService, EmailService>();




/// Setting up our EmailTemplate Service
builder.Services.Configure<MailSettings>(builder.Configuration.GetSection("MailSettings"));


/// Setting up of Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(b =>
    {
        b.RequireHttpsMetadata = false;
        b.SaveToken = false;
        b.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidIssuer = builder.Configuration["JWT:Issuer"],
            ValidAudience = builder.Configuration["JWT:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["JWT:Key"])),
        };
    });



//Handle CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("default", builder =>
    {
        builder.WithOrigins("https://localhost:3000", "http://localhost:3000")
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});




/// Add services to the container. (MIDDLEWARES)
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "JWT Authentication",
        Description = "Enter a valid JWT bearer token",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Id = JwtBearerDefaults.AuthenticationScheme,
            Type = ReferenceType.SecurityScheme
        }
    };

    options.AddSecurityDefinition(securityScheme.Reference.Id, securityScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {securityScheme, new string[] {} }
    });
});

//builder.Services.AddSwaggerGen();

var app = builder.Build();



using (IServiceScope? scope = app.Services.CreateScope())
{
    var service = scope.ServiceProvider;
    var loggerFactory = service.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger<Program>();
    logger.LogInformation("trying to seeding the DB." + connectionString);
    try
    {
        var dbContext = service.GetRequiredService<AppDbContext>();
        var identityDbContext = service.GetRequiredService<AppIdentityContext>();
        //  dbContext.Database.EnsureDeleted();
        dbContext.Database.Migrate(); // This line adds any pending migrations
        identityDbContext.Database.Migrate();
        var context = service.GetRequiredService<AppIdentityContext>();
        var userManager = service.GetRequiredService<UserManager<AppUser>>();
        var roleManager = service.GetRequiredService<RoleManager<IdentityRole>>();
        await DefaultRoles.SeedRoles(roleManager);
        await DefaultUsers.SeedUsers(userManager);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred seeding the DB.");
    }
}
//Add support to logging request with SERILOG
app.UseSerilogRequestLogging();


// Configure the HTTP request pipeline.

app.UseSwagger();
app.UseSwaggerUI();






app.UseHttpsRedirection();

app.UseCors("default");

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();


app.Run();

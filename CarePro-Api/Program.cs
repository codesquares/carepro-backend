using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Authentication;
using Application.Interfaces.Common;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using CloudinaryDotNet;
using Domain.Entities;
using Domain.Settings;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services;
using MongoDB.Driver;
using Infrastructure.Content.Services.Authentication;
using Infrastructure.Services;
using Infrastructure.Services.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);



// MongoDB Configuration
var connectionString = builder.Configuration.GetConnectionString("MongoDbConnection")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__MongoDbConnection")
    ?? Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING")
    ?? "mongodb://localhost:27017/CarePro_Local_DB";

// Extract database name from connection string
var mongoUrl = new MongoUrl(connectionString);
var databaseName = mongoUrl.DatabaseName ?? "CarePro_Local_DB";

builder.Services.AddDbContext<CareProDbContext>(options =>
{
    options.UseMongoDB(connectionString, databaseName);
});

/// Configure JWT

builder.Services.Configure<JWT>(builder.Configuration.GetSection("JWT"));

//builder.Services.Configure<JWT>(builder.Configuration.GetSection("JwtSettings"));



builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromHours(2); // Set token lifespan here
});


//builder.Services.AddIdentity<AppUser, IdentityRole>()
//    .AddEntityFrameworkStores<CareProDbContext>()  // replace with your actual DbContext
//    .AddDefaultTokenProviders();




/// Configure cloudinary service
var cloudinarySettings = builder.Configuration.GetSection("CloudinarySettings");
var account = new Account(
    cloudinarySettings["CloudName"] ?? Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME"),
    cloudinarySettings["ApiKey"] ?? Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY"),
    cloudinarySettings["ApiSecret"] ?? Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET")
);

var cloudinary = new Cloudinary(account)
{
    Api = { Secure = true }
};

builder.Services.AddSingleton(cloudinary);
builder.Services.AddScoped<CloudinaryService>();

// Register your services that use CloudinaryService here
//builder.Services.AddScoped<ICareGiverService, CareGiverService>();

builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("CloudinarySettings"));
builder.Services.AddSingleton(x =>
{
    var config = builder.Configuration.GetSection("CloudinarySettings").Get<CloudinarySettings>();
    if (config != null)
    {
        var account = new Account(config.CloudName, config.ApiKey, config.ApiSecret);
        return new Cloudinary(account);
    }
    return new Cloudinary(new Account("default", "default", "default"));
});




/// Setting up Lifespan for our Services
/// Dependency injection, to enable us use the repository services in controller

//builder.Services.AddScoped<IAuthResponseService, AuthResponseService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICareGiverService, CareGiverService>();
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IGigServices, GigServices>();
builder.Services.AddScoped<IClientOrderService, ClientOrderService>();
builder.Services.AddScoped<ICertificationService, CertificationService>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IVerificationService, VerificationService>();
builder.Services.AddScoped<IQuestionBankService, QuestionBankService>();
builder.Services.AddScoped<IAssessmentService, AssessmentService>();
builder.Services.AddScoped<IClientPreferenceService, ClientPreferenceService>();
builder.Services.AddScoped<IClientRecommendationService, ClientRecommendationService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IEarningsService, EarningsService>();
builder.Services.AddScoped<IWithdrawalRequestService, WithdrawalRequestService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<ITrainingMaterialService, TrainingMaterialService>();

// Location services
builder.Services.AddScoped<ILocationService, LocationService>();
builder.Services.AddScoped<IGeocodingService, GeocodingService>();
builder.Services.AddHttpClient<GeocodingService>();

// Contract services (Smart Contract Generation feature)
builder.Services.AddScoped<IContractService, ContractService>();
builder.Services.AddScoped<IContractNotificationService, ContractNotificationService>();
builder.Services.AddScoped<IContractLLMService, OpenAIContractService>();

// Order Tasks services (Enhanced Contract Generation feature)
builder.Services.AddScoped<IOrderTasksService, OrderTasksService>();

// Dojah webhook services
builder.Services.AddScoped<ISignatureVerificationService, SignatureVerificationService>();
builder.Services.AddScoped<IRateLimitingService, RateLimitingService>();
builder.Services.AddScoped<IDojahDataFormattingService, DojahDataFormattingService>();
builder.Services.AddScoped<IDojahApiService, DojahApiService>();
builder.Services.AddHttpClient<IDojahApiService, DojahApiService>();
builder.Services.AddMemoryCache();

builder.Services.AddHostedService<DailyEarningService>();
builder.Services.AddHostedService<UnreadNotificationEmailBackgroundService>();



builder.Services.AddScoped<ITokenHandler, Infrastructure.Content.Services.Authentication.TokenHandler>();

// Add Origin Validation Service
builder.Services.AddScoped<IOriginValidationService, OriginValidationService>();

// Add SignalR
builder.Services.AddSignalR();



///Flutterwave dependency injection
builder.Services.AddSingleton<FlutterwaveService>();



/// Setting up our EmailTemplate Service
builder.Services.Configure<MailSettings>(builder.Configuration.GetSection("MailSettings"));


//// Configure JWT

//builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//    .AddJwtBearer(options =>
//    options.TokenValidationParameters = new TokenValidationParameters
//    {
//        ValidateIssuer = true,
//        ValidateAudience = true,
//        ValidateLifetime = true,
//        ValidateIssuerSigningKey = true,
//        ValidIssuer = builder.Configuration["Jwt:Issuer"],
//        ValidAudience = builder.Configuration["Jwt:Audience"],
//        IssuerSigningKey = new SymmetricSecurityKey(
//            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
//    });

/// Implementing Authentication for chat users (To ensure only Authorized Users Chat)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/chathub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? Environment.GetEnvironmentVariable("JWT__Issuer") ?? "CarePro",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? Environment.GetEnvironmentVariable("JWT__Audience") ?? "CarePro",
            IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? Environment.GetEnvironmentVariable("JWT__Key") ?? "default-secret-key"))
        };

    });


// Add MongoDB client and register ChatRepository
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var settings = MongoClientSettings.FromConnectionString(connectionString);
    settings.UseTls = true;
    settings.AllowInsecureTls = true;
    settings.SslSettings = new SslSettings
    {
        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12,
        CheckCertificateRevocation = false
    };
    settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
    settings.ConnectTimeout = TimeSpan.FromSeconds(10);
    return new MongoClient(settings);
});

builder.Services.AddScoped(sp =>
{
    var client = sp.GetRequiredService<IMongoClient>();
    var database = client.GetDatabase(databaseName);
    return database;
});

// Register ChatRepository
builder.Services.AddScoped<ChatRepository>();








builder.Services.AddHttpContextAccessor();






//Handle CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("default", builder =>
    {
        builder.WithOrigins("https://oncarepro.com", "https://www.oncarepro.com", "http://oncarepro.com", "http://www.oncarepro.com",
            "https://api.oncarepro.com", "http://api.oncarepro.com",
            "https://care-pro-frontend.onrender.com", "https://localhost:5173", "http://localhost:5173",
            "https://localhost:5174", "http://localhost:5174", "https://budmfp9jxr.us-east-1.awsapprunner.com",
            "http://carepro-frontend-staging.s3-website-us-east-1.amazonaws.com", "https://carepro-frontend-staging.s3-website-us-east-1.amazonaws.com")
               .AllowAnyHeader()
               .AllowAnyMethod()
               .AllowCredentials();
    });
});





/// Add services to the container. (MIDDLEWARES)
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

/// Add Swagger
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

/// Configure SignalR
builder.Services.AddSignalR();


//builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseRouting();
app.UseCors("default");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/chathub");
app.MapHub<NotificationHub>("/notificationHub");

app.Run();
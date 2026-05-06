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
using CarePro_Api.Middleware;
using CarePro_Api.Middleware.RateLimiting;
using CarePro_Api.Filters;
using Infrastructure.Content.Services.Handlers;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

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
builder.Services.AddScoped<IGoogleAuthService, GoogleAuthService>();
builder.Services.AddHttpClient<GoogleAuthService>();
builder.Services.AddScoped<ICareGiverService, CareGiverService>();
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IGigServices, GigServices>();
builder.Services.AddScoped<IClientOrderService, ClientOrderService>();
builder.Services.AddScoped<ICertificationService, CertificationService>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IVerificationService, VerificationService>();
builder.Services.AddScoped<IWebhookLogService, WebhookLogService>();
builder.Services.AddScoped<IQuestionBankService, QuestionBankService>();
builder.Services.AddScoped<IAssessmentService, AssessmentService>();
builder.Services.AddScoped<IEligibilityService, EligibilityService>();
builder.Services.AddScoped<IClientPreferenceService, ClientPreferenceService>();
builder.Services.AddScoped<ICareRequestService, CareRequestService>();
builder.Services.AddScoped<ICareRequestResponseService, CareRequestResponseService>();
builder.Services.AddScoped<IClientRecommendationService, ClientRecommendationService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// Care request matching engine
builder.Services.AddScoped<ICareRequestMatchingService, CareRequestMatchingService>();

// Wallet, Ledger & Billing services (must be registered before services that depend on them)
builder.Services.AddSingleton<WalletLockManager>(); // Per-caregiver mutex for wallet race condition protection
builder.Services.AddScoped<ICaregiverWalletService, CaregiverWalletService>();
builder.Services.AddScoped<IEarningsLedgerService, EarningsLedgerService>();
builder.Services.AddScoped<IBillingRecordService, BillingRecordService>();

builder.Services.AddScoped<IEarningsService, EarningsService>();
builder.Services.AddScoped<IWithdrawalRequestService, WithdrawalRequestService>();
builder.Services.AddScoped<ICaregiverBankAccountService, CaregiverBankAccountService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<ITrainingMaterialService, TrainingMaterialService>();

// Secure payment services
builder.Services.AddScoped<IPendingPaymentService, PendingPaymentService>();

// Booking commitment (gig access fee) services
builder.Services.AddScoped<IBookingCommitmentService, BookingCommitmentService>();

// Subscription & recurring billing services
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddHostedService<RecurringBillingService>();

// Content sanitization (XSS prevention)
builder.Services.AddSingleton<IContentSanitizer, ContentSanitizer>();

// Contact pattern detection & chat compliance (off-platform leakage prevention)
builder.Services.AddSingleton<IContactPatternDetector, ContactPatternDetector>();
builder.Services.AddScoped<IChatComplianceService, ChatComplianceService>();

// Location services
builder.Services.AddScoped<ILocationService, LocationService>();
builder.Services.AddScoped<IGeocodingService, GeocodingService>();
builder.Services.AddHttpClient<GeocodingService>();

// Contract services (Smart Contract Generation feature)
builder.Services.AddScoped<IContractService, ContractService>();
builder.Services.AddScoped<IContractNotificationService, ContractNotificationService>();
builder.Services.AddScoped<IContractLLMService, OpenAIContractService>();
builder.Services.AddScoped<IContractTemplateService, ContractTemplateService>();
builder.Services.AddScoped<IContractPdfService, ContractPdfService>();

// Order Tasks services (Enhanced Contract Generation feature)
builder.Services.AddScoped<IOrderTasksService, OrderTasksService>();

// Order Negotiation services (Pre-Contract Negotiation Phase)
builder.Services.AddScoped<IOrderNegotiationService, OrderNegotiationService>();

// Task Sheets services (Caregiver visit session tracking)
builder.Services.AddScoped<ITaskSheetService, TaskSheetService>();

// Visit Management services (GPS check-in, observation & incident reports)
builder.Services.AddScoped<IVisitCheckinService, VisitCheckinService>();
builder.Services.AddScoped<IObservationReportService, ObservationReportService>();
builder.Services.AddScoped<IIncidentReportService, IncidentReportService>();
builder.Services.AddScoped<IVisitCancellationService, VisitCancellationService>();
builder.Services.AddScoped<IClientWalletService, ClientWalletService>();
builder.Services.AddScoped<IRefundRequestService, RefundRequestService>();

builder.Services.AddScoped<IDisputeService, DisputeService>();

// Gig templates (template-driven gig creation)
builder.Services.AddScoped<IGigTemplateService, GigTemplateService>();

// Dojah webhook services
builder.Services.AddScoped<ISignatureVerificationService, SignatureVerificationService>();
builder.Services.AddScoped<IRateLimitingService, RateLimitingService>();
builder.Services.AddScoped<IDojahDataFormattingService, DojahDataFormattingService>();
builder.Services.AddScoped<IDojahApiService, DojahApiService>();
builder.Services.AddHttpClient<IDojahApiService, DojahApiService>();
builder.Services.AddScoped<IWebhookLogService, WebhookLogService>();

// Dojah document verification service
builder.Services.AddScoped<DojahDocumentVerificationService>();
builder.Services.AddHttpClient<DojahDocumentVerificationService>();
builder.Services.AddMemoryCache();

// Google Sheets service (production signup tracking)
builder.Services.AddScoped<IGoogleSheetsService, GoogleSheetsService>();

builder.Services.AddHostedService<DailyEarningService>();
// Old background service - now replaced by specialized processors
// builder.Services.AddHostedService<UnreadNotificationEmailBackgroundService>();

// New sophisticated email notification system
builder.Services.AddScoped<IEmailNotificationTrackingService, EmailNotificationTrackingService>();
builder.Services.AddHostedService<ImmediateNotificationProcessor>();
builder.Services.AddHostedService<DailyBatchNotificationProcessor>();
builder.Services.AddHostedService<ContractReminderProcessor>();

// Care request matching background processor
builder.Services.AddHostedService<CareRequestMatchingProcessor>();

// GDPR: Hard-delete gigs past 30-day grace period (runs daily)
builder.Services.AddHostedService<GigHardDeleteProcessor>();

// GDPR: Send deletion reminder notifications at 25 and 29 days (runs daily)
builder.Services.AddHostedService<GigDeletionReminderProcessor>();

// Cleanup orphaned email inline-image assets older than 90 days (runs daily)
builder.Services.AddHostedService<EmailAssetCleanupProcessor>();



builder.Services.AddScoped<ITokenHandler, Infrastructure.Content.Services.Authentication.TokenHandler>();

// Add Origin Validation Service
builder.Services.AddScoped<IOriginValidationService, OriginValidationService>();

// Add SignalR
builder.Services.AddSignalR();

// Register MediatR — scans Infrastructure assembly for all handlers
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SendNotificationCommandHandler>());



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
                    (context.HttpContext.Request.Path.StartsWithSegments("/chathub") ||
                     context.HttpContext.Request.Path.StartsWithSegments("/notificationHub")))
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
            "https://care-pro-frontend.onrender.com", "https://localhost:5173", "http://localhost:5173", "https://staging.oncarepro.com", "http://staging.oncarepro.com",
            "https://localhost:5174", "http://localhost:5174", "https://budmfp9jxr.us-east-1.awsapprunner.com",
            "http://carepro-frontend-staging.s3-website-us-east-1.amazonaws.com", "https://carepro-frontend-staging.s3-website-us-east-1.amazonaws.com",
            "http://127.0.0.1:5173", "http://127.0.0.1:5174", "http://127.0.0.1:3000")
               .AllowAnyHeader()
               .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS", "PATCH")
               .AllowCredentials();
    });
});





/// Add services to the container. (MIDDLEWARES)
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddControllers(options =>
{
    // Add validation filter for RFC 7807 ProblemDetails with backward compatibility
    options.Filters.Add<ValidationProblemDetailsFilter>();
});

// Configure ProblemDetails for consistent error responses
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        // Add trace ID to all ProblemDetails responses
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        // Backward compatibility: add message field
        context.ProblemDetails.Extensions["message"] = context.ProblemDetails.Detail;
        context.ProblemDetails.Extensions["success"] = false;
    };
});

builder.Services.AddEndpointsApiExplorer();

/// Add Swagger
builder.Services.AddSwaggerGen(options =>
{
    // Disambiguate types that share a simple name across namespaces (e.g.
    // Domain.Entities.CareRequestResponse vs Application.DTOs.CareRequestResponse).
    // Without this, Swashbuckle throws while building /swagger/v1/swagger.json
    // and the endpoint returns HTTP 500.
    options.CustomSchemaIds(type => type.FullName?.Replace("+", ".") ?? type.Name);

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

// =============================================================================
// Rate limiting configuration
// -----------------------------------------------------------------------------
// Reads RateLimiting:* config and the legacy env vars ENABLE_RATE_LIMITING and
// MAX_REQUESTS_PER_MINUTE so existing task definitions keep working.
// Picks Redis-backed store when REDIS_CONNECTION_STRING is set and reachable;
// otherwise falls back to in-memory (acceptable for single-instance deployments).
// =============================================================================
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimiting"));
builder.Services.PostConfigure<RateLimitOptions>(opts =>
{
    var enableEnv = Environment.GetEnvironmentVariable("ENABLE_RATE_LIMITING");
    if (!string.IsNullOrWhiteSpace(enableEnv) && bool.TryParse(enableEnv, out var enabled))
    {
        opts.Enabled = enabled;
    }

    var maxPerMinEnv = Environment.GetEnvironmentVariable("MAX_REQUESTS_PER_MINUTE");
    if (!string.IsNullOrWhiteSpace(maxPerMinEnv) && int.TryParse(maxPerMinEnv, out var maxPerMin) && maxPerMin > 0)
    {
        opts.General.MaxRequests = maxPerMin;
        opts.General.WindowSeconds = 60;
    }

    if (string.IsNullOrWhiteSpace(opts.RedisConnectionString))
    {
        opts.RedisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
    }
});

// In-memory store is always registered so it can serve as the Redis fallback.
builder.Services.AddSingleton<InMemoryRateLimitStore>();

builder.Services.AddSingleton<IRateLimitStore>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<RateLimitOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<Program>>();
    var inMemory = sp.GetRequiredService<InMemoryRateLimitStore>();

    if (string.IsNullOrWhiteSpace(opts.RedisConnectionString))
    {
        logger.LogInformation("Rate limiter using in-memory store (no Redis connection string configured)");
        return inMemory;
    }

    try
    {
        var muxer = ConnectionMultiplexer.Connect(opts.RedisConnectionString);
        logger.LogInformation("Rate limiter using Redis store at {Endpoint}", muxer.GetEndPoints().FirstOrDefault()?.ToString() ?? "unknown");
        var redisLogger = sp.GetRequiredService<ILogger<RedisRateLimitStore>>();
        return new RedisRateLimitStore(muxer, inMemory, redisLogger);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to connect to Redis for rate limiting; falling back to in-memory store");
        return inMemory;
    }
});

// Periodic cleanup for the in-memory store (no-op for Redis entries which expire by TTL).
builder.Services.AddHostedService<RateLimitCleanupService>();

// =============================================================================
// Forwarded headers
// -----------------------------------------------------------------------------
// Behind the AWS ALB, the real client IP arrives in X-Forwarded-For. Without
// this, RemoteIpAddress would be the ALB's address and per-IP rate limits would
// collapse all callers into a single bucket.
// =============================================================================
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // ALB is the only proxy in front of the task. ALB appends the true client IP
    // as the last value in X-Forwarded-For, so a single forward hop is correct.
    options.ForwardLimit = 1;
    // Allow the ALB regardless of source IP (its IP rotates within the VPC).
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});


//builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.

// Forwarded headers must run before anything that inspects RemoteIpAddress or scheme.
app.UseForwardedHeaders();

// CORS must run early - before any middleware that might return a response
// This ensures preflight OPTIONS requests get proper CORS headers
app.UseCors("default");

// Global exception handler - catches all exceptions
app.UseGlobalExceptionHandler();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Rate limiting runs AFTER authentication so authenticated users can be keyed by user id
// instead of by IP (otherwise admins behind a shared NAT share one bucket).
app.UseRateLimiting();

// Idempotency middleware for signup endpoints — replays previous response when the
// same Idempotency-Key header is sent on a retry. Opt-in: no header, no behavior change.
app.UseSignupIdempotency();

app.MapControllers();
app.MapHub<ChatHub>("/chathub");
app.MapHub<NotificationHub>("/notificationHub");

// One-time startup migration: sync existing verified users to caregiver profiles
using (var scope = app.Services.CreateScope())
{
    try
    {
        var verificationService = scope.ServiceProvider.GetRequiredService<IVerificationService>();
        var migrationLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var updated = await verificationService.BackfillCaregiverVerificationStateAsync();
        if (updated > 0)
        {
            migrationLogger.LogInformation("Startup migration: backfilled {Count} caregiver verification states", updated);
        }
    }
    catch (Exception ex)
    {
        var migrationLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        migrationLogger.LogError(ex, "Startup migration failed for caregiver verification backfill - app will continue normally");
    }
}

// Seed gig template data from predefined templates
using (var scope = app.Services.CreateScope())
{
    try
    {
        var gigTemplateService = scope.ServiceProvider.GetRequiredService<IGigTemplateService>();
        var seedLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var seedData = GigTemplateSeedData.GetSeedData();
        await gigTemplateService.SeedTemplatesAsync(seedData);
        seedLogger.LogInformation("Gig template seed check completed");
    }
    catch (Exception ex)
    {
        var seedLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        seedLogger.LogError(ex, "Gig template seeding failed - app will continue normally");
    }
}

app.Run();
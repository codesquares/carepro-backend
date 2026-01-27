using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Middleware
{
    /// <summary>
    /// Global exception handler middleware that catches all unhandled exceptions
    /// and returns RFC 7807 ProblemDetails with backward-compatible 'message' field.
    /// </summary>
    public class GlobalExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;
        private readonly IHostEnvironment _environment;

        public GlobalExceptionHandlerMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionHandlerMiddleware> logger,
            IHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            // Generate a correlation ID for tracking
            var traceId = context.TraceIdentifier ?? Guid.NewGuid().ToString();
            
            // Log the full exception details server-side
            _logger.LogError(exception, 
                "Unhandled exception occurred. TraceId: {TraceId}, Path: {Path}, Method: {Method}",
                traceId, context.Request.Path, context.Request.Method);

            // Determine status code and title based on exception type
            var (statusCode, title, detail) = exception switch
            {
                UnauthorizedAccessException => (
                    StatusCodes.Status401Unauthorized, 
                    "Unauthorized", 
                    "Authentication required to access this resource."),
                KeyNotFoundException => (
                    StatusCodes.Status404NotFound, 
                    "Not Found", 
                    "The requested resource was not found."),
                ArgumentException argEx => (
                    StatusCodes.Status400BadRequest, 
                    "Bad Request", 
                    _environment.IsDevelopment() ? argEx.Message : "Invalid request. Please check your input."),
                InvalidOperationException invEx => (
                    StatusCodes.Status400BadRequest, 
                    "Bad Request", 
                    _environment.IsDevelopment() ? invEx.Message : "The operation could not be completed."),
                TimeoutException => (
                    StatusCodes.Status504GatewayTimeout, 
                    "Gateway Timeout", 
                    "The request timed out. Please try again."),
                _ => (
                    StatusCodes.Status500InternalServerError, 
                    "Internal Server Error", 
                    _environment.IsDevelopment() 
                        ? exception.Message 
                        : $"An unexpected error occurred. Reference: {traceId}")
            };

            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = statusCode;

            var problemDetails = new ProblemDetailsWithBackwardCompat
            {
                Type = $"https://httpstatuses.com/{statusCode}",
                Title = title,
                Status = statusCode,
                Detail = detail,
                Instance = context.Request.Path,
                // RFC 7807 extension - trace ID for debugging
                TraceId = traceId,
                // Backward compatibility for existing frontend
                Message = detail,
                Success = false
            };

            // Add stack trace in development only
            if (_environment.IsDevelopment())
            {
                problemDetails.Extensions["stackTrace"] = exception.StackTrace;
                if (exception.InnerException != null)
                {
                    problemDetails.Extensions["innerException"] = exception.InnerException.Message;
                }
            }

            var jsonResponse = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            await context.Response.WriteAsync(jsonResponse);
        }
    }

    /// <summary>
    /// RFC 7807 ProblemDetails with backward-compatible fields for existing frontend.
    /// </summary>
    public class ProblemDetailsWithBackwardCompat : ProblemDetails
    {
        /// <summary>
        /// Correlation ID for tracking errors in logs
        /// </summary>
        public string TraceId { get; set; } = string.Empty;
        
        /// <summary>
        /// Backward compatibility: Same as Detail for existing frontend
        /// </summary>
        public string Message { get; set; } = string.Empty;
        
        /// <summary>
        /// Backward compatibility: Always false for error responses
        /// </summary>
        public bool Success { get; set; }
    }

    /// <summary>
    /// Extension method for easy middleware registration
    /// </summary>
    public static class GlobalExceptionHandlerMiddlewareExtensions
    {
        public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        }
    }
}

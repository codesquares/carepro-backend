using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text.Json;

namespace CarePro_Api.Filters
{
    /// <summary>
    /// Action filter that transforms model validation errors into RFC 7807 ProblemDetails
    /// with backward-compatible fields for existing frontend.
    /// </summary>
    public class ValidationProblemDetailsFilter : IActionFilter
    {
        private readonly IHostEnvironment _environment;

        public ValidationProblemDetailsFilter(IHostEnvironment environment)
        {
            _environment = environment;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (!context.ModelState.IsValid)
            {
                var errors = context.ModelState
                    .Where(e => e.Value?.Errors.Count > 0)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                    );

                var firstError = context.ModelState
                    .Where(e => e.Value?.Errors.Count > 0)
                    .SelectMany(e => e.Value!.Errors)
                    .FirstOrDefault()?.ErrorMessage ?? "Validation failed";

                var traceId = context.HttpContext.TraceIdentifier;

                var problemDetails = new ValidationProblemDetailsWithBackwardCompat
                {
                    Type = "https://httpstatuses.com/400",
                    Title = "Validation Failed",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = firstError,
                    Instance = context.HttpContext.Request.Path,
                    Errors = errors,
                    // Backward compatibility
                    TraceId = traceId,
                    Message = firstError,
                    Success = false
                };

                context.Result = new BadRequestObjectResult(problemDetails)
                {
                    ContentTypes = { "application/problem+json" }
                };
            }
        }

        public void OnActionExecuted(ActionExecutedContext context) { }
    }

    /// <summary>
    /// RFC 7807 ValidationProblemDetails with backward-compatible fields.
    /// </summary>
    public class ValidationProblemDetailsWithBackwardCompat : ValidationProblemDetails
    {
        /// <summary>
        /// Correlation ID for tracking errors in logs
        /// </summary>
        public string TraceId { get; set; } = string.Empty;

        /// <summary>
        /// Backward compatibility: First validation error message
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Backward compatibility: Always false for error responses
        /// </summary>
        public bool Success { get; set; }
    }
}

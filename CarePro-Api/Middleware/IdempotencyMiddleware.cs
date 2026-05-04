using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Domain.Entities;
using Infrastructure.Content.Data;
using Infrastructure.Content.Services.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CarePro_Api.Middleware
{
    /// <summary>
    /// Replays the response of a previously-processed signup request when the client
    /// retries with the same Idempotency-Key header. Defeats double-clicks and network
    /// retries that would otherwise create duplicate accounts.
    ///
    /// Behavior:
    ///  • Active only on the four signup POST endpoints.
    ///  • Active only when the caller sends an Idempotency-Key header. If the header is
    ///    missing, behavior is identical to today (so old clients are unaffected).
    ///  • Records are kept for 24h. After that the same key may be reused.
    ///  • In-flight duplicate (same key arrives while the first request is still running)
    ///    returns 409 with a "retry shortly" message rather than running twice.
    ///  • A different endpoint reusing the same key returns 422.
    ///  • 5xx responses are NOT cached, so the client can safely retry after a server error.
    /// </summary>
    public class IdempotencyMiddleware
    {
        private const string HeaderName = "Idempotency-Key";
        private const string ReplayHeaderName = "Idempotent-Replay";
        private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

        private static readonly HashSet<string> ProtectedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/CareGivers/AddCaregiverUser",
            "/api/CareGivers/GoogleSignUp",
            "/api/Clients/AddClientUser",
            "/api/Clients/GoogleSignUp",
        };

        private readonly RequestDelegate _next;
        private readonly ILogger<IdempotencyMiddleware> _logger;

        public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, CareProDbContext db)
        {
            var path = context.Request.Path.Value ?? string.Empty;

            // Only POSTs to one of the signup endpoints are eligible.
            if (!HttpMethods.IsPost(context.Request.Method) || !ProtectedPaths.Contains(path))
            {
                await _next(context);
                return;
            }

            // Header is opt-in. No header → behave exactly like before.
            if (!context.Request.Headers.TryGetValue(HeaderName, out var keyValues))
            {
                await _next(context);
                return;
            }

            var key = keyValues.ToString().Trim();
            if (!IsValidKey(key))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    Message = $"Invalid {HeaderName}. Must be 8-128 characters, alphanumeric / dash / underscore."
                });
                return;
            }

            var endpoint = $"{context.Request.Method} {path}";
            var now = DateTime.UtcNow;

            // Try to atomically reserve this key. Unique index on Key makes this a single-winner race.
            var reservation = new IdempotencyRecord
            {
                Key = key,
                Endpoint = endpoint,
                CreatedAt = now,
                ExpiresAt = now + Ttl
            };

            try
            {
                await db.IdempotencyRecords.AddAsync(reservation);
                await db.SaveChangesAsync();
            }
            catch (Exception ex) when (SignupGuards.IsDuplicateKeyException(ex))
            {
                await HandleExistingRecordAsync(context, db, key, endpoint);
                return;
            }
            catch (Exception ex)
            {
                // Any other DB failure: log, but do not block the signup. Behave as if no key was sent.
                _logger.LogWarning(ex, "Idempotency reservation failed for key {Key}; processing without idempotency.", key);
                await _next(context);
                return;
            }

            // We hold the reservation. Buffer the response so we can both send it and persist it.
            var originalBody = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            string capturedBody;
            try
            {
                await _next(context);
            }
            finally
            {
                buffer.Seek(0, SeekOrigin.Begin);
                capturedBody = await new StreamReader(buffer).ReadToEndAsync();
                buffer.Seek(0, SeekOrigin.Begin);
                await buffer.CopyToAsync(originalBody);
                context.Response.Body = originalBody;
            }

            var status = context.Response.StatusCode;

            if (status >= 500)
            {
                // Don't cache server errors — let the client retry safely with the same key.
                try
                {
                    db.IdempotencyRecords.Remove(reservation);
                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to release idempotency reservation after 5xx for key {Key}", key);
                }
                return;
            }

            try
            {
                reservation.ResponseStatus = status;
                reservation.ResponseBody = capturedBody;
                reservation.ContentType = context.Response.ContentType;
                db.IdempotencyRecords.Update(reservation);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist idempotency response for key {Key}", key);
            }
        }

        private async Task HandleExistingRecordAsync(HttpContext context, CareProDbContext db, string key, string endpoint)
        {
            var existing = await db.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key == key);

            if (existing == null)
            {
                // Race: it was deleted between insert-fail and read. Just process normally.
                await _next(context);
                return;
            }

            if (existing.ExpiresAt < DateTime.UtcNow)
            {
                // Record is stale. Clear and proceed (caller will retry).
                try
                {
                    db.IdempotencyRecords.Remove(existing);
                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove expired idempotency record {Key}", key);
                }

                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new
                {
                    Message = "Previous request with this Idempotency-Key has expired. Please retry with a new key."
                });
                return;
            }

            if (!string.Equals(existing.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                await context.Response.WriteAsJsonAsync(new
                {
                    Message = $"{HeaderName} has already been used for a different endpoint."
                });
                return;
            }

            if (existing.ResponseStatus == null)
            {
                // Original request is still being processed.
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new
                {
                    Message = "An earlier request with this Idempotency-Key is still being processed. Please retry shortly."
                });
                return;
            }

            // Replay the original response.
            context.Response.StatusCode = existing.ResponseStatus.Value;
            if (!string.IsNullOrEmpty(existing.ContentType))
            {
                context.Response.ContentType = existing.ContentType;
            }
            context.Response.Headers[ReplayHeaderName] = "true";
            if (!string.IsNullOrEmpty(existing.ResponseBody))
            {
                await context.Response.WriteAsync(existing.ResponseBody);
            }
        }

        private static bool IsValidKey(string s)
        {
            if (s.Length < 8 || s.Length > 128) return false;
            return s.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
        }
    }

    public static class IdempotencyMiddlewareExtensions
    {
        public static IApplicationBuilder UseSignupIdempotency(this IApplicationBuilder app)
            => app.UseMiddleware<IdempotencyMiddleware>();
    }
}

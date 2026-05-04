using System;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace Infrastructure.Content.Services.Common
{
    /// <summary>
    /// Helpers shared by signup flows so concurrent / duplicate-email registrations
    /// are handled consistently across services (CareGiver, Client, Google OAuth).
    /// </summary>
    public static class SignupGuards
    {
        /// <summary>
        /// Canonical email normalization for signup / lookup paths.
        /// Trim + invariant lowercase. Returns empty string for null/whitespace input.
        /// </summary>
        public static string NormalizeEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return string.Empty;
            }

            return email.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Returns true when the supplied exception (or its inner chain) represents
        /// a MongoDB duplicate-key write error (server error code 11000 / 11001).
        /// EF Core wraps Mongo write errors in <see cref="DbUpdateException"/>.
        /// </summary>
        public static bool IsDuplicateKeyException(Exception? ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                switch (current)
                {
                    case MongoWriteException mwe when mwe.WriteError?.Category == ServerErrorCategory.DuplicateKey:
                        return true;
                    case MongoBulkWriteException bwe when bwe.WriteErrors != null:
                        foreach (var we in bwe.WriteErrors)
                        {
                            if (we.Category == ServerErrorCategory.DuplicateKey)
                            {
                                return true;
                            }
                        }
                        break;
                    case MongoCommandException mce when mce.Code == 11000 || mce.Code == 11001:
                        return true;
                }
            }

            return false;
        }
    }
}

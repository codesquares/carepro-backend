using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Single source of truth for "which admins may receive an admin-bound notification".
    /// Same rule as the login gate (AuthService): only Approved admins have working access, and a
    /// null/blank Status (accounts predating the field) counts as Approved. Every admin-bound
    /// notification path must resolve its recipients through here so Pending/Rejected admins never get alerts.
    /// </summary>
    public static class AdminRecipients
    {
        public static bool IsApproved(AdminUser a)
            => string.IsNullOrWhiteSpace(a.Status)
               || string.Equals(a.Status, AdminUserStatus.Approved, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Non-deleted, Approved admins, optionally narrowed by a role/department scope.
        /// The scope is translated by Mongo; the approval check runs in memory because the Mongo LINQ
        /// provider doesn't reliably translate StringComparison overloads.
        /// </summary>
        public static async Task<List<AdminUser>> GetApprovedAsync(
            CareProDbContext db, Expression<Func<AdminUser, bool>>? scope = null)
        {
            IQueryable<AdminUser> q = db.AdminUsers.Where(a => !a.IsDeleted);
            if (scope != null) q = q.Where(scope);
            return (await q.ToListAsync()).Where(IsApproved).ToList();
        }
    }
}

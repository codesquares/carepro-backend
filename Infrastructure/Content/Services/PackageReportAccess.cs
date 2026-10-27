using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Ownership rules shared by the observation- and incident-report services for the package-assignment
    /// path (the legacy order path keeps its own inline checks, untouched). Identity always comes from the
    /// JWT-derived userId passed in; nothing here trusts a request-supplied caregiver/client id.
    /// Client ownership resolves via PackageRequest.ClientId (not Assignment.ClientId) so it survives a
    /// caregiver reassignment — same rule as TaskSheetService.GetVisitsForPackageRequestAsClientAsync.
    /// </summary>
    internal static class PackageReportAccess
    {
        /// <summary>
        /// For creating a report: the caller must be the assignment's caregiver, and care must have been
        /// able to start (not PendingAcceptance / Declined). A staff-Cancelled assignment still accepts a
        /// report — an incident that happened before a reassignment must never become unreportable.
        /// When taskSheetId is given it must be one of THIS assignment's visits.
        /// </summary>
        public static async Task<Assignment> LoadForCaregiverAsync(
            CareProDbContext db, string assignmentId, string caregiverId, string? taskSheetId)
        {
            if (!ObjectId.TryParse(assignmentId, out var assignmentOid))
                throw new ArgumentException("Invalid assignment ID format.");

            var assignment = await db.Assignments.FirstOrDefaultAsync(a => a.Id == assignmentOid)
                ?? throw new KeyNotFoundException($"Assignment '{assignmentId}' not found.");

            if (assignment.CaregiverId != caregiverId)
                throw new UnauthorizedAccessException("Only the assigned caregiver can create reports for this assignment.");

            if (assignment.Status == AssignmentStatuses.PendingAcceptance || assignment.Status == AssignmentStatuses.Declined)
                throw new ArgumentException("This assignment has not been accepted, so no visit can be reported on.");

            if (!string.IsNullOrWhiteSpace(taskSheetId))
            {
                if (!ObjectId.TryParse(taskSheetId, out var sheetOid))
                    throw new ArgumentException("Invalid task sheet ID format.");

                var sheet = await db.TaskSheets.FirstOrDefaultAsync(ts => ts.Id == sheetOid)
                    ?? throw new KeyNotFoundException($"Task sheet '{taskSheetId}' not found.");

                if (sheet.AssignmentId != assignmentId)
                    throw new ArgumentException("Task sheet does not belong to the specified assignment.");
            }

            return assignment;
        }

        /// <summary>For reading: admin, the assignment's caregiver, or the owning client. Anyone else gets 403.</summary>
        public static async Task<(Assignment assignment, PackageRequest? request)> AuthorizeViewAsync(
            CareProDbContext db, string assignmentId, string userId, bool isAdmin, bool isClient)
        {
            if (!ObjectId.TryParse(assignmentId, out var assignmentOid))
                throw new ArgumentException("Invalid assignment ID format.");

            var assignment = await db.Assignments.FirstOrDefaultAsync(a => a.Id == assignmentOid)
                ?? throw new KeyNotFoundException($"Assignment '{assignmentId}' not found.");

            PackageRequest? request = null;
            if (ObjectId.TryParse(assignment.PackageRequestId, out var requestOid))
                request = await db.PackageRequests.FirstOrDefaultAsync(pr => pr.Id == requestOid);

            if (!isAdmin)
            {
                var allowed = isClient
                    ? request != null && request.ClientId == userId
                    : assignment.CaregiverId == userId;
                if (!allowed)
                    throw new UnauthorizedAccessException("You are not authorized to view reports for this assignment.");
            }

            return (assignment, request);
        }

        public static async Task<string?> GetClientIdAsync(CareProDbContext db, string packageRequestId)
        {
            if (!ObjectId.TryParse(packageRequestId, out var oid)) return null;
            var request = await db.PackageRequests.FirstOrDefaultAsync(pr => pr.Id == oid);
            return request?.ClientId;
        }
    }
}

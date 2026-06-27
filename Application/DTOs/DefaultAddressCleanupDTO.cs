using System.ComponentModel.DataAnnotations;

namespace Application.DTOs;

public class DefaultAddressCleanupRequest
{
    /// <summary>
    /// Dry-run only reports impact. Execute mode applies updates.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// Allowed values: All, Caregivers, Clients
    /// </summary>
    [StringLength(20)]
    public string Scope { get; set; } = "All";

    /// <summary>
    /// Number of caregiver recipients to preview for admin email follow-up.
    /// </summary>
    [Range(1, 500)]
    public int PreviewLimit { get; set; } = 100;

    /// <summary>
    /// Optional reason for audit logging. Required when DryRun is false.
    /// </summary>
    [StringLength(500)]
    public string? Reason { get; set; }
}

public class DefaultAddressCleanupResult
{
    public bool DryRun { get; set; }
    public string Scope { get; set; } = "All";
    public string PlaceholderAddress { get; set; } = string.Empty;

    public int AffectedCaregiverUsers { get; set; }
    public int AffectedClientUsers { get; set; }
    public int AffectedLocationRecords { get; set; }

    public int UpdatedCaregiverUsers { get; set; }
    public int UpdatedClientUsers { get; set; }
    public int UpdatedLocationRecords { get; set; }

    public int FailedUsers { get; set; }
    public List<string> FailedUserIds { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Prepared list for optional admin-side bulk email follow-up.
    /// </summary>
    public List<CaregiverRecipientPreview> CaregiverRecipientPreview { get; set; } = new();
}

public class CaregiverRecipientPreview
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
}

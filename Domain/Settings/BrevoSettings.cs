namespace Domain.Settings
{
    /// <summary>
    /// Config for Brevo's Contacts/List REST API (marketing segmentation), distinct from
    /// MailSettings which only configures Brevo as a transactional SMTP relay. List IDs are
    /// created once in the Brevo dashboard; marketing then builds/edits campaigns against
    /// them with no further engineering required per campaign.
    /// </summary>
    public class BrevoSettings
    {
        public string? ApiKey { get; set; }
        public string BaseUrl { get; set; } = "https://api.brevo.com/v3";

        public int? CaregiversUnverifiedListId { get; set; }
        public int? CaregiversNoGigListId { get; set; }
        public int? CaregiversAssessmentPendingListId { get; set; }
        public int? ClientsAbandonedCareRequestListId { get; set; }
        public int? ClientsPendingCommitmentPaymentListId { get; set; }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
    }
}

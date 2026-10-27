using System;

namespace Domain.Entities
{
    /// <summary>
    /// One row per (caregiver, notice type, enforcement-date snapshot). The Id IS that composite key, so MongoDB's primary-key
    /// uniqueness makes "claim a notice" an atomic insert: two admins (or two API instances) pressing send at once cannot both claim
    /// the same caregiver. <see cref="Result"/> is a concurrency token so an explicit retry of a failed row cannot race either.
    /// </summary>
    public class VettingNotice
    {
        /// <summary>"{caregiverId}|{noticeType}|{dateKey}"</summary>
        public string Id { get; set; } = string.Empty;
        public string? CaregiverId { get; set; }
        /// <summary>initial | reminder_7d | reminder_1d</summary>
        public string? NoticeType { get; set; }
        /// <summary>Snapshot of the enforcement dates the notice was about, e.g. "L202610190000-Cnone". A changed date = a new key = a new notice.</summary>
        public string? DateKey { get; set; }
        /// <summary>pending | sent | failed</summary>
        public string? Result { get; set; }
        public DateTime? AttemptedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public int? Attempts { get; set; }
        /// <summary>Exception type name only — never an address or any personal data.</summary>
        public string? Error { get; set; }
    }

    public static class VettingNoticeResults
    {
        public const string Pending = "pending";
        public const string Sent = "sent";
        public const string Failed = "failed";
    }
}

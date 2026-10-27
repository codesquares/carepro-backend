using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public class VettingNoticeSampleDTO
    {
        public string FirstName { get; set; } = string.Empty;
        public List<string> MissingSteps { get; set; } = new();
    }

    public class VettingNoticePreviewDTO
    {
        public string Type { get; set; } = string.Empty;
        /// <summary>false when neither enforcement date is set, or the deadline has already passed.</summary>
        public bool CanSend { get; set; }
        public string? Note { get; set; }
        public DateTime? LockoutDate { get; set; }
        public DateTime? ClearanceDate { get; set; }
        public DateTime? Deadline { get; set; }
        /// <summary>Caregivers a send would email right now. A send must be confirmed with exactly this number.</summary>
        public int RecipientCount { get; set; }
        public int AlreadySentCount { get; set; }
        /// <summary>Failed earlier for this type+dates; only an explicit retry includes them (they are NOT in RecipientCount unless retryFailed).</summary>
        public int FailedCount { get; set; }
        public Dictionary<string, int> ByStep { get; set; } = new();
        /// <summary>First 5: first name and missing steps only — no email address.</summary>
        public List<VettingNoticeSampleDTO> Sample { get; set; } = new();
    }

    public class VettingNoticeSendResultDTO
    {
        public string Type { get; set; } = string.Empty;
        public int Attempted { get; set; }
        public int Sent { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
    }

    public class VettingNoticeConflictException : Exception { public VettingNoticeConflictException(string m) : base(m) { } }
    public class VettingNoticeValidationException : Exception { public VettingNoticeValidationException(string m) : base(m) { } }

    public interface IVettingNoticeService
    {
        Task<VettingNoticePreviewDTO> PreviewAsync(string type, bool retryFailed = false);

        /// <summary>Never automatic: only called by the admin endpoint. confirmRecipientCount must equal the current recipient count (409 otherwise).</summary>
        Task<VettingNoticeSendResultDTO> SendAsync(string type, int confirmRecipientCount, bool retryFailed, AdminIdentity admin);
    }
}

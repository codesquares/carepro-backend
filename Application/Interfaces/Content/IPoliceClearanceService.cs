using Application.DTOs;
using System;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>Caregiver-facing police clearance operations. The caregiver id always comes from the JWT.</summary>
    public interface IPoliceClearanceService
    {
        Task<PoliceClearanceStatusResponse> GetStatusAsync(string caregiverId);

        /// <summary>Throws <see cref="PoliceClearanceValidationException"/> (400) or <see cref="PoliceClearanceConflictException"/> (409).</summary>
        Task<PoliceClearanceStatusResponse> SubmitAsync(string caregiverId, SubmitPoliceClearanceRequest request);

        /// <summary>Owner-only file read. Throws UnauthorizedAccessException for someone else's record, KeyNotFoundException if absent.</summary>
        Task<ClearanceFileContent> GetOwnFileAsync(string caregiverId, string clearanceId, int fileIndex);
    }

    public class PoliceClearanceValidationException : Exception
    {
        public PoliceClearanceValidationException(string message) : base(message) { }
    }

    public class PoliceClearanceConflictException : Exception
    {
        public PoliceClearanceConflictException(string message) : base(message) { }
    }
}

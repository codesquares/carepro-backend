using System;

namespace Domain.Entities
{
    /// <summary>
    /// An assignment / package-request transition lost a race (someone else accepted, declined, cancelled or assigned first).
    /// Still an <see cref="InvalidOperationException"/> so every existing handler maps it to 409; the controllers also expose
    /// <see cref="ErrorCode"/> so the UI can tell "someone else just changed this" from other 409s (not ready, wrong type…).
    /// </summary>
    public class AssignmentConflictException : InvalidOperationException
    {
        public string ErrorCode { get; } = "ASSIGNMENT_CONFLICT";
        public AssignmentConflictException(string message) : base(message) { }
    }
}

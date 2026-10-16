using System;

namespace Domain.Entities
{
    /// <summary>Payroll approval needs an explicit confirmation because uncleared HARD-flagged visits exist.</summary>
    public class UnclearedFlaggedVisitsException : InvalidOperationException
    {
        public const string Code = "uncleared_flagged_visits";
        public int Count { get; }
        public UnclearedFlaggedVisitsException(int count)
            : base($"This payroll has {count} uncleared flagged visit(s). Clear them, or approve explicitly with {count} uncleared flagged visit(s).")
        { Count = count; }
    }
}

using System;

namespace Domain.Entities
{
    /// <summary>
    /// Care (starting/submitting a visit) is blocked until the contract's client cancellation window has ended.
    /// Derives from InvalidOperationException so any caller that doesn't know about it still treats it as a 4xx conflict.
    /// </summary>
    public class CancellationWindowOpenException : InvalidOperationException
    {
        public const string Code = "cancellation_window_open";
        public string ErrorCode => Code;
        public DateTime WindowEndsAt { get; }

        public CancellationWindowOpenException(DateTime windowEndsAtUtc)
            : base($"Care can begin after {windowEndsAtUtc:dddd, MMMM dd, yyyy 'at' HH:mm} UTC. The client has a cancellation period until then.")
        {
            WindowEndsAt = windowEndsAtUtc;
        }
    }
}

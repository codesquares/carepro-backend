using Domain.Entities;
using System;

namespace Infrastructure.Content.Services
{
    /// <summary>Shared rule: no visit may start or be submitted while the client's cancellation window is open.</summary>
    public static class CancellationWindowGuard
    {
        /// <summary>
        /// Throws <see cref="CancellationWindowOpenException"/> while the window is open. A contract without a window
        /// (legacy, or window configured as 0 hours so it ends at send time) never blocks. A cancelled contract blocks
        /// care outright (separately from the window).
        /// </summary>
        public static void EnsureCareMayBegin(Contract contract, DateTime nowUtc)
        {
            if (contract.CancelledAt != null)
                throw new InvalidOperationException("This order was cancelled by the client, so no visits can be recorded.");
            if (contract.CancellationWindowEndsAt.HasValue && nowUtc < contract.CancellationWindowEndsAt.Value)
                throw new CancellationWindowOpenException(contract.CancellationWindowEndsAt.Value);
        }
    }
}

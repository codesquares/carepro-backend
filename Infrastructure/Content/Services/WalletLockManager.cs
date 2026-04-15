using System.Collections.Concurrent;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Singleton service providing per-caregiver mutual exclusion for wallet mutations.
    /// 
    /// WHY: All wallet operations follow a read-then-validate-then-write pattern.
    /// Without locking, two concurrent requests for the same caregiver can both read
    /// the same balance, both pass validation, and both write — causing double-spending
    /// or double-crediting.
    /// 
    /// HOW: A ConcurrentDictionary maps each caregiverId to a SemaphoreSlim(1,1).
    /// Before any wallet mutation, the caller acquires the semaphore for that caregiver.
    /// This serializes all mutations for the same wallet while allowing concurrent
    /// mutations on different wallets (no global bottleneck).
    /// 
    /// SCALING: This protects single-instance deployments. For multi-instance deployments,
    /// use a distributed lock (e.g., Redis RedLock) instead or in addition.
    /// The CaregiverWallet.Version field provides a second safety net via optimistic concurrency.
    /// </summary>
    public class WalletLockManager
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        /// <summary>
        /// Gets or creates a per-caregiver semaphore. Thread-safe via ConcurrentDictionary.
        /// </summary>
        private SemaphoreSlim GetLock(string caregiverId)
        {
            return _locks.GetOrAdd(caregiverId, _ => new SemaphoreSlim(1, 1));
        }

        /// <summary>
        /// Acquires the per-caregiver lock, executes the action, and releases the lock.
        /// If the lock cannot be acquired within the timeout, throws TimeoutException.
        /// </summary>
        /// <param name="caregiverId">The caregiver whose wallet is being mutated.</param>
        /// <param name="action">The async wallet mutation to execute under lock.</param>
        /// <param name="timeoutMs">Max wait time in milliseconds (default: 10 seconds).</param>
        public async Task ExecuteWithLockAsync(string caregiverId, Func<Task> action, int timeoutMs = 10_000)
        {
            var semaphore = GetLock(caregiverId);

            if (!await semaphore.WaitAsync(timeoutMs))
            {
                throw new TimeoutException(
                    $"Could not acquire wallet lock for caregiver {caregiverId} within {timeoutMs}ms. " +
                    "Another operation may be in progress.");
            }

            try
            {
                await action();
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Overload that returns a value from the locked operation.
        /// </summary>
        public async Task<T> ExecuteWithLockAsync<T>(string caregiverId, Func<Task<T>> action, int timeoutMs = 10_000)
        {
            var semaphore = GetLock(caregiverId);

            if (!await semaphore.WaitAsync(timeoutMs))
            {
                throw new TimeoutException(
                    $"Could not acquire wallet lock for caregiver {caregiverId} within {timeoutMs}ms. " +
                    "Another operation may be in progress.");
            }

            try
            {
                return await action();
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}

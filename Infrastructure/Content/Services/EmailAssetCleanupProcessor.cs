using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Background service that periodically cleans up orphaned email asset images
    /// from the Cloudinary "email-assets" folder. Assets older than 90 days are
    /// deleted to prevent unbounded storage growth and costs.
    ///
    /// Runs once every 24 hours. Uses the Cloudinary Search API to find assets
    /// uploaded more than 90 days ago in the email-assets folder, then deletes them.
    /// </summary>
    public class EmailAssetCleanupProcessor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<EmailAssetCleanupProcessor> _logger;

        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);
        private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);
        private const int AssetMaxAgeDays = 90;
        private const int BatchSize = 50;
        private const string AssetFolder = "email-assets";

        public EmailAssetCleanupProcessor(IServiceScopeFactory scopeFactory, ILogger<EmailAssetCleanupProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(InitialDelay, stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("EmailAssetCleanupProcessor: Starting cleanup cycle");

                    try
                    {
                        await CleanupExpiredAssetsAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "EmailAssetCleanupProcessor: Unhandled error during cleanup cycle");
                    }

                    await Task.Delay(RunInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
            }

            _logger.LogInformation("EmailAssetCleanupProcessor: Stopped");
        }

        private async Task CleanupExpiredAssetsAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var cloudinary = scope.ServiceProvider.GetRequiredService<Cloudinary>();

            var cutoffDate = DateTime.UtcNow.AddDays(-AssetMaxAgeDays);
            var cutoffStr = cutoffDate.ToString("yyyy-MM-dd");

            int totalDeleted = 0;
            int totalFailed = 0;
            string? nextCursor = null;

            do
            {
                stoppingToken.ThrowIfCancellationRequested();

                try
                {
                    var search = cloudinary.Search()
                        .Expression($"folder:{AssetFolder} AND uploaded_at<{cutoffStr}")
                        .MaxResults(BatchSize)
                        .SortBy("uploaded_at", "asc");

                    if (!string.IsNullOrEmpty(nextCursor))
                    {
                        search.NextCursor(nextCursor);
                    }

                    var searchResult = await search.ExecuteAsync();

                    if (searchResult == null || searchResult.Resources == null || searchResult.Resources.Count == 0)
                    {
                        _logger.LogDebug("EmailAssetCleanupProcessor: No more expired assets found");
                        break;
                    }

                    _logger.LogInformation(
                        "EmailAssetCleanupProcessor: Found {Count} expired assets in batch (total estimated: {Total})",
                        searchResult.Resources.Count, searchResult.TotalCount);

                    foreach (var resource in searchResult.Resources)
                    {
                        stoppingToken.ThrowIfCancellationRequested();

                        try
                        {
                            var deleteParams = new DeletionParams(resource.PublicId)
                            {
                                ResourceType = ResourceType.Image
                            };
                            var deleteResult = await cloudinary.DestroyAsync(deleteParams);

                            if (deleteResult.Result == "ok")
                            {
                                totalDeleted++;
                                _logger.LogDebug(
                                    "EmailAssetCleanupProcessor: Deleted asset {PublicId} (uploaded {Date})",
                                    resource.PublicId, resource.UploadedAt);
                            }
                            else
                            {
                                totalFailed++;
                                _logger.LogWarning(
                                    "EmailAssetCleanupProcessor: Failed to delete {PublicId}: {Result}",
                                    resource.PublicId, deleteResult.Result);
                            }
                        }
                        catch (Exception ex)
                        {
                            totalFailed++;
                            _logger.LogWarning(ex,
                                "EmailAssetCleanupProcessor: Error deleting asset {PublicId}",
                                resource.PublicId);
                        }
                    }

                    nextCursor = searchResult.NextCursor;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "EmailAssetCleanupProcessor: Error during search/delete batch");
                    break;
                }
            } while (!string.IsNullOrEmpty(nextCursor));

            _logger.LogInformation(
                "EmailAssetCleanupProcessor: Cleanup complete. Deleted: {Deleted}, Failed: {Failed}",
                totalDeleted, totalFailed);
        }
    }
}

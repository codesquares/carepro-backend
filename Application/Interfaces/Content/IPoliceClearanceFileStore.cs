using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Private storage for police clearance files. Files are stored as authenticated (non-public) assets:
    /// there is NO anonymous URL, and no storage URL is ever returned or logged. Reads go through
    /// <see cref="DownloadAsync"/> on the backend, which the controllers only call after checking the requester.
    /// </summary>
    public interface IPoliceClearanceFileStore
    {
        Task<StoredClearanceFile> UploadAsync(byte[] bytes, string extension);

        /// <summary>Returns the stored bytes, or null if the asset no longer exists.</summary>
        Task<byte[]?> DownloadAsync(string publicId, string? resourceType);

        Task<ClearanceFileDeleteOutcome> DeleteAsync(string publicId, string? resourceType);
    }

    public record StoredClearanceFile(string PublicId, string ResourceType);

    public enum ClearanceFileDeleteOutcome
    {
        Deleted,
        /// <summary>The asset was already gone — nothing left to remove.</summary>
        NotFound,
        Failed
    }
}

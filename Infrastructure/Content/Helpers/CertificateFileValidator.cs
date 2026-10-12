using System;

namespace Infrastructure.Content.Helpers
{
    /// <summary>
    /// Validates a certificate upload: PDF, JPG or PNG only, checked by the declared content type (when there is one)
    /// AND the file's magic bytes. The file name / extension is never trusted. Reuses the magic-byte detection the
    /// police-clearance upload already uses.
    /// </summary>
    public static class CertificateFileValidator
    {
        public const string NotAllowedMessage = "Only PDF, JPG or PNG files are accepted. Please upload the original document or a clear photo/scan of it.";

        /// <returns>null when the file is acceptable, otherwise a message safe to show the caregiver.</returns>
        public static string? Validate(byte[]? bytes, string? declaredContentType)
        {
            if (bytes == null || bytes.Length == 0)
                return "The certificate file is empty.";

            var actual = PoliceClearanceFileValidator.Detect(bytes);
            if (actual == ClearanceFileKind.Unknown)
                return NotAllowedMessage;

            // The base64/JSON upload has no content type to compare; the multipart upload does.
            if (declaredContentType != null)
            {
                var declared = PoliceClearanceFileValidator.KindFromDeclared(declaredContentType);
                if (declared == ClearanceFileKind.Unknown || declared != actual)
                    return NotAllowedMessage;
            }

            return null;
        }
    }
}

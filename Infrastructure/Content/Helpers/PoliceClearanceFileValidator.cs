using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Infrastructure.Content.Helpers
{
    public enum ClearanceFileKind { Unknown, Pdf, Jpeg, Png }

    public record IncomingClearanceFile(string FileName, string? DeclaredContentType, byte[] Bytes);

    public record ValidatedClearanceFile(string SafeFileName, string ContentType, string Extension, byte[] Bytes);

    public record ClearanceFileValidationResult(bool IsValid, string? Error, List<ValidatedClearanceFile> Files);

    /// <summary>
    /// Validates uploaded clearance files by declared content type AND magic bytes (never the extension).
    /// A submission is a single PDF, or 1..MaxFiles images (JPG/PNG). Size limits are per file and in total.
    /// </summary>
    public static class PoliceClearanceFileValidator
    {
        private static ClearanceFileKind Detect(byte[] b)
        {
            if (b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D) return ClearanceFileKind.Pdf; // %PDF-
            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ClearanceFileKind.Jpeg;
            if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return ClearanceFileKind.Png;
            return ClearanceFileKind.Unknown;
        }

        private static (string contentType, string ext) Describe(ClearanceFileKind k) => k switch
        {
            ClearanceFileKind.Pdf => ("application/pdf", ".pdf"),
            ClearanceFileKind.Jpeg => ("image/jpeg", ".jpg"),
            ClearanceFileKind.Png => ("image/png", ".png"),
            _ => ("application/octet-stream", "")
        };

        private static ClearanceFileKind KindFromDeclared(string? declared)
        {
            var ct = (declared ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
            return ct switch
            {
                "application/pdf" => ClearanceFileKind.Pdf,
                "image/jpeg" or "image/jpg" => ClearanceFileKind.Jpeg,
                "image/png" => ClearanceFileKind.Png,
                _ => ClearanceFileKind.Unknown
            };
        }

        // Active content has no place in a scanned certificate; refuse rather than store and later serve it to staff.
        private static readonly string[] PdfActiveMarkers = { "/JavaScript", "/JS ", "/JS(", "/Launch", "/EmbeddedFile", "/OpenAction" };

        private static bool PdfHasActiveContent(byte[] bytes)
        {
            var text = Encoding.Latin1.GetString(bytes);
            return PdfActiveMarkers.Any(m => text.Contains(m, StringComparison.Ordinal));
        }

        private static string SafeName(string? raw)
        {
            var name = Path.GetFileName(raw ?? string.Empty);
            name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (name.Length > 120) name = name[..120];
            return string.IsNullOrWhiteSpace(name) ? "clearance" : name;
        }

        public static ClearanceFileValidationResult Validate(IReadOnlyList<IncomingClearanceFile> files, PoliceClearanceSettings settings)
        {
            static ClearanceFileValidationResult Fail(string msg) => new(false, msg, new List<ValidatedClearanceFile>());

            if (files == null || files.Count == 0)
                return Fail("Please attach your police clearance (a PDF, or up to 5 photos/scans).");
            if (files.Count > settings.MaxFiles)
                return Fail($"You can attach at most {settings.MaxFiles} files.");

            long total = 0;
            var result = new List<ValidatedClearanceFile>();
            foreach (var f in files)
            {
                if (f.Bytes == null || f.Bytes.Length == 0)
                    return Fail("One of the attached files is empty.");
                if (f.Bytes.Length > settings.MaxFileSizeBytes)
                    return Fail($"Each file must be {settings.MaxFileSizeMB} MB or smaller.");
                total += f.Bytes.Length;

                var declared = KindFromDeclared(f.DeclaredContentType);
                if (declared == ClearanceFileKind.Unknown)
                    return Fail("Only PDF, JPG or PNG files are accepted.");
                var actual = Detect(f.Bytes);
                if (actual == ClearanceFileKind.Unknown || actual != declared)
                    return Fail("One of the files isn't a valid PDF, JPG or PNG. Please upload the original document or a clear photo/scan of it.");
                if (actual == ClearanceFileKind.Pdf && PdfHasActiveContent(f.Bytes))
                    return Fail("This PDF contains embedded scripts or attachments and can't be accepted. Please upload a plain scan of the document.");

                var (ct, ext) = Describe(actual);
                result.Add(new ValidatedClearanceFile(SafeName(f.FileName), ct, ext, f.Bytes));
            }

            if (total > settings.MaxTotalSizeBytes)
                return Fail($"The files together must be {settings.MaxTotalSizeMB} MB or smaller.");
            if (result.Any(r => r.ContentType == "application/pdf") && result.Count > 1)
                return Fail("Attach either a single PDF or up to 5 photos/scans — not a mix, and only one PDF.");

            return new ClearanceFileValidationResult(true, null, result);
        }
    }
}

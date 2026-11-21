using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace DynamicIsland.Services;

public enum FileActionKind
{
    ConvertToPng,
    ConvertToJpg,
    StripExifAndResize,
    ImagesToPdf,
    MergePdfs,
    CompressVideo720p,
    CompressVideo1080p,
    UnzipArchive,
    BatchRenameTimestamp,
    CopyAsDataUri
}

public sealed record FileActionOption(
    FileActionKind Kind,
    string Label,
    string Subtitle,
    string Glyph);

public sealed record FileActionResult(
    bool Success,
    string Title,
    string Message,
    IReadOnlyList<string> OutputPaths,
    string? CopiedClipboardText = null);

/// <summary>
/// Built-in zero-dependency file converter &amp; batch action pipeline for the Drop Shelf and Floating Basket.
/// Uses Windows Imaging Component (WIC), <see cref="MediaTranscoder"/>, <see cref="ZipFile"/>, and PDFsharp.
/// </summary>
internal static class FileActionPipeline
{
    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif", ".ico"
    };

    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".webm"
    };

    public static bool IsImageFile(string path) =>
        !string.IsNullOrWhiteSpace(path) && ImageExts.Contains(Path.GetExtension(path));

    public static bool IsPdfFile(string path) =>
        !string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    public static bool IsZipFile(string path) =>
        !string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

    public static bool IsVideoFile(string path) =>
        !string.IsNullOrWhiteSpace(path) && VideoExts.Contains(Path.GetExtension(path));

    /// <summary>
    /// Returns the contextual file actions available for the given file paths.
    /// </summary>
    public static IReadOnlyList<FileActionOption> GetAvailableActions(IReadOnlyList<string> paths)
    {
        var list = new List<FileActionOption>();
        var existingFiles = paths.Where(File.Exists).ToList();
        if (existingFiles.Count == 0) return list;

        bool anyImages = existingFiles.Any(IsImageFile);
        bool allPdfs = existingFiles.Count >= 2 && existingFiles.All(IsPdfFile);
        int pdfCount = existingFiles.Count(IsPdfFile);
        bool anyZip = existingFiles.Any(IsZipFile);
        bool anyVideo = existingFiles.Any(IsVideoFile);

        if (anyImages)
        {
            list.Add(new FileActionOption(
                FileActionKind.ConvertToPng,
                "Convert to PNG",
                "Lossless WIC image conversion",
                "\uEB9F"));
            list.Add(new FileActionOption(
                FileActionKind.ConvertToJpg,
                "Convert to JPG (90%)",
                "Compact JPEG conversion",
                "\uEB9F"));
            list.Add(new FileActionOption(
                FileActionKind.ImagesToPdf,
                "Images \u2192 PDF Document",
                "Combine image(s) into a clean PDF",
                "\uEA90"));
            list.Add(new FileActionOption(
                FileActionKind.StripExifAndResize,
                "Strip Metadata / Max 1920px",
                "Clean EXIF & downscale large photos",
                "\uE72E"));
        }

        if (pdfCount >= 2 || allPdfs)
        {
            list.Add(new FileActionOption(
                FileActionKind.MergePdfs,
                $"Merge {pdfCount} PDFs",
                "Combine PDF pages into one file",
                "\uEA90"));
        }

        if (anyVideo)
        {
            list.Add(new FileActionOption(
                FileActionKind.CompressVideo720p,
                "Compress Video (720p MP4)",
                "Hardware H.264 transcode for sharing",
                "\uE714"));
            list.Add(new FileActionOption(
                FileActionKind.CompressVideo1080p,
                "Compress Video (1080p MP4)",
                "High-definition H.264 MP4 transcode",
                "\uE714"));
        }

        if (anyZip)
        {
            list.Add(new FileActionOption(
                FileActionKind.UnzipArchive,
                "Extract ZIP Archive",
                "Unzip into folder & add to Shelf",
                "\uF012"));
        }

        if (existingFiles.Count >= 1)
        {
            list.Add(new FileActionOption(
                FileActionKind.BatchRenameTimestamp,
                "Clean Timestamp Copy",
                "Create clean-named copies with date & index",
                "\uE8AC"));
            list.Add(new FileActionOption(
                FileActionKind.CopyAsDataUri,
                "Copy as Base64 / Data URI",
                "Encode first file to clipboard for web/dev",
                "\uE943"));
        }

        return list;
    }

    public static async Task<FileActionResult> ExecuteAsync(
        FileActionKind kind,
        IReadOnlyList<string> paths,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var files = paths.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            return new FileActionResult(false, "No Valid Files", "Selected items do not exist on disk", Array.Empty<string>());
        }

        try
        {
            return kind switch
            {
                FileActionKind.ConvertToPng => await Task.Run(() => ConvertImages(files, ".png", maxDim: 0, progress, ct), ct),
                FileActionKind.ConvertToJpg => await Task.Run(() => ConvertImages(files, ".jpg", maxDim: 0, progress, ct), ct),
                FileActionKind.StripExifAndResize => await Task.Run(() => ConvertImages(files, null, maxDim: 1920, progress, ct), ct),
                FileActionKind.ImagesToPdf => await Task.Run(() => ConvertImagesToPdf(files, progress, ct), ct),
                FileActionKind.MergePdfs => await Task.Run(() => MergePdfFiles(files, progress, ct), ct),
                FileActionKind.CompressVideo720p => await TranscodeVideosAsync(files, VideoEncodingQuality.HD720p, progress, ct),
                FileActionKind.CompressVideo1080p => await TranscodeVideosAsync(files, VideoEncodingQuality.HD1080p, progress, ct),
                FileActionKind.UnzipArchive => await Task.Run(() => UnzipArchives(files, progress, ct), ct),
                FileActionKind.BatchRenameTimestamp => await Task.Run(() => BatchCopyRenamed(files, progress, ct), ct),
                FileActionKind.CopyAsDataUri => await Task.Run(() => EncodeDataUri(files[0]), ct),
                _ => new FileActionResult(false, "Unsupported Action", kind.ToString(), Array.Empty<string>())
            };
        }
        catch (OperationCanceledException)
        {
            return new FileActionResult(false, "Action Cancelled", "File operation was cancelled.", Array.Empty<string>());
        }
        catch (Exception ex)
        {
            App.Log($"[FileActionPipeline] {kind} failed: {ex.Message}");
            return new FileActionResult(false, "Action Failed", ex.Message, Array.Empty<string>());
        }
    }

    public static FileActionResult ConvertImages(
        IReadOnlyList<string> files,
        string? targetExt,
        int maxDim = 0,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var images = files.Where(IsImageFile).ToList();
        if (images.Count == 0)
        {
            return new FileActionResult(false, "No Images Found", "Select PNG, JPG, BMP, WebP, or HEIC images", Array.Empty<string>());
        }

        var outputs = new List<string>();
        for (int i = 0; i < images.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            string src = images[i];
            string ext = targetExt ?? Path.GetExtension(src).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif"))
            {
                ext = ".png";
            }

            string dir = Path.GetDirectoryName(src) ?? Path.GetTempPath();
            string stem = Path.GetFileNameWithoutExtension(src);
            string suffix = maxDim > 0 ? "_clean" : "_converted";
            string outPath = GetUniqueFilePath(dir, $"{stem}{suffix}", ext);

            try
            {
                using (var fs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0)
                    {
                        throw new InvalidDataException($"Image '{Path.GetFileName(src)}' contains no readable frames.");
                    }

                    BitmapSource frame = decoder.Frames[0];

                    if (maxDim > 0 && (frame.PixelWidth > maxDim || frame.PixelHeight > maxDim))
                    {
                        double scale = Math.Min((double)maxDim / frame.PixelWidth, (double)maxDim / frame.PixelHeight);
                        frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                    }

                    // Re-materialize pixel buffer so EXIF/GPS metadata blocks are stripped cleanly
                    var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                    int w = bgra.PixelWidth;
                    int h = bgra.PixelHeight;
                    int stride = checked(w * 4);
                    long pixelBufferBytes = checked((long)h * stride);
                    if (pixelBufferBytes <= 0 || pixelBufferBytes > 512L * 1024 * 1024)
                    {
                        throw new InvalidOperationException($"Image dimensions {w}x{h} exceed supported pixel buffer limit.");
                    }

                    byte[] pixels = new byte[pixelBufferBytes];
                    bgra.CopyPixels(pixels, stride, 0);
                    var cleanBmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
                    cleanBmp.Freeze();

                    BitmapEncoder encoder = ext switch
                    {
                        ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 90 },
                        ".bmp" => new BmpBitmapEncoder(),
                        ".tif" => new TiffBitmapEncoder(),
                        _ => new PngBitmapEncoder()
                    };
                    encoder.Frames.Add(BitmapFrame.Create(cleanBmp));

                    using var outStream = new FileStream(outPath, FileMode.Create, FileAccess.Write);
                    encoder.Save(outStream);
                }
            }
            catch
            {
                try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                throw;
            }

            outputs.Add(outPath);
            progress?.Report((double)(i + 1) / images.Count);
        }

        string label = maxDim > 0
            ? $"Cleaned {outputs.Count} image{(outputs.Count == 1 ? "" : "s")}"
            : $"Converted {outputs.Count} image{(outputs.Count == 1 ? "" : "s")} to {targetExt?.TrimStart('.').ToUpperInvariant()}";

        return new FileActionResult(true, label, Path.GetFileName(outputs[0]), outputs);
    }

    public static FileActionResult ConvertImagesToPdf(IReadOnlyList<string> files, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var images = files.Where(IsImageFile).ToList();
        if (images.Count == 0)
        {
            return new FileActionResult(false, "No Images Found", "Drop one or more images to build a PDF", Array.Empty<string>());
        }

        string dir = Path.GetDirectoryName(images[0]) ?? Path.GetTempPath();
        string baseName = images.Count == 1
            ? Path.GetFileNameWithoutExtension(images[0])
            : $"Combined_{images.Count}_Images";
        string outPdf = GetUniqueFilePath(dir, baseName, ".pdf");

        var streamsToDispose = new List<IDisposable>();
        try
        {
            using var doc = new PdfDocument();
            doc.Info.Title = baseName;
            doc.Info.Creator = "Dynamic Island for Windows";

            for (int i = 0; i < images.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                string imgPath = images[i];
                // Normalize via WIC to an in-memory PNG stream so PDFsharp can embed any WIC format (including BMP/WebP/TIFF)
                var pngStream = new MemoryStream();
                streamsToDispose.Add(pngStream);

                using (var fs = new FileStream(imgPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0)
                    {
                        throw new InvalidDataException($"Image '{Path.GetFileName(imgPath)}' contains no readable frames.");
                    }
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));
                    encoder.Save(pngStream);
                }
                pngStream.Position = 0;

                var xImg = XImage.FromStream(pngStream);
                streamsToDispose.Add(xImg);

                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(xImg.PointWidth > 0 ? xImg.PointWidth : xImg.PixelWidth * 72.0 / 96.0);
                page.Height = XUnit.FromPoint(xImg.PointHeight > 0 ? xImg.PointHeight : xImg.PixelHeight * 72.0 / 96.0);

                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(xImg, 0, 0, page.Width.Point, page.Height.Point);

                progress?.Report((double)(i + 1) / images.Count);
            }

            doc.Save(outPdf);
            return new FileActionResult(
                true,
                $"Created PDF ({images.Count} page{(images.Count == 1 ? "" : "s")})",
                Path.GetFileName(outPdf),
                new[] { outPdf });
        }
        catch
        {
            try { if (File.Exists(outPdf)) File.Delete(outPdf); } catch { }
            throw;
        }
        finally
        {
            foreach (var d in streamsToDispose)
            {
                try { d.Dispose(); } catch { }
            }
        }
    }

    public static FileActionResult MergePdfFiles(IReadOnlyList<string> files, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var pdfs = files.Where(IsPdfFile).ToList();
        if (pdfs.Count < 2)
        {
            return new FileActionResult(false, "Need 2+ PDFs", "Place at least two PDF files on the shelf to merge", Array.Empty<string>());
        }

        string dir = Path.GetDirectoryName(pdfs[0]) ?? Path.GetTempPath();
        string outPdf = GetUniqueFilePath(dir, $"Merged_{pdfs.Count}_PDFs", ".pdf");

        try
        {
            using var outputDoc = new PdfDocument();
            outputDoc.Info.Title = $"Merged ({pdfs.Count} documents)";
            outputDoc.Info.Creator = "Dynamic Island for Windows";

            int totalPages = 0;
            for (int i = 0; i < pdfs.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var inputDoc = PdfReader.Open(pdfs[i], PdfDocumentOpenMode.Import);
                for (int p = 0; p < inputDoc.PageCount; p++)
                {
                    ct.ThrowIfCancellationRequested();
                    outputDoc.AddPage(inputDoc.Pages[p]);
                    totalPages++;
                }
                progress?.Report((double)(i + 1) / pdfs.Count);
            }

            outputDoc.Save(outPdf);
            return new FileActionResult(
                true,
                $"Merged {pdfs.Count} PDFs ({totalPages} pages)",
                Path.GetFileName(outPdf),
                new[] { outPdf });
        }
        catch
        {
            try { if (File.Exists(outPdf)) File.Delete(outPdf); } catch { }
            throw;
        }
    }

    public static async Task<FileActionResult> TranscodeVideosAsync(
        IReadOnlyList<string> files,
        VideoEncodingQuality quality,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var videos = files.Where(IsVideoFile).ToList();
        if (videos.Count == 0)
        {
            return new FileActionResult(false, "No Videos Found", "Select MP4, MOV, MKV, or AVI files", Array.Empty<string>());
        }

        var outputs = new List<string>();
        string tag = quality == VideoEncodingQuality.HD720p ? "720p" : "1080p";

        for (int i = 0; i < videos.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            string srcPath = videos[i];
            string dir = Path.GetDirectoryName(srcPath) ?? Path.GetTempPath();
            string stem = Path.GetFileNameWithoutExtension(srcPath);
            string outPath = GetUniqueFilePath(dir, $"{stem}_{tag}", ".mp4");

            try
            {
                var srcFile = await StorageFile.GetFileFromPathAsync(srcPath).AsTask(ct);
                var folder = await StorageFolder.GetFolderFromPathAsync(dir).AsTask(ct);
                var destFile = await folder.CreateFileAsync(Path.GetFileName(outPath), CreationCollisionOption.ReplaceExisting).AsTask(ct);

                var profile = MediaEncodingProfile.CreateMp4(quality);
                var transcoder = new MediaTranscoder();
                var prep = await transcoder.PrepareFileTranscodeAsync(srcFile, destFile, profile).AsTask(ct);
                if (!prep.CanTranscode)
                {
                    try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                    return new FileActionResult(false, "Transcode Unsupported", $"Reason: {prep.FailureReason}", outputs);
                }

                var transcodeOp = prep.TranscodeAsync();
                transcodeOp.Progress = (_, pct) =>
                {
                    double overall = (i + (pct / 100.0)) / videos.Count;
                    progress?.Report(overall);
                };

                await transcodeOp.AsTask(ct);
                if (!File.Exists(outPath) || new FileInfo(outPath).Length == 0)
                {
                    try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                    return new FileActionResult(false, "Transcode Failed", $"Produced empty output for '{Path.GetFileName(srcPath)}'.", outputs);
                }

                outputs.Add(outPath);
            }
            catch
            {
                try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                throw;
            }
        }

        return new FileActionResult(
            true,
            $"Compressed {outputs.Count} Video{(outputs.Count == 1 ? "" : "s")} ({tag})",
            Path.GetFileName(outputs[0]),
            outputs);
    }

    public static FileActionResult UnzipArchives(IReadOnlyList<string> files, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        const int maxEntries = 10_000;
        const long maxTotalBytes = 4L * 1024 * 1024 * 1024;
        const long bombCheckMinBytes = 10L * 1024 * 1024;
        const double maxCompressionRatio = 100.0;

        var zips = files.Where(IsZipFile).ToList();
        if (zips.Count == 0)
        {
            return new FileActionResult(false, "No ZIP Archives", "Select a .zip file to extract", Array.Empty<string>());
        }

        var outputs = new List<string>();
        for (int i = 0; i < zips.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            string zipPath = zips[i];
            string parentDir = Path.GetDirectoryName(zipPath) ?? Path.GetTempPath();
            string folderName = Path.GetFileNameWithoutExtension(zipPath);
            string extractDir = GetUniqueDirectoryPath(parentDir, folderName);
            Directory.CreateDirectory(extractDir);

            string canonicalDest = Path.GetFullPath(extractDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                if (archive.Entries.Count > maxEntries)
                {
                    throw new InvalidDataException($"ZIP archive exceeds maximum allowed entry count ({maxEntries}).");
                }

                long totalExtractedBytes = 0;
                int entriesProcessed = 0;
                byte[] copyBuf = new byte[65536];

                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (++entriesProcessed > maxEntries)
                    {
                        throw new InvalidDataException($"ZIP archive exceeds maximum allowed entry count ({maxEntries}).");
                    }

                    string normalizedEntry = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    string targetPath = Path.GetFullPath(Path.Combine(canonicalDest, normalizedEntry));
                    if (!targetPath.StartsWith(canonicalDest, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"Blocked unsafe archive path traversal entry '{entry.FullName}'.");
                    }

                    if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                    {
                        Directory.CreateDirectory(targetPath);
                        continue;
                    }

                    if (entry.Length > bombCheckMinBytes &&
                        (entry.CompressedLength <= 0 || (double)entry.Length / entry.CompressedLength > maxCompressionRatio))
                    {
                        throw new InvalidDataException($"Blocked suspicious compression ratio in ZIP entry '{entry.FullName}'.");
                    }

                    string? entryParent = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(entryParent))
                    {
                        Directory.CreateDirectory(entryParent);
                    }

                    using var inStream = entry.Open();
                    using var outStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    long entryWritten = 0;
                    int read;
                    while ((read = inStream.Read(copyBuf, 0, copyBuf.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        entryWritten += read;
                        totalExtractedBytes += read;

                        if (totalExtractedBytes > maxTotalBytes)
                        {
                            throw new InvalidDataException("ZIP extraction exceeded 4 GB safety limit.");
                        }

                        if (entryWritten > bombCheckMinBytes &&
                            (entry.CompressedLength <= 0 || (double)entryWritten / entry.CompressedLength > maxCompressionRatio))
                        {
                            throw new InvalidDataException($"Blocked decompression bomb in ZIP entry '{entry.FullName}'.");
                        }

                        outStream.Write(copyBuf, 0, read);
                    }
                }
            }
            catch
            {
                try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true); } catch { }
                throw;
            }

            outputs.Add(extractDir);
            progress?.Report((double)(i + 1) / zips.Count);
        }

        return new FileActionResult(
            true,
            $"Extracted {outputs.Count} Archive{(outputs.Count == 1 ? "" : "s")}",
            Path.GetFileName(outputs[0]),
            outputs);
    }

    public static FileActionResult BatchCopyRenamed(IReadOnlyList<string> files, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var outputs = new List<string>();
        string dateStamp = DateTime.Now.ToString("yyyyMMdd");

        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            string src = files[i];
            if (!File.Exists(src)) continue;
            string dir = Path.GetDirectoryName(src) ?? Path.GetTempPath();
            string ext = Path.GetExtension(src);
            string stem = Path.GetFileNameWithoutExtension(src);
            string outPath = GetUniqueFilePath(dir, $"{dateStamp}_{i + 1:D2}_{stem}", ext);
            File.Copy(src, outPath, overwrite: false);
            outputs.Add(outPath);
            progress?.Report((double)(i + 1) / files.Count);
        }

        return new FileActionResult(
            outputs.Count > 0,
            $"Renamed {outputs.Count} File{(outputs.Count == 1 ? "" : "s")}",
            outputs.Count > 0 ? Path.GetFileName(outputs[0]) : "",
            outputs);
    }

    public static FileActionResult EncodeDataUri(string filePath)
    {
        var fi = new FileInfo(filePath);
        if (!fi.Exists || fi.Length > 15 * 1024 * 1024)
        {
            return new FileActionResult(false, "File Too Large", "Base64 encoding is capped at 15 MB", Array.Empty<string>());
        }

        byte[] bytes = File.ReadAllBytes(filePath);
        string b64 = Convert.ToBase64String(bytes);
        string ext = fi.Extension.ToLowerInvariant();
        string mime = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };

        string dataUri = $"data:{mime};base64,{b64}";
        return new FileActionResult(
            true,
            "Data URI Copied",
            $"{fi.Name} ({DropShelfService.FormatFileSize(fi.Length)}) encoded",
            Array.Empty<string>(),
            CopiedClipboardText: dataUri);
    }

    private static string GetUniqueFilePath(string directory, string baseName, string extension)
    {
        if (!extension.StartsWith('.')) extension = "." + extension;
        string candidate = Path.Combine(directory, baseName + extension);
        int counter = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName}_{counter++}{extension}");
        }
        return candidate;
    }

    private static string GetUniqueDirectoryPath(string parentDir, string folderName)
    {
        string candidate = Path.Combine(parentDir, folderName);
        int counter = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
        {
            candidate = Path.Combine(parentDir, $"{folderName}_{counter++}");
        }
        return candidate;
    }
}

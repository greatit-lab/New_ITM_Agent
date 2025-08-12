using ITM_Agent.Common;
using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using IOPath = System.IO.Path; // 'Path' 클래스의 모호성을 해결하기 위한 별칭

namespace ITM_Agent.Core
{
    public class PdfMergeManager
    {
        private readonly ILogger _logger;

        public PdfMergeManager(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void MergeImagesToPdf(List<string> imagePaths, string outputPdfPath)
        {
            try
            {
                if (imagePaths == null || imagePaths.Count == 0)
                {
                    _logger.Event("[PdfMergeManager] No images to merge.");
                    return;
                }

                string pdfDirectory = IOPath.GetDirectoryName(outputPdfPath);
                if (string.IsNullOrEmpty(pdfDirectory))
                {
                    _logger.Error($"[PdfMergeManager] Invalid output PDF path: {outputPdfPath}");
                    return;
                }
                Directory.CreateDirectory(pdfDirectory);

                _logger.Event($"[PdfMergeManager] Starting PDF merge. Output: {IOPath.GetFileName(outputPdfPath)}, Images: {imagePaths.Count}");

                using (var writer = new PdfWriter(outputPdfPath))
                using (var pdfDoc = new PdfDocument(writer))
                using (var document = new Document(pdfDoc))
                {
                    document.SetMargins(0, 0, 0, 0);

                    for (int i = 0; i < imagePaths.Count; i++)
                    {
                        string imgPath = imagePaths[i];
                        try
                        {
                            // 파일 스트림 잠김을 방지하기 위해 byte 배열로 먼저 읽어옵니다.
                            byte[] imgBytes = File.ReadAllBytes(imgPath);
                            var imgData = ImageDataFactory.Create(imgBytes);
                            var img = new Image(imgData);

                            if (i > 0) document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));

                            pdfDoc.SetDefaultPageSize(new PageSize(img.GetImageWidth(), img.GetImageHeight()));
                            img.SetAutoScale(false);
                            img.SetFixedPosition(0, 0);
                            img.SetWidth(img.GetImageWidth());
                            img.SetHeight(img.GetImageHeight());
                            document.Add(img);

                            _logger.Debug($"[PdfMergeManager] Added page {i + 1}: {IOPath.GetFileName(imgPath)}");
                        }
                        catch (Exception exImg)
                        {
                            _logger.Error($"[PdfMergeManager] Error adding image '{imgPath}': {exImg.Message}");
                        }
                    }
                }

                // 이미지 파일 삭제
                int deletedCount = 0;
                foreach (string imgPath in imagePaths)
                {
                    if (DeleteFileWithRetry(imgPath))
                    {
                        deletedCount++;
                    }
                }

                _logger.Event($"[PdfMergeManager] Merge completed. {deletedCount}/{imagePaths.Count} image files deleted.");
            }
            catch (Exception ex)
            {
                _logger.Error($"[PdfMergeManager] MergeImagesToPdf failed: {ex.Message}");
                throw;
            }
        }

        private bool DeleteFileWithRetry(string filePath, int maxRetries = 5, int delayMs = 300)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.SetAttributes(filePath, FileAttributes.Normal);
                        File.Delete(filePath);
                    }
                    return true;
                }
                catch (IOException) when (attempt < maxRetries)
                {
                    Thread.Sleep(delayMs);
                }
                catch (Exception ex)
                {
                    _logger.Error($"[PdfMergeManager] Delete retry {attempt} failed for {filePath}: {ex.Message}");
                    if (attempt >= maxRetries) return false;
                    Thread.Sleep(delayMs);
                }
            }
            return false;
        }
    }
}

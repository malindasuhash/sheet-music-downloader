// Program.cs
// Requires NuGet packages:
//   dotnet add package SixLabors.ImageSharp
//   dotnet add package PdfSharpCore

using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageStitchAndPdf
{
    class Program
    {
        // Page dimensions (A4 at ~300 DPI by default — adjust as needed)
        const int PageWidth = 2480;
        const int PageHeight = 3508;
        const int Margin = 50;
        const int VerticalSpacing = 30;

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("usage: imagestitchandpdf <inputfolder> <outputpdf> [--horizontal]");
                return;
            }

            string inputFolder =  args[0];
            string outputPdf = args[1];
            bool horizontal = args.Contains("--horizontal");

            var imageFiles = Directory.GetFiles(inputFolder)
                .Where(f => new[] { ".png", ".jpg", ".jpeg", ".bmp" }
                    .Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f)
                .ToList();

            if (imageFiles.Count == 0)
            {
                Console.WriteLine("No images found in input folder.");
                return;
            }

            Console.WriteLine($"Found {imageFiles.Count} images.");

            if (horizontal)
            {
                StitchHorizontally(imageFiles, outputPdf);
            }
            else
            {
                StitchVerticallyAndPaginate(imageFiles, outputPdf);
            }
        }

        /// <summary>
        /// Stitches all images side-by-side into a single wide image, then exports as a single-page PDF.
        /// </summary>
        static void StitchHorizontally(List<string> imageFiles, string outputPdf)
        {
            const float scale = 0.5f;

            var loaded = imageFiles.Select(f =>
            {
                var img = SixLabors.ImageSharp.Image.Load<Rgba32>(f);
                int newWidth = (int)(img.Width * scale);
                int newHeight = (int)(img.Height * scale);
                img.Mutate(ctx => ctx.Resize(newWidth, newHeight));
                return img;
            }).ToList();

            int totalWidth = loaded.Sum(i => i.Width);
            int maxHeight = loaded.Max(i => i.Height);

            using var combined = new Image<Rgba32>(totalWidth, maxHeight, SixLabors.ImageSharp.Color.White);

            int xOffset = 0;
            foreach (var img in loaded)
            {
                combined.Mutate(ctx => ctx.DrawImage(img, new SixLabors.ImageSharp.Point(xOffset, 0), 1f));
                xOffset += img.Width;
                img.Dispose();
            }

            string tempPng = Path.Combine(Path.GetTempPath(), $"stitched_{Guid.NewGuid()}.png");
            combined.Save(tempPng);

            CreatePdfFromImages(new List<string> { tempPng }, outputPdf, fitToPage: true);

            File.Delete(tempPng);
            Console.WriteLine($"Saved horizontally-stitched PDF to {outputPdf}");
        }

        /// <summary>
        /// Lays out images vertically on pages of fixed size, starting a new page
        /// when the current one would overflow. Each page is rendered as a temp PNG,
        /// then all pages are combined into a single PDF.
        /// </summary>
        static void StitchVerticallyAndPaginate(List<string> imageFiles, string outputPdf)
        {
            var pages = new List<Image<Rgba32>>();
            var current = NewPage();
            int y = Margin;

            foreach (var file in imageFiles)
            {
                using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(file);

                // Scale image to fit page width if it's too wide
                int targetWidth = Math.Min(img.Width, PageWidth - 2 * Margin);
                double scale = (double)targetWidth / img.Width;
                int targetHeight = (int)(img.Height * scale);

                if (y + targetHeight + Margin > PageHeight)
                {
                    pages.Add(current);
                    current = NewPage();
                    y = Margin;
                }

                using var resized = img.Clone(ctx => ctx.Resize(targetWidth, targetHeight));
                current.Mutate(ctx => ctx.DrawImage(resized, new SixLabors.ImageSharp.Point(Margin, y), 1f));

                y += targetHeight + VerticalSpacing;
                Console.WriteLine($"Placed {Path.GetFileName(file)} at y={y - targetHeight - VerticalSpacing}, size {targetWidth}x{targetHeight}");
            }

            pages.Add(current);

            // Save each page to a temp PNG, then build the PDF
            var tempFiles = new List<string>();
            for (int i = 0; i < pages.Count; i++)
            {
                string tempPng = Path.Combine(Path.GetTempPath(), $"page_{i}_{Guid.NewGuid()}.png");
                pages[i].Save(tempPng);
                tempFiles.Add(tempPng);
                pages[i].Dispose();
            }

            CreatePdfFromImages(tempFiles, outputPdf, fitToPage: false);

            foreach (var f in tempFiles)
                File.Delete(f);

            Console.WriteLine($"Saved {pages.Count} page(s) to {outputPdf}");
        }

        static Image<Rgba32> NewPage()
        {
            return new Image<Rgba32>(PageWidth, PageHeight, SixLabors.ImageSharp.Color.White);
        }

        /// <summary>
        /// Creates a PDF where each input image becomes one page.
        /// If fitToPage is true, the image is scaled to fit a standard A4 page;
        /// otherwise the page size matches the image dimensions (assumed already page-sized).
        /// </summary>
        static void CreatePdfFromImages(List<string> imagePaths, string outputPdf, bool fitToPage, string title = null)
        {
            using var document = new PdfDocument();

            var titleFont = new XFont("Arial", 24, XFontStyle.Bold);

            foreach (var path in imagePaths)
            {
                var imageInfo = SixLabors.ImageSharp.Image.Identify(path);
                int pxWidth = imageInfo.Width;
                int pxHeight = imageInfo.Height;

                var page = document.AddPage();

                if (fitToPage)
                {
                    page.Width = XUnit.FromPoint(595);
                    page.Height = XUnit.FromPoint(842);
                }
                else
                {
                    const double dpi = 300.0;
                    page.Width = XUnit.FromPoint(pxWidth * 72.0 / dpi);
                    page.Height = XUnit.FromPoint(pxHeight * 72.0 / dpi);
                }

                using var gfx = XGraphics.FromPdfPage(page);
                using var xImage = XImage.FromFile(path);

                double titleHeight = 0;
                if (!string.IsNullOrEmpty(title))
                {
                    var titleSize = gfx.MeasureString(title, titleFont);
                    titleHeight = titleSize.Height + 20; // padding below title

                    gfx.DrawString(
                        title,
                        titleFont,
                        XBrushes.Black,
                        new XRect(0, 10, page.Width, titleSize.Height),
                        XStringFormats.TopCenter);
                }

                // Available area for the image, shifted down by titleHeight
                double availableWidth = page.Width;
                double availableHeight = page.Height - titleHeight;

                if (fitToPage)
                {
                    double imgAspect = (double)xImage.PixelWidth / xImage.PixelHeight;
                    double areaAspect = availableWidth / availableHeight;

                    double drawWidth, drawHeight;
                    if (imgAspect > areaAspect)
                    {
                        drawWidth = availableWidth;
                        drawHeight = availableWidth / imgAspect;
                    }
                    else
                    {
                        drawHeight = availableHeight;
                        drawWidth = availableHeight * imgAspect;
                    }

                    double x = (page.Width - drawWidth) / 2;
                    double y = titleHeight + (availableHeight - drawHeight) / 2;
                    gfx.DrawImage(xImage, x, y, drawWidth, drawHeight);
                }
                else
                {
                    gfx.DrawImage(xImage, 0, titleHeight, availableWidth, availableHeight);
                }
            }

            document.Save(outputPdf);
        }
    }
}
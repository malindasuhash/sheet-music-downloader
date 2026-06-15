// Program.cs
// Requires NuGet packages:
//   dotnet add package SixLabors.ImageSharp
//   dotnet add package PdfSharpCore

using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Text.RegularExpressions;

namespace CreateFile
{
    class Program
    {
        // Page dimensions (A4 at ~300 DPI by default — adjust as needed)
        const int PageWidth = 2480;
        const int PageHeight = 3508;
        const int Margin = 100;
        const int VerticalSpacing = 80;
        const float ScaleFactor = 1f;

        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("usage: CreateFile <inputfolder> <outputpdf> <Title>");
                Console.WriteLine("e.g.:  CreateFile C:\\5f8f9d732900015ab \"chasing-cars.pdf\" \"Chasing Cars by Snow Patrol\"");
                return;
            }

            string inputFolder = args[0];
            string outputPdf = args[1];
            string title = args[2];

            var imageFiles = Directory.GetFiles(inputFolder)
                .Where(f => new[] { ".png", ".jpg", ".jpeg", ".bmp" }
                    .Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(fs => new OrderedFile { FileName = fs, Order = Convert.ToInt32(Regex.Matches(fs, "image_(\\d+)\\.png$")[0].Groups[1].Value) })
                .OrderBy(f => f.Order)
                .ToList(); // file name = image_1.png, image_2.png, etc.

            if (imageFiles.Count == 0)
            {
                Console.WriteLine("No images found in input folder.");
                return;
            }

            Console.WriteLine($"Found {imageFiles.Count} images.");

            StitchVerticallyAndPaginate(imageFiles, outputPdf, title);
        }

        /// <summary>
        /// Lays out images vertically on pages of fixed size, starting a new page
        /// when the current one would overflow. Each page is rendered as a temp PNG,
        /// then all pages are combined into a single PDF.
        /// </summary>
        static void StitchVerticallyAndPaginate(List<OrderedFile> imageFiles, string outputPdf, string title)
        {
            var pages = new List<Image<Rgba32>>();
            var current = NewPage();
            int y = Margin;
            int x = Margin;
            int totalWidth = 0;
            int totalHeight = 0;

            // This is to scale down images if they are too large for the page. Adjust as needed.
            foreach (var file in imageFiles)
            {
                Console.WriteLine($"Processing {Path.GetFileName(file.FileName)}...");
                using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(file.FileName);

                // Scale image to fit page width if it's too wide

                int newWidth = (int)(img.Width * ScaleFactor);
                int newHeight = (int)(img.Height * ScaleFactor);

                if (totalWidth + newWidth > PageWidth)
                {
                    x = Margin;
                    y += newHeight + VerticalSpacing;
                    totalHeight += newHeight + VerticalSpacing;
                    totalWidth = 0;
                }

                if (totalHeight + newHeight + VerticalSpacing > PageHeight)
                {
                    pages.Add(current);
                    current = NewPage();
                    x = Margin;
                    y = Margin;
                    totalWidth = 0;
                    totalHeight = 0;
                }

                totalWidth += newWidth + VerticalSpacing;

                using var resized = img.Clone(ctx => ctx.Resize(newWidth, newHeight));
                current.Mutate(ctx => ctx.DrawImage(resized, new SixLabors.ImageSharp.Point(x, y), 1f));

                x += newWidth;
                Console.WriteLine($"Placed {Path.GetFileName(file.FileName)} at x = {x}, y={y}, size {newWidth}x{newHeight}");
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

            CreatePdfFromImages(tempFiles, outputPdf, fitToPage: false, title);

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
        static void CreatePdfFromImages(List<string> imagePaths, string outputPdf, bool fitToPage, string title)
        {
            using var document = new PdfDocument();

            var titleFont = new XFont("Arial", 15, XFontStyle.Bold);

            bool titleAdded = false;

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
                if (!string.IsNullOrEmpty(title) && titleAdded == false)
                {
                    var titleSize = gfx.MeasureString(title, titleFont);
                    titleHeight = titleSize.Height + 20; // padding below title

                    gfx.DrawString(
                        title,
                        titleFont,
                        XBrushes.Black,
                        new XRect(0, 10, page.Width, titleSize.Height),
                        XStringFormats.TopCenter);

                    titleAdded = true;
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
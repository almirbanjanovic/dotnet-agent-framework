using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

var scriptDir = AppContext.BaseDirectory;
// Navigate up from bin/Debug/net9.0 to the generate-pdfs folder, then up to contoso-sharepoint
var sharepointDir = Path.GetFullPath(Path.Combine(scriptDir, "..", "..", "..", ".."));

Console.WriteLine($"SharePoint directory: {sharepointDir}");

var txtFiles = Directory.GetFiles(sharepointDir, "*.txt", SearchOption.AllDirectories)
    .Where(f => !f.Contains(Path.Combine("generate-pdfs", ""), StringComparison.OrdinalIgnoreCase))
    .ToArray();

if (txtFiles.Length == 0)
{
    Console.WriteLine("No .txt files found.");
    return;
}

Console.WriteLine($"Found {txtFiles.Length} text files to convert.\n");

foreach (var txtFile in txtFiles)
{
    var content = File.ReadAllText(txtFile);
    var pdfFile = Path.ChangeExtension(txtFile, ".pdf");
    var relativePath = Path.GetRelativePath(sharepointDir, pdfFile);

    var lines = content.Split('\n');
    var title = lines.Length > 0 ? lines[0].Trim() : Path.GetFileNameWithoutExtension(txtFile);
    var subtitle = lines.Length > 1 ? lines[1].Trim() : "";
    var bodyStartLine = string.IsNullOrWhiteSpace(subtitle) ? 1 : 2;
    var body = string.Join('\n', lines.Skip(bodyStartLine)).Trim();

    var document = new PdfDocument();
    document.Info.Title = title;
    document.Info.Author = "Contoso Outdoors";

    var titleFont = new XFont("Arial", 16, XFontStyle.Bold);
    var subtitleFont = new XFont("Arial", 9, XFontStyle.Italic);
    var bodyFont = new XFont("Arial", 10, XFontStyle.Regular);
    var footerFont = new XFont("Arial", 7, XFontStyle.Italic);

    double marginX = 50;
    double marginTop = 50;
    double marginBottom = 40;
    double lineHeight = 14;

    // Split body into paragraphs, then render across pages
    var paragraphs = body.Split('\n');
    int paragraphIndex = 0;
    int pageNumber = 0;
    bool titleDrawn = false;

    while (paragraphIndex < paragraphs.Length)
    {
        var page = document.AddPage();
        page.Size = PdfSharpCore.PageSize.Letter;
        var gfx = XGraphics.FromPdfPage(page);
        double y = marginTop;
        double pageWidth = page.Width.Point;
        double contentWidth = pageWidth - 2 * marginX;
        double pageBottom = page.Height.Point - marginBottom;
        pageNumber++;

        if (!titleDrawn)
        {
            gfx.DrawString(title, titleFont, XBrushes.DarkBlue, new XRect(marginX, y, contentWidth, 24), XStringFormats.TopLeft);
            y += 26;

            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                gfx.DrawString(subtitle, subtitleFont, XBrushes.Gray, new XRect(marginX, y, contentWidth, 14), XStringFormats.TopLeft);
                y += 16;
            }

            // Separator line
            gfx.DrawLine(XPens.LightGray, marginX, y, pageWidth - marginX, y);
            y += 12;
            titleDrawn = true;
        }

        while (paragraphIndex < paragraphs.Length)
        {
            var para = paragraphs[paragraphIndex].TrimEnd();

            if (string.IsNullOrWhiteSpace(para))
            {
                y += lineHeight * 0.5;
                paragraphIndex++;
                continue;
            }

            // Estimate how many lines this paragraph needs (rough word wrap)
            var estimatedCharsPerLine = (int)(contentWidth / 5.5);
            var wrappedLines = (int)Math.Ceiling((double)para.Length / Math.Max(estimatedCharsPerLine, 1));
            var blockHeight = wrappedLines * lineHeight;

            if (y + blockHeight > pageBottom)
                break; // Need a new page

            // Use XTextFormatter for word wrap
            var tf = new PdfSharpCore.Drawing.Layout.XTextFormatter(gfx);
            tf.DrawString(para, bodyFont, XBrushes.Black, new XRect(marginX, y, contentWidth, blockHeight + lineHeight));
            y += blockHeight + 2;
            paragraphIndex++;
        }

        // Footer
        var footer = $"Contoso Outdoors — Confidential    |    Page {pageNumber}";
        gfx.DrawString(footer, footerFont, XBrushes.Gray,
            new XRect(0, page.Height.Point - 25, pageWidth, 14), XStringFormats.TopCenter);
    }

    document.Save(pdfFile);
    Console.WriteLine($"  ✓ {relativePath}");
}

Console.WriteLine($"\nDone. Generated {txtFiles.Length} PDF files.");

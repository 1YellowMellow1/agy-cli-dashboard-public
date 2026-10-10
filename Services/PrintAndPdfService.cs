using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgyCliDashboard.Services;

/// <summary>
/// Service für das Drucken von reinem Text auf Windows-Druckern sowie die direkte
/// Erzeugung von standardkonformen, mehrseitigen A4-PDF-Dateien (ohne externe Abhängigkeiten).
/// </summary>
public static class PrintAndPdfService
{
    private static readonly Encoding WinAnsi;

    static PrintAndPdfService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        WinAnsi = Encoding.GetEncoding(1252);
    }

    /// <summary>
    /// Ermittelt synchron und in Mikrosekunden alle auf dem System installierten Drucker direkt aus der Windows-Registry.
    /// </summary>
    public static List<string> GetInstalledPrinters()
    {
        var printers = new List<string>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Devices");
            if (key != null)
            {
                foreach (var valName in key.GetValueNames())
                {
                    if (!string.IsNullOrWhiteSpace(valName) && !printers.Contains(valName, StringComparer.OrdinalIgnoreCase))
                    {
                        printers.Add(valName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[PrintAndPdfService ERROR] GetInstalledPrinters Registry: {ex.Message}");
        }

        if (printers.Count == 0)
        {
            printers.Add("Microsoft Print to PDF");
        }

        return printers;
    }

    /// <summary>
    /// Ermittelt synchron und in Mikrosekunden den Standarddrucker direkt aus der Windows-Registry.
    /// </summary>
    public static string GetDefaultPrinter()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Windows");
            if (key != null)
            {
                string? device = key.GetValue("Device") as string;
                if (!string.IsNullOrWhiteSpace(device))
                {
                    string name = device.Split(',')[0].Trim();
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[PrintAndPdfService ERROR] GetDefaultPrinter Registry: {ex.Message}");
        }

        return "Microsoft Print to PDF";
    }

    /// <summary>
    /// Sendet den Text mit professionellem WordPad-Dokumenten-Layout (Segoe UI, A4-Ränder, Word-Wrapping, Kopf- und Fußzeile)
    /// über die Windows GDI+ PrintDocument-Engine an den angegebenen Windows-Drucker.
    /// </summary>
    public static async Task<bool> PrintTextAsync(string plainText, string printerName, int copies = 1, string documentTitle = "Antigravity CLI Dokument")
    {
        if (string.IsNullOrWhiteSpace(plainText)) return false;

        // Normalisiere alle Zeilenumbrüche garantiert auf Windows CRLF
        string normalizedText = plainText.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

        string tempTextFile = Path.Combine(Path.GetTempPath(), $"agy_print_data_{Guid.NewGuid():N}.txt");
        string tempScriptFile = Path.Combine(Path.GetTempPath(), $"agy_print_job_{Guid.NewGuid():N}.ps1");

        try
        {
            await File.WriteAllTextAsync(tempTextFile, normalizedText, Encoding.UTF8);

            int safeCopies = Math.Clamp(copies, 1, 10);
            string cleanTitle = (documentTitle ?? "Dokument").Trim();
            cleanTitle = System.Text.RegularExpressions.Regex.Replace(cleanTitle, @"[\r\n\t]+", " ");
            if (cleanTitle.Length > 60) cleanTitle = cleanTitle.Substring(0, 57) + "...";

            string escapedPrinter = printerName.Replace("'", "''");
            string escapedTextPath = tempTextFile.Replace("'", "''");
            string escapedTitle = cleanTitle.Replace("'", "''");

            string psScript = $@"
Add-Type -AssemblyName System.Drawing

$textFile = '{escapedTextPath}'
$printerName = '{escapedPrinter}'
$safeCopies = {safeCopies}
$docTitle = '{escapedTitle}'

if (-not (Test-Path $textFile)) {{ exit 1 }}
$content = [System.IO.File]::ReadAllText($textFile, [System.Text.Encoding]::UTF8)

$doc = New-Object System.Drawing.Printing.PrintDocument
$doc.PrintController = New-Object System.Drawing.Printing.StandardPrintController
$doc.PrinterSettings.PrinterName = $printerName
$doc.PrinterSettings.Copies = $safeCopies
$doc.DocumentName = $docTitle

$fontBody = New-Object System.Drawing.Font('Segoe UI', 10.5, [System.Drawing.FontStyle]::Regular)
$fontBold = New-Object System.Drawing.Font('Segoe UI', 10.5, [System.Drawing.FontStyle]::Bold)
$fontHeader = New-Object System.Drawing.Font('Segoe UI', 9.0, [System.Drawing.FontStyle]::Regular)
$brushText = [System.Drawing.Brushes]::Black
$brushMuted = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(100, 116, 139))
$penLine = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(203, 213, 225), 0.75)

# Zeilen mit sauberem Word-Wrapping vorbereiten
$rawLines = $content -split ""`r?`n""
$renderLines = New-Object System.Collections.Generic.List[PSObject]

$bmp = New-Object System.Drawing.Bitmap 10, 10
$gMeasure = [System.Drawing.Graphics]::FromImage($bmp)
$printableWidth = 650.0

foreach ($rawLine in $rawLines) {{
    if ([string]::IsNullOrWhiteSpace($rawLine)) {{
        $renderLines.Add([PSCustomObject]@{{ Text = """"; IsBold = $false; Indent = 0.0; IsEmpty = $true }})
        continue
    }}

    $trimmed = $rawLine.TrimStart()
    $isBullet = $trimmed.StartsWith(""• "") -or $trimmed.StartsWith(""- "") -or $trimmed.StartsWith(""* "")
    $indent = if ($isBullet) {{ 16.0 }} else {{ 0.0 }}
    $effectiveWidth = $printableWidth - $indent

    $words = $rawLine -split ""\s+""
    $cur = """"
    foreach ($w in $words) {{
        $candidate = if ($cur -eq """") {{ $w }} else {{ ""$cur $w"" }}
        $sz = $gMeasure.MeasureString($candidate, $fontBody)
        if ($sz.Width -gt $effectiveWidth -and $cur -ne """") {{
            $renderLines.Add([PSCustomObject]@{{ Text = $cur; IsBold = $false; Indent = $indent; IsEmpty = $false }})
            $cur = $w
        }} else {{
            $cur = $candidate
        }}
    }}
    if ($cur -ne """") {{
        $renderLines.Add([PSCustomObject]@{{ Text = $cur; IsBold = $false; Indent = $indent; IsEmpty = $false }})
    }}
}}
$gMeasure.Dispose()
$bmp.Dispose()

$script:lineIdx = 0
$script:pageNum = 1

$doc.add_PrintPage({{
    param($sender, $e)
    $left = 60.0
    $right = $e.PageBounds.Width - 60.0
    $top = 65.0
    $bottom = $e.PageBounds.Height - 65.0

    # Kopfzeile (Dokumententitel & feine Trennlinie)
    $e.Graphics.DrawString($docTitle, $fontHeader, $brushMuted, $left, ($top - 26.0))
    $e.Graphics.DrawLine($penLine, $left, ($top - 10.0), $right, ($top - 10.0))

    $y = $top
    $lineH = $fontBody.GetHeight($e.Graphics) * 1.35

    while ($script:lineIdx -lt $renderLines.Count) {{
        $item = $renderLines[$script:lineIdx]
        $advance = if ($item.IsEmpty) {{ $lineH * 0.7 }} else {{ $lineH }}

        if ($y + $advance -gt $bottom) {{
            $e.HasMorePages = $true
            $script:pageNum++
            return
        }}

        $script:lineIdx++
        if (-not $item.IsEmpty) {{
            $curFont = if ($item.IsBold) {{ $fontBold }} else {{ $fontBody }}
            $e.Graphics.DrawString($item.Text, $curFont, $brushText, ($left + $item.Indent), $y)
        }}
        $y += $advance
    }}

    # Fußzeile (Seitenzahl)
    $pageStr = ""Seite "" + $script:pageNum
    $e.Graphics.DrawString($pageStr, $fontHeader, $brushMuted, $left, ($bottom + 10.0))
    $e.HasMorePages = $false
}})

$doc.Print()
$doc.Dispose()
$fontBody.Dispose()
$fontBold.Dispose()
$fontHeader.Dispose()
$brushMuted.Dispose()
$penLine.Dispose()
";

            await File.WriteAllTextAsync(tempScriptFile, psScript, Encoding.UTF8);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -NonInteractive -File \"{tempScriptFile}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                return proc.ExitCode == 0;
            }
            return false;
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[PrintAndPdfService ERROR] PrintTextAsync: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (File.Exists(tempTextFile)) File.Delete(tempTextFile); } catch { }
            try { if (File.Exists(tempScriptFile)) File.Delete(tempScriptFile); } catch { }
        }
    }

    /// <summary>
    /// Erzeugt ein standardkonformes mehrseitiges A4-PDF-Dokument aus dem übergebenen reinen Text.
    /// </summary>
    public static byte[] GeneratePdf(string plainText, string documentTitle = "Antigravity CLI Dokument")
    {
        var rawLines = (plainText ?? "").Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var wrappedLines = new List<string>();

        const int maxCharsPerLine = 84;
        foreach (var rawLine in rawLines)
        {
            if (rawLine.Length <= maxCharsPerLine)
            {
                wrappedLines.Add(rawLine);
            }
            else
            {
                // Wort-basiertes Umbrechen langer Zeilen
                string current = rawLine;
                while (current.Length > maxCharsPerLine)
                {
                    int splitIdx = current.LastIndexOf(' ', maxCharsPerLine);
                    if (splitIdx <= 0) splitIdx = maxCharsPerLine; // Kein Leerzeichen: harter Umbruch

                    wrappedLines.Add(current.Substring(0, splitIdx).TrimEnd());
                    current = current.Substring(splitIdx).TrimStart();
                }
                if (current.Length > 0)
                {
                    wrappedLines.Add(current);
                }
            }
        }

        if (wrappedLines.Count == 0)
        {
            wrappedLines.Add(string.Empty);
        }

        const int linesPerPage = 48;
        int totalPages = Math.Max(1, (int)Math.Ceiling(wrappedLines.Count / (double)linesPerPage));
        string cleanTitle = (documentTitle ?? "Dokument").Trim();
        cleanTitle = System.Text.RegularExpressions.Regex.Replace(cleanTitle, @"[\r\n\t]+", " ");
        if (cleanTitle.Length > 60) cleanTitle = cleanTitle.Substring(0, 57) + "...";
        string printDate = DateTime.Now.ToString("dd.MM.yyyy HH:mm");

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // 1. PDF-Header mit Standard-Binärmarker (Bytes > 127 zur Erkennung binärer PDF-Streams)
        writer.Write(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A });

        var objectOffsets = new List<long>();

        // Obj 1: Catalog
        objectOffsets.Add(ms.Position);
        writer.Write(Encoding.ASCII.GetBytes("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"));

        // Obj 2: Pages (Platzhalter-Referenzen werden unten gebildet)
        long pagesObjPos = ms.Position;
        objectOffsets.Add(pagesObjPos);

        var pageObjNums = new List<int>();
        var streamObjNums = new List<int>();
        int currentObjNum = 5;
        for (int p = 0; p < totalPages; p++)
        {
            pageObjNums.Add(currentObjNum++);
            streamObjNums.Add(currentObjNum++);
        }

        string kidsStr = string.Join(" ", pageObjNums.Select(n => $"{n} 0 R"));
        writer.Write(Encoding.ASCII.GetBytes($"2 0 obj\n<< /Type /Pages /Kids [{kidsStr}] /Count {totalPages} >>\nendobj\n"));

        // Obj 3: Font Regular (Helvetica mit WinAnsi)
        objectOffsets.Add(ms.Position);
        writer.Write(Encoding.ASCII.GetBytes("3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n"));

        // Obj 4: Font Bold (Helvetica-Bold mit WinAnsi)
        objectOffsets.Add(ms.Position);
        writer.Write(Encoding.ASCII.GetBytes("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n"));

        // Pages und Streams rendern
        for (int p = 0; p < totalPages; p++)
        {
            int pageObjNum = pageObjNums[p];
            int streamObjNum = streamObjNums[p];

            // Page Object
            objectOffsets.Add(ms.Position);
            string pageDict = $"{pageObjNum} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {streamObjNum} 0 R >>\nendobj\n";
            writer.Write(Encoding.ASCII.GetBytes(pageDict));

            // Stream Inhalt vorbereiten
            var streamBytes = new List<byte>();

            // Header-Linie & Titel
            streamBytes.AddRange(Encoding.ASCII.GetBytes("0.7 w\n0.3 0.3 0.3 RG\n50 802 m 545 802 l S\n")); // Trennlinie oben
            streamBytes.AddRange(Encoding.ASCII.GetBytes("BT\n/F2 10 Tf\n0.1 0.1 0.1 rg\n50 808 Td\n("));
            AppendEscapedWinAnsi(streamBytes, cleanTitle);
            streamBytes.AddRange(Encoding.ASCII.GetBytes(") Tj\nET\n"));

            // Text-Zeilen für diese Seite
            int startIdx = p * linesPerPage;
            int countOnPage = Math.Min(linesPerPage, wrappedLines.Count - startIdx);

            streamBytes.AddRange(Encoding.ASCII.GetBytes("BT\n/F1 10 Tf\n14 TL\n0.05 0.05 0.05 rg\n50 784 Td\n"));
            for (int i = 0; i < countOnPage; i++)
            {
                string lineText = wrappedLines[startIdx + i];
                if (i > 0)
                {
                    streamBytes.AddRange(Encoding.ASCII.GetBytes("T*\n("));
                }
                else
                {
                    streamBytes.AddRange(Encoding.ASCII.GetBytes("("));
                }
                AppendEscapedWinAnsi(streamBytes, lineText);
                streamBytes.AddRange(Encoding.ASCII.GetBytes(") Tj\n"));
            }
            streamBytes.AddRange(Encoding.ASCII.GetBytes("ET\n"));

            // Footer: Trennlinie + Datum links + "Seite X von Y" rechts
            streamBytes.AddRange(Encoding.ASCII.GetBytes("0.7 w\n0.3 0.3 0.3 RG\n50 48 m 545 48 l S\n"));
            streamBytes.AddRange(Encoding.ASCII.GetBytes("BT\n/F1 8 Tf\n0.4 0.4 0.4 rg\n50 36 Td\n("));
            AppendEscapedWinAnsi(streamBytes, printDate);
            streamBytes.AddRange(Encoding.ASCII.GetBytes(") Tj\nET\n"));

            string pageFooter = $"Seite {p + 1} von {totalPages}";
            streamBytes.AddRange(Encoding.ASCII.GetBytes($"BT\n/F1 8 Tf\n0.4 0.4 0.4 rg\n490 36 Td\n("));
            AppendEscapedWinAnsi(streamBytes, pageFooter);
            streamBytes.AddRange(Encoding.ASCII.GetBytes(") Tj\nET\n"));

            // Stream Object schreiben
            objectOffsets.Add(ms.Position);
            writer.Write(Encoding.ASCII.GetBytes($"{streamObjNum} 0 obj\n<< /Length {streamBytes.Count} >>\nstream\n"));
            writer.Write(streamBytes.ToArray());
            writer.Write(Encoding.ASCII.GetBytes("\nendstream\nendobj\n"));
        }

        // XRef Table
        long xrefPos = ms.Position;
        int totalObjects = objectOffsets.Count + 1;
        writer.Write(Encoding.ASCII.GetBytes($"xref\n0 {totalObjects}\n0000000000 65535 f \n"));
        foreach (var offset in objectOffsets)
        {
            writer.Write(Encoding.ASCII.GetBytes($"{offset:D10} 00000 n \n"));
        }

        // Trailer
        writer.Write(Encoding.ASCII.GetBytes($"trailer\n<< /Size {totalObjects} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n"));

        return ms.ToArray();
    }

    private static void AppendEscapedWinAnsi(List<byte> target, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        byte[] encoded = WinAnsi.GetBytes(text);
        foreach (byte b in encoded)
        {
            if (b == 0x28 || b == 0x29 || b == 0x5C) // '(', ')', '\'
            {
                target.Add(0x5C); // Escape mit '\'
            }
            target.Add(b);
        }
    }
}

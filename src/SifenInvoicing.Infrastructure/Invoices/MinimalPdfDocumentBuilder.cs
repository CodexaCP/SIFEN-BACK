using System.Text;

namespace SifenInvoicing.Infrastructure.Invoices;

internal static class MinimalPdfDocumentBuilder
{
    public static byte[] Build(string pageCommands, int width, int height)
    {
        var streamBytes = Encoding.ASCII.GetBytes(pageCommands);
        var objects = new List<string>
        {
            "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n",
            "2 0 obj << /Type /Pages /Count 1 /Kids [3 0 R] >> endobj\n",
            $"3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >> endobj\n",
            "4 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj\n",
            "5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >> endobj\n"
        };

        var header = "%PDF-1.4\n";
        var contentPrefix = $"6 0 obj << /Length {streamBytes.Length} >> stream\n";
        var contentSuffix = "\nendstream endobj\n";

        using var ms = new MemoryStream();
        var encoding = Encoding.ASCII;
        ms.Write(encoding.GetBytes(header));

        var offsets = new List<long> { 0 };
        foreach (var obj in objects)
        {
            offsets.Add(ms.Position);
            ms.Write(encoding.GetBytes(obj));
        }

        offsets.Add(ms.Position);
        ms.Write(encoding.GetBytes(contentPrefix));
        ms.Write(streamBytes);
        ms.Write(encoding.GetBytes(contentSuffix));

        var xrefPosition = ms.Position;
        ms.Write(encoding.GetBytes($"xref\n0 {offsets.Count}\n"));
        ms.Write(encoding.GetBytes("0000000000 65535 f \n"));
        foreach (var offset in offsets.Skip(1))
        {
            ms.Write(encoding.GetBytes($"{offset:0000000000} 00000 n \n"));
        }

        ms.Write(encoding.GetBytes($"trailer << /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefPosition}\n%%EOF"));
        return ms.ToArray();
    }
}

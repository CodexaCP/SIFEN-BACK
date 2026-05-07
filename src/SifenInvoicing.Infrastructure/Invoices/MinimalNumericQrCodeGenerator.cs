using SifenInvoicing.Domain.Common;

namespace SifenInvoicing.Infrastructure.Invoices;

internal static class MinimalNumericQrCodeGenerator
{
    private static readonly int[] Exp = BuildExpTable();
    private static readonly int[] Log = BuildLogTable(Exp);
    private static readonly int[] GeneratorPolynomialDegree10 = BuildGeneratorPolynomial(10);

    public static bool[,] GenerateVersion2L(string numericPayload)
    {
        if (string.IsNullOrWhiteSpace(numericPayload) || numericPayload.Any(ch => !char.IsAsciiDigit(ch)))
        {
            throw new DomainException("KuDE QR payload must be numeric.");
        }

        if (numericPayload.Length > 77)
        {
            throw new DomainException("KuDE QR payload exceeds QR capacity for version 2-L.");
        }

        var bits = BuildDataBits(numericPayload);
        var codewords = BitsToCodewords(bits);
        var ecCodewords = CalculateErrorCorrection(codewords, 10);
        var finalBits = CodewordsToBits(codewords.Concat(ecCodewords).ToArray());

        return BuildMatrix(finalBits);
    }

    private static List<bool> BuildDataBits(string payload)
    {
        var bits = new List<bool>();
        AppendBits(bits, 0b0001, 4);
        AppendBits(bits, payload.Length, 10);

        for (var index = 0; index < payload.Length; index += 3)
        {
            var chunkLength = Math.Min(3, payload.Length - index);
            var chunk = int.Parse(payload.Substring(index, chunkLength));
            AppendBits(bits, chunk, chunkLength switch
            {
                3 => 10,
                2 => 7,
                _ => 4
            });
        }

        var maxBits = 34 * 8;
        AppendBits(bits, 0, Math.Min(4, maxBits - bits.Count));
        while (bits.Count % 8 != 0)
        {
            bits.Add(false);
        }

        var data = BitsToCodewords(bits);
        var pads = new[] { 0xEC, 0x11 };
        for (var i = 0; data.Count < 34; i++)
        {
            data.Add(pads[i % 2]);
        }

        return CodewordsToBits(data.ToArray());
    }

    private static List<int> BitsToCodewords(List<bool> bits)
    {
        var codewords = new List<int>();
        for (var i = 0; i < bits.Count; i += 8)
        {
            var value = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value << 1) | (bits[i + bit] ? 1 : 0);
            }

            codewords.Add(value);
        }

        return codewords;
    }

    private static List<bool> CodewordsToBits(int[] codewords)
    {
        var bits = new List<bool>(codewords.Length * 8);
        foreach (var codeword in codewords)
        {
            AppendBits(bits, codeword, 8);
        }

        return bits;
    }

    private static int[] CalculateErrorCorrection(List<int> dataCodewords, int ecCount)
    {
        var message = new int[dataCodewords.Count + ecCount];
        for (var i = 0; i < dataCodewords.Count; i++)
        {
            message[i] = dataCodewords[i];
        }

        for (var i = 0; i < dataCodewords.Count; i++)
        {
            var factor = message[i];
            if (factor == 0)
            {
                continue;
            }

            for (var j = 0; j < GeneratorPolynomialDegree10.Length; j++)
            {
                message[i + j] ^= GfMultiply(GeneratorPolynomialDegree10[j], factor);
            }
        }

        return message[^ecCount..];
    }

    private static bool[,] BuildMatrix(List<bool> dataBits)
    {
        const int size = 25;
        var modules = new bool[size, size];
        var reserved = new bool[size, size];

        PlaceFinder(modules, reserved, 0, 0);
        PlaceFinder(modules, reserved, size - 7, 0);
        PlaceFinder(modules, reserved, 0, size - 7);
        PlaceTiming(modules, reserved);
        PlaceAlignment(modules, reserved, 18, 18);
        ReserveFormatAreas(reserved);
        modules[17, 8] = true;
        reserved[17, 8] = true;

        PlaceData(modules, reserved, dataBits);
        ApplyMask0(modules, reserved);
        PlaceFormatInfo(modules, reserved);
        return modules;
    }

    private static void PlaceFinder(bool[,] modules, bool[,] reserved, int startX, int startY)
    {
        for (var y = -1; y <= 7; y++)
        {
            for (var x = -1; x <= 7; x++)
            {
                var row = startY + y;
                var col = startX + x;
                if (row < 0 || row >= 25 || col < 0 || col >= 25)
                {
                    continue;
                }

                var isBorder = x is -1 or 7 || y is -1 or 7;
                var isOuter = x is 0 or 6 || y is 0 or 6;
                var isInner = x is >= 2 and <= 4 && y is >= 2 and <= 4;
                modules[row, col] = !isBorder && (isOuter || isInner);
                reserved[row, col] = true;
            }
        }
    }

    private static void PlaceTiming(bool[,] modules, bool[,] reserved)
    {
        for (var i = 8; i < 17; i++)
        {
            modules[6, i] = i % 2 == 0;
            modules[i, 6] = i % 2 == 0;
            reserved[6, i] = true;
            reserved[i, 6] = true;
        }
    }

    private static void PlaceAlignment(bool[,] modules, bool[,] reserved, int centerX, int centerY)
    {
        for (var y = -2; y <= 2; y++)
        {
            for (var x = -2; x <= 2; x++)
            {
                var row = centerY + y;
                var col = centerX + x;
                if (reserved[row, col])
                {
                    continue;
                }

                var absX = Math.Abs(x);
                var absY = Math.Abs(y);
                modules[row, col] = absX == 2 || absY == 2 || (absX == 0 && absY == 0);
                reserved[row, col] = true;
            }
        }
    }

    private static void ReserveFormatAreas(bool[,] reserved)
    {
        for (var i = 0; i < 9; i++)
        {
            if (i != 6)
            {
                reserved[8, i] = true;
                reserved[i, 8] = true;
            }
        }

        for (var i = 0; i < 8; i++)
        {
            reserved[24 - i, 8] = true;
            reserved[8, 24 - i] = true;
        }
    }

    private static void PlaceData(bool[,] modules, bool[,] reserved, List<bool> bits)
    {
        var bitIndex = 0;
        var upward = true;

        for (var col = 24; col > 0; col -= 2)
        {
            if (col == 6)
            {
                col--;
            }

            for (var offset = 0; offset < 25; offset++)
            {
                var row = upward ? 24 - offset : offset;
                for (var delta = 0; delta < 2; delta++)
                {
                    var currentCol = col - delta;
                    if (reserved[row, currentCol])
                    {
                        continue;
                    }

                    modules[row, currentCol] = bitIndex < bits.Count && bits[bitIndex++];
                }
            }

            upward = !upward;
        }
    }

    private static void ApplyMask0(bool[,] modules, bool[,] reserved)
    {
        for (var row = 0; row < 25; row++)
        {
            for (var col = 0; col < 25; col++)
            {
                if (!reserved[row, col] && (row + col) % 2 == 0)
                {
                    modules[row, col] = !modules[row, col];
                }
            }
        }
    }

    private static void PlaceFormatInfo(bool[,] modules, bool[,] reserved)
    {
        const int format = 0b111011111000100;
        var bits = new bool[15];
        for (var i = 0; i < 15; i++)
        {
            bits[i] = ((format >> i) & 1) == 1;
        }

        var positionsA = new (int Row, int Col)[] { (8,0),(8,1),(8,2),(8,3),(8,4),(8,5),(8,7),(8,8),(7,8),(5,8),(4,8),(3,8),(2,8),(1,8),(0,8) };
        var positionsB = new (int Row, int Col)[] { (24,8),(23,8),(22,8),(21,8),(20,8),(19,8),(18,8),(17,8),(8,24),(8,23),(8,22),(8,21),(8,20),(8,19),(8,18) };

        for (var i = 0; i < 15; i++)
        {
            modules[positionsA[i].Row, positionsA[i].Col] = bits[i];
            modules[positionsB[i].Row, positionsB[i].Col] = bits[i];
            reserved[positionsA[i].Row, positionsA[i].Col] = true;
            reserved[positionsB[i].Row, positionsB[i].Col] = true;
        }
    }

    private static void AppendBits(List<bool> bits, int value, int length)
    {
        for (var i = length - 1; i >= 0; i--)
        {
            bits.Add(((value >> i) & 1) == 1);
        }
    }

    private static int[] BuildGeneratorPolynomial(int degree)
    {
        var poly = new List<int> { 1 };
        for (var i = 0; i < degree; i++)
        {
            var next = new int[poly.Count + 1];
            for (var j = 0; j < poly.Count; j++)
            {
                next[j] ^= poly[j];
                next[j + 1] ^= GfMultiply(poly[j], Exp[i]);
            }

            poly = next.ToList();
        }

        return poly.ToArray();
    }

    private static int[] BuildExpTable()
    {
        var exp = new int[512];
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            exp[i] = x;
            x <<= 1;
            if (x >= 256)
            {
                x ^= 0x11D;
            }
        }

        for (var i = 255; i < 512; i++)
        {
            exp[i] = exp[i - 255];
        }

        return exp;
    }

    private static int[] BuildLogTable(int[] exp)
    {
        var log = new int[256];
        for (var i = 0; i < 255; i++)
        {
            log[exp[i]] = i;
        }

        return log;
    }

    private static int GfMultiply(int a, int b)
    {
        if (a == 0 || b == 0)
        {
            return 0;
        }

        return Exp[Log[a] + Log[b]];
    }
}

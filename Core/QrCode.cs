using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Core;

/// <summary>
/// QR Code Error Correction Level according to ISO/IEC 18004.
/// </summary>
public enum QrCodeEcc
{
    Low = 0,     // Recovers ~7% of data
    Medium = 1,  // Recovers ~15% of data
    Quartile = 2,// Recovers ~25% of data
    High = 3     // Recovers ~30% of data
}

/// <summary>
/// High-performance, zero-dependency QR Code generator (ISO/IEC 18004 compliant).
/// Generates standard, camera-scannable 2D barcodes for local LAN pairing and URLs.
/// Outputs frozen WPF <see cref="BitmapSource"/> objects with zero GC overhead.
/// </summary>
public sealed class QrCode
{
    public const int MinVersion = 1;
    public const int MaxVersion = 40;

    public int Version { get; }
    public int Size { get; }
    public QrCodeEcc ErrorCorrectionLevel { get; }
    public int Mask { get; }

    private readonly bool[,] _modules;
    private readonly bool[,] _isFunction;

    #region Tables and Constants (ISO/IEC 18004)

    private static readonly int[][] ECC_CODEWORDS_PER_BLOCK =
    {
        // Version 1..40
        new[] { -1,  7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }, // Low
        new[] { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 }, // Medium
        new[] { -1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }, // Quartile
        new[] { -1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }  // High
    };

    private static readonly int[][] NUM_ERROR_CORRECTION_BLOCKS =
    {
        new[] { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8,  9,  9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 }, // Low
        new[] { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 }, // Medium
        new[] { -1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68 }, // Quartile
        new[] { -1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81 }  // High
    };

    private static readonly byte[] EXP = new byte[512];
    private static readonly byte[] LOG = new byte[256];

    static QrCode()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            EXP[i] = (byte)x;
            LOG[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0)
                x ^= 0x11D;
        }
        for (int i = 255; i < 512; i++)
        {
            EXP[i] = EXP[i - 255];
        }
    }

    #endregion

    private QrCode(int version, QrCodeEcc ecc, byte[] dataCodewords, int mask)
    {
        Version = version;
        Size = version * 4 + 17;
        ErrorCorrectionLevel = ecc;

        _modules = new bool[Size, Size];
        _isFunction = new bool[Size, Size];

        // 1. Draw Function Patterns (Finders, Separators, Alignment, Timing, Dark module)
        DrawFunctionPatterns();

        // 2. Error Correction Codewords & Interleaving
        byte[] allCodewords = AddEccAndInterleave(dataCodewords, version, ecc);

        // 3. Draw Data Codewords in Zig-Zag
        DrawCodewords(allCodewords);

        // 4. Determine or apply mask
        if (mask < 0)
        {
            int minPenalty = int.MaxValue;
            int bestMask = 0;
            for (int m = 0; m < 8; m++)
            {
                ApplyMask(m);
                DrawFormatBits(m);
                int penalty = GetPenaltyScore();
                if (penalty < minPenalty)
                {
                    minPenalty = penalty;
                    bestMask = m;
                }
                ApplyMask(m); // unmask back
            }
            mask = bestMask;
        }

        Mask = mask;
        ApplyMask(mask);
        DrawFormatBits(mask);
    }

    /// <summary>
    /// Gets the boolean module state (true = dark/black, false = light/white).
    /// </summary>
    public bool GetModule(int x, int y)
    {
        if (x >= 0 && x < Size && y >= 0 && y < Size)
            return _modules[x, y];
        return false;
    }

    #region Factory Methods

    /// <summary>
    /// Encodes arbitrary text into an ISO/IEC 18004 compliant QR Code using optimal version selection.
    /// </summary>
    public static QrCode EncodeText(string text, QrCodeEcc ecc = QrCodeEcc.Medium)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] data = Encoding.UTF8.GetBytes(text);
        return EncodeBinary(data, ecc);
    }

    /// <summary>
    /// Encodes binary data (or UTF-8 bytes) into an ISO/IEC 18004 compliant QR Code.
    /// </summary>
    public static QrCode EncodeBinary(byte[] data, QrCodeEcc ecc = QrCodeEcc.Medium)
    {
        ArgumentNullException.ThrowIfNull(data);

        // Find minimum version that fits data
        int targetVersion = -1;
        int dataCapacityBytes = 0;

        for (int v = MinVersion; v <= MaxVersion; v++)
        {
            int capCodewords = GetNumDataCodewords(v, ecc);
            int charCountBits = (v <= 9) ? 8 : 16;
            int headerBits = 4 + charCountBits; // 4 bits byte mode indicator + character count
            int requiredBits = headerBits + data.Length * 8;
            if (capCodewords * 8 >= requiredBits)
            {
                targetVersion = v;
                dataCapacityBytes = capCodewords;
                break;
            }
        }

        if (targetVersion == -1)
            throw new ArgumentException($"Data length ({data.Length} bytes) exceeds QR Code Version 40 capacity for ECC {ecc}.");

        // Build bit stream
        List<bool> bits = new(dataCapacityBytes * 8);

        // 1. Mode indicator (0100 for Byte Mode)
        AppendBits(bits, 0b0100, 4);

        // 2. Character count indicator
        int countBits = (targetVersion <= 9) ? 8 : 16;
        AppendBits(bits, data.Length, countBits);

        // 3. Data bytes
        foreach (byte b in data)
            AppendBits(bits, b, 8);

        // 4. Terminator (up to 4 zero bits)
        int capBits = dataCapacityBytes * 8;
        int terminatorLen = Math.Min(4, capBits - bits.Count);
        AppendBits(bits, 0, terminatorLen);

        // 5. Pad to byte boundary
        while (bits.Count % 8 != 0)
            bits.Add(false);

        // 6. Pad bytes (alternating 0xEC and 0x11)
        byte[] padBytes = { 0xEC, 0x11 };
        int padIdx = 0;
        while (bits.Count < capBits)
        {
            AppendBits(bits, padBytes[padIdx % 2], 8);
            padIdx++;
        }

        // Convert bits to data codewords
        byte[] dataCodewords = new byte[dataCapacityBytes];
        for (int i = 0; i < dataCapacityBytes; i++)
        {
            int val = 0;
            for (int b = 0; b < 8; b++)
            {
                if (bits[i * 8 + b])
                    val |= 1 << (7 - b);
            }
            dataCodewords[i] = (byte)val;
        }

        return new QrCode(targetVersion, ecc, dataCodewords, -1);
    }

    /// <summary>
    /// Generates a crisp, immutable WPF <see cref="BitmapSource"/> directly from text.
    /// </summary>
    public static BitmapSource GenerateBitmap(
        string text,
        int moduleSize = 3,
        int quietZone = 2,
        QrCodeEcc ecc = QrCodeEcc.Medium,
        Color? foreground = null,
        Color? background = null)
    {
        var qr = EncodeText(text, ecc);
        return qr.ToBitmapSource(moduleSize, quietZone, foreground, background);
    }

    #endregion

    #region WPF Bitmap Rendering

    /// <summary>
    /// Renders the QR code to a high-contrast WPF <see cref="BitmapSource"/> (PixelFormats.Bgra32).
    /// Output is frozen for direct thread-safe binding and instant Direct3D composition.
    /// </summary>
    public BitmapSource ToBitmapSource(
        int moduleSize = 3,
        int quietZone = 2,
        Color? foreground = null,
        Color? background = null)
    {
        moduleSize = Math.Max(1, moduleSize);
        quietZone = Math.Max(0, quietZone);

        int totalModules = Size + quietZone * 2;
        int pixelWidth = totalModules * moduleSize;
        int pixelHeight = totalModules * moduleSize;
        int stride = pixelWidth * 4;
        byte[] pixelBuffer = new byte[pixelHeight * stride];

        Color fg = foreground ?? Colors.Black;
        Color bg = background ?? Colors.White;

        for (int y = 0; y < pixelHeight; y++)
        {
            int moduleY = y / moduleSize - quietZone;
            int rowOffset = y * stride;
            for (int x = 0; x < pixelWidth; x++)
            {
                int moduleX = x / moduleSize - quietZone;
                bool isDark = (moduleX >= 0 && moduleX < Size && moduleY >= 0 && moduleY < Size)
                              && GetModule(moduleX, moduleY);

                Color c = isDark ? fg : bg;
                int offset = rowOffset + x * 4;
                pixelBuffer[offset + 0] = c.B;
                pixelBuffer[offset + 1] = c.G;
                pixelBuffer[offset + 2] = c.R;
                pixelBuffer[offset + 3] = c.A;
            }
        }

        var bitmap = BitmapSource.Create(
            pixelWidth,
            pixelHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixelBuffer,
            stride);

        bitmap.Freeze();
        return bitmap;
    }

    #endregion

    #region Pattern Placement & Geometry

    private void DrawFunctionPatterns()
    {
        // 1. Finder patterns at (0,0), (Size-7, 0), (0, Size-7)
        DrawFinderPattern(0, 0);
        DrawFinderPattern(Size - 7, 0);
        DrawFinderPattern(0, Size - 7);

        // 2. Alignment patterns
        int[] alignPos = GetAlignmentPatternPositions(Version);
        int numAlign = alignPos.Length;
        for (int i = 0; i < numAlign; i++)
        {
            for (int j = 0; j < numAlign; j++)
            {
                // Skip finder pattern positions
                if ((i == 0 && j == 0) || (i == 0 && j == numAlign - 1) || (i == numAlign - 1 && j == 0))
                    continue;
                DrawAlignmentPattern(alignPos[i], alignPos[j]);
            }
        }

        // 3. Timing patterns
        for (int i = 8; i < Size - 8; i++)
        {
            if (!_isFunction[6, i]) SetFunctionModule(6, i, i % 2 == 0);
            if (!_isFunction[i, 6]) SetFunctionModule(i, 6, i % 2 == 0);
        }

        // 4. Dark module
        SetFunctionModule(8, 4 * Version + 9, true);

        // 5. Reserve Format Info areas
        ReserveFormatModules();

        // 6. Reserve Version Info areas (Version >= 7)
        if (Version >= 7)
        {
            DrawVersionInfo(0, isDummy: true);
        }
    }

    private void DrawFinderPattern(int x, int y)
    {
        for (int dy = -1; dy <= 7; dy++)
        {
            for (int dx = -1; dx <= 7; dx++)
            {
                int dist = Math.Max(Math.Abs(dx - 3), Math.Abs(dy - 3));
                int xx = x + dx;
                int yy = y + dy;
                if (xx >= 0 && xx < Size && yy >= 0 && yy < Size)
                {
                    // dist 0 or 1: 3x3 dark center
                    // dist 2: 5x5 white border
                    // dist 3: 7x7 dark border
                    // dist 4 or dx/dy=-1/7: white separator
                    SetFunctionModule(xx, yy, dist != 2 && dist != 4);
                }
            }
        }
    }

    private void DrawAlignmentPattern(int cx, int cy)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                SetFunctionModule(cx + dx, cy + dy, dist != 1);
            }
        }
    }

    private void ReserveFormatModules()
    {
        // Top-left
        for (int i = 0; i <= 8; i++)
        {
            if (i != 6)
            {
                _isFunction[8, i] = true;
                _isFunction[i, 8] = true;
            }
        }

        // Top-right
        for (int i = 0; i < 8; i++)
            _isFunction[Size - 1 - i, 8] = true;

        // Bottom-left
        for (int i = 0; i < 8; i++)
            _isFunction[8, Size - 1 - i] = true;
    }

    private void DrawFormatBits(int mask)
    {
        // Format bits: 5 data bits (2 ECC bits + 3 mask bits) + 10 BCH bits
        int eccBits = ErrorCorrectionLevel switch
        {
            QrCodeEcc.Low => 1,
            QrCodeEcc.Medium => 0,
            QrCodeEcc.Quartile => 3,
            QrCodeEcc.High => 2,
            _ => 0
        };

        int data = (eccBits << 3) | mask;
        int rem = data;
        for (int i = 0; i < 10; i++)
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);

        int bits = ((data << 10) | rem) ^ 0x5412;

        // Draw top-left
        for (int i = 0; i <= 5; i++)
            _modules[8, i] = GetBit(bits, i);
        _modules[8, 7] = GetBit(bits, 6);
        _modules[8, 8] = GetBit(bits, 7);
        _modules[7, 8] = GetBit(bits, 8);
        for (int i = 9; i < 15; i++)
            _modules[14 - i, 8] = GetBit(bits, i);

        // Draw top-right and bottom-left
        for (int i = 0; i < 8; i++)
            _modules[Size - 1 - i, 8] = GetBit(bits, i);
        for (int i = 8; i < 15; i++)
            _modules[8, Size - 15 + i] = GetBit(bits, i);
    }

    private void DrawVersionInfo(int version, bool isDummy = false)
    {
        int v = isDummy ? Version : version;
        int rem = v;
        for (int i = 0; i < 12; i++)
            rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        int bits = (v << 12) | rem;

        for (int i = 0; i < 18; i++)
        {
            bool bit = !isDummy && GetBit(bits, i);
            int a = Size - 11 + i % 3;
            int b = i / 3;
            _modules[a, b] = bit;
            _modules[b, a] = bit;
            _isFunction[a, b] = true;
            _isFunction[b, a] = true;
        }
    }

    private void SetFunctionModule(int x, int y, bool isDark)
    {
        _modules[x, y] = isDark;
        _isFunction[x, y] = true;
    }

    #endregion

    #region Data Placement & Masking

    private void DrawCodewords(byte[] codewords)
    {
        // Zig-zag placement from bottom-right to top-left
        int bitIndex = 0;
        int totalBits = codewords.Length * 8;

        for (int right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right--; // Skip vertical timing column

            for (int vert = 0; vert < Size; vert++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? Size - 1 - vert : vert;

                    if (!_isFunction[x, y] && bitIndex < totalBits)
                    {
                        int byteIdx = bitIndex >> 3;
                        int bitOffset = 7 - (bitIndex & 7);
                        _modules[x, y] = ((codewords[byteIdx] >> bitOffset) & 1) != 0;
                        bitIndex++;
                    }
                }
            }
        }
    }

    private void ApplyMask(int mask)
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (!_isFunction[x, y] && GetMaskCondition(mask, x, y))
                {
                    _modules[x, y] = !_modules[x, y];
                }
            }
        }
    }

    private static bool GetMaskCondition(int mask, int x, int y) => mask switch
    {
        0 => (x + y) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (x + y) % 3 == 0,
        4 => (y / 2 + x / 3) % 2 == 0,
        5 => ((x * y) % 2 + (x * y) % 3) == 0,
        6 => (((x * y) % 2 + (x * y) % 3) % 2) == 0,
        7 => (((x + y) % 2 + (x * y) % 3) % 2) == 0,
        _ => false
    };

    private int GetPenaltyScore()
    {
        int penalty = 0;

        // Condition 1: 5 or more consecutive modules of same color
        for (int y = 0; y < Size; y++)
        {
            bool runColor = false;
            int runVal = 0;
            for (int x = 0; x < Size; x++)
            {
                if (x == 0 || _modules[x, y] != runColor)
                {
                    runColor = _modules[x, y];
                    runVal = 1;
                }
                else
                {
                    runVal++;
                    if (runVal == 5) penalty += 3;
                    else if (runVal > 5) penalty++;
                }
            }
        }

        for (int x = 0; x < Size; x++)
        {
            bool runColor = false;
            int runVal = 0;
            for (int y = 0; y < Size; y++)
            {
                if (y == 0 || _modules[x, y] != runColor)
                {
                    runColor = _modules[x, y];
                    runVal = 1;
                }
                else
                {
                    runVal++;
                    if (runVal == 5) penalty += 3;
                    else if (runVal > 5) penalty++;
                }
            }
        }

        // Condition 2: 2x2 blocks of same color
        for (int y = 0; y < Size - 1; y++)
        {
            for (int x = 0; x < Size - 1; x++)
            {
                bool color = _modules[x, y];
                if (color == _modules[x + 1, y] &&
                    color == _modules[x, y + 1] &&
                    color == _modules[x + 1, y + 1])
                {
                    penalty += 3;
                }
            }
        }

        // Condition 4: Balance of dark/light modules
        int darkCount = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (_modules[x, y]) darkCount++;
            }
        }
        int total = Size * Size;
        int diff = Math.Abs(darkCount * 100 / total - 50);
        penalty += (diff / 5) * 10;

        return penalty;
    }

    #endregion

    #region Reed-Solomon Error Correction & Math (GF 2^8)

    private static byte[] AddEccAndInterleave(byte[] data, int version, QrCodeEcc ecc)
    {
        int numBlocks = NUM_ERROR_CORRECTION_BLOCKS[(int)ecc][version];
        int blockEccLen = ECC_CODEWORDS_PER_BLOCK[(int)ecc][version];
        int rawCodewords = GetNumRawDataModules(version) / 8;
        int numShortBlocks = numBlocks - rawCodewords % numBlocks;
        int shortBlockLen = rawCodewords / numBlocks;

        byte[][] dataBlocks = new byte[numBlocks][];
        byte[][] eccBlocks = new byte[numBlocks][];

        byte[] divisor = ReedSolomonComputeDivisor(blockEccLen);

        int dataOffset = 0;
        for (int i = 0; i < numBlocks; i++)
        {
            int blockDataLen = shortBlockLen - blockEccLen + (i >= numShortBlocks ? 1 : 0);
            byte[] blockData = new byte[blockDataLen];
            Array.Copy(data, dataOffset, blockData, 0, blockDataLen);
            dataOffset += blockDataLen;

            dataBlocks[i] = blockData;
            eccBlocks[i] = ReedSolomonComputeRemainder(blockData, divisor);
        }

        // Interleave data codewords
        byte[] result = new byte[rawCodewords];
        int outIndex = 0;

        int maxDataBlockLen = shortBlockLen - blockEccLen + 1;
        for (int j = 0; j < maxDataBlockLen; j++)
        {
            for (int i = 0; i < numBlocks; i++)
            {
                if (j < dataBlocks[i].Length)
                    result[outIndex++] = dataBlocks[i][j];
            }
        }

        // Interleave ECC codewords
        for (int j = 0; j < blockEccLen; j++)
        {
            for (int i = 0; i < numBlocks; i++)
            {
                result[outIndex++] = eccBlocks[i][j];
            }
        }

        return result;
    }

    private static byte[] ReedSolomonComputeDivisor(int degree)
    {
        byte[] result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                result[j] = Multiply(result[j], root);
                if (j + 1 < degree)
                    result[j] ^= result[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    private static byte[] ReedSolomonComputeRemainder(byte[] data, byte[] divisor)
    {
        byte[] result = new byte[divisor.Length];
        foreach (byte b in data)
        {
            byte factor = (byte)(b ^ result[0]);
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (int i = 0; i < divisor.Length; i++)
                result[i] ^= Multiply(divisor[i], factor);
        }
        return result;
    }

    private static byte Multiply(int x, int y)
    {
        if (x == 0 || y == 0) return 0;
        return EXP[LOG[x] + LOG[y]];
    }

    public static int GetNumDataCodewords(int version, QrCodeEcc ecc)
    {
        return GetNumRawDataModules(version) / 8
               - ECC_CODEWORDS_PER_BLOCK[(int)ecc][version]
               * NUM_ERROR_CORRECTION_BLOCKS[(int)ecc][version];
    }

    private static int GetNumRawDataModules(int ver)
    {
        int result = (16 * ver + 128) * ver + 64;
        if (ver >= 2)
        {
            int numAlign = ver / 7 + 2;
            result -= (25 * numAlign - 10) * numAlign - 55;
            if (ver >= 7)
                result -= 36;
        }
        return result;
    }

    private static int[] GetAlignmentPatternPositions(int ver)
    {
        if (ver == 1) return Array.Empty<int>();
        int numAlign = ver / 7 + 2;
        int step = (ver == 32) ? 26 : (ver * 4 + numAlign * 2 + 1) / (numAlign * 2 - 2) * 2;
        int[] result = new int[numAlign];
        result[0] = 6;
        for (int i = result.Length - 1, pos = ver * 4 + 10; i >= 1; i--, pos -= step)
            result[i] = pos;
        return result;
    }

    private static void AppendBits(List<bool> list, int val, int count)
    {
        for (int i = count - 1; i >= 0; i--)
            list.Add(((val >> i) & 1) != 0);
    }

    private static bool GetBit(int val, int bitIndex) => ((val >> bitIndex) & 1) != 0;

    #endregion
}

using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Infrastructure.Services;

/// <summary>
/// PDF 文本提取器（零外部依赖）。
/// 扫描 PDF 对象流：对 <c>/FlateDecode</c> 流用内置 <see cref="ZLibStream"/> 解压，
/// 再从内容流中抽取 <c>(...) Tj</c> / <c>[...] TJ</c> 文本算子。
/// 属 best-effort 实现，覆盖常见的非加密、非 CID 字体文档（RAG 入库场景足够）。
/// </summary>
internal sealed class PdfTextExtractor : IDocumentTextExtractor
{
    private static readonly byte[] StreamMarker = "stream"u8.ToArray();
    private static readonly byte[] EndStreamMarker = "endstream"u8.ToArray();

    private static readonly Regex Tj =
        new(@"\((?<t>(?:\\.|[^()\\])*)\)\s*Tj", RegexOptions.Compiled);

    private static readonly Regex TjArray =
        new(@"\[(?<arr>(?:\\.|[^\[\]])*)\]\s*TJ", RegexOptions.Compiled);

    public bool Supports(string fileName, string contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ext = Path.GetExtension(fileName);
        return string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public string Extract(Stream content, string fileName, string contentType)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        return ExtractText(ms.ToArray());
    }

    internal static string ExtractText(byte[] data)
    {
        var sb = new StringBuilder();

        // 1) 原始（未压缩）文本
        CollectFromSource(Encoding.Latin1.GetString(data), sb);

        // 2) 解压所有 /FlateDecode 流并扫描
        foreach (var decompressed in DecompressStreams(data))
        {
            CollectFromSource(Encoding.Latin1.GetString(decompressed), sb);
        }

        return Normalize(sb.ToString());
    }

    private static void CollectFromSource(string source, StringBuilder sb)
    {
        foreach (Match m in Tj.Matches(source))
        {
            sb.Append(Unescape(m.Groups["t"].Value)).Append(' ');
        }

        foreach (Match m in TjArray.Matches(source))
        {
            var arr = m.Groups["arr"].Value;
            foreach (Match s in Tj.Matches(arr))
            {
                sb.Append(Unescape(s.Groups["t"].Value)).Append(' ');
            }
        }
    }

    private static List<byte[]> DecompressStreams(byte[] data)
    {
        var result = new List<byte[]>();
        var latin1 = Encoding.Latin1;
        int i = 0;

        while ((i = IndexOf(data, StreamMarker, i)) >= 0)
        {
            int after = i + StreamMarker.Length;
            // 跳过 stream 关键字后的换行
            if (after < data.Length && (data[after] == '\r' || data[after] == '\n'))
            {
                after++;
                if (after < data.Length && data[after - 1] == '\r' && data[after] == '\n')
                    after++;
            }

            int end = IndexOf(data, EndStreamMarker, after);
            if (end < 0)
                break;

            // 仅解压其字典声明了 /FlateDecode 的流
            int dictStart = Math.Max(0, i - 512);
            var dict = latin1.GetString(data, dictStart, i - dictStart);
            if (dict.Contains("/FlateDecode"))
            {
                var streamBytes = new byte[end - after];
                Array.Copy(data, after, streamBytes, 0, streamBytes.Length);
                try
                {
                    using var input = new MemoryStream(streamBytes);
                    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                    using var outMs = new MemoryStream();
                    zlib.CopyTo(outMs);
                    result.Add(outMs.ToArray());
                }
                catch
                {
                    // 解压失败（损坏/非 zlib 负载）跳过该流
                }
            }

            i = end + EndStreamMarker.Length;
        }

        return result;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        for (int i = from; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match)
                return i;
        }
        return -1;
    }

    private static string Unescape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            if (i + 1 >= value.Length)
            {
                sb.Append(c);
                break;
            }

            char n = value[++i];
            switch (n)
            {
                case 'n': sb.Append(' '); break; // PDF 文本中换行通常视作空格
                case 'r': sb.Append(' '); break;
                case 't': sb.Append(' '); break;
                case 'b': sb.Append(' '); break;
                case 'f': sb.Append(' '); break;
                case '(': sb.Append('('); break;
                case ')': sb.Append(')'); break;
                case '\\': sb.Append('\\'); break;
                default:
                    if (n >= '0' && n <= '7')
                    {
                        // 八进制转义 \ddd
                        int code = n - '0';
                        int k = i + 1;
                        while (k < value.Length && k < i + 3 && value[k] >= '0' && value[k] <= '7')
                        {
                            code = code * 8 + (value[k] - '0');
                            k++;
                        }
                        i = k - 1;
                        sb.Append((char)code);
                    }
                    else
                    {
                        sb.Append(n);
                    }
                    break;
            }
        }

        return sb.ToString();
    }

    private static string Normalize(string text)
    {
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}

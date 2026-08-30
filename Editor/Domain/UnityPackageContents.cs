using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// unitypackageが何をどこへ取り込むかを読み取る。
    ///
    /// unitypackageはgzipで圧縮したtarで、アセット1件につきGUIDを名前とするディレクトリを持ち、
    /// その中の`pathname`が取り込み先を持つ。ここではその一覧だけを取り出す。
    /// 取り込みで消えないファイルを知るために、新しい版が何を持っているかが要る
    /// </summary>
    internal static class UnityPackageContents
    {
        private const int BlockSize = 512;
        private const int NameOffset = 0;
        private const int NameLength = 100;
        private const int SizeOffset = 124;
        private const int SizeLength = 12;

        private const string PathnameEntry = "pathname";

        /// <summary>
        /// 取り込み先のパスを読み取る。
        ///
        /// 壊れた書庫では読めたところまでを返さず、例外を投げる。
        /// 途中までの一覧を「新しい版の中身」として扱うと、
        /// 残りのファイルを消してよいと判断してしまう
        /// </summary>
        public static IReadOnlyList<string> ReadPathnames(Stream unityPackage)
        {
            if (unityPackage == null)
            {
                throw new ArgumentNullException(nameof(unityPackage));
            }

            var pathnames = new List<string>();

            using (var gzip = new GZipStream(unityPackage, CompressionMode.Decompress, leaveOpen: true))
            {
                var header = new byte[BlockSize];
                while (true)
                {
                    ReadExactly(gzip, header, BlockSize);

                    // ファイル名が空のブロックは終端を表す
                    if (header[NameOffset] == 0)
                    {
                        break;
                    }

                    var name = ReadString(header, NameOffset, NameLength);
                    var size = ReadOctal(header, SizeOffset, SizeLength);
                    var content = ReadContent(gzip, size);

                    if (IsPathnameEntry(name))
                    {
                        var pathname = Encoding.UTF8.GetString(content).Trim().Replace('\\', '/');
                        if (pathname.Length > 0)
                        {
                            pathnames.Add(pathname);
                        }
                    }
                }
            }

            return pathnames;
        }

        private static bool IsPathnameEntry(string name)
        {
            // エントリは`<guid>/pathname`の形をとる
            var separator = name.LastIndexOf('/');
            return separator >= 0
                && string.Equals(name.Substring(separator + 1), PathnameEntry, StringComparison.Ordinal);
        }

        private static byte[] ReadContent(Stream stream, long size)
        {
            if (size < 0 || size > int.MaxValue)
            {
                throw new InvalidDataException("unitypackageのエントリの大きさが読み取れません");
            }

            var content = new byte[size];
            ReadExactly(stream, content, (int)size);

            // 中身はブロック境界まで埋められている
            var padding = (int)(BlockSize - size % BlockSize) % BlockSize;
            if (padding > 0)
            {
                ReadExactly(stream, new byte[padding], padding);
            }

            return content;
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int count)
        {
            var read = 0;
            while (read < count)
            {
                var chunk = stream.Read(buffer, read, count - read);
                if (chunk <= 0)
                {
                    throw new EndOfStreamException("unitypackageが途中で終わっています");
                }

                read += chunk;
            }
        }

        private static string ReadString(byte[] header, int offset, int length)
        {
            var end = offset;
            var limit = offset + length;
            while (end < limit && header[end] != 0)
            {
                end++;
            }

            return Encoding.UTF8.GetString(header, offset, end - offset);
        }

        private static long ReadOctal(byte[] header, int offset, int length)
        {
            var text = ReadString(header, offset, length).Trim();
            if (text.Length == 0)
            {
                return 0;
            }

            long value = 0;
            foreach (var character in text)
            {
                if (character < '0' || character > '7')
                {
                    throw new InvalidDataException(string.Format(
                        CultureInfo.InvariantCulture, "tarヘッダの数値が読み取れません: {0}", text));
                }

                value = value * 8 + (character - '0');
            }

            return value;
        }
    }
}

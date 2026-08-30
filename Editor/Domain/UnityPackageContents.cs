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
        private const string AssetEntry = "asset";

        /// <summary>
        /// 取り込み先と中身を読み取る。
        ///
        /// 壊れた書庫では読めたところまでを返さず、例外を投げる。
        /// 途中までの一覧を「新しい版の中身」として扱うと、
        /// 残りのファイルを消してよいと判断してしまう
        /// </summary>
        public static IReadOnlyList<UnityPackageEntry> Read(Stream unityPackage)
        {
            if (unityPackage == null)
            {
                throw new ArgumentNullException(nameof(unityPackage));
            }

            // エントリは`<guid>/`の下に分かれて並び、中身と取り込み先の順序は決まっていない
            var pathnames = new Dictionary<string, string>(StringComparer.Ordinal);
            var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var order = new List<string>();

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

                    if (!TrySplitEntry(name, out var guid, out var kind))
                    {
                        continue;
                    }

                    if (kind == PathnameEntry)
                    {
                        var pathname = Encoding.UTF8.GetString(content).Trim().Replace('\\', '/');
                        if (pathname.Length == 0)
                        {
                            continue;
                        }

                        if (!pathnames.ContainsKey(guid))
                        {
                            order.Add(guid);
                        }

                        pathnames[guid] = pathname;
                    }
                    else if (kind == AssetEntry)
                    {
                        assets[guid] = content;
                    }
                }
            }

            var entries = new List<UnityPackageEntry>(order.Count);
            foreach (var guid in order)
            {
                assets.TryGetValue(guid, out var asset);
                entries.Add(new UnityPackageEntry(pathnames[guid], asset));
            }

            return entries;
        }

        private static bool TrySplitEntry(string name, out string guid, out string kind)
        {
            guid = null;
            kind = null;

            // エントリは`<guid>/asset`のような形をとる
            var separator = name.LastIndexOf('/');
            if (separator <= 0)
            {
                return false;
            }

            guid = name.Substring(0, separator);
            kind = name.Substring(separator + 1);
            return true;
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

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Tests
{
    /// <summary>
    /// unitypackageから取り込み先を読み取る処理の検証。
    ///
    /// 読み取った一覧は「新しい版に無いファイルを消す」判断の材料になるため、
    /// 取りこぼしや、壊れた書庫を黙って受け入れることが起きないかを確かめる。
    /// </summary>
    public class UnityPackageContentsTests
    {
        [Test]
        public void ReadPathnames_ReadsTheDestinationOfEveryEntry()
        {
            var package = BuildPackage(
                ("11111111111111111111111111111111", "Assets/Example/Editor/Foo.cs", "class Foo {}"),
                ("22222222222222222222222222222222", "Assets/Example/package.json", "{}"));

            using (var stream = new MemoryStream(package))
            {
                Assert.That(UnityPackageContents.ReadPathnames(stream), Is.EqualTo(new[]
                {
                    "Assets/Example/Editor/Foo.cs",
                    "Assets/Example/package.json",
                }));
            }
        }

        // フォルダのエントリは中身を持たない。取り込み先だけは並ぶ
        [Test]
        public void ReadPathnames_IncludesFolderEntries()
        {
            var package = BuildPackage(("33333333333333333333333333333333", "Assets/Example/Editor", null));

            using (var stream = new MemoryStream(package))
            {
                Assert.That(UnityPackageContents.ReadPathnames(stream), Is.EqualTo(new[] { "Assets/Example/Editor" }));
            }
        }

        // 途中まで読めた一覧を返すと、残りのファイルを消してよいと判断してしまう
        [Test]
        public void ReadPathnames_Throws_WhenTheArchiveIsTruncated()
        {
            var package = BuildPackage(("44444444444444444444444444444444", "Assets/Example/Editor/Foo.cs", "class Foo {}"));
            var truncated = new byte[package.Length / 2];
            Array.Copy(package, truncated, truncated.Length);

            using (var stream = new MemoryStream(truncated))
            {
                Assert.That(() => UnityPackageContents.ReadPathnames(stream), Throws.InstanceOf<Exception>());
            }
        }

        /// <summary>
        /// unitypackageの形を組み立てる。
        ///
        /// gzipで圧縮したtarで、アセット1件につきGUIDのディレクトリを持ち、
        /// その中に中身(asset)、メタ(asset.meta)、取り込み先(pathname)が並ぶ。
        /// assetがnullのものはフォルダのエントリとして扱う
        /// </summary>
        private static byte[] BuildPackage(params (string Guid, string Pathname, string Asset)[] entries)
        {
            var body = new MemoryStream();
            foreach (var entry in entries)
            {
                if (entry.Asset != null)
                {
                    WriteEntry(body, entry.Guid + "/asset", Encoding.UTF8.GetBytes(entry.Asset));
                }

                WriteEntry(body, entry.Guid + "/asset.meta", Encoding.UTF8.GetBytes("guid: " + entry.Guid));
                WriteEntry(body, entry.Guid + "/pathname", Encoding.UTF8.GetBytes(entry.Pathname));
            }

            // tarの終端は空のブロックで示す
            body.Write(new byte[1024], 0, 1024);

            var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
            {
                var bytes = body.ToArray();
                gzip.Write(bytes, 0, bytes.Length);
            }

            return compressed.ToArray();
        }

        private static void WriteEntry(Stream stream, string name, IReadOnlyList<byte> content)
        {
            var header = new byte[512];
            Encoding.UTF8.GetBytes(name).CopyTo(header, 0);

            // 大きさは8進数の文字列で、末尾はNUL
            var size = Encoding.UTF8.GetBytes(Convert.ToString(content.Count, 8).PadLeft(11, '0'));
            size.CopyTo(header, 124);

            stream.Write(header, 0, header.Length);

            var body = new byte[content.Count];
            for (var i = 0; i < content.Count; i++)
            {
                body[i] = content[i];
            }

            stream.Write(body, 0, body.Length);

            // 中身はブロック境界まで埋める
            var padding = (512 - body.Length % 512) % 512;
            if (padding > 0)
            {
                stream.Write(new byte[padding], 0, padding);
            }
        }
    }
}

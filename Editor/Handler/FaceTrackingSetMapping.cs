using System.Collections.Generic;
using System.Linq;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator
{
    /// <summary>
    /// ネイティブフェイストラッキングのトラッキングキーと、メッシュのシェイプキーの対応付けを他のパッケージへ公開する。
    ///
    /// Kx VRC NativeFT Manager が、アバターに本ツールのコンポーネントが無いときにも
    /// ARKit名のシェイプキーからBlendshapes Setを作れるようにするための入口。
    /// 対応表は本ツールがSetを書くときと同じものを使う。
    /// Managerはこの型をリフレクションで引くので、型名・メソッド名・引数と戻り値の型は変えない
    /// </summary>
    public static class FaceTrackingSetMapping
    {
        /// <summary>
        /// トラッキングキーの数。VRChat SDK の VRCAvatarDescriptor.FaceTrackingKey.Max と一致する
        /// </summary>
        public static int KeyCount => FaceTrackingBlendshapesSetTable.KeyCount;

        /// <summary>
        /// キーの番号順の名前。VRChat SDK の FaceTrackingKey の名前と一致する。
        /// 呼び出し側はSDKの列挙体と突き合わせてから使う
        /// </summary>
        public static string[] GetKeyNames()
        {
            return FaceTrackingBlendshapesSetTable.GetKeyNames().ToArray();
        }

        /// <summary>
        /// メッシュにあるシェイプキー名から、キーごとに割り当てるシェイプキー名を返す。
        /// 戻り値はキーの番号を添字にした長さ<see cref="KeyCount"/>の配列で、割り当てが無いキーは空文字。
        ///
        /// Unified Expressions名とARKit名を候補にし、Unified Expressions名を優先する。
        /// どの候補も完全一致しなかったキーは、大文字小文字を無視して探し直す
        /// </summary>
        public static string[] FindBlendShapeNames(IEnumerable<string> meshBlendShapeNames)
        {
            return FaceTrackingBlendshapesSetTable.Build(meshBlendShapeNames, ignoreCase: true).BlendShapeNames;
        }
    }
}

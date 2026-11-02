using UnityEngine;
using ARKitBlendShapeGenerator.UseCase;

namespace ARKitBlendShapeGenerator
{
    /// <summary>
    /// メッシュのARKit名のシェイプキーをUnified Expressions名へ変換する処理を、他のパッケージへ公開する。
    ///
    /// Kx VRC NativeFT Manager が、アバターに本ツールのコンポーネントが無いときに、
    /// ビルド用の複製の上でARKitのシェイプキーからUEのシェイプキーを作るための入口。
    /// 変換は出力名をUnified Expressionsにしたときの既存ARKitシェイプキーの変換と同じで、
    /// browInnerUpやcheekPuffなどUEで左右・上下に分かれるものは変形を分けて書く。
    /// Managerはこの型をリフレクションで引くので、型名・メソッド名・引数と戻り値の型は変えない
    /// </summary>
    public static class UnifiedExpressionsConversion
    {
        /// <summary>
        /// レンダラーのメッシュにあるARKit名のシェイプキーから、UE名のシェイプキーを書き足す。
        /// 戻り値は書き足したシェイプキー名。
        ///
        /// 書き足すものが無ければメッシュは差し替えず、空の配列を返す。
        /// 書き足したときは複製したメッシュをレンダラーへ差し替える。元のメッシュアセットは変えないので、
        /// ビルド用のアバターの複製に対して呼ぶこと
        /// </summary>
        public static string[] ConvertArkitShapes(SkinnedMeshRenderer renderer)
        {
            return GenerateBlendShapesUseCase.ConvertArkitToUnifiedExpressions(renderer).ToArray();
        }
    }
}

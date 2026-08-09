namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// 生成処理のログ出力の抽象
    /// 実装はInfra層（UnityDebugLogger）が提供する
    /// </summary>
    internal interface IGenerationLogger
    {
        /// <summary>debugMode有効時の詳細ログ</summary>
        void Debug(string message);

        void Error(string message);
    }
}

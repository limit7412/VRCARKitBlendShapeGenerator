using UnityEditor;
using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Infra
{
    /// <summary>
    /// 先送りした再生成をエディタの更新に合わせて実行する。
    /// 実行時刻の決め方はPreviewRebuildDebouncerが持ち、ここはUnityのAPIとつなぐだけ。
    ///
    /// 要求元はプレビューノードのRefreshで、いずれもエディタのメインスレッドから呼ばれる
    /// </summary>
    internal static class PreviewRebuildScheduler
    {
        private static readonly PreviewRebuildDebouncer Debouncer = new PreviewRebuildDebouncer();

        private static bool _isHooked;

        /// <summary>再生成を要求する。要求が続く間はまとめられ、落ち着いてから1回だけ実行される</summary>
        public static void Request()
        {
            Debouncer.Request(EditorApplication.timeSinceStartup);

            if (_isHooked)
            {
                return;
            }

            _isHooked = true;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!Debouncer.TryConsume(EditorApplication.timeSinceStartup))
            {
                return;
            }

            _isHooked = false;
            EditorApplication.update -= Tick;
            ARKitBlendShapeGeneratorPreviewState.NotifyDeferredRebuild();
        }
    }
}

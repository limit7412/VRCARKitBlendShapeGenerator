using ARKitBlendShapeGenerator.Domain;

namespace ARKitBlendShapeGenerator.Infra
{
    /// <summary>
    /// UnityEngine.Debugに出力するIGenerationLogger実装
    /// </summary>
    internal sealed class UnityDebugLogger : IGenerationLogger
    {
        public static readonly UnityDebugLogger Instance = new UnityDebugLogger();

        private UnityDebugLogger()
        {
        }

        public void Debug(string message)
        {
            UnityEngine.Debug.Log($"[ARKitGenerator] {message}");
        }

        public void Error(string message)
        {
            UnityEngine.Debug.LogError("[ARKitGenerator] " + message);
        }
    }
}

using System.Linq;
using UnityEditor;
using UnityEngine;
using static ARKitBlendShapeGenerator.Localization;

namespace ARKitBlendShapeGenerator.Handler
{
    /// <summary>
    /// ヒエラルキーの右クリック（GameObjectメニュー）から、ARKitBlendShapeGeneratorComponentを付けたオブジェクトを作る
    ///
    /// プレハブは配らない。中身はコンポーネント1つだけなので、プレハブにすると
    /// 版を上げるたびに既定値をプレハブへ書き直す手間が増えるうえ、利用者の設定がオーバーライドとして残る。
    /// ここで作れば、既定値はコンポーネントのResetの1か所で決まる。
    ///
    /// 作ったオブジェクトにはSkinnedMeshRendererが無いため、Target Rendererの自動設定はアバタールートの配下から探す
    /// （TargetRendererResolver）
    /// </summary>
    internal static class CreateGeneratorMenu
    {
        private const string MenuRoot = "GameObject/Kx VRC ARKit BlendShape Generator/";

        public const string ObjectName = "Kx ARKit BlendShape Generator";

        // 10から49の範囲はヒエラルキーの右クリックにも出る
        private const int Priority = 10;

        [MenuItem(MenuRoot + ObjectName, false, Priority)]
        private static void Create(MenuCommand command)
        {
            CreateObject(command.context as GameObject);
        }

        /// <summary>
        /// parentの子にGeneratorを付けたオブジェクトを作る。parentがnullならシーンの直下に作る。
        ///
        /// アバターにすでにGeneratorがあるときは作らず、既存のGeneratorを選択してnullを返す。
        /// 作ってから重複コンポーネントガードに任せると、新しい側が残る並びのときに
        /// 利用者が設定した既存のGeneratorの方が消えるため
        /// </summary>
        internal static GameObject CreateObject(GameObject parent)
        {
            var existing = FindExistingGenerator(parent);
            if (existing != null)
            {
                Debug.LogWarning("[ARKitGenerator] " + S("create.log.already_exists"), existing);
                Selection.activeGameObject = existing.gameObject;

                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog(S("dialog.title"), S("create.dialog.already_exists"), S("common.ok"));
                }

                return null;
            }

            var gameObject = new GameObject(ObjectName);

            // 右クリックしたオブジェクトの子にする。複数選択では選択ごとに呼ばれ、それぞれの子に作られる。
            // Resetがアバタールートを見てTarget Rendererを決めるため、親を決めてからコンポーネントを付ける
            GameObjectUtility.SetParentAndAlign(gameObject, parent);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create " + ObjectName);

            // AddComponentでResetが走り、既定値が入る
            gameObject.AddComponent<ARKitBlendShapeGeneratorComponent>();

            Selection.activeGameObject = gameObject;
            return gameObject;
        }

        /// <summary>
        /// parentを含むアバターにすでにあるGeneratorを返す。無ければnull
        /// </summary>
        internal static ARKitBlendShapeGeneratorComponent FindExistingGenerator(GameObject parent)
        {
            if (parent == null)
            {
                return null;
            }

            var avatarRoot = DuplicateComponentGuard.FindAvatarRootForUniqueness(parent.transform);
            return avatarRoot
                .GetComponentsInChildren<ARKitBlendShapeGeneratorComponent>(true)
                .FirstOrDefault(c => c != null);
        }
    }
}

using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ARKitBlendShapeGenerator.Handler
{
    /// <summary>
    /// アバタールートに直接付いたGeneratorを、ルートの子に作る専用のオブジェクトへ移すメニュー
    ///
    /// 以前はアバタールートへ付けることを推奨していたため、既存のアバターにはルートに付いたGeneratorが残っている。
    /// 右クリックから作る場合と同じ構成（ルートの子に専用のオブジェクト）へ、設定を保ったまま移せるようにする。
    ///
    /// メニューはヒエラルキーの右クリックと、コンポーネントの歯車メニューの2か所に置く。
    /// どちらも移動は1回のUndoで元に戻る
    /// </summary>
    internal static class SeparateGeneratorMenu
    {
        private const string HierarchyMenuPath =
            "GameObject/Kx VRC ARKit BlendShape Generator/Move Generator from Avatar Root to Separate Object";

        private const string ComponentMenuPath =
            "CONTEXT/ARKitBlendShapeGeneratorComponent/Move to Separate Object";

        // 作成メニューのすぐ下に並べる。10から49の範囲はヒエラルキーの右クリックにも出る
        private const int Priority = 11;

        [MenuItem(HierarchyMenuPath, false, Priority)]
        private static void SeparateFromHierarchy(MenuCommand command)
        {
            // 複数選択では選択ごとに呼ばれる。メニューバーから呼ばれてcontextが空のときは選択中のオブジェクトを使う
            var target = command.context as GameObject ?? Selection.activeGameObject;
            if (target == null)
            {
                return;
            }

            var component = target.GetComponent<ARKitBlendShapeGeneratorComponent>();
            if (CanSeparate(component))
            {
                Separate(component);
            }
        }

        [MenuItem(HierarchyMenuPath, true, Priority)]
        private static bool ValidateSeparateFromHierarchy()
        {
            return Selection.gameObjects.Any(go => CanSeparate(go.GetComponent<ARKitBlendShapeGeneratorComponent>()));
        }

        [MenuItem(ComponentMenuPath, false)]
        private static void SeparateFromComponent(MenuCommand command)
        {
            var component = command.context as ARKitBlendShapeGeneratorComponent;
            if (CanSeparate(component))
            {
                Separate(component);
            }
        }

        [MenuItem(ComponentMenuPath, true)]
        private static bool ValidateSeparateFromComponent(MenuCommand command)
        {
            return CanSeparate(command.context as ARKitBlendShapeGeneratorComponent);
        }

        /// <summary>
        /// componentがアバタールート（Avatar Descriptorを持つオブジェクト）に付いていて、移せる状態かを返す。
        /// アバタールート以外に付いたGeneratorは、利用者がその場所を選んで置いたものとみなして対象にしない
        /// </summary>
        internal static bool CanSeparate(ARKitBlendShapeGeneratorComponent component)
        {
            if (component == null || EditorUtility.IsPersistent(component))
            {
                return false;
            }

            // EditorアセンブリはVRC SDKを参照していないため、型名で判定する
            return component.GetComponent("VRCAvatarDescriptor") != null;
        }

        /// <summary>
        /// sourceを、sourceのオブジェクトの子に作る専用のオブジェクトへ移し、作ったオブジェクトを返す。
        /// 設定値はすべて引き継ぎ、移動全体を1回のUndoにまとめる
        /// </summary>
        internal static GameObject Separate(ARKitBlendShapeGeneratorComponent source)
        {
            var avatarRoot = source.gameObject;

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Move " + CreateGeneratorMenu.ObjectName + " to Separate Object");

            // コンポーネントを付けて設定を写すまでは、オブジェクトをアバターの外（シーンの直下）に置いておく。
            // 先にアバターの子にすると、同じアバターにGeneratorが2つある瞬間ができ、
            // 重複コンポーネントガードがOnValidateで警告を出して削除を予約してしまう。
            // 親を変えてもOnValidateは呼ばれないため、写し終えてから子にすれば重複の判定にかからない
            var gameObject = new GameObject(CreateGeneratorMenu.ObjectName);
            var destination = gameObject.AddComponent<ARKitBlendShapeGeneratorComponent>();

            // AddComponentのResetで入った既定値（Target Rendererや視線系のマッピング）を、元の設定で上書きする。
            // Target Rendererなどの参照も同じシーンの中なのでそのまま引き継がれる
            EditorUtility.CopySerialized(source, destination);

            GameObjectUtility.SetParentAndAlign(gameObject, avatarRoot);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create " + CreateGeneratorMenu.ObjectName);
            Undo.DestroyObjectImmediate(source);

            Undo.CollapseUndoOperations(undoGroup);

            Selection.activeGameObject = gameObject;
            return gameObject;
        }
    }
}

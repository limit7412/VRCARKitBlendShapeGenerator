using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ARKitBlendShapeGenerator.Handler
{
    /// <summary>
    /// アバタールートに直接付いたARKitBlendShapeGeneratorComponentを、ルートの子に作る専用のオブジェクトへ移すメニュー
    ///
    /// 右クリックから専用のオブジェクトを作れるようになる前は、アバタールートへ直接付ける使い方が主だった。
    /// 既存のアバターを設定し直さずに専用のオブジェクトへ分けられるよう、設定値をそのまま引き継いで移す。
    /// 新しいオブジェクトの作成、設定値の複製、元のコンポーネントの削除は1回のUndoでまとめて戻る。
    ///
    /// 対象はアバタールート（Avatar Descriptorを持つオブジェクト）に付いたものだけとする。
    /// 顔メッシュなど、それ以外の場所に付いたコンポーネントは、利用者が意図してそこへ置いたものと見なす。
    ///
    /// Target Rendererが未設定のままでも、移した後に選ばれるレンダラーは変わらない。
    /// 未設定のときは、まずコンポーネント自身の配下を探し、見つからなければアバタールートの配下を探すためである
    /// （TargetRendererResolver）。移した先の専用のオブジェクトにはレンダラーが無いので、
    /// アバタールートに付いていたときと同じくアバタールートの配下から選ばれる。
    /// </summary>
    internal static class SeparateGeneratorMenu
    {
        private const string MenuPath = "GameObject/Kx VRC ARKit BlendShape Generator/Move Generator out of Avatar Root";

        private const string ContextMenuPath = "CONTEXT/ARKitBlendShapeGeneratorComponent/Move to Separate Object";

        // 作成メニュー（10）の直後に並べる。10から49の範囲はヒエラルキーの右クリックにも出る
        private const int Priority = 11;

        private const string UndoName = "Move " + CreateGeneratorMenu.ObjectName + " to Separate Object";

        [MenuItem(MenuPath, false, Priority)]
        private static void MoveFromHierarchy(MenuCommand command)
        {
            // ヒエラルキーの右クリックでは選択中のオブジェクトごとにcontextを変えて呼ばれる。
            // メニューバーから選んだときはcontextが空で1回だけ呼ばれるため、選択全体を対象にする
            var targets = command.context is GameObject context
                ? new[] { context }
                : Selection.gameObjects;

            var moved = new List<Object>();
            foreach (var target in targets)
            {
                var component = FindMovableComponent(target);
                if (component != null)
                {
                    moved.Add(MoveToSeparateObject(component));
                }
            }

            if (moved.Count > 0)
            {
                Selection.objects = moved.ToArray();
            }
        }

        [MenuItem(MenuPath, true, Priority)]
        private static bool ValidateMoveFromHierarchy()
        {
            return Selection.gameObjects.Any(go => FindMovableComponent(go) != null);
        }

        [MenuItem(ContextMenuPath, false)]
        private static void MoveFromInspector(MenuCommand command)
        {
            if (command.context is ARKitBlendShapeGeneratorComponent component && CanMove(component))
            {
                Selection.activeGameObject = MoveToSeparateObject(component);
            }
        }

        [MenuItem(ContextMenuPath, true)]
        private static bool ValidateMoveFromInspector(MenuCommand command)
        {
            return command.context is ARKitBlendShapeGeneratorComponent component && CanMove(component);
        }

        /// <summary>
        /// gameObjectに直接付いていて、移す対象になるコンポーネントを返す。無ければnull
        /// </summary>
        internal static ARKitBlendShapeGeneratorComponent FindMovableComponent(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

            var component = gameObject.GetComponent<ARKitBlendShapeGeneratorComponent>();
            return component != null && CanMove(component) ? component : null;
        }

        /// <summary>
        /// コンポーネントがアバタールートに付いているか判定する
        /// </summary>
        internal static bool CanMove(ARKitBlendShapeGeneratorComponent component)
        {
            return component != null && component.GetComponent<VRCAvatarDescriptor>() != null;
        }

        /// <summary>
        /// componentの設定値を引き継いだコンポーネントを、componentのオブジェクトの子に作るオブジェクトへ付け、
        /// 元のcomponentを削除する。作ったオブジェクトを返す
        /// </summary>
        internal static GameObject MoveToSeparateObject(ARKitBlendShapeGeneratorComponent component)
        {
            var parent = component.transform;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            var group = Undo.GetCurrentGroup();

            // 新しいオブジェクトは、まずアバターの外（シーンの直下）で作ってコンポーネントを付ける。
            // アバターの中で付けると、元のコンポーネントを消すまでの間に1つのアバターへ2つ並び、
            // 重複を取り除くガードが新しい側の削除を予約してしまう
            var gameObject = new GameObject(CreateGeneratorMenu.ObjectName);
            Undo.RegisterCreatedObjectUndo(gameObject, UndoName);

            // AddComponentでResetが走るが、直後に元の設定値で上書きする
            var moved = Undo.AddComponent<ARKitBlendShapeGeneratorComponent>(gameObject);
            Undo.RecordObject(moved, UndoName);
            EditorUtility.CopySerialized(component, moved);

            Undo.DestroyObjectImmediate(component);

            // 元のコンポーネントを消してから、アバタールートの子へ移す。
            // 位置は親に合わせる（作成メニューのSetParentAndAlignと同じ置き方）
            Undo.SetTransformParent(gameObject.transform, parent, UndoName);
            Undo.RecordObject(gameObject.transform, UndoName);
            gameObject.transform.localPosition = Vector3.zero;
            gameObject.transform.localRotation = Quaternion.identity;
            gameObject.transform.localScale = Vector3.one;
            Undo.RecordObject(gameObject, UndoName);
            gameObject.layer = parent.gameObject.layer;

            Undo.CollapseUndoOperations(group);
            return gameObject;
        }
    }
}

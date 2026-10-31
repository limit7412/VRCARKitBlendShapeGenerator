using System;
using System.Collections.Generic;

namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// ネイティブフェイストラッキングのBlendshapes Set 1件分の内容。
    /// SDKの型に依存しない形で持ち、Descriptorへの書き込みはInfra側が行う
    /// </summary>
    internal sealed class FaceTrackingBlendshapesSetPlan
    {
        /// <summary>
        /// トラッキングキーの番号を添字にしたシェイプキー名。割り当てが無いキーは空文字
        /// </summary>
        public readonly string[] BlendShapeNames;

        public FaceTrackingBlendshapesSetPlan(string[] blendShapeNames)
        {
            BlendShapeNames = blendShapeNames;
        }

        /// <summary>
        /// 割り当てのあるキーの数
        /// </summary>
        public int AssignedCount
        {
            get
            {
                int count = 0;
                foreach (var name in BlendShapeNames)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    /// <summary>
    /// VRChatのネイティブフェイストラッキングが受け取るUnified Expressionsのトラッキングキーごとに、
    /// 本ツールが生成しうるシェイプキー名のうちどれを割り当てるかを決める対応表。
    ///
    /// ネイティブFTは実行時にキーと同名のシェイプキーを自動で結び付けない。
    /// Avatar DescriptorのBlendshapes Setに書かれた名前だけを動かし、空欄のキーは何もしない。
    /// SDKのエディタにはキー名からシェイプキーを探す自動入力があるが、エディタ上のメッシュを見るため、
    /// NDMFのビルド中にしか存在しない生成物は見つけられない。
    /// そこで同じ探索をビルド時に行い、結果をSetとして書き込む。
    ///
    /// キーごとの候補は、SDKの自動入力（FaceTrackingAutoFill）が持つ候補のうち、
    /// 本ツールの出力名（Unified Expressions名とARKit名）に当たるものに絞っている。
    /// 候補の並びがそのまま優先順位で、Unified Expressions名をARKit名より先に置く。
    /// SDKの自動入力はキー名の完全一致を最優先し、次に候補を大文字小文字を無視して探すため、
    /// 両方の名前がメッシュにあるときの選び方も同じになる。
    ///
    /// Unified Expressionsでは左右や上下に分かれているが本ツールが1つのシェイプキーで出すもの
    /// （LipSuckUpper、LipPucker、mouthFunnel など）は、分かれたキーのそれぞれへ同じ名前を割り当てる。
    /// 統合名（BrowDownLeft、MouthSmileLeft）も同様に、まとめられている基底キーの全部へ割り当てる。
    /// SDKの自動入力も同じ割り当てを行う。
    /// </summary>
    internal static class FaceTrackingBlendshapesSetTable
    {
        /// <summary>
        /// Blendshapes Setの名前。再ビルドで同名のSetを置き換えるための目印。
        /// 他のパッケージからも参照できるよう、値はRuntimeアセンブリの公開定数に置いている
        /// </summary>
        public const string SetName = FaceTrackingSetNames.Generator;

        /// <summary>
        /// 1キーの候補。先頭がキー名で、SDKの列挙体（VRCAvatarDescriptor.FaceTrackingKey）の名前と一致する
        /// </summary>
        private sealed class KeyEntry
        {
            public readonly string KeyName;
            public readonly string[] Candidates;

            public KeyEntry(string keyName, params string[] aliases)
            {
                KeyName = keyName;
                Candidates = new string[aliases.Length + 1];
                Candidates[0] = keyName;
                Array.Copy(aliases, 0, Candidates, 1, aliases.Length);
            }
        }

        /// <summary>
        /// SDKの列挙体の順に並べたキーと候補。
        /// 添字がキーの番号で、SDK 3.10.6 の FaceTrackingKey（Max = 102）に合わせている。
        /// 順序はDescriptorへ書き込む直前にSDKの列挙体と突き合わせて確かめる
        /// </summary>
        private static readonly KeyEntry[] Keys =
        {
            // 視線
            new KeyEntry("EyeLookOutRight", "eyeLookOutRight"),
            new KeyEntry("EyeLookInRight", "eyeLookInRight"),
            new KeyEntry("EyeLookUpRight", "eyeLookUpRight"),
            new KeyEntry("EyeLookDownRight", "eyeLookDownRight"),
            new KeyEntry("EyeLookOutLeft", "eyeLookOutLeft"),
            new KeyEntry("EyeLookInLeft", "eyeLookInLeft"),
            new KeyEntry("EyeLookUpLeft", "eyeLookUpLeft"),
            new KeyEntry("EyeLookDownLeft", "eyeLookDownLeft"),

            // 目
            new KeyEntry("EyeClosedRight", "eyeBlinkRight"),
            new KeyEntry("EyeClosedLeft", "eyeBlinkLeft"),
            new KeyEntry("EyeDilationRight"),
            new KeyEntry("EyeDilationLeft"),
            new KeyEntry("EyeConstrictRight"),
            new KeyEntry("EyeConstrictLeft"),
            new KeyEntry("EyeSquintRight", "eyeSquintRight"),
            new KeyEntry("EyeSquintLeft", "eyeSquintLeft"),
            new KeyEntry("EyeWideRight", "eyeWideRight"),
            new KeyEntry("EyeWideLeft", "eyeWideLeft"),

            // 眉。BrowPinchとBrowLowererは統合名BrowDownにまとめられている
            new KeyEntry("BrowPinchRight", "BrowDownRight", "browDownRight"),
            new KeyEntry("BrowPinchLeft", "BrowDownLeft", "browDownLeft"),
            new KeyEntry("BrowLowererRight", "BrowDownRight", "browDownRight"),
            new KeyEntry("BrowLowererLeft", "BrowDownLeft", "browDownLeft"),
            new KeyEntry("BrowInnerUpRight", "browInnerUp"),
            new KeyEntry("BrowInnerUpLeft", "browInnerUp"),
            new KeyEntry("BrowOuterUpRight", "browOuterUpRight"),
            new KeyEntry("BrowOuterUpLeft", "browOuterUpLeft"),

            // 鼻
            new KeyEntry("NasalDilationRight"),
            new KeyEntry("NasalDilationLeft"),
            new KeyEntry("NasalConstrictRight"),
            new KeyEntry("NasalConstrictLeft"),

            // 頬
            new KeyEntry("CheekSquintRight", "cheekSquintRight"),
            new KeyEntry("CheekSquintLeft", "cheekSquintLeft"),
            new KeyEntry("CheekPuffRight", "cheekPuff"),
            new KeyEntry("CheekPuffLeft", "cheekPuff"),
            new KeyEntry("CheekSuckRight"),
            new KeyEntry("CheekSuckLeft"),

            // 顎
            new KeyEntry("JawOpen", "jawOpen"),
            new KeyEntry("JawRight", "jawRight"),
            new KeyEntry("JawLeft", "jawLeft"),
            new KeyEntry("JawForward", "jawForward"),
            new KeyEntry("JawBackward"),
            new KeyEntry("JawClench"),
            new KeyEntry("JawMandibleRaise"),
            new KeyEntry("MouthClosed", "mouthClose"),

            // 唇。本ツールは左右に分けずに出すため、左右のキーへ同じ名前を割り当てる
            new KeyEntry("LipSuckUpperRight", "LipSuckUpper", "mouthRollUpper"),
            new KeyEntry("LipSuckUpperLeft", "LipSuckUpper", "mouthRollUpper"),
            new KeyEntry("LipSuckLowerRight", "LipSuckLower", "mouthRollLower"),
            new KeyEntry("LipSuckLowerLeft", "LipSuckLower", "mouthRollLower"),
            new KeyEntry("LipSuckCornerRight"),
            new KeyEntry("LipSuckCornerLeft"),
            new KeyEntry("LipFunnelUpperRight", "LipFunnelUpper", "mouthFunnel"),
            new KeyEntry("LipFunnelUpperLeft", "LipFunnelUpper", "mouthFunnel"),
            new KeyEntry("LipFunnelLowerRight", "LipFunnelLower", "mouthFunnel"),
            new KeyEntry("LipFunnelLowerLeft", "LipFunnelLower", "mouthFunnel"),
            new KeyEntry("LipPuckerUpperRight", "LipPucker", "mouthPucker"),
            new KeyEntry("LipPuckerUpperLeft", "LipPucker", "mouthPucker"),
            new KeyEntry("LipPuckerLowerRight", "LipPucker", "mouthPucker"),
            new KeyEntry("LipPuckerLowerLeft", "LipPucker", "mouthPucker"),

            // 口
            new KeyEntry("MouthUpperUpRight", "mouthUpperUpRight"),
            new KeyEntry("MouthUpperUpLeft", "mouthUpperUpLeft"),
            new KeyEntry("MouthUpperDeepenRight"),
            new KeyEntry("MouthUpperDeepenLeft"),
            new KeyEntry("NoseSneerRight", "noseSneerRight"),
            new KeyEntry("NoseSneerLeft", "noseSneerLeft"),
            new KeyEntry("MouthLowerDownRight", "mouthLowerDownRight"),
            new KeyEntry("MouthLowerDownLeft", "mouthLowerDownLeft"),
            // ARKitのmouthLeft/Rightは上下に分かれていないため、上下のキーへ同じ名前を割り当てる
            new KeyEntry("MouthUpperRight", "mouthRight"),
            new KeyEntry("MouthUpperLeft", "mouthLeft"),
            new KeyEntry("MouthLowerRight", "mouthRight"),
            new KeyEntry("MouthLowerLeft", "mouthLeft"),
            // MouthCornerPullとMouthCornerSlantは統合名MouthSmileにまとめられている
            new KeyEntry("MouthCornerPullRight", "MouthSmileRight", "mouthSmileRight"),
            new KeyEntry("MouthCornerPullLeft", "MouthSmileLeft", "mouthSmileLeft"),
            new KeyEntry("MouthCornerSlantRight", "MouthSmileRight", "mouthSmileRight"),
            new KeyEntry("MouthCornerSlantLeft", "MouthSmileLeft", "mouthSmileLeft"),
            new KeyEntry("MouthFrownRight", "mouthFrownRight"),
            new KeyEntry("MouthFrownLeft", "mouthFrownLeft"),
            new KeyEntry("MouthStretchRight", "mouthStretchRight"),
            new KeyEntry("MouthStretchLeft", "mouthStretchLeft"),
            new KeyEntry("MouthDimpleRight", "mouthDimpleRight"),
            new KeyEntry("MouthDimpleLeft", "mouthDimpleLeft"),
            new KeyEntry("MouthRaiserUpper", "mouthShrugUpper"),
            new KeyEntry("MouthRaiserLower", "mouthShrugLower"),
            new KeyEntry("MouthPressRight", "mouthPress"),
            new KeyEntry("MouthPressLeft", "mouthPress"),
            new KeyEntry("MouthTightenerRight"),
            new KeyEntry("MouthTightenerLeft"),

            // 舌
            new KeyEntry("TongueOut", "tongueOut"),
            new KeyEntry("TongueUp"),
            new KeyEntry("TongueDown"),
            new KeyEntry("TongueRight"),
            new KeyEntry("TongueLeft"),
            new KeyEntry("TongueRoll"),
            new KeyEntry("TongueBendDown"),
            new KeyEntry("TongueCurlUp"),
            new KeyEntry("TongueSquish"),
            new KeyEntry("TongueFlat"),
            new KeyEntry("TongueTwistRight"),
            new KeyEntry("TongueTwistLeft"),

            // 喉・首
            new KeyEntry("SoftPalateClose"),
            new KeyEntry("ThroatSwallow"),
            new KeyEntry("NeckFlexRight"),
            new KeyEntry("NeckFlexLeft"),
        };

        /// <summary>
        /// キーの数。SDKの FaceTrackingKey.Max と一致していなければ書き込みを行わない
        /// </summary>
        public static int KeyCount => Keys.Length;

        /// <summary>
        /// キーの番号順の名前。SDKの列挙体との突き合わせに使う
        /// </summary>
        public static IEnumerable<string> GetKeyNames()
        {
            foreach (var key in Keys)
            {
                yield return key.KeyName;
            }
        }

        /// <summary>
        /// キーの候補名（先頭がキー名、以降が優先順位順の別名）
        /// </summary>
        public static IReadOnlyList<string> GetCandidates(string keyName)
        {
            foreach (var key in Keys)
            {
                if (key.KeyName == keyName)
                {
                    return key.Candidates;
                }
            }

            return new string[0];
        }

        /// <summary>
        /// メッシュにあるシェイプキー名から、キーごとの割り当てを決める。
        /// 候補の照合は完全一致で行う。候補は本ツールの出力名そのものなので、大文字小文字を無視する必要がない
        /// </summary>
        public static FaceTrackingBlendshapesSetPlan Build(IEnumerable<string> meshBlendShapeNames)
        {
            var available = new HashSet<string>(StringComparer.Ordinal);
            if (meshBlendShapeNames != null)
            {
                foreach (var name in meshBlendShapeNames)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        available.Add(name);
                    }
                }
            }

            var names = new string[Keys.Length];
            for (int i = 0; i < Keys.Length; i++)
            {
                names[i] = string.Empty;
                foreach (var candidate in Keys[i].Candidates)
                {
                    if (available.Contains(candidate))
                    {
                        names[i] = candidate;
                        break;
                    }
                }
            }

            return new FaceTrackingBlendshapesSetPlan(names);
        }
    }
}

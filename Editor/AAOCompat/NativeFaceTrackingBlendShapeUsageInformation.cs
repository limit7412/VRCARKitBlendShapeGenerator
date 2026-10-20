using System;
using System.Collections.Generic;
using System.Linq;
using Anatawa12.AvatarOptimizer.API;
using UnityEngine;

namespace ARKitBlendShapeGenerator.AAOCompat
{
    /// <summary>
    /// 目印（NativeFaceTrackingBlendShapeUsage）をAAOへ説明する。
    ///
    /// AAOはビルド中のアバターにある各コンポーネントについて、登録されたComponentInformationから
    /// 「何に依存し、何を動かすか」を集めて最適化の判断に使う。
    /// 目印はネイティブFTの代わりにここで申告する。VRCAvatarDescriptorの情報はAAO自身が登録しており、
    /// 同じ型へ二重に登録すると例外になるため、Descriptorではなく目印に登録する
    /// </summary>
    [ComponentInformation(typeof(NativeFaceTrackingBlendShapeUsage))]
    internal sealed class NativeFaceTrackingBlendShapeUsageInformation
        : ComponentInformation<NativeFaceTrackingBlendShapeUsage>
    {
        private const string BlendShapePropertyPrefix = "blendShape.";

        protected override void CollectDependency(
            NativeFaceTrackingBlendShapeUsage component,
            ComponentDependencyCollector collector)
        {
            // 他から参照されないコンポーネントはAAOのGCに取り除かれる。
            // 目印はAAOの処理が終わるまで残す必要があるので、起点として扱わせる
            collector.MarkEntrypoint();

            foreach (var renderer in Renderers(component))
            {
                // FTが動かすメッシュなので、表示されていなくても取り除かれないようにする
                collector.AddDependency(renderer).EvenIfDependantDisabled();
            }
        }

        protected override void CollectMutations(
            NativeFaceTrackingBlendShapeUsage component,
            ComponentMutationsCollector collector)
        {
            // ModifyPropertiesで申告したプロパティは「実行時に値が変わる」と見なされる。
            // 未アニメーションとしての固定も、同じ動かされ方どうしの統合も対象から外れる
            foreach (var renderer in Renderers(component))
            {
                var properties = component.entries
                    .Where(entry => entry != null && entry.renderer == renderer
                                    && !string.IsNullOrEmpty(entry.blendShapeName))
                    .Select(entry => BlendShapePropertyPrefix + entry.blendShapeName)
                    .Distinct()
                    .ToArray();
                collector.ModifyProperties(renderer, properties);
            }
        }

        protected override void ApplySpecialMapping(
            NativeFaceTrackingBlendShapeUsage component,
            MappingSource mappingSource)
        {
            // AAOがメッシュやBlendShapeを統合・改名・削除したあと、各BlendShapeの行き先を問い合わせて記録する。
            // 目印のrenderer参照そのものはこのあとAAOが置き換えるので、ここでは元の参照で問い合わせる
            foreach (var entry in component.entries)
            {
                if (entry == null)
                {
                    continue;
                }

                entry.resolved = false;
                entry.resolvedRenderer = null;
                entry.resolvedBlendShapeName = null;

                if (entry.renderer == null || string.IsNullOrEmpty(entry.blendShapeName))
                {
                    continue;
                }

                var info = mappingSource.GetMappedComponent(entry.renderer);
                if (!info.TryMapProperty(BlendShapePropertyPrefix + entry.blendShapeName, out var mapped))
                {
                    continue;
                }

                if (!(mapped.Component is SkinnedMeshRenderer mappedRenderer)
                    || string.IsNullOrEmpty(mapped.Property)
                    || !mapped.Property.StartsWith(BlendShapePropertyPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                entry.resolved = true;
                entry.resolvedRenderer = mappedRenderer;
                entry.resolvedBlendShapeName = mapped.Property.Substring(BlendShapePropertyPrefix.Length);
            }

            component.mappingApplied = true;
        }

        private static IEnumerable<SkinnedMeshRenderer> Renderers(NativeFaceTrackingBlendShapeUsage component)
        {
            if (component.entries == null)
            {
                return Enumerable.Empty<SkinnedMeshRenderer>();
            }

            return component.entries
                .Where(entry => entry != null && entry.renderer != null)
                .Select(entry => entry.renderer)
                .Distinct();
        }
    }
}

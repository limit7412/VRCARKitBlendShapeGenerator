using System.Collections.Generic;

namespace ARKitBlendShapeGenerator.Domain
{
    /// <summary>
    /// ARKitマッピング定義
    /// </summary>
    public class ARKitMapping
    {
        public string arkitName;
        public List<SourceMapping> sources;
        public BlendShapeSide side;  // 左右フィルタリング用

        public ARKitMapping(string name, List<SourceMapping> sources, BlendShapeSide side = BlendShapeSide.Both)
        {
            this.arkitName = name;
            this.sources = sources;
            this.side = side;
        }
    }

    /// <summary>
    /// ソースBlendShapeマッピング
    /// </summary>
    public class SourceMapping
    {
        public float weight;
        public string[] names;

        public SourceMapping(float weight, params string[] names)
        {
            this.weight = weight;
            this.names = names;
        }
    }
}

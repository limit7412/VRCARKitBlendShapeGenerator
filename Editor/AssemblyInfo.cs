using System.Runtime.CompilerServices;

// テストアセンブリからinternal型（Engine, UseCase, IMeshRepository等）へアクセスするため
[assembly: InternalsVisibleTo("ARKitBlendShapeGenerator.Editor.Tests")]

// AAO互換アセンブリとそのテストから、Localizationと FaceTrackingDescriptorWriter の定数へアクセスするため
[assembly: InternalsVisibleTo("ARKitBlendShapeGenerator.Editor.AAOCompat")]
[assembly: InternalsVisibleTo("ARKitBlendShapeGenerator.Editor.AAOCompat.Tests")]

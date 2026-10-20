using System.Runtime.CompilerServices;

// テストアセンブリからinternal型（FaceTrackingUsageCollector、FaceTrackingSetResolver）へアクセスするため
[assembly: InternalsVisibleTo("ARKitBlendShapeGenerator.Editor.AAOCompat.Tests")]

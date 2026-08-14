using System.Runtime.CompilerServices;

// Editorアセンブリからinternalメンバー（EditorOnValidateHook、TargetRendererResolver）へアクセスするため
[assembly: InternalsVisibleTo("ARKitBlendShapeGenerator.Editor")]

// テストアセンブリからTargetRendererResolverへアクセスするため
[assembly: InternalsVisibleTo("ARKitBlendShapeGenerator.Editor.Tests")]

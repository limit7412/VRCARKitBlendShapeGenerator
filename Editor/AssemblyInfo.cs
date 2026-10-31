using System.Runtime.CompilerServices;

// テストアセンブリからinternal型（Engine, UseCase, IMeshRepository等）へアクセスするため
[assembly: InternalsVisibleTo("ARKitBlendShapeGenerator.Editor.Tests")]

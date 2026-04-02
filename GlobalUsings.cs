// Global using aliases to resolve ambiguities between Godot types and System types.
// ImplicitUsings is enabled in the .csproj, which auto-imports System.IO (FileAccess)
// and System (Environment), creating conflicts with the identically-named Godot types.

global using FileAccess = Godot.FileAccess;
global using Environment = Godot.Environment;

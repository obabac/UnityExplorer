using System;
using System.Collections.Generic;

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record Page<T>(int Total, IReadOnlyList<T> Items);

    public record StatusDto(
        string Version,
        string UnityVersion,
        string Platform,
        string Runtime,
        string ExplorerVersion,
        bool Ready,
        int ScenesLoaded,
        IReadOnlyList<string> Selection);

    public record SceneDto(string Id, string Name, int Index, bool IsLoaded, int RootCount);
    public record ObjectCardDto(string Id, string Name, string Path, string Tag, int Layer, bool Active, int ComponentCount);
    public record ComponentCardDto(string Type, string? Summary);

    public record TransformDto(Vector3Dto Pos, Vector3Dto Rot, Vector3Dto Scale);
    public record Vector3Dto(float X, float Y, float Z);
    public record CameraInfoDto(bool IsFreecam, string Name, float Fov, Vector3Dto Pos, Vector3Dto Rot);
    public record LogLine(DateTimeOffset T, string Level, string Message, string Source, string? Category = null);
    public record LogTailDto(IReadOnlyList<LogLine> Items);

    public record SelectionDto(string? ActiveId, IReadOnlyList<string> Items);
    public record PickHit(string Id, string Name, string Path);
    public record PickResultDto(string Mode, bool Hit, string? Id, IReadOnlyList<PickHit>? Items);
    public record HookDto(string Signature, bool Enabled);
    public record ConsoleScriptDto(string Name, string Path);
    public record VersionInfoDto(string ExplorerVersion, string McpVersion, string UnityVersion, string Runtime);
#endif
}

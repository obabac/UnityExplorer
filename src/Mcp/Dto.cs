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
#elif MONO
    public class Page<T>
    {
        public int Total { get; set; }
        public IList<T> Items { get; set; }

        public Page()
            : this(0, new List<T>())
        {
        }

        public Page(int total, IList<T> items)
        {
            Total = total;
            Items = items ?? new List<T>();
        }
    }

    public class StatusDto
    {
        public string Version { get; set; }
        public string UnityVersion { get; set; }
        public string Platform { get; set; }
        public string Runtime { get; set; }
        public string ExplorerVersion { get; set; }
        public bool Ready { get; set; }
        public int ScenesLoaded { get; set; }
        public IList<string> Selection { get; set; }

        public StatusDto()
        {
            Version = string.Empty;
            UnityVersion = string.Empty;
            Platform = string.Empty;
            Runtime = string.Empty;
            ExplorerVersion = string.Empty;
            Selection = new List<string>();
        }
    }

    public class SceneDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Index { get; set; }
        public bool IsLoaded { get; set; }
        public int RootCount { get; set; }

        public SceneDto()
        {
            Id = string.Empty;
            Name = string.Empty;
        }
    }

    public class ObjectCardDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Tag { get; set; }
        public int Layer { get; set; }
        public bool Active { get; set; }
        public int ComponentCount { get; set; }

        public ObjectCardDto()
        {
            Id = string.Empty;
            Name = string.Empty;
            Path = string.Empty;
            Tag = string.Empty;
        }
    }

    public class ComponentCardDto
    {
        public string Type { get; set; }
        public string Summary { get; set; }

        public ComponentCardDto()
        {
            Type = string.Empty;
            Summary = string.Empty;
        }
    }

    public class TransformDto
    {
        public Vector3Dto Pos { get; set; }
        public Vector3Dto Rot { get; set; }
        public Vector3Dto Scale { get; set; }

        public TransformDto()
        {
            Pos = new Vector3Dto();
            Rot = new Vector3Dto();
            Scale = new Vector3Dto();
        }
    }

    public class Vector3Dto
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    public class CameraInfoDto
    {
        public bool IsFreecam { get; set; }
        public string Name { get; set; }
        public float Fov { get; set; }
        public Vector3Dto Pos { get; set; }
        public Vector3Dto Rot { get; set; }

        public CameraInfoDto()
        {
            Name = string.Empty;
            Pos = new Vector3Dto();
            Rot = new Vector3Dto();
        }
    }

    public class LogLine
    {
        public DateTimeOffset T { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public string Source { get; set; }
        public string? Category { get; set; }

        public LogLine()
        {
            Level = string.Empty;
            Message = string.Empty;
            Source = string.Empty;
            Category = null;
        }
    }

    public class LogTailDto
    {
        public IList<LogLine> Items { get; set; }

        public LogTailDto()
        {
            Items = new List<LogLine>();
        }
    }

    public class SelectionDto
    {
        public string? ActiveId { get; set; }
        public IList<string> Items { get; set; }

        public SelectionDto()
        {
            Items = new List<string>();
        }
    }

    public class PickHit
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }

        public PickHit()
        {
            Id = string.Empty;
            Name = string.Empty;
            Path = string.Empty;
        }
    }

    public class PickResultDto
    {
        public string Mode { get; set; }
        public bool Hit { get; set; }
        public string? Id { get; set; }
        public IList<PickHit> Items { get; set; }

        public PickResultDto()
        {
            Mode = string.Empty;
            Items = new List<PickHit>();
            Id = null;
        }
    }

    public class HookDto
    {
        public string Signature { get; set; }
        public bool Enabled { get; set; }

        public HookDto()
        {
            Signature = string.Empty;
        }
    }

    public class ConsoleScriptDto
    {
        public string Name { get; set; }
        public string Path { get; set; }

        public ConsoleScriptDto()
        {
            Name = string.Empty;
            Path = string.Empty;
        }
    }

    public class VersionInfoDto
    {
        public string ExplorerVersion { get; set; }
        public string McpVersion { get; set; }
        public string UnityVersion { get; set; }
        public string Runtime { get; set; }

        public VersionInfoDto()
        {
            ExplorerVersion = string.Empty;
            McpVersion = string.Empty;
            UnityVersion = string.Empty;
            Runtime = string.Empty;
        }
    }
#endif
}

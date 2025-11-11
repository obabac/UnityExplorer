using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

#if INTEROP
using ModelContextProtocol.Server;
#endif

namespace UnityExplorer.Mcp
{
#if INTEROP
    [McpServerResourceType]
    public static class UnityResources
    {
        [McpServerResource, Description("Status snapshot resource.")]
        public static Task<StatusDto> Status(CancellationToken ct)
            => UnityReadTools.GetStatus(ct);

        [McpServerResource, Description("List scenes resource.")]
        public static Task<Page<SceneDto>> Scenes(int? limit, int? offset, CancellationToken ct)
            => UnityReadTools.ListScenes(limit, offset, ct);

        [McpServerResource, Description("Object details by id.")]
        public static Task<ObjectCardDto> ObjectById(string id, CancellationToken ct)
            => UnityReadTools.GetObject(id, ct);

        [McpServerResource, Description("Components for object id (paged).")]
        public static Task<Page<ComponentCardDto>> ObjectComponents(string id, int? limit, int? offset, CancellationToken ct)
            => UnityReadTools.GetComponents(id, limit, offset, ct);

        [McpServerResource, Description("Tail recent MCP log buffer.")]
        public static Task<LogTailDto> LogsTail(int? count, CancellationToken ct)
            => Task.FromResult(LogBuffer.Tail(count ?? 200));

        [McpServerResource, Description("List objects under a scene (paged).")]
        public static Task<Page<ObjectCardDto>> SceneObjects(string sceneId, string? name, string? type, bool? activeOnly, int? limit, int? offset, CancellationToken ct)
            => UnityReadTools.ListObjects(sceneId, name, type, activeOnly, limit, offset, ct);

        [McpServerResource, Description("Search objects across scenes.")]
        public static Task<Page<ObjectCardDto>> Search(string? query, string? name, string? type, string? path, bool? activeOnly, int? limit, int? offset, CancellationToken ct)
            => UnityReadTools.SearchObjects(query, name, type, path, activeOnly, limit, offset, ct);

        [McpServerResource, Description("Active camera info.")]
        public static Task<CameraInfoDto> CameraActive(CancellationToken ct)
            => UnityReadTools.GetCameraInfo(ct);

        [McpServerResource, Description("Current selection / inspected tabs.")]
        public static Task<SelectionDto> Selection(CancellationToken ct)
            => UnityReadTools.GetSelection(ct);
    }
#endif
}

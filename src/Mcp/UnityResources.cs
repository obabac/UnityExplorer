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
        [McpServerResource("unity://status"), Description("Status snapshot resource.")]
        public static Task<StatusDto> Status(CancellationToken ct)
            => UnityReadTools.GetStatus(ct);

        [McpServerResource("unity://scenes"), Description("List scenes resource.")]
        public static Task<Page<SceneDto>> Scenes(int? limit, int? offset, CancellationToken ct)
            => UnityReadTools.ListScenes(limit, offset, ct);

        [McpServerResource("unity://object/{id}"), Description("Object details by id.")]
        public static Task<ObjectCardDto> ObjectById(string id, CancellationToken ct)
            => UnityReadTools.GetObject(id, ct);

        [McpServerResource("unity://object/{id}/components"), Description("Components for object id (paged).")]
        public static Task<Page<ComponentCardDto>> ObjectComponents(string id, int? limit, int? offset, CancellationToken ct)
            => UnityReadTools.GetComponents(id, limit, offset, ct);

        [McpServerResource("unity://logs/tail"), Description("Tail recent MCP log buffer.")]
        public static Task<LogTailDto> LogsTail(int? count, CancellationToken ct)
            => Task.FromResult(LogBuffer.Tail(count ?? 200));

        [McpServerResource("unity://scene/{sceneId}/objects"), Description("List objects under a scene (paged).")]
        public static Task<Page<ObjectCardDto>> SceneObjects(string sceneId, string? name, string? type, bool? activeOnly, int? limit, int? offset, CancellationToken ct)
            => UnityReadTools.ListObjects(sceneId, name, type, activeOnly, limit, offset, ct);

        [McpServerResource("unity://search"), Description("Search objects across scenes.")]
        public static Task<Page<ObjectCardDto>> Search(string? query, string? name, string? type, string? path, bool? activeOnly, int? limit, int? offset, CancellationToken ct)
            => UnityReadTools.SearchObjects(query, name, type, path, activeOnly, limit, offset, ct);
    }
#endif
}

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace UnityExplorer.Mcp.ContractTests;

public class JsonRpcToolsTests
{
    [Fact]
    public async Task ListTools_JsonRpc_Response_If_Server_Available()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "list-tools-test",
            method = "list_tools",
            @params = new { }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.TryGetProperty("error", out _).Should().BeFalse();
        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("tools", out var tools).Should().BeTrue();
        tools.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task ListTools_Includes_InputSchema_For_All_Tools()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "list-tools-inputschema-test",
            method = "list_tools",
            @params = new { }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("tools", out var tools).Should().BeTrue();
        tools.ValueKind.Should().Be(JsonValueKind.Array);

        foreach (var tool in tools.EnumerateArray())
        {
            tool.TryGetProperty("name", out var name).Should().BeTrue();
            name.ValueKind.Should().Be(JsonValueKind.String);

            tool.TryGetProperty("inputSchema", out var schema).Should().BeTrue();
            schema.ValueKind.Should().Be(JsonValueKind.Object);
        }

        var toolMap = tools
            .EnumerateArray()
            .Where(t => t.TryGetProperty("name", out _))
            .ToDictionary(t => t.GetProperty("name").GetString()!);

        foreach (var schema in toolMap.Values.Select(t => t.GetProperty("inputSchema")))
        {
            schema.TryGetProperty("properties", out var props).Should().BeTrue();
            props.ValueKind.Should().Be(JsonValueKind.Object);
            schema.TryGetProperty("additionalProperties", out var additional).Should().BeTrue();
            additional.GetBoolean().Should().BeFalse();
        }

        if (toolMap.TryGetValue("MousePick", out var mouseTool))
        {
            var schema = mouseTool.GetProperty("inputSchema");
            var props = schema.GetProperty("properties");
            props.TryGetProperty("mode", out var modeProp).Should().BeTrue();
            modeProp.GetProperty("type").GetString().Should().Be("string");
            modeProp.TryGetProperty("enum", out var enumProp).Should().BeTrue();
            enumProp.EnumerateArray().Select(e => e.GetString()).Should().Contain(new[] { "world", "ui" });
            props.TryGetProperty("normalized", out var normalized).Should().BeTrue();
            normalized.GetProperty("type").GetString().Should().Be("boolean");
        }

        if (toolMap.TryGetValue("SetActive", out var setActive))
        {
            var schema = setActive.GetProperty("inputSchema");
            schema.TryGetProperty("required", out var required).Should().BeTrue();
            var requiredFields = required.EnumerateArray().Select(e => e.GetString()).ToArray();
            requiredFields.Should().Contain(new[] { "objectId", "active" });
            var props = schema.GetProperty("properties");
            props.TryGetProperty("objectId", out var objectId).Should().BeTrue();
            objectId.GetProperty("type").GetString().Should().Be("string");
            props.TryGetProperty("confirm", out var confirm).Should().BeTrue();
            confirm.GetProperty("type").GetString().Should().Be("boolean");
        }
    }

    [Fact]
    public async Task CallTool_GetStatus_JsonRpc_Response_If_Server_Available()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-status-test",
            method = "call_tool",
            @params = new
            {
                name = "GetStatus",
                arguments = new { }
            }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.TryGetProperty("error", out _).Should().BeFalse();
        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task CallTool_GetVersion_Returns_Version_Info_If_Server_Available()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-getversion-test",
            method = "call_tool",
            @params = new
            {
                name = "GetVersion",
                arguments = new { }
            }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
        if (contentArr.GetArrayLength() == 0)
            return;

        var first = contentArr[0];
        first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
        var version = jsonEl;
        version.TryGetProperty("ExplorerVersion", out _).Should().BeTrue();
        version.TryGetProperty("McpVersion", out _).Should().BeTrue();
        version.TryGetProperty("UnityVersion", out _).Should().BeTrue();
        version.TryGetProperty("Runtime", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CallTool_Returns_Text_And_Json_Content_For_Inspector_Clients()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-content-shape",
            method = "call_tool",
            @params = new
            {
                name = "GetStatus",
                arguments = new { }
            }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
        contentArr.GetArrayLength().Should().BeGreaterThan(0);

        var first = contentArr[0];
        first.GetProperty("type").GetString().Should().Be("text");
        first.TryGetProperty("mimeType", out var mimeType).Should().BeTrue();
        mimeType.GetString().Should().Be("application/json");
        first.TryGetProperty("text", out var text).Should().BeTrue();
        text.ValueKind.Should().Be(JsonValueKind.String);
        text.GetString().Should().NotBeNullOrWhiteSpace();
        first.TryGetProperty("json", out var jsonPayload).Should().BeTrue();
        jsonPayload.ValueKind.Should().Be(JsonValueKind.Object);

        var parsedFromText = JsonDocument.Parse(text.GetString()!).RootElement;
        parsedFromText.TryGetProperty("Ready", out _).Should().BeTrue();
        parsedFromText.TryGetProperty("ScenesLoaded", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CallTool_MousePick_Returns_Result_Shape_If_Server_Available()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-mousepick-test",
            method = "call_tool",
            @params = new
            {
                name = "MousePick",
                arguments = new { mode = "world" }
            }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
        if (contentArr.GetArrayLength() == 0)
            return;

        var first = contentArr[0];
        first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
        var pick = jsonEl;
        pick.TryGetProperty("Mode", out var mode).Should().BeTrue();
        mode.GetString()!.Should().NotBeNullOrWhiteSpace();
        pick.TryGetProperty("Hit", out var hit).Should().BeTrue();
        (hit.ValueKind == JsonValueKind.True || hit.ValueKind == JsonValueKind.False).Should().BeTrue();
        pick.TryGetProperty("Id", out _).Should().BeTrue();
        pick.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task CallTool_MousePick_Ui_Returns_Items_When_Available()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-mousepick-ui-test",
            method = "call_tool",
            @params = new
            {
                name = "MousePick",
                arguments = new { mode = "ui", x = 0.5, y = 0.5, normalized = true }
            }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
        if (contentArr.GetArrayLength() == 0)
            return;

        var first = contentArr[0];
        first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
        var pick = jsonEl;
        pick.TryGetProperty("Mode", out var mode).Should().BeTrue();
        mode.GetString().Should().Be("ui");
        pick.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
        if (items.GetArrayLength() < 1)
            return;

        pick.TryGetProperty("Id", out var id).Should().BeTrue();
        id.ValueKind.Should().Be(JsonValueKind.String);

        foreach (var item in items.EnumerateArray())
        {
            item.TryGetProperty("Id", out var iid).Should().BeTrue();
            iid.ValueKind.Should().Be(JsonValueKind.String);
            item.TryGetProperty("Name", out _).Should().BeTrue();
            item.TryGetProperty("Path", out _).Should().BeTrue();
        }

        var primaryId = id.GetString();
        if (!string.IsNullOrWhiteSpace(primaryId))
        {
            var followPayload = new
            {
                jsonrpc = "2.0",
                id = "call-tool-getobject-from-ui-pick",
                method = "call_tool",
                @params = new
                {
                    name = "GetObject",
                    arguments = new { id = primaryId }
                }
            };
            using var followRes = await JsonRpcTestClient.PostMessageAsync(client, followPayload, cts.Token);
            followRes.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task CallTool_ListObjects_PseudoScenes_Returns_Page_Shape()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        async Task AssertPageAsync(string sceneId)
        {
            var payload = new
            {
                jsonrpc = "2.0",
                id = $"call-tool-listobjects-{sceneId}",
                method = "call_tool",
                @params = new
                {
                    name = "ListObjects",
                    arguments = new { sceneId, limit = 5, offset = 0 }
                }
            };

            using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
            res.EnsureSuccessStatusCode();

            var body = await res.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            root.TryGetProperty("result", out var result).Should().BeTrue();
            result.TryGetProperty("content", out var contentArr).Should().BeTrue();
            contentArr.ValueKind.Should().Be(JsonValueKind.Array);
            contentArr.GetArrayLength().Should().BeGreaterThan(0);

            var first = contentArr[0];
            first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
            var page = jsonEl;
            page.TryGetProperty("Total", out var total).Should().BeTrue();
            (total.ValueKind == JsonValueKind.Number).Should().BeTrue();
            page.TryGetProperty("Items", out var items).Should().BeTrue();
            items.ValueKind.Should().Be(JsonValueKind.Array);
        }

        await AssertPageAsync("scn:ddol");
        await AssertPageAsync("scn:hide");
    }

    [Fact]
    public async Task CallTool_ListChildren_Returns_Page_Shape()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var seedPayload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-listobjects-for-children",
            method = "call_tool",
            @params = new
            {
                name = "ListObjects",
                arguments = new { limit = 1, offset = 0 }
            }
        };

        using var seedRes = await JsonRpcTestClient.PostMessageAsync(client, seedPayload, cts.Token);
        seedRes.EnsureSuccessStatusCode();

        var seedBody = await seedRes.Content.ReadAsStringAsync(cts.Token);
        using var seedDoc = JsonDocument.Parse(seedBody);
        var seedRoot = seedDoc.RootElement;

        seedRoot.TryGetProperty("result", out var seedResult).Should().BeTrue();
        seedResult.TryGetProperty("content", out var seedContentArr).Should().BeTrue();
        seedContentArr.ValueKind.Should().Be(JsonValueKind.Array);
        seedContentArr.GetArrayLength().Should().BeGreaterThan(0);

        var seedContent = seedContentArr[0];
        seedContent.TryGetProperty("json", out var seedJson).Should().BeTrue();
        seedJson.TryGetProperty("Items", out var seedItems).Should().BeTrue();
        seedItems.ValueKind.Should().Be(JsonValueKind.Array);
        seedItems.GetArrayLength().Should().BeGreaterThan(0);

        var firstItem = seedItems[0];
        var objectId = firstItem.GetProperty("Id").GetString();
        objectId.Should().NotBeNullOrWhiteSpace();

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-listchildren",
            method = "call_tool",
            @params = new
            {
                name = "ListChildren",
                arguments = new { objectId, limit = 5, offset = 0 }
            }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
        contentArr.GetArrayLength().Should().BeGreaterThan(0);

        var first = contentArr[0];
        first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
        var page = jsonEl;
        page.TryGetProperty("Total", out var total).Should().BeTrue();
        (total.ValueKind == JsonValueKind.Number).Should().BeTrue();
        page.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task CallTool_TailLogs_Returns_Items_Array_If_Server_Available()
    {
        if (!JsonRpcTestClient.TryCreate(out var http))
            return;

        using var client = http;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var payload = new
        {
            jsonrpc = "2.0",
            id = "call-tool-taillogs-test",
            method = "call_tool",
            @params = new
            {
                name = "TailLogs",
                arguments = new { count = 20 }
            }
        };

        using var res = await JsonRpcTestClient.PostMessageAsync(client, payload, cts.Token);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var contentArr).Should().BeTrue();
        contentArr.ValueKind.Should().Be(JsonValueKind.Array);
        if (contentArr.GetArrayLength() == 0)
            return;

        var first = contentArr[0];
        first.TryGetProperty("json", out var jsonEl).Should().BeTrue();
        var logs = jsonEl;
        logs.TryGetProperty("Items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
    }
}

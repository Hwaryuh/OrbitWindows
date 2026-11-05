using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Orbit.Core.Typing;

/// <summary>The text box as the engine is told about it: <c>TextContext</c> in the engine's protocol.</summary>
public sealed record EngineContext(int[] Selection, int[]? Marked, string Text, int Start)
{
    /// <summary>Nothing is ever composed away from the caret here.</summary>
    public bool MarksAtCaret => true;
}

/// <summary>A key as the engine takes it: <c>WindowsKey</c> in the engine's protocol.</summary>
public readonly record struct EngineKey(int Vk, int Scan, bool Extended = false, bool Shift = false, bool Ctrl = false, bool Alt = false, bool Win = false, bool CapsLock = false);

/// <summary>A change the engine wants: <c>EditOp</c> in the engine's protocol.</summary>
public sealed record EngineEdit(string Kind, string Text, int[]? Replace = null)
{
    public const string Mark = "mark";
    public const string Insert = "insert";
}

/// <summary>The engine's answer to a key.</summary>
public sealed record EngineReply(bool Handled, IReadOnlyList<EngineEdit> Edits, bool Replay);

/// <summary>What the typing logic needs from the engine, so it can be tested without the DLL.</summary>
public interface ITypingEngine
{
    EngineReply Key(EngineKey key, EngineContext context);

    /// <summary>The word stays as shown and the engine lets go of it.</summary>
    void LetGo();
}

/// <summary>
/// The Swift decision engine (src/TypingEngine), called in-process. Its state is global and unguarded:
/// call from one thread at a time, and keep one instance per process. Requests and answers are JSON
/// and hold what was typed, so they are never logged.
/// </summary>
public sealed unsafe class EngineBridge : ITypingEngine
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>What the build ships the engine as.</summary>
    public const string LibraryName = "TypingEngine.dll";

    private readonly delegate* unmanaged[Cdecl]<byte*, byte*> _request;
    private readonly delegate* unmanaged[Cdecl]<byte*, void> _free;
    private readonly int _client;

    private EngineBridge(nint library, string resources, string support)
    {
        var start = (delegate* unmanaged[Cdecl]<byte*, int>)NativeLibrary.GetExport(library, "engine_start");
        var warmUp = (delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(library, "engine_warm_up");
        _request = (delegate* unmanaged[Cdecl]<byte*, byte*>)NativeLibrary.GetExport(library, "engine_request");
        _free = (delegate* unmanaged[Cdecl]<byte*, void>)NativeLibrary.GetExport(library, "engine_free");

        byte[] config = Encode(JsonSerializer.Serialize(new
        {
            resources,
            support,
            version = "0",
            language = "ko-KR",
            ownApp = "orbit.app.exe",
            converter = false,
            platform = "windows",
        }, Json));
        fixed (byte* text = config)
        {
            if (start(text) != 0) throw new InvalidOperationException("The engine refused its configuration.");
        }
        warmUp();
        var answer = Ask(new Request("attach"));
        _client = answer.Reply?.Client ?? throw new InvalidOperationException("The engine gave no client.");
    }

    /// <summary>
    /// Loads the engine from <paramref name="folder"/>. Takes about a second. If this throws the
    /// feature stays off until Orbit restarts. The library stays loaded: it has no shutdown, and
    /// unloading a started Swift runtime is riskier than keeping it.
    /// </summary>
    public static EngineBridge Start(string folder, string support)
    {
        Directory.CreateDirectory(support);
        PrepareSettings(Path.Combine(support, "settings.json"));
        nint library = NativeLibrary.Load(Path.Combine(folder, LibraryName));
        return new EngineBridge(library, Path.Combine(folder, "Resources"), support);
    }

    /// <summary>
    /// Return on a word only confirms it by default, which is right for a composition. Here the word
    /// is already plain text, so Return has to reach the app. Other settings in the file are kept.
    /// </summary>
    internal static void PrepareSettings(string path)
    {
        JsonObject settings;
        try
        {
            settings = File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject found ? found : [];
        }
        catch (JsonException)
        {
            settings = [];
        }
        settings["returnConfirmsOnly"] = false;
        AtomicFile.WriteAllText(path, settings.ToJsonString());
    }

    public EngineReply Key(EngineKey key, EngineContext context)
    {
        var answer = Ask(new Request("key", _client, key, context, Claimed: true));
        var reply = answer.Reply;
        return new EngineReply(reply?.Handled ?? false, reply?.Ops ?? [], reply?.Replay ?? false);
    }

    public void LetGo() => Ask(new Request("terminated", _client));

    private Answer Ask(Request request)
    {
        byte[] text = Encode(JsonSerializer.Serialize(request, Json));
        byte* answer;
        fixed (byte* pointer = text) answer = _request(pointer);
        if (answer is null) throw new InvalidOperationException("The engine gave no answer.");
        try
        {
            var parsed = JsonSerializer.Deserialize<Answer>(Marshal.PtrToStringUTF8((nint)answer)!, Json)
                ?? throw new InvalidOperationException("The engine gave an empty answer.");
            if (parsed.Error is not null) throw new InvalidOperationException("The engine reported an error.");
            return parsed;
        }
        finally
        {
            _free(answer);
        }
    }

    private static byte[] Encode(string text)
    {
        byte[] bytes = new byte[Encoding.UTF8.GetByteCount(text) + 1];
        Encoding.UTF8.GetBytes(text, bytes);
        return bytes;
    }

    private sealed record Request(string Op, int? Client = null, EngineKey? Key = null, EngineContext? Context = null, bool? Claimed = null);

    private sealed record Answer(ReplyBody? Reply, string? Error);

    private sealed record ReplyBody(bool Handled, List<EngineEdit>? Ops, int? Client, bool? Replay);
}

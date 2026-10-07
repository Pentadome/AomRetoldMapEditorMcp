using System.Diagnostics;
using System.Text.Json;

namespace AomMcp;

/// <summary>Regenerates command catalogs and discovers review-only layouts using passive reads.</summary>
internal static class Generator
{
    /// <summary>Writes a catalog and attempts passive layout discovery without activating candidates.</summary>
    /// <param name="exe">Installed game executable path.</param>
    /// <param name="output">Directory for catalog, decoded UI, and candidate layout files.</param>
    /// <param name="ui">Existing decoded editor XML directory, or null to attempt CryBar extraction.</param>
    /// <param name="pid">Process to inspect, or null to require exactly one running game.</param>
    /// <remarks>Discovery failure still permits catalog output; guessed addresses are never emitted.</remarks>
    public static void Generate(string exe, string output, string? ui, int? pid)
    {
        Directory.CreateDirectory(output);
        ui ??= ExportUi(exe, output);
        var catalog = new Catalog(exe, ui);
        File.WriteAllText(
            Path.Combine(output, "catalog.json"),
            JsonSerializer.Serialize(
                new
                {
                    exeSha256 = Layout.Hash(exe),
                    commands = catalog.Commands.Values,
                    actions = catalog.Actions.Values,
                    tools = catalog.Tools(),
                    limitations = "Command metadata and UI action coverage regenerate from installed build. Native return is an acknowledgement, not semantic verification.",
                },
                Layout.Json
            )
        );
        try
        {
            var data = Path.Combine(output, "data");
            Directory.CreateDirectory(data);
            var crybar = FindCryBar() ?? throw new FileNotFoundException("CryBar required for game catalogs.");
            var archive = GameDataCatalog.Archive(exe);
            // Exact shipped definition paths; resource nodes are in proto, not map CRC resources.xml.
            var paths = new List<string>
            {
                "gameplay/proto.xml.XMB", "gameplay/techtree.xml.XMB",
                "gameplay/major_gods.xml.XMB", "gameplay/minor_gods.xml.XMB",
                "map_definitions/terrain_types.xml.XMB", "map_definitions/water_bodies.xml.XMB",
            };
            using var entries = JsonDocument.Parse(Run(crybar, ["bar", "list", archive, "--json"]));
            var powers = entries.RootElement.EnumerateArray().Select(e => e.GetProperty("Path").GetString()!)
                .Where(p => p.StartsWith("gameplay/god_powers/", StringComparison.Ordinal)
                    && p.EndsWith(".godpowers.XMB", StringComparison.Ordinal)).ToArray();
            if (powers.Length == 0)
                throw new InvalidDataException("No shipped godpower definitions found.");
            paths.AddRange(powers);
            // Forest/cliff definitions and terrain mixes (paint palette names/order) for editor_terrain_catalog.
            paths.Add("map_definitions/forest.xml.XMB");
            paths.Add("map_definitions/cliff_types.xml.XMB");
            var mixes = entries.RootElement.EnumerateArray().Select(e => e.GetProperty("Path").GetString()!)
                .Where(p => p.StartsWith("map_definitions/mixes/", StringComparison.Ordinal) && p.EndsWith(".xml.XMB", StringComparison.Ordinal)).ToArray();
            Directory.CreateDirectory(Path.Combine(data, "mixes"));
            foreach (var mix in mixes)
                _ = Run(crybar, ["bar", "export", archive, mix, "--decompress", "--convert", "-o",
                    Path.Combine(data, "mixes", Path.GetFileName(mix)[..^4])]);
            foreach (var path in paths)
                _ = Run(crybar, ["bar", "export", archive, path, "--decompress", "--convert", "-o",
                    Path.Combine(data, Path.GetFileName(path)[..^4])]); // Remove .XMB after decoding.
            GameDataCatalog.Generate(exe, output, data, powers.Select(p => Path.GetFileName(p)[..^4]).ToArray());
        }
        catch (Exception e)
        {
            Console.WriteLine("Game catalog metadata unavailable: " + e.Message);
        }
        try
        {
            var layout = Discover(exe, pid);
            var candidates = Path.Combine(output, "candidates");
            Directory.CreateDirectory(candidates);
            var file = Path.Combine(candidates, layout.ExeSha256 + ".json");
            File.WriteAllText(file, JsonSerializer.Serialize(layout, Layout.Json));
            Console.WriteLine(
                $"Discovered candidate layout: {file}. Review/validate before using --layout on an unknown build; no automatic activation."
            );
        }
        catch (Exception e)
        {
            Console.WriteLine(
                "Layout discovery unavailable: "
                    + e.Message
                    + ". Catalog generated; no guessed addresses emitted."
            );
        }
        Console.WriteLine(
            $"Generated {catalog.Commands.Count} command tools and {catalog.Actions.Count} shipped UI actions in {output}."
        );
    }

    static string? FindCryBar()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "crybar", "crybar.exe");
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    static string? ExportUi(string exe, string output)
    {
        var crybar = FindCryBar();
        if (crybar == null)
        {
            Console.WriteLine(
                "CryBar not found. Supply --ui with decoded editor XML to generate menu/dialog action tools."
            );
            return null;
        }
        // Installed editor UI archive and .xml.XMB suffixes come from game/ui, not a public SDK.
        // CryBar CLI subcommands/options/JSON "Path" come from CryBar's bar list/export interface.
        var archive = Path.Combine(
            Path.GetDirectoryName(exe)!,
            "game",
            "ui",
            "UIDefaultEditor.bar"
        );
        var entries = JsonDocument.Parse(Run(crybar, ["bar", "list", archive, "--json"]));
        var directory = Path.Combine(output, "ui");
        Directory.CreateDirectory(directory);
        foreach (var entry in entries.RootElement.EnumerateArray())
        {
            var path = entry.GetProperty("Path").GetString()!;
            if (
                !path.EndsWith(".xml.XMB", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            )
                continue;
            var basename = Path.GetFileName(path.Replace('\\', '/'));
            // Drop four chars (".XMB") after --convert decodes binary XML into text XML.
            if (basename.EndsWith(".XMB", StringComparison.OrdinalIgnoreCase))
                basename = basename[..^4];
            _ = Run(
                crybar,
                [
                    "bar",
                    "export",
                    archive,
                    path,
                    "--decompress",
                    "--convert",
                    "-o",
                    Path.Combine(directory, basename),
                ]
            );
        }
        entries.Dispose();
        return directory;
    }

    static string Run(string exe, string[] args)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var p =
            Process.Start(info) ?? throw new InvalidOperationException("Could not start CryBar.");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        // Host policy: extraction subprocess gets 30 s; not a CryBar format requirement.
        if (!p.WaitForExit(30000))
        {
            p.Kill(true);
            throw new TimeoutException("CryBar timed out.");
        }
        if (p.ExitCode != 0)
            throw new InvalidOperationException(stderr.GetAwaiter().GetResult());
        return stdout.GetAwaiter().GetResult();
    }

    internal static Layout Discover(string exe, int? pid)
    {
        var image = new PeImage(exe);
        using var process = pid.HasValue ? Process.GetProcessById(pid.Value) : OneProcess();
        if (
            !string.Equals(
                Path.GetFullPath(process.MainModule!.FileName),
                Path.GetFullPath(exe),
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new InvalidOperationException("Wrong live image.");
        // Windows SDK PROCESS_QUERY_INFORMATION (0x400) | PROCESS_VM_READ (0x10); no writes/debugger.
        using var handle = Win.OpenProcess(0x410, false, (uint)process.Id);
        if (handle.IsInvalid)
            throw new InvalidOperationException("Read access unavailable.");
        var @base = process.MainModule.BaseAddress;
        byte[] Read(int rva, int size)
        {
            if (rva < 0 || size < 0 || (long)rva + size > image.ImageSize)
                throw new InvalidDataException("Candidate outside image.");
            var bytes = new byte[size];
            Win.Check(
                Win.ReadProcessMemory(handle, @base + rva, bytes, (nuint)size, out var n)
                    && n == (nuint)size,
                "Read runtime code"
            );
            return bytes;
        }
        byte[] At(nint address, int size)
        {
            var bytes = new byte[size];
            Win.Check(
                Win.ReadProcessMemory(handle, address, bytes, (nuint)size, out var n)
                    && n == (nuint)size,
                "Read candidate context"
            );
            return bytes;
        }
        // PE/COFF's conventional .text section contains code; inspect live, not transformed disk bytes.
        // Opcode forms below are AMD64 encodings observed in this retail compiler build
        // (research/runtime-candidates.json, research/LIVE-FINDINGS.md), not stable engine ABI signatures.
        var section = image.Sections.Single(s => s.Name == ".text");
        var text = Read(section.Rva, section.VirtualSize);
        int Target(string name)
        {
            var names = image.Names(name).ToHashSet();
            var targets = new HashSet<int>();
            for (var i = 0; i + 7 < text.Length; i++)
            {
                // 48 8D 15 disp32 = lea rdx,[rip+disp32]; 7 bytes, displacement starts +3.
                if (text[i] != 0x48 || text[i + 1] != 0x8d || text[i + 2] != 0x15)
                    continue; // name in RDX, not error-message R8.
                var destination = section.Rva + i + 7 + BitConverter.ToInt32(text, i + 3);
                if (!names.Contains(destination))
                    continue;
                // Host search windows: 64 preceding bytes for R8 argument, up to 4096 for stack stores.
                // R8 source: 4C 8D 05 = lea r8,[rip+disp32]; 4C 8B 84/44 24 = mov r8,[rsp+disp32/8].
                var r8 = -1;
                for (var j = Math.Max(0, i - 64); j < i; j++)
                    if (
                        text[j] == 0x4c
                        && (
                            (text[j + 1] == 0x8d && text[j + 2] == 0x05)
                            || (
                                text[j + 1] == 0x8b
                                && text[j + 2] is 0x84 or 0x44
                                && text[j + 3] == 0x24
                            )
                        )
                    )
                        r8 = j;
                if (r8 < 0)
                    continue;
                if (text[r8 + 1] == 0x8d)
                    targets.Add(section.Rva + r8 + 7 + BitConverter.ToInt32(text, r8 + 3));
                else
                {
                    // ModRM 84 uses 32-bit stack displacement; 44 uses 8-bit; both have SIB 24.
                    var slot =
                        text[r8 + 2] == 0x84 ? BitConverter.ToInt32(text, r8 + 4) : text[r8 + 4];
                    // 48 89 84/44 24 stores RAX into that stack slot; preceding 7-byte
                    // 48 8D 05 lea rax,[rip+disp32] supplies recovered native target.
                    for (var j = Math.Max(7, i - 4096); j + 8 <= r8; j++)
                    {
                        if (
                            text[j] != 0x48
                            || text[j + 1] != 0x89
                            || text[j + 3] != 0x24
                            || text[j + 2] is not (0x84 or 0x44)
                        )
                            continue;
                        var stored =
                            text[j + 2] == 0x84 ? BitConverter.ToInt32(text, j + 4) : text[j + 4];
                        if (
                            stored == slot
                            && text[j - 7] == 0x48
                            && text[j - 6] == 0x8d
                            && text[j - 5] == 0x05
                        )
                            targets.Add(section.Rva + j + BitConverter.ToInt32(text, j - 4));
                    }
                }
            }
            if (targets.Count != 1)
                throw new InvalidDataException(
                    $"Cannot uniquely recover {name} native target ({targets.Count} candidates)."
                );
            return targets.Single();
        }
        static int Find(byte[] b, byte[] prefix, int start = 0)
        {
            var i = b.AsSpan(start).IndexOf(prefix);
            if (i < 0)
                throw new InvalidDataException(
                    "Unrecognized compiler pattern; manual layout update required."
                );
            return start + i;
        }
        // AMD64 RIP-relative MOV/LEA here is 7 bytes with signed disp32 at +3 (Intel encoding).
        static int Rip(byte[] b, int at, int function) =>
            function + at + 7 + BitConverter.ToInt32(b, at + 3);
        // Names come from executable registration/help; bounded reads 96/512/128 bytes below
        // were chosen to cover the observed wrapper/placement/mode helper, not fixed function lengths.
        int wrapper = Target("trExecuteConsoleCommand"),
            placement = Target("uiPlaceAtPointer");
        byte[] code = Read(wrapper, 96),
            place = Read(placement, 512);
        // 48 8B F9 = mov rdi,rcx; 48 8B D7 = mov rdx,rdi. Observed string-forwarding wrapper.
        _ = Find(code, [0x48, 0x8b, 0xf9]);
        _ = Find(code, [0x48, 0x8b, 0xd7]); // RCX string -> RDI -> RDX executor.
        // 48 8B 1D = mov rbx,[rip+disp32] loads globals; 48 8B 9B = mov rbx,[rbx+disp32]
        // loads editor pointer. E8 rel32 is CALL (5 bytes, displacement +1).
        var context = Rip(code, Find(code, [0x48, 0x8b, 0x1d]), wrapper);
        var gameLoad = Find(place, [0x48, 0x8b, 0x1d]);
        var editorGlobal = Rip(place, gameLoad, placement);
        var editorPtr = BitConverter.ToInt32(place, Find(place, [0x48, 0x8b, 0x9b], gameLoad) + 3);
        var checkCall = Find(place, [0xe8], gameLoad + 7);
        var helper = placement + checkCall + 5 + BitConverter.ToInt32(place, checkCall + 1);
        var mode = Read(helper, 128);
        // 80 B9 = cmp byte [rcx+disp32],imm8: mode flag displacement +2.
        // 8B B3 = mov esi,[rbx+disp32]: player +2; 44 8B 83 = mov r8d,[rbx+disp32]: proto +3.
        var flag = BitConverter.ToInt32(mode, Find(mode, [0x80, 0xb9]) + 2);
        var playerOffset = BitConverter.ToInt32(place, Find(place, [0x8b, 0xb3]) + 2);
        var protoOffset = BitConverter.ToInt32(place, Find(place, [0x44, 0x8b, 0x83]) + 3);
        int pointerGlobal = -1,
            hoverOffset = -1;
        // 14-byte pattern: 48 8B 05 disp32 loads pointer into RAX, then 80 B8 disp32 00
        // compares a hover byte to zero. Second displacement +9, final immediate +13.
        for (var i = 0; i + 14 < place.Length; i++)
            if (
                place[i] == 0x48
                && place[i + 1] == 0x8b
                && place[i + 2] == 0x05
                && place[i + 7] == 0x80
                && place[i + 8] == 0xb8
                && place[i + 13] == 0
            )
            {
                var offset = BitConverter.ToInt32(place, i + 9);
                // ponytail: largest matching displacement is an observed-build heuristic;
                // use explicit disassembly/matching if compiler changes. Candidate still needs review.
                if (offset > hoverOffset)
                {
                    hoverOffset = offset;
                    pointerGlobal = Rip(place, i, placement);
                }
            }
        // -1 means no candidate; 32768 is host field-offset safety ceiling, not recovered object size.
        if (
            pointerGlobal < 0
            || flag is < 0 or > 32768
            || editorPtr is < 0 or > 32768
            || playerOffset == protoOffset
        )
            throw new InvalidDataException("Incomplete editor-layout patterns.");
        nint window = 0;
        uint thread = 0;
        Win.Enumerate callback = (hwnd, _) =>
        {
            var tid = Win.GetWindowThreadProcessId(hwnd, out var owner);
            // Host-sized class-name buffer; MythRetold is observed retail class (same as Game).
            var cls = new char[256];
            var length = owner == process.Id ? Win.GetClassName(hwnd, cls, cls.Length) : 0;
            if (length > 0 && new string(cls, 0, length) == "MythRetold")
            {
                window = hwnd;
                thread = tid;
            }
            return true;
        };
        Win.Check(Win.EnumWindows(callback, 0), "EnumWindows");
        // AMD64 pointers are 8 bytes; context +4 DWORD owner and +8 endpoint are recovered
        // assumptions validated against live thread/non-null pointer (Layout defaults; LIVE-FINDINGS.md).
        var contextPtr = (nint)BitConverter.ToUInt64(Read(context, 8));
        if (
            window == 0
            || contextPtr == 0
            || BitConverter.ToUInt32(At(contextPtr + 4, 4)) != thread
            || BitConverter.ToUInt64(At(contextPtr + 8, 8)) == 0
        )
            throw new InvalidDataException(
                "Candidate command context does not match live window thread."
            );
        // Recovered one-byte editor flag's active value is 1, independently observed during research.
        var game = (nint)BitConverter.ToUInt64(Read(editorGlobal, 8));
        if (
            game == 0
            || At(game + flag, 1)[0] != 1
            || BitConverter.ToUInt64(At(game + editorPtr, 8)) == 0
        )
            throw new InvalidDataException("Open scenario editor for layout discovery.");
        return new Layout
        {
            ExeSha256 = Layout.Hash(exe),
            FileVersion = FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "",
            DispatcherRva = (uint)wrapper,
            ContextRva = (uint)context,
            EditorGlobalRva = (uint)editorGlobal,
            EditorFlagOffset = (uint)flag,
            EditorPointerOffset = (uint)editorPtr,
            PointerGlobalRva = (uint)pointerGlobal,
            HoverOffset = (uint)hoverOffset,
            ProtoOffset = (uint)protoOffset,
            PlayerOffset = (uint)playerOffset,
            // Host signature choice: first 32 live bytes, within Layout's 16..64 guard range.
            PrefixHex = Convert.ToHexString(Read(wrapper, 32)),
            Provenance =
                "PASSIVE CANDIDATE: runtime registration/name references, narrow compiler patterns, thread/context checks. Owner/endpoint offsets 4/8 are validated against live thread but ABI/editing on a new build still needs review. No automatic activation; no engine calls during discovery.",
        };
    }

    static Process OneProcess()
    {
        // Verified installed executable basename without .exe (Process.GetProcessesByName contract).
        var p = Process.GetProcessesByName("AoMRT_s");
        if (p.Length == 1)
            return p[0];
        foreach (var x in p)
            x.Dispose();
        throw new InvalidOperationException("Expected one game process or specify --pid.");
    }
}

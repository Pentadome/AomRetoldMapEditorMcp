using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

// SHA-256 of inspected AoMRT_s.exe 100.19.17020.0; research/evidence.json pins this one build.
const string knownHash = "dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff";
// Observed local Steam installation; positional exe-path overrides it.
const string defaultExe =
    @"C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\AoMRT_s.exe";

if (args is ["--self-test"])
{
    // Byte alphabet has 256 values; constant data entropy=0, uniform byte entropy=log2(256)=8 bits.
    Require(Entropy(new byte[256]) == 0, "constant entropy");
    Require(
        Entropy(Enumerable.Range(0, 256).Select(x => (byte)x).ToArray()) == 8,
        "uniform entropy"
    );
    Require(Count(Encoding.ASCII.GetBytes("\0test\0test\0"), "test") == 2, "adjacent anchors");
    Require(Count(Encoding.ASCII.GetBytes("\0testing\0"), "test") == 0, "exact anchors");
    Console.WriteLine("Self-test passed.");
    return;
}
if (args.Length > 2 || (args.Length == 2 && (!int.TryParse(args[1], out var id) || id <= 0)))
    throw new ArgumentException("Usage: Probe [exe-path [pid]] | --self-test");

var exe = Path.GetFullPath(args.Length > 0 ? args[0] : defaultExe);
var bytes = File.ReadAllBytes(exe);
var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
using var pe = new PEReader(new MemoryStream(bytes, writable: false));
var headers = pe.PEHeaders;
var header = headers.PEHeader ?? throw new BadImageFormatException("Missing PE header.");
var sections = headers
    .SectionHeaders.Select(s => new
    {
        s.Name,
        rva = $"0x{s.VirtualAddress:x}",
        s.VirtualSize,
        s.SizeOfRawData,
        entropy = Math.Round(Entropy(bytes.AsSpan(s.PointerToRawData, s.SizeOfRawData)), 4), // Host display precision: 4 decimals.
        flags = s.SectionCharacteristics.ToString(),
    })
    .ToArray();
// Candidate editor/XS names from installed documentation/help; counting an anchor does not prove
// registration or callable ABI (xsExecute was a searched hypothesis, not an established evaluator).
var anchors = new[]
{
    "uiPlaceAtPointer",
    "uiChangeBrushSize",
    "uiSaveScenarioPrompt",
    "saveScenario",
    "loadScenario",
    "trExecuteConsoleCommand",
    "xsExecute",
}.ToDictionary(name => name, name => Count(bytes, name));
int exportFunctions = 0,
    exportNames = 0;
if (header.ExportTableDirectory.Size > 0)
{
    var export = pe.GetSectionData(header.ExportTableDirectory.RelativeVirtualAddress).GetReader();
    // Microsoft PE/COFF IMAGE_EXPORT_DIRECTORY: NumberOfFunctions at +20, NumberOfNames at +24.
    export.Offset = 20;
    exportFunctions = export.ReadInt32();
    exportNames = export.ReadInt32();
}
var samples = new Dictionary<string, int> { ["entryPoint"] = header.AddressOfEntryPoint };

// 0x2ddc460 came from this hash's PE exported correlation-vector constructor (research/evidence.json).
// This RVA is evidence for one inspected build, never a callable engine address.
if (hash == knownHash)
    samples["exportedCorrelationVectorConstructor"] = 0x2ddc460;
var live = args.Length == 2 ? ReadLive(int.Parse(args[1])) : null;
Console.WriteLine(
    JsonSerializer.Serialize(
        new
        {
            exe,
            sha256 = hash,
            version = FileVersionInfo.GetVersionInfo(exe).FileVersion,
            machine = headers.CoffHeader.Machine.ToString(),
            native = headers.CorHeader is null,
            imageBase = $"0x{header.ImageBase:x}",
            entryPointRva = $"0x{header.AddressOfEntryPoint:x}",
            flags = header.DllCharacteristics.ToString(),
            exportFunctions,
            exportNames,
            sections,
            anchors,
            runningPids = Process.GetProcessesByName("AoMRT_s").Select(p => p.Id).ToArray(),
            // Host-selected 32-byte samples compare disk/live prefixes, not entire function bodies.
            samples = samples.ToDictionary(
                p => p.Key,
                p => new
                {
                    rva = $"0x{p.Value:x}",
                    diskHex = Convert.ToHexStringLower(
                        pe.GetSectionData(p.Value).GetContent(0, 32).AsSpan()
                    ),
                }
            ),
            live,
        },
        new JsonSerializerOptions { WriteIndented = true }
    )
);

object ReadLive(int pid)
{
    try
    {
        using var process = Process.GetProcessById(pid);
        var module = process.MainModule ?? throw new InvalidOperationException("No main module.");
        Require(
            string.Equals(
                Path.GetFullPath(module.FileName),
                exe,
                StringComparison.OrdinalIgnoreCase
            ),
            "PID executable does not match inspected file."
        );
        // Windows SDK: PROCESS_QUERY_INFORMATION=0x0400, PROCESS_VM_READ=0x0010.
        // Query + read only. No debugger, writes, remote threads, or DLL loading.
        using var handle = Native.OpenProcess(0x0400 | 0x0010, false, pid);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var results = samples.ToDictionary(
            p => p.Key,
            p =>
            {
                var memory = new byte[32];
                var ok = Native.ReadProcessMemory(
                    handle,
                    module.BaseAddress + p.Value,
                    memory,
                    (nuint)memory.Length,
                    out var read
                );
                if (!ok)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                Require(read == (nuint)memory.Length, "Short memory read.");
                return new
                {
                    memoryHex = Convert.ToHexStringLower(memory),
                    matchesDisk = memory
                        .AsSpan()
                        .SequenceEqual(pe.GetSectionData(p.Value).GetContent(0, 32).AsSpan()),
                };
            }
        );
        return new
        {
            pid,
            readAccess = true,
            samples = results,
        };
    }
    catch (Exception e)
        when (e
                is Win32Exception
                    or InvalidOperationException
                    or ArgumentException
                    or NotSupportedException
        )
    {
        return new
        {
            pid,
            readAccess = false,
            error = e.Message,
        };
    }
}

static double Entropy(ReadOnlySpan<byte> bytes)
{
    if (bytes.IsEmpty)
        return 0;
    Span<int> counts = stackalloc int[256]; // One counter per possible 8-bit byte value.
    counts.Clear();
    foreach (var b in bytes)
        counts[b]++;
    double result = 0;
    foreach (var count in counts)
        if (count > 0)
        {
            var p = (double)count / bytes.Length;
            result -= p * Math.Log2(p);
        }
    return result;
}

static int Count(ReadOnlySpan<byte> bytes, string name)
{
    // Native strings are NUL-delimited; both boundaries exclude longer names like "testing".
    var pattern = Encoding.ASCII.GetBytes("\0" + name + "\0");
    int count = 0,
        index;
    while ((index = bytes.IndexOf(pattern)) >= 0)
    {
        count++;
        bytes = bytes[(index + 1)..];
    }
    return count;
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

/// <summary>Query/read-only Windows process interop for the passive executable probe.</summary>
static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(
        uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        int pid
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(
        SafeProcessHandle process,
        nint address,
        [Out] byte[] buffer,
        nuint size,
        out nuint bytesRead
    );
}

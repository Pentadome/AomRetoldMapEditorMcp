#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
#include <cwchar>
#include <cstring>

// Host-defined ASCII "AOMC" numeric tag and private v1 message name; match src/AomMcp/Bridge.cs.
// RegisterWindowMessageW assigns the actual Windows message ID; Magic is not that ID or a game value.
constexpr DWORD Magic = 0x414f4d43;
constexpr wchar_t MessageName[] = L"AomEditorMcp.Dispatch.v1";
// Byte-packed private ABI (no alignment padding), serialized by Bridge.Execute/SelfTest.
#pragma pack(push, 1)
struct Packet {
    DWORD magic, version, size, pid;
    DWORD thread, nonce;
    volatile LONG state; // Host ABI: 0=pending, -1=claimed, 1=finished (one-shot).
    DWORD result; // 1=dispatcher returned; 2..11=Execute refusal codes, not OS errors.
    DWORD dispatcher, context, ownerOffset, endpointOffset;
    DWORD editorGlobal, editorFlagOffset, editorPointerOffset, prefixSize;
    DWORD pointerGlobal, hoverOffset, flags, payloadSize; // flags: 1=map-hover guard, 2=proto/player guard.
    DWORD protoOffset, playerOffset, expectedProto, expectedPlayer;
    ULONGLONG deadline; // Offset 96: GetTickCount64 milliseconds, shared with host.
    DWORD actualThread, callsStarted; // Offsets 104/108; callsStarted=1 immediately before dispatch.
    unsigned char prefix[64]; // Offset 112; host-chosen maximum runtime signature length.
    char payload[16384]; // Offset 176; host-chosen 16 KiB ANSI buffer, including final NUL.
};
#pragma pack(pop)
static_assert(sizeof(Packet) == 16560); // 176 + 16384; must equal Bridge.PacketSize.

static bool Read(const void* address, void* out, SIZE_T size) {
    SIZE_T n = 0;
    return ReadProcessMemory(GetCurrentProcess(), address, out, size, &n) && n == size;
}

// Host supplies a build-pinned, validated layout and an allowlisted, escaped command.
// The MCP client cannot supply addresses or raw console text.
// Private refusal codes 2..11 map to Bridge.Reason; 1 means acknowledgement only.
// Layout values are recovered per build (layouts/<SHA-256>.json), never guessed platform constants.
static DWORD Execute(Packet* p, HWND window) {
    if (p->deadline < GetTickCount64()) return 2; // Never start a timed-out request later.
    if (p->payloadSize == 0 || p->payloadSize >= sizeof(p->payload) ||
        p->payload[p->payloadSize] != 0 || std::memchr(p->payload, 0, p->payloadSize) != nullptr ||
        p->prefixSize == 0 || p->prefixSize > sizeof(p->prefix)) return 3;
    // Retail Steam executable basename, verified against the host's configured executable/hash.
    auto* base = reinterpret_cast<unsigned char*>(GetModuleHandleW(L"AoMRT_s.exe"));
    MODULEINFO module{};
    if (base == nullptr || !GetModuleInformation(GetCurrentProcess(), reinterpret_cast<HMODULE>(base), &module, sizeof(module))) return 4;
    // Host safety ceilings: 4 KiB for context fields, 32 KiB for editor/pointer-object fields.
    // Not recovered object sizes; matching runtime signatures and hash are still required.
    if (p->dispatcher >= module.SizeOfImage || p->context > module.SizeOfImage - sizeof(void*) ||
        p->editorGlobal > module.SizeOfImage - sizeof(void*) || p->prefixSize > module.SizeOfImage - p->dispatcher ||
        p->ownerOffset > 4096 || p->endpointOffset > 4096 || p->editorFlagOffset > 32768 ||
        p->editorPointerOffset > 32768 || p->protoOffset > 32768 || p->playerOffset > 32768) return 5;
    unsigned char actual[64];
    if (!Read(base + p->dispatcher, actual, p->prefixSize) || std::memcmp(actual, p->prefix, p->prefixSize) != 0) return 6;
    unsigned char* context = nullptr;
    DWORD owner = 0;
    void* endpoint = nullptr;
    if (!Read(base + p->context, &context, sizeof(context)) || context == nullptr ||
        !Read(context + p->ownerOffset, &owner, sizeof(owner)) || owner != GetCurrentThreadId() ||
        !Read(context + p->endpointOffset, &endpoint, sizeof(endpoint)) || endpoint == nullptr) return 7;
    unsigned char* game = nullptr;
    unsigned char* editor = nullptr;
    unsigned char editorFlag = 0;
    // Editor flag is one byte with active value 1; observed in research/LIVE-FINDINGS.md.
    if (!Read(base + p->editorGlobal, &game, sizeof(game)) || game == nullptr ||
        !Read(game + p->editorFlagOffset, &editorFlag, 1) || editorFlag != 1 ||
        !Read(game + p->editorPointerOffset, &editor, sizeof(editor)) || editor == nullptr) return 8;
    if (GetForegroundWindow() != window) return 9;
    if ((p->flags & 1) != 0) {
        unsigned char* pointer = nullptr;
        unsigned char hover = 0;
        if (p->pointerGlobal > module.SizeOfImage - sizeof(void*) || p->hoverOffset > 32768 ||
            !Read(base + p->pointerGlobal, &pointer, sizeof(pointer)) || pointer == nullptr ||
            !Read(pointer + p->hoverOffset, &hover, 1) || hover == 0) return 10;
    }
    if ((p->flags & 2) != 0) {
        DWORD proto = 0, player = 0;
        if (!Read(editor + p->protoOffset, &proto, sizeof(proto)) || proto != p->expectedProto ||
            !Read(editor + p->playerOffset, &player, sizeof(player)) || player != p->expectedPlayer) return 11;
    }
    if ((p->flags & ~3u) != 0) return 3; // Only the two documented guard bits (mask 1|2) exist.
    p->callsStarted = 1;
    // Do not catch and hide engine access violations: state would not be trustworthy afterward.
    reinterpret_cast<void (*)(const char*)>(base + p->dispatcher)(p->payload);
    return 1; // Native return acknowledgement, not semantic success or a command's return value.
}

extern "C" __declspec(dllexport) LRESULT CALLBACK EditorHook(int code, WPARAM wParam, LPARAM lParam) {
    if (code >= 0 && lParam != 0) {
        const auto* m = reinterpret_cast<const CWPSTRUCT*>(lParam);
        const UINT message = RegisterWindowMessageW(MessageName);
        if (message != 0 && m->message == message && m->wParam == Magic) {
            wchar_t name[100]; // Host-sized buffer for session-local mapping name + PID + 8-digit nonce.
            const DWORD nonce = static_cast<DWORD>(m->lParam);
            const DWORD pid = GetCurrentProcessId();
            // Win32 Local\\ namespace = this session; format must match Bridge's decimal PID/x8 nonce.
            // Standard bounded formatter works with both MSVC and MinGW; buffer size is in wchar_t units.
            if (std::swprintf(name, sizeof(name) / sizeof(*name), L"Local\\AomEditorMcp-%lu-%08lx", pid, nonce) > 0) {
                HANDLE mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, name);
                if (mapping != nullptr) {
                    auto* p = static_cast<Packet*>(MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(Packet)));
                    if (p != nullptr) {
                        // Private protocol version 1; CAS 0 -> -1 claims request, final state 1 completes it.
                        if (p->magic == Magic && p->version == 1 && p->size == sizeof(Packet) && p->pid == pid &&
                            p->nonce == nonce && p->thread == GetCurrentThreadId() && InterlockedCompareExchange(&p->state, -1, 0) == 0) {
                            p->actualThread = GetCurrentThreadId();
                            p->result = Execute(p, m->hwnd);
                            InterlockedExchange(&p->state, 1);
                        }
                        UnmapViewOfFile(p);
                    }
                    CloseHandle(mapping);
                }
            }
        }
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

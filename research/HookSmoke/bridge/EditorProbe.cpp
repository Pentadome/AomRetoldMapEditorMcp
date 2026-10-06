#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cwchar>
#include <cstdio>
#include <cstring>

// Host-defined ASCII "AOMP" numeric marker and v2 message name; match HookSmoke/Program.cs.
// RegisterWindowMessageW assigns OS message ID separately; these are not engine constants.
constexpr DWORD Magic = 0x414f4d50;
constexpr wchar_t MessageName[] = L"AomEditorProbe.Handshake.v2";

// Private AMD64 probe ABI: DWORD fields at 0..20, uint64 moduleBase at 24, DWORDs at 32/36.
struct Result {
    DWORD magic;
    DWORD targetPid;
    DWORD nonce;
    volatile LONG status; // Private state machine: 0=pending, -1=claimed, 1=finished.
    DWORD targetThreadId;
    DWORD actualThreadId;
    unsigned long long moduleBase;
    DWORD operation; // 0 = identity, 1 = echo, 2 = prepare villager, 3 = place once, 4 = finish tool.
    DWORD engineResult; // 0=not requested, 1=returned; 2..9 are private RunOperation refusal codes.
};
static_assert(sizeof(Result) == 40); // 24-byte prefix + 8-byte module base + 8-byte action/result.

static bool ReadSelf(const void* address, void* output, SIZE_T size) {
    SIZE_T read = 0;
    return ReadProcessMemory(GetCurrentProcess(), address, output, size, &read) && read == size;
}

static DWORD RunOperation(DWORD operation, DWORD nonce, HWND window) {
    if (operation < 1 || operation > 4) return 2;
    auto* base = reinterpret_cast<unsigned char*>(GetModuleHandleW(L"AoMRT_s.exe"));
    if (base == nullptr) return 3;
    // Addresses/prefix below recovered from AoMRT_s.exe SHA-256 dd15d1...b84fff, version 100.19.17020.0;
    // research/runtime-candidates.json, LIVE-FINDINGS.md and accepted layouts/<SHA-256>.json record evidence.
    // Dispatcher RVA=0x2062ec0, context global=0x542bd10; expected is first 32 live dispatcher bytes.
    // Host pins executable SHA-256; also reject unexpected runtime code and context.
    constexpr unsigned char expected[] = {
        0x48, 0x89, 0x5c, 0x24, 0x08, 0x57, 0x48, 0x83, 0xec, 0x30, 0x48, 0x8b, 0x1d, 0x3f, 0x8e, 0x3c,
        0x03, 0x48, 0x8b, 0xf9, 0x48, 0x85, 0xdb, 0x74, 0x1c, 0x48, 0x8b, 0xcb, 0xe8, 0xbf, 0x28, 0xb5
    };
    unsigned char actual[sizeof(expected)];
    if (!ReadSelf(base + 0x2062ec0, actual, sizeof(actual)) || std::memcmp(actual, expected, sizeof(actual)) != 0)
        return 4;
    unsigned char* context = nullptr;
    // Recovered context layout: DWORD owner thread at +4, AMD64 endpoint pointer at +8.
    struct Context { DWORD other; DWORD ownerThread; void* endpoint; } state{};
    if (!ReadSelf(base + 0x542bd10, &context, sizeof(context)) || context == nullptr ||
        !ReadSelf(context, &state, sizeof(state)) || state.endpoint == nullptr || state.ownerThread != GetCurrentThreadId())
        return 5;
    auto dispatch = reinterpret_cast<void (*)(const char*)>(base + 0x2062ec0);
    if (operation == 1) {
        char command[64]; // Host buffer for fixed echo expression; AOM_MCP_PROBE_ is an arbitrary test marker.
        if (sprintf_s(command, "echo(\"AOM_MCP_PROBE_%08lx\")", nonce) <= 0) return 6;
        dispatch(command); // Retail echo is a no-op.
        return 1;
    }
    // Recovered build-specific fields: game global RVA 0x53e4de8, editor flag byte +0x909
    // (1=active), editor pointer +0x6d0; same accepted layout/evidence as dispatcher above.
    // uiPlaceAtPointer assumes a live editor; fail closed before touching world state.
    unsigned char* game = nullptr;
    unsigned char* editor = nullptr;
    unsigned char editorFlag = 0;
    if (GetForegroundWindow() != window) return 7;
    if (!ReadSelf(base + 0x53e4de8, &game, sizeof(game)) || game == nullptr ||
        !ReadSelf(game + 0x909, &editorFlag, 1) || editorFlag != 1 ||
        !ReadSelf(game + 0x6d0, &editor, sizeof(editor)) || editor == nullptr) return 8;
    if (operation == 2) {
        // Shipped editor.con/native help provide mode/command names; player 1 is this test's owner.
        // VillagerGreek is installed proto name, not a guessed runtime prototype ID.
        dispatch("editMode(\"PlaceUnit\")");
        dispatch("uiSetPlacementPlayer(1)");
        dispatch("uiSetProtoCursor(\"VillagerGreek\", true)");
    } else if (operation == 3) {
        // Recovered editor proto/player fields +0x3f4/+0x600; -1 means no cursor/player.
        // Pointer global RVA 0x53e52b0 and hover byte +0xdf0; live placement evidence corrected swapped offsets.
        int proto = -1;
        int player = -1;
        unsigned char* pointerState = nullptr;
        unsigned char overMap = 0;
        if (!ReadSelf(editor + 0x3f4, &proto, sizeof(proto)) || proto < 0 ||
            !ReadSelf(editor + 0x600, &player, sizeof(player)) || player != 1 ||
            !ReadSelf(base + 0x53e52b0, &pointerState, sizeof(pointerState)) || pointerState == nullptr ||
            !ReadSelf(pointerState + 0xdf0, &overMap, 1) || overMap == 0) return 9;
        dispatch("uiPlaceAtPointer(false)");
    } else {
        dispatch("uiClearCursor()");
        dispatch("editMode(\"None\")"); // Standard Escape sequence; remove preview, keep placed unit.
    }
    return 1; // Wrapper return, not independently verified editing success.
}

// Temporary fixed-action proof. No raw commands/RVAs or executable-memory patches.
extern "C" __declspec(dllexport) LRESULT CALLBACK ProbeHook(int code, WPARAM wParam, LPARAM lParam) {
    if (code >= 0 && lParam != 0) {
        const auto* message = reinterpret_cast<const CWPSTRUCT*>(lParam);
        const UINT handshake = RegisterWindowMessageW(MessageName);
        if (handshake != 0 && message->message == handshake && message->wParam == Magic) {
            const DWORD pid = GetCurrentProcessId();
            const DWORD nonce = static_cast<DWORD>(message->lParam);
            wchar_t name[96]; // Host buffer; Local\\ session namespace + PID + 8-hex-digit nonce match C#.
            if (swprintf_s(name, L"Local\\AomEditorProbe-v2-%lu-%08lx", pid, nonce) > 0) {
                HANDLE mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, name);
                if (mapping != nullptr) {
                    auto* result = static_cast<Result*>(MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(Result)));
                    if (result != nullptr) {
                        const DWORD tid = GetCurrentThreadId();
                        if (result->magic == Magic && result->targetPid == pid && result->nonce == nonce &&
                            result->status == 0 && result->targetThreadId == tid) {
                            HMODULE module = nullptr;
                            if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                                reinterpret_cast<LPCWSTR>(&ProbeHook), &module) && InterlockedCompareExchange(&result->status, -1, 0) == 0) {
                                result->actualThreadId = tid;
                                result->moduleBase = reinterpret_cast<unsigned long long>(module);
                                if (result->operation != 0)
                                    result->engineResult = RunOperation(result->operation, nonce, message->hwnd);
                                InterlockedExchange(&result->status, 1);
                            }
                        }
                        UnmapViewOfFile(result);
                    }
                    CloseHandle(mapping);
                }
            }
        }
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

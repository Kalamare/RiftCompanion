// Local probe diagnostics only. Launches our own executable under the Windows debugger API.
#include <Windows.h>
#include <DbgHelp.h>
#include <cstdio>
#include <string>
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    std::wstring command = L"\"" + std::wstring(argv[1]) + L"\" --bridge-check";
    STARTUPINFOW startup{sizeof(startup)};
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(nullptr, command.data(), nullptr, nullptr, FALSE,
        DEBUG_ONLY_THIS_PROCESS, nullptr, nullptr, &startup, &process)) return 3;
    SymSetOptions(SYMOPT_DEFERRED_LOADS | SYMOPT_UNDNAME | SYMOPT_LOAD_LINES);
    SymInitialize(process.hProcess, nullptr, FALSE);
    const auto deadline = GetTickCount64() + 25000;
    bool done = false;
    while (!done && GetTickCount64() < deadline) {
        DEBUG_EVENT event{};
        if (!WaitForDebugEvent(&event, 1000)) continue;
        DWORD disposition = DBG_CONTINUE;
        if (event.dwDebugEventCode == CREATE_PROCESS_DEBUG_EVENT && event.u.CreateProcessInfo.hFile)
            CloseHandle(event.u.CreateProcessInfo.hFile);
        if (event.dwDebugEventCode == LOAD_DLL_DEBUG_EVENT && event.u.LoadDll.hFile)
            CloseHandle(event.u.LoadDll.hFile);
        if (event.dwDebugEventCode == EXIT_PROCESS_DEBUG_EVENT) done = true;
        if (event.dwDebugEventCode == EXCEPTION_DEBUG_EVENT) {
            auto& exception = event.u.Exception;
            DWORD code = exception.ExceptionRecord.ExceptionCode;
            if (code != EXCEPTION_BREAKPOINT) disposition = DBG_EXCEPTION_NOT_HANDLED;
            if (code != EXCEPTION_BREAKPOINT && code != 0x406d1388) {
                printf("exception=0x%08lx first=%lu address=%p parameters=", code,
                    exception.dwFirstChance, exception.ExceptionRecord.ExceptionAddress);
                for (DWORD i=0;i<exception.ExceptionRecord.NumberParameters;++i)
                    printf(" %llu", static_cast<unsigned long long>(exception.ExceptionRecord.ExceptionInformation[i]));
                puts("");
                SymRefreshModuleList(process.hProcess);
                HANDLE thread = OpenThread(THREAD_GET_CONTEXT | THREAD_QUERY_INFORMATION, FALSE, event.dwThreadId);
                CONTEXT context{}; context.ContextFlags = CONTEXT_FULL;
                if (thread && GetThreadContext(thread, &context)) {
                    STACKFRAME64 frame{};
                    frame.AddrPC.Offset=context.Rip; frame.AddrPC.Mode=AddrModeFlat;
                    frame.AddrFrame.Offset=context.Rbp; frame.AddrFrame.Mode=AddrModeFlat;
                    frame.AddrStack.Offset=context.Rsp; frame.AddrStack.Mode=AddrModeFlat;
                    for (int i=0;i<40;++i) {
                        if (!StackWalk64(IMAGE_FILE_MACHINE_AMD64, process.hProcess, thread, &frame,
                            &context, nullptr, SymFunctionTableAccess64, SymGetModuleBase64, nullptr)) break;
                        char buffer[sizeof(SYMBOL_INFO)+MAX_SYM_NAME]{};
                        auto symbol = reinterpret_cast<SYMBOL_INFO*>(buffer);
                        symbol->SizeOfStruct=sizeof(SYMBOL_INFO);symbol->MaxNameLen=MAX_SYM_NAME;
                        DWORD64 offset=0;
                        IMAGEHLP_MODULE64 module{};module.SizeOfStruct=sizeof(module);
                        SymGetModuleInfo64(process.hProcess,frame.AddrPC.Offset,&module);
                        if (SymFromAddr(process.hProcess,frame.AddrPC.Offset,&offset,symbol))
                            printf("%s!%s + %llu\n",module.ModuleName,symbol->Name,offset);
                        else printf("%s+0x%llx\n",module.ModuleName,frame.AddrPC.Offset-module.BaseOfImage);
                    }
                }
                if(thread)CloseHandle(thread);
                fflush(stdout);
            }
        }
        ContinueDebugEvent(event.dwProcessId,event.dwThreadId,disposition);
    }
    if(!done)TerminateProcess(process.hProcess,4);
    SymCleanup(process.hProcess);
    CloseHandle(process.hThread);CloseHandle(process.hProcess);
    return done ? 0 : 4;
}

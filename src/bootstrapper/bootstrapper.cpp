#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>

#include <filesystem>
#include <fstream>
#include <string>
#include <stdexcept>
#include <vector>

#include "resource.h"

namespace {

constexpr wchar_t kTitle[] = L"Inertial Mouse Installer";
constexpr wchar_t kManagedExe[] = L"InertialMouseInstaller.Managed.exe";
constexpr wchar_t kRuntimeWingetCommand[] =
    L"winget install --id Microsoft.DotNet.DesktopRuntime.10 --exact --source winget "
    L"--accept-package-agreements --accept-source-agreements";

std::wstring Quote(const std::wstring& value)
{
    std::wstring quoted = L"\"";
    for (wchar_t ch : value) {
        if (ch == L'"') {
            quoted += L'\\';
        }
        quoted += ch;
    }
    quoted += L"\"";
    return quoted;
}

bool IsAdministrator()
{
    BOOL isAdmin = FALSE;
    PSID adminGroup = nullptr;
    SID_IDENTIFIER_AUTHORITY ntAuthority = SECURITY_NT_AUTHORITY;

    if (AllocateAndInitializeSid(
            &ntAuthority,
            2,
            SECURITY_BUILTIN_DOMAIN_RID,
            DOMAIN_ALIAS_RID_ADMINS,
            0,
            0,
            0,
            0,
            0,
            0,
            &adminGroup)) {
        CheckTokenMembership(nullptr, adminGroup, &isAdmin);
        FreeSid(adminGroup);
    }

    return isAdmin == TRUE;
}

bool CommandNeedsAdmin(const std::vector<std::wstring>& args)
{
    if (args.empty()) {
        return true;
    }

    const std::wstring command = args[0];
    return command == L"install"
        || command == L"build"
        || command == L"toolchain"
        || command == L"uninstall"
        || command == L"uninstall-full";
}

std::wstring CurrentExePath()
{
    std::wstring path(MAX_PATH, L'\0');
    DWORD length = GetModuleFileNameW(nullptr, path.data(), static_cast<DWORD>(path.size()));

    while (length == path.size()) {
        path.resize(path.size() * 2);
        length = GetModuleFileNameW(nullptr, path.data(), static_cast<DWORD>(path.size()));
    }

    path.resize(length);
    return path;
}

std::vector<std::wstring> GetArguments()
{
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    std::vector<std::wstring> args;

    if (argv != nullptr) {
        for (int index = 1; index < argc; ++index) {
            args.emplace_back(argv[index]);
        }
        LocalFree(argv);
    }

    return args;
}

std::wstring JoinArguments(const std::vector<std::wstring>& args)
{
    std::wstring joined;
    for (const auto& arg : args) {
        if (!joined.empty()) {
            joined += L' ';
        }
        joined += Quote(arg);
    }
    return joined;
}

int RelaunchElevated(const std::vector<std::wstring>& args)
{
    const std::wstring exe = CurrentExePath();
    const std::wstring parameters = JoinArguments(args);

    SHELLEXECUTEINFOW info{};
    info.cbSize = sizeof(info);
    info.fMask = SEE_MASK_NOCLOSEPROCESS;
    info.lpVerb = L"runas";
    info.lpFile = exe.c_str();
    info.lpParameters = parameters.empty() ? nullptr : parameters.c_str();
    info.nShow = SW_SHOWNORMAL;

    if (!ShellExecuteExW(&info)) {
        return static_cast<int>(GetLastError());
    }

    WaitForSingleObject(info.hProcess, INFINITE);
    DWORD exitCode = 0;
    GetExitCodeProcess(info.hProcess, &exitCode);
    CloseHandle(info.hProcess);
    return static_cast<int>(exitCode);
}

bool DirectoryHasDesktopRuntime10(const std::filesystem::path& directory)
{
    std::error_code ec;
    if (!std::filesystem::is_directory(directory, ec)) {
        return false;
    }

    for (const auto& entry : std::filesystem::directory_iterator(directory, ec)) {
        if (!entry.is_directory(ec)) {
            continue;
        }

        const std::wstring name = entry.path().filename().wstring();
        if (name.rfind(L"10.", 0) == 0) {
            return true;
        }
    }

    return false;
}

bool HasDesktopRuntime10()
{
    wchar_t programFiles[MAX_PATH]{};
    if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_PROGRAM_FILES, nullptr, SHGFP_TYPE_CURRENT, programFiles))) {
        const auto runtimeDir = std::filesystem::path(programFiles)
            / L"dotnet"
            / L"shared"
            / L"Microsoft.WindowsDesktop.App";

        if (DirectoryHasDesktopRuntime10(runtimeDir)) {
            return true;
        }
    }

    wchar_t dotnetRoot[32767]{};
    DWORD length = GetEnvironmentVariableW(L"DOTNET_ROOT", dotnetRoot, static_cast<DWORD>(std::size(dotnetRoot)));
    if (length > 0 && length < std::size(dotnetRoot)) {
        const auto runtimeDir = std::filesystem::path(dotnetRoot)
            / L"shared"
            / L"Microsoft.WindowsDesktop.App";
        if (DirectoryHasDesktopRuntime10(runtimeDir)) {
            return true;
        }
    }

    return false;
}

int RunProcessAndWait(const std::wstring& file, const std::wstring& parameters, int show)
{
    std::wstring command = Quote(file);
    if (!parameters.empty()) {
        command += L" ";
        command += parameters;
    }

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESHOWWINDOW;
    startup.wShowWindow = static_cast<WORD>(show);

    PROCESS_INFORMATION process{};
    DWORD creationFlags = 0;
    if (show == SW_HIDE) {
        creationFlags |= CREATE_NO_WINDOW;
    }

    if (!CreateProcessW(
            nullptr,
            command.data(),
            nullptr,
            nullptr,
            FALSE,
            creationFlags,
            nullptr,
            nullptr,
            &startup,
            &process)) {
        return static_cast<int>(GetLastError());
    }

    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD exitCode = 0;
    GetExitCodeProcess(process.hProcess, &exitCode);
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return static_cast<int>(exitCode);
}

bool InstallDesktopRuntime10()
{
    const int choice = MessageBoxW(
        nullptr,
        L"This installer requires Microsoft .NET 10 Desktop Runtime.\n\n"
        L"It is not installed on this computer. Install it now with winget?",
        kTitle,
        MB_ICONQUESTION | MB_YESNO | MB_DEFBUTTON1);

    if (choice != IDYES) {
        return false;
    }

    std::wstring command = L"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command ";
    command += Quote(std::wstring(kRuntimeWingetCommand) + L"; exit $LASTEXITCODE");

    const int exitCode = RunProcessAndWait(L"powershell.exe", command, SW_HIDE);
    if (exitCode != 0) {
        std::wstring message =
            L"Could not install .NET 10 Desktop Runtime automatically.\n\n"
            L"Run this manually:\n\n";
        message += kRuntimeWingetCommand;
        MessageBoxW(nullptr, message.c_str(), kTitle, MB_ICONERROR | MB_OK);
        return false;
    }

    return HasDesktopRuntime10();
}

std::filesystem::path GetExtractionDirectory()
{
    wchar_t basePath[MAX_PATH]{};
    const int folder = IsAdministrator() ? CSIDL_COMMON_APPDATA : CSIDL_LOCAL_APPDATA;

    if (FAILED(SHGetFolderPathW(nullptr, folder, nullptr, SHGFP_TYPE_CURRENT, basePath))) {
        GetTempPathW(static_cast<DWORD>(std::size(basePath)), basePath);
    }

    return std::filesystem::path(basePath) / L"InertialMouseInstaller" / L"runtime";
}

std::filesystem::path ExtractManagedExe()
{
    HRSRC resource = FindResourceW(nullptr, MAKEINTRESOURCEW(IDR_MANAGED_EXE), L"BIN");
    if (resource == nullptr) {
        throw std::runtime_error("embedded managed installer resource was not found");
    }

    HGLOBAL loaded = LoadResource(nullptr, resource);
    DWORD size = SizeofResource(nullptr, resource);
    const void* data = LockResource(loaded);
    if (data == nullptr || size == 0) {
        throw std::runtime_error("embedded managed installer resource is empty");
    }

    const auto directory = GetExtractionDirectory();
    std::filesystem::create_directories(directory);

    const auto destination = directory / kManagedExe;
    std::ofstream output(destination, std::ios::binary | std::ios::trunc);
    output.write(static_cast<const char*>(data), size);
    output.close();

    return destination;
}

int LaunchManagedInstaller(const std::filesystem::path& managedExe, const std::vector<std::wstring>& args)
{
    const std::wstring bootstrapperPath = CurrentExePath();
    SetEnvironmentVariableW(L"IM_BOOTSTRAPPER_PATH", bootstrapperPath.c_str());

    std::wstring command = Quote(managedExe.wstring());
    const std::wstring joinedArgs = JoinArguments(args);
    if (!joinedArgs.empty()) {
        command += L" ";
        command += joinedArgs;
    }

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};

    std::wstring mutableCommand = command;
    BOOL ok = CreateProcessW(
        nullptr,
        mutableCommand.data(),
        nullptr,
        nullptr,
        TRUE,
        0,
        nullptr,
        managedExe.parent_path().c_str(),
        &startup,
        &process);

    if (!ok) {
        return static_cast<int>(GetLastError());
    }

    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD exitCode = 0;
    GetExitCodeProcess(process.hProcess, &exitCode);
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return static_cast<int>(exitCode);
}

void ShowError(const std::wstring& message)
{
    MessageBoxW(nullptr, message.c_str(), kTitle, MB_ICONERROR | MB_OK);
}

} // namespace

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    try {
        const auto args = GetArguments();

        if (CommandNeedsAdmin(args) && !IsAdministrator()) {
            return RelaunchElevated(args);
        }

        if (!HasDesktopRuntime10() && !InstallDesktopRuntime10()) {
            return 1;
        }

        const auto managedExe = ExtractManagedExe();
        return LaunchManagedInstaller(managedExe, args);
    } catch (const std::exception& ex) {
        const std::string narrow = ex.what();
        const std::wstring wide(narrow.begin(), narrow.end());
        ShowError(wide);
        return 1;
    }
}

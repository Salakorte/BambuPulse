#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winternl.h>
#include <dshow.h>
#include <objbase.h>

#include <chrono>
#include <atomic>
#include <climits>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <functional>
#include <memory>
#include <map>
#include <mutex>
#include <queue>
#include <stdexcept>
#include <string>
#include <thread>
#include <algorithm>

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "oleaut32.lib")
#pragma comment(lib, "strmiids.lib")
#pragma comment(lib, "user32.lib")

namespace {

using CreateAgentFn = void* (__cdecl*)(std::string);
using DestroyAgentFn = int (__cdecl*)(void*);
using InitLogFn = int (__cdecl*)(void*);
using SetConfigDirFn = int (__cdecl*)(void*, std::string);
using SetCertFileFn = int (__cdecl*)(void*, std::string, std::string);
using UpdateCertFn = int (__cdecl*)(void*);
using SetCountryCodeFn = int (__cdecl*)(void*, std::string);
using SetExtraHttpHeaderFn = int (__cdecl*)(void*, std::map<std::string, std::string>);
using SetHttpErrorFn = int (__cdecl*)(void*, std::function<void(unsigned, std::string)>);
using QueueOnMainFn = std::function<void(std::function<void()>)>;
using SetQueueOnMainFn = int (__cdecl*)(void*, QueueOnMainFn);
using CameraProgressFn = void (__cdecl*)(int);
using StartFn = int (__cdecl*)(void*);
using ConnectServerFn = int (__cdecl*)(void*);
using IsServerConnectedFn = bool (__cdecl*)(void*);
using IsUserLoginFn = bool (__cdecl*)(void*);
using ChangeUserFn = int (__cdecl*)(void*, std::string);
using SetServerCallbackFn = int (__cdecl*)(void*, std::function<void(int, int)>);
using SetCountryCallbackFn = int (__cdecl*)(void*, std::function<std::string()>);
using GetCameraUrlFn = int (__cdecl*)(void*, std::string, std::function<void(std::string)>);

constexpr UINT kDispatchCallbacksMessage = WM_APP + 0x431;

class CallbackQueue
{
public:
    explicit CallbackQueue(HWND window) : window_(window) {}

    void stop()
    {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            stopping_ = true;
            std::queue<std::function<void()>> empty;
            callbacks_.swap(empty);
        }
    }

    void enqueue(std::function<void()> callback)
    {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (stopping_)
            {
                dropped_.fetch_add(1, std::memory_order_relaxed);
                return;
            }
            callbacks_.push(std::move(callback));
        }
        queued_.fetch_add(1, std::memory_order_relaxed);
        if (!PostMessageW(window_, kDispatchCallbacksMessage, 0, 0))
            postFailures_.fetch_add(1, std::memory_order_relaxed);
    }

    void dispatch()
    {
        while (true)
        {
            std::function<void()> callback;
            {
                std::lock_guard<std::mutex> lock(mutex_);
                if (callbacks_.empty())
                    return;
                callback = std::move(callbacks_.front());
                callbacks_.pop();
            }
            try
            {
                callback();
            }
            catch (const std::exception& ex)
            {
                OutputDebugStringA(ex.what());
                OutputDebugStringA("\n");
            }
            catch (...)
            {
                OutputDebugStringA("Unknown exception in a Bambu Cloud callback.\n");
            }
            dispatched_.fetch_add(1, std::memory_order_relaxed);
        }
    }

    struct Stats
    {
        std::uint64_t queued;
        std::uint64_t dispatched;
        std::uint64_t postFailures;
        std::uint64_t dropped;
    };

    Stats stats() const
    {
        return {
            queued_.load(std::memory_order_relaxed),
            dispatched_.load(std::memory_order_relaxed),
            postFailures_.load(std::memory_order_relaxed),
            dropped_.load(std::memory_order_relaxed)
        };
    }

private:
    HWND window_;
    std::mutex mutex_;
    std::queue<std::function<void()>> callbacks_;
    bool stopping_ = false;
    std::atomic<std::uint64_t> queued_{0};
    std::atomic<std::uint64_t> dispatched_{0};
    std::atomic<std::uint64_t> postFailures_{0};
    std::atomic<std::uint64_t> dropped_{0};
};

std::mutex g_callbackQueueMutex;
std::shared_ptr<CallbackQueue> g_callbackQueue;

struct NativeAgent
{
    HMODULE module = nullptr;
    void* agent = nullptr;
    DestroyAgentFn destroyAgent = nullptr;
    ConnectServerFn connectServer = nullptr;
    IsServerConnectedFn isServerConnected = nullptr;
    IsUserLoginFn isUserLogin = nullptr;
    ChangeUserFn changeUser = nullptr;
    GetCameraUrlFn getCameraUrl = nullptr;
    std::shared_ptr<CallbackQueue> callbackQueue;
    std::atomic<int> serverReturnCode{INT_MIN};
    std::atomic<int> serverReasonCode{INT_MIN};
    std::atomic<unsigned> lastHttpError{0};
    std::atomic<std::uint64_t> httpErrorCount{0};

    ~NativeAgent()
    {
        if (agent && destroyAgent)
            destroyAgent(agent);
        {
            std::lock_guard<std::mutex> lock(g_callbackQueueMutex);
            if (g_callbackQueue == callbackQueue)
                g_callbackQueue.reset();
        }
        if (callbackQueue)
            callbackQueue->stop();
        callbackQueue.reset();
        if (module)
            FreeLibrary(module);
    }
};

void dispatchCallbacks()
{
    std::shared_ptr<CallbackQueue> queue;
    {
        std::lock_guard<std::mutex> lock(g_callbackQueueMutex);
        queue = g_callbackQueue;
    }
    if (queue)
        queue->dispatch();
}

struct CameraSession
{
    HMODULE sourceModule = nullptr;
    IGraphBuilder* graph = nullptr;
    IMediaControl* control = nullptr;
    IVideoWindow* videoWindow = nullptr;
    IBasicVideo* basicVideo = nullptr;
    IBaseFilter* source = nullptr;
    bool ownsComInitialization = false;
};

std::mutex g_cameraRequestMutex;
DLL_DIRECTORY_COOKIE g_pluginDirectoryCookie = nullptr;
DLL_DIRECTORY_COOKIE g_studioDirectoryCookie = nullptr;

void copyUtf8(const std::string& value, char* output, int capacity)
{
    if (!output || capacity <= 0)
        return;
    const size_t count = (std::min)(value.size(), static_cast<size_t>(capacity - 1));
    std::memcpy(output, value.data(), count);
    output[count] = '\0';
}

void setError(char* output, int capacity, const char* message, HRESULT hr = S_OK)
{
    if (!output || capacity <= 0)
        return;
    char buffer[256]{};
    if (FAILED(hr))
        std::snprintf(buffer, sizeof(buffer), "%s (0x%08lx)", message, hr);
    else
        std::snprintf(buffer, sizeof(buffer), "%s", message);
    copyUtf8(buffer, output, capacity);
}

template<typename T>
T loadFunction(HMODULE module, const char* name)
{
    return reinterpret_cast<T>(GetProcAddress(module, name));
}

bool addDllDirectories(const wchar_t* pluginDirectory, const wchar_t* studioDirectory)
{
    if (!g_pluginDirectoryCookie && pluginDirectory)
        g_pluginDirectoryCookie = AddDllDirectory(pluginDirectory);
    if (!g_studioDirectoryCookie && studioDirectory)
        g_studioDirectoryCookie = AddDllDirectory(studioDirectory);
    return g_pluginDirectoryCookie != nullptr;
}

std::string wideToUtf8(const wchar_t* value)
{
    const int size = WideCharToMultiByte(CP_UTF8, 0, value, -1, nullptr, 0, nullptr, nullptr);
    if (size <= 1)
        return {};
    std::string result(static_cast<size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, 0, value, -1, result.data(), size, nullptr, nullptr);
    result.resize(static_cast<size_t>(size - 1));
    return result;
}

std::string getWindowsVersion()
{
    using RtlGetVersionFn = NTSTATUS (WINAPI*)(PRTL_OSVERSIONINFOW);
    auto ntdll = GetModuleHandleW(L"ntdll.dll");
    auto rtlGetVersion = ntdll
        ? reinterpret_cast<RtlGetVersionFn>(GetProcAddress(ntdll, "RtlGetVersion"))
        : nullptr;

    RTL_OSVERSIONINFOW version{};
    version.dwOSVersionInfoSize = sizeof(version);
    if (!rtlGetVersion || rtlGetVersion(&version) != 0)
        return "10.0";

    char value[64]{};
    std::snprintf(value, sizeof(value), "%lu.%lu.%lu",
        version.dwMajorVersion, version.dwMinorVersion, version.dwBuildNumber);
    return value;
}

std::unique_ptr<NativeAgent> createNativeAgent(
    const wchar_t* pluginDirectory,
    const wchar_t* studioDirectory,
    const wchar_t* configDirectory,
    const wchar_t* certDirectory,
    HWND callbackWindow,
    const char* country,
    const char* userInfo,
    const char* deviceId,
    const char* studioVersion,
    CameraProgressFn progress,
    char* error,
    int errorCapacity)
{
    if (!pluginDirectory || !studioDirectory || !configDirectory || !callbackWindow ||
        !certDirectory || !country || !userInfo || !deviceId || !*deviceId ||
        !studioVersion || !*studioVersion)
    {
        setError(error, errorCapacity, "Camera Cloud configuration is incomplete.");
        return nullptr;
    }

    if (!addDllDirectories(pluginDirectory, studioDirectory))
    {
        setError(error, errorCapacity, "Could not add Bambu Studio's plugin directory.");
        return nullptr;
    }

    if (progress)
        progress(1);
    const std::wstring modulePath = std::wstring(pluginDirectory) + L"\\bambu_networking.dll";
    HMODULE module = LoadLibraryExW(modulePath.c_str(), nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_USER_DIRS);
    if (!module)
    {
        setError(error, errorCapacity, "Could not load Bambu Studio's Cloud plugin.");
        return nullptr;
    }

    auto createAgent = loadFunction<CreateAgentFn>(module, "bambu_network_create_agent");
    auto destroyAgent = loadFunction<DestroyAgentFn>(module, "bambu_network_destroy_agent");
    auto initLog = loadFunction<InitLogFn>(module, "bambu_network_init_log");
    auto setConfigDir = loadFunction<SetConfigDirFn>(module, "bambu_network_set_config_dir");
    auto setCertFile = loadFunction<SetCertFileFn>(module, "bambu_network_set_cert_file");
    auto updateCert = loadFunction<UpdateCertFn>(module, "bambu_network_update_cert");
    auto setCountryCode = loadFunction<SetCountryCodeFn>(module, "bambu_network_set_country_code");
    auto setExtraHttpHeader = loadFunction<SetExtraHttpHeaderFn>(module, "bambu_network_set_extra_http_header");
    auto setHttpError = loadFunction<SetHttpErrorFn>(module, "bambu_network_set_on_http_error_fn");
    auto setQueueOnMain = loadFunction<SetQueueOnMainFn>(module, "bambu_network_set_queue_on_main_fn");
    auto start = loadFunction<StartFn>(module, "bambu_network_start");
    auto connectServer = loadFunction<ConnectServerFn>(module, "bambu_network_connect_server");
    auto isServerConnected = loadFunction<IsServerConnectedFn>(module, "bambu_network_is_server_connected");
    auto isUserLogin = loadFunction<IsUserLoginFn>(module, "bambu_network_is_user_login");
    auto changeUser = loadFunction<ChangeUserFn>(module, "bambu_network_change_user");
    auto setServerCallback = loadFunction<SetServerCallbackFn>(module, "bambu_network_set_on_server_connected_fn");
    auto setCountryCallback = loadFunction<SetCountryCallbackFn>(module, "bambu_network_set_get_country_code_fn");
    auto getCameraUrl = loadFunction<GetCameraUrlFn>(module, "bambu_network_get_camera_url");
    if (!createAgent || !destroyAgent || !initLog || !setConfigDir || !setCertFile || !updateCert ||
        !setCountryCode || !setExtraHttpHeader || !setHttpError || !setQueueOnMain ||
        !start || !connectServer || !isServerConnected || !isUserLogin ||
        !changeUser || !setServerCallback || !setCountryCallback || !getCameraUrl)
    {
        FreeLibrary(module);
        setError(error, errorCapacity, "The installed Bambu Studio plugin lacks the required camera API.");
        return nullptr;
    }

    auto result = std::make_unique<NativeAgent>();
    result->module = module;
    result->destroyAgent = destroyAgent;
    result->connectServer = connectServer;
    result->isServerConnected = isServerConnected;
    result->isUserLogin = isUserLogin;
    result->changeUser = changeUser;
    result->getCameraUrl = getCameraUrl;

    try
    {
        result->callbackQueue = std::make_shared<CallbackQueue>(callbackWindow);
        {
            std::lock_guard<std::mutex> lock(g_callbackQueueMutex);
            g_callbackQueue = result->callbackQueue;
        }
        if (progress)
            progress(2);
        result->agent = createAgent(wideToUtf8(configDirectory));
        if (!result->agent)
            throw std::runtime_error("Bambu Studio could not create its Cloud agent.");

        if (setConfigDir(result->agent, wideToUtf8(configDirectory)) != 0)
            throw std::runtime_error("Bambu Studio rejected the camera configuration directory.");
        if (initLog(result->agent) != 0)
            throw std::runtime_error("Bambu Studio's Cloud logging could not be initialized.");

        const std::string certFolder = wideToUtf8(certDirectory);
        if (setCertFile(result->agent, certFolder, "slicer_base64.cer") != 0)
            throw std::runtime_error("Bambu Studio could not load its Cloud certificate.");

        const std::string countryCode(country);
        std::map<std::string, std::string> headers{
            {"X-BBL-Client-Type", "slicer"},
            {"X-BBL-Client-Name", "BambuStudio"},
            {"X-BBL-Client-Version", studioVersion},
            {"X-BBL-OS-Type", "windows"},
            {"X-BBL-OS-Version", getWindowsVersion()},
            {"X-BBL-Device-ID", deviceId},
            {"X-BBL-Language", "en"}
        };
        if (setExtraHttpHeader(result->agent, headers) != 0)
            throw std::runtime_error("Bambu Studio rejected the Cloud request headers.");

        auto onServerConnected = [native = result.get()](int returnCode, int reasonCode)
        {
            native->serverReasonCode.store(reasonCode, std::memory_order_relaxed);
            native->serverReturnCode.store(returnCode, std::memory_order_release);
        };
        auto getCountryCode = [countryCode]() { return countryCode; };
        auto onHttpError = [native = result.get()](unsigned status, std::string)
        {
            native->lastHttpError.store(status, std::memory_order_relaxed);
            native->httpErrorCount.fetch_add(1, std::memory_order_relaxed);
        };
        const auto callbackQueue = result->callbackQueue;
        auto queueOnMain = [callbackQueue](std::function<void()> callback)
        {
            callbackQueue->enqueue(std::move(callback));
        };
        if (setServerCallback(result->agent, onServerConnected) != 0)
            throw std::runtime_error("Bambu Studio rejected the Cloud connection callback.");
        if (setCountryCallback(result->agent, getCountryCode) != 0)
            throw std::runtime_error("Bambu Studio rejected the Cloud region callback.");
        if (setHttpError(result->agent, onHttpError) != 0)
            throw std::runtime_error("Bambu Studio rejected the Cloud HTTP error callback.");
        if (setQueueOnMain(result->agent, queueOnMain) != 0)
            throw std::runtime_error("Bambu Studio rejected the Cloud callback queue.");
        if (setCountryCode(result->agent, countryCode) != 0)
            throw std::runtime_error("Bambu Studio rejected the Cloud region.");

        if (progress)
            progress(3);
        if (start(result->agent) != 0)
            throw std::runtime_error("Bambu Studio's Cloud agent failed to start.");
        const int certResult = updateCert(result->agent);
        if (certResult != 0)
            throw std::runtime_error(
                "Bambu Studio could not update the Cloud certificate credentials "
                "(error " + std::to_string(certResult) + ").");
        if (changeUser(result->agent, std::string(userInfo)) != 0)
            throw std::runtime_error("Bambu Studio rejected the Cloud account session.");
        if (!result->isUserLogin(result->agent))
            throw std::runtime_error("Bambu Studio did not recognize the supplied Cloud login session. The account token and profile were accepted by Cloud, but this networking plugin rejected their combined login information.");

        return result;
    }
    catch (const std::exception& ex)
    {
        setError(error, errorCapacity, ex.what());
        return nullptr;
    }
}

void releaseCameraSession(CameraSession* session)
{
    if (!session)
        return;
    if (session->control)
        session->control->Stop();
    if (session->videoWindow)
    {
        session->videoWindow->put_Visible(OAFALSE);
        session->videoWindow->put_Owner(0);
        session->videoWindow->Release();
    }
    if (session->basicVideo)
        session->basicVideo->Release();
    if (session->control)
        session->control->Release();
    if (session->source)
        session->source->Release();
    if (session->graph)
        session->graph->Release();
    if (session->sourceModule)
        FreeLibrary(session->sourceModule);
    if (session->ownsComInitialization)
        CoUninitialize();
    delete session;
}

} // namespace

extern "C" __declspec(dllexport) void __cdecl CameraCloud_DispatchCallbacks()
{
    dispatchCallbacks();
}

extern "C" __declspec(dllexport) int __cdecl CameraCloud_GetCameraUrl(
    const wchar_t* pluginDirectory,
    const wchar_t* studioDirectory,
    const wchar_t* configDirectory,
    const wchar_t* certDirectory,
    HWND callbackWindow,
    const char* country,
    const char* userInfo,
    const char* serial,
    const char* devVersion,
    const char* studioVersion,
    const char* deviceId,
    CameraProgressFn progress,
    char* output,
    int outputCapacity,
    char* error,
    int errorCapacity)
{
    if (output && outputCapacity > 0)
        output[0] = '\0';
    if (error && errorCapacity > 0)
        error[0] = '\0';
    if (!serial || !*serial || !userInfo || !*userInfo || !deviceId || !*deviceId ||
        !studioVersion || !*studioVersion || !callbackWindow || !IsWindow(callbackWindow))
    {
        setError(error, errorCapacity, "Printer or Cloud account data is missing.");
        return 1;
    }

    if (progress)
        progress(0);
    std::lock_guard<std::mutex> requestLock(g_cameraRequestMutex);
    auto nativeAgent = createNativeAgent(pluginDirectory, studioDirectory,
        configDirectory, certDirectory, callbackWindow, country, userInfo, deviceId, studioVersion,
        progress, error, errorCapacity);
    if (!nativeAgent)
        return 2;

    if (progress)
        progress(4);
    const int connectResult = nativeAgent->connectServer(nativeAgent->agent);
    if (connectResult != 0)
    {
        char message[192]{};
        const int callbackResult = nativeAgent->serverReturnCode.load(std::memory_order_acquire);
        if (callbackResult == INT_MIN)
            std::snprintf(message, sizeof(message),
                "Bambu Cloud connection could not be started (error %d; server callback not received).",
                connectResult);
        else
            std::snprintf(message, sizeof(message),
                "Bambu Cloud connection could not be started (error %d; server callback %d/%d).",
                connectResult, callbackResult,
                nativeAgent->serverReasonCode.load(std::memory_order_relaxed));
        setError(error, errorCapacity, message);
        return 3;
    }

    if (progress)
        progress(5);
    const auto connectionDeadline = std::chrono::steady_clock::now() + std::chrono::seconds(30);
    while (!nativeAgent->isServerConnected(nativeAgent->agent) &&
           std::chrono::steady_clock::now() < connectionDeadline)
        std::this_thread::sleep_for(std::chrono::milliseconds(200));
    if (!nativeAgent->isServerConnected(nativeAgent->agent))
    {
        setError(error, errorCapacity, "Bambu Studio could not connect its camera session to Cloud.");
        return 4;
    }

    struct CameraUrlCallbackState
    {
        std::mutex mutex;
        std::condition_variable ready;
        bool completed = false;
        std::string url;
    };
    auto callbackState = std::make_shared<CameraUrlCallbackState>();
    const std::string packedDevice = std::string(serial) + "|" +
        (devVersion ? devVersion : "") + "|\"tutk\"";
    nativeAgent->lastHttpError.store(0, std::memory_order_relaxed);
    nativeAgent->httpErrorCount.store(0, std::memory_order_relaxed);
    const auto queueStatsBefore = nativeAgent->callbackQueue->stats();
    if (progress)
        progress(6);
    const int accepted = nativeAgent->getCameraUrl(nativeAgent->agent, packedDevice,
        [callbackState](std::string result)
        {
            {
                std::lock_guard<std::mutex> lock(callbackState->mutex);
                callbackState->url = std::move(result);
                callbackState->completed = true;
            }
            callbackState->ready.notify_one();
        });
    if (accepted != 0)
    {
        setError(error, errorCapacity, "Bambu Studio did not accept the Cloud camera request.");
        return 5;
    }

    {
        if (progress)
            progress(7);
        std::unique_lock<std::mutex> lock(callbackState->mutex);
        if (!callbackState->ready.wait_for(lock, std::chrono::seconds(45), [&] { return callbackState->completed; }))
        {
            const unsigned httpError = nativeAgent->lastHttpError.load(std::memory_order_relaxed);
            const std::uint64_t httpErrors = nativeAgent->httpErrorCount.load(std::memory_order_relaxed);
            const auto queueStats = nativeAgent->callbackQueue->stats();
            char message[256]{};
            std::snprintf(message, sizeof(message),
                "Bambu Cloud camera request timed out (main-thread callbacks: %llu queued, %llu ran, %llu PostMessage failures, %llu dropped; HTTP errors: %llu, last status: %u).",
                static_cast<unsigned long long>(queueStats.queued - queueStatsBefore.queued),
                static_cast<unsigned long long>(queueStats.dispatched - queueStatsBefore.dispatched),
                static_cast<unsigned long long>(queueStats.postFailures - queueStatsBefore.postFailures),
                static_cast<unsigned long long>(queueStats.dropped - queueStatsBefore.dropped),
                static_cast<unsigned long long>(httpErrors), httpError);
            setError(error, errorCapacity, message);
            return 6;
        }
    }

    if (progress)
        progress(8);
    const std::string& url = callbackState->url;
    if (url.rfind("bambu:///", 0) != 0)
    {
        const std::string message = std::string("Bambu Cloud camera request failed (Studio login state: ") +
            (nativeAgent->isUserLogin(nativeAgent->agent) ? "valid): " : "not recognized): ") +
            (url.empty() ? "no camera URL was returned." : url);
        copyUtf8(message, error, errorCapacity);
        return 7;
    }
    copyUtf8(url, output, outputCapacity);
    return 0;
}

extern "C" __declspec(dllexport) int __cdecl CameraCloud_StartVideo(
    const wchar_t* pluginDirectory,
    const wchar_t* studioDirectory,
    const char* url,
    HWND parent,
    int width,
    int height,
    void** outputSession,
    char* error,
    int errorCapacity)
{
    if (outputSession)
        *outputSession = nullptr;
    if (error && errorCapacity > 0)
        error[0] = '\0';
    if (!pluginDirectory || !studioDirectory || !url || !parent || !outputSession)
    {
        setError(error, errorCapacity, "Camera video configuration is incomplete.");
        return 1;
    }
    if (!addDllDirectories(pluginDirectory, studioDirectory))
    {
        setError(error, errorCapacity, "Could not add Bambu Studio's plugin directory.");
        return 2;
    }

    auto* session = new CameraSession();
    const HRESULT comResult = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (SUCCEEDED(comResult))
        session->ownsComInitialization = true;
    else if (comResult != RPC_E_CHANGED_MODE)
    {
        delete session;
        setError(error, errorCapacity, "DirectShow could not initialize COM.", comResult);
        return 3;
    }

    const std::wstring sourcePath = std::wstring(pluginDirectory) + L"\\BambuSource.dll";
    session->sourceModule = LoadLibraryExW(sourcePath.c_str(), nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_USER_DIRS);
    if (!session->sourceModule)
    {
        setError(error, errorCapacity, "Could not load Bambu Studio's camera source.");
        releaseCameraSession(session);
        return 4;
    }

    const auto getClassObject = reinterpret_cast<HRESULT(STDAPICALLTYPE*)(
        REFCLSID, REFIID, void**)>(GetProcAddress(session->sourceModule, "DllGetClassObject"));
    CLSID sourceClass{};
    HRESULT hr = CLSIDFromString(
        L"{233E64FB-2041-4A6C-AFAB-FF9BCF83E7AA}", &sourceClass);
    IClassFactory* factory = nullptr;
    if (SUCCEEDED(hr) && getClassObject)
        hr = getClassObject(sourceClass, IID_IClassFactory, reinterpret_cast<void**>(&factory));
    if (FAILED(hr) || !factory)
    {
        setError(error, errorCapacity, "Bambu Studio's camera filter could not be created.", hr);
        releaseCameraSession(session);
        return 5;
    }

    hr = factory->CreateInstance(nullptr, IID_IBaseFilter,
                                reinterpret_cast<void**>(&session->source));
    factory->Release();
    if (FAILED(hr))
    {
        setError(error, errorCapacity, "Bambu Studio's camera filter could not be initialized.", hr);
        releaseCameraSession(session);
        return 6;
    }

    IFileSourceFilter* fileSource = nullptr;
    hr = session->source->QueryInterface(IID_IFileSourceFilter,
                                         reinterpret_cast<void**>(&fileSource));
    if (SUCCEEDED(hr))
    {
        const int chars = MultiByteToWideChar(CP_UTF8, 0, url, -1, nullptr, 0);
        std::wstring wideUrl(static_cast<size_t>(chars), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, url, -1, wideUrl.data(), chars);
        hr = fileSource->Load(wideUrl.c_str(), nullptr);
        fileSource->Release();
    }
    if (FAILED(hr))
    {
        setError(error, errorCapacity, "Bambu Studio could not open the camera URL.", hr);
        releaseCameraSession(session);
        return 7;
    }

    hr = CoCreateInstance(CLSID_FilterGraph, nullptr, CLSCTX_INPROC_SERVER,
                          IID_IGraphBuilder, reinterpret_cast<void**>(&session->graph));
    if (SUCCEEDED(hr))
        hr = session->graph->AddFilter(session->source, L"Bambu Cloud Camera");

    IEnumPins* pins = nullptr;
    if (SUCCEEDED(hr))
        hr = session->source->EnumPins(&pins);
    bool rendered = false;
    if (SUCCEEDED(hr))
    {
        IPin* pin = nullptr;
        while (pins->Next(1, &pin, nullptr) == S_OK)
        {
            PIN_DIRECTION direction{};
            if (SUCCEEDED(pin->QueryDirection(&direction)) && direction == PINDIR_OUTPUT)
            {
                const HRESULT renderResult = session->graph->Render(pin);
                rendered = SUCCEEDED(renderResult) || rendered;
            }
            pin->Release();
        }
        pins->Release();
    }
    if (FAILED(hr) || !rendered)
    {
        setError(error, errorCapacity, "DirectShow could not connect the Bambu camera stream.", hr);
        releaseCameraSession(session);
        return 8;
    }

    hr = session->graph->QueryInterface(IID_IMediaControl,
                                        reinterpret_cast<void**>(&session->control));
    if (SUCCEEDED(hr))
        hr = session->graph->QueryInterface(IID_IVideoWindow,
                                            reinterpret_cast<void**>(&session->videoWindow));
    if (SUCCEEDED(hr))
        hr = session->graph->QueryInterface(IID_IBasicVideo,
                                             reinterpret_cast<void**>(&session->basicVideo));
    if (SUCCEEDED(hr))
    {
        session->videoWindow->put_AutoShow(OAFALSE);
        hr = session->videoWindow->put_Owner(reinterpret_cast<OAHWND>(parent));
    }
    if (SUCCEEDED(hr))
        hr = session->videoWindow->put_WindowStyle(WS_CHILD | WS_CLIPSIBLINGS);
    if (SUCCEEDED(hr))
        hr = session->videoWindow->SetWindowPosition(0, 0, width, height);
    if (SUCCEEDED(hr))
        hr = session->videoWindow->put_Visible(OATRUE);
    if (SUCCEEDED(hr))
        hr = session->control->Run();
    if (FAILED(hr))
    {
        setError(error, errorCapacity, "Bambu Studio could not start camera playback.", hr);
        releaseCameraSession(session);
        return 9;
    }

    *outputSession = session;
    return 0;
}

extern "C" __declspec(dllexport) void __cdecl CameraCloud_ResizeVideo(
    void* opaqueSession, int width, int height)
{
    auto* session = static_cast<CameraSession*>(opaqueSession);
    if (session && session->videoWindow)
    {
        session->videoWindow->SetWindowPosition(0, 0, width, height);
        if (session->basicVideo && width > 0 && height > 0)
        {
            long sourceWidth = 0;
            long sourceHeight = 0;
            if (SUCCEEDED(session->basicVideo->GetVideoSize(&sourceWidth, &sourceHeight)) &&
                sourceWidth > 0 && sourceHeight > 0)
            {
                const double scale = (std::min)(
                    static_cast<double>(width) / sourceWidth,
                    static_cast<double>(height) / sourceHeight);
                const long destinationWidth = static_cast<long>(sourceWidth * scale + 0.5);
                const long destinationHeight = static_cast<long>(sourceHeight * scale + 0.5);
                const long left = (width - destinationWidth) / 2;
                const long top = (height - destinationHeight) / 2;
                session->basicVideo->SetDestinationPosition(
                    left, top, destinationWidth, destinationHeight);
            }
        }
    }
}

extern "C" __declspec(dllexport) int __cdecl CameraCloud_GetVideoSize(
    void* opaqueSession, int* width, int* height)
{
    auto* session = static_cast<CameraSession*>(opaqueSession);
    if (!session || !session->basicVideo || !width || !height)
        return 1;

    long sourceWidth = 0;
    long sourceHeight = 0;
    const HRESULT result = session->basicVideo->GetVideoSize(&sourceWidth, &sourceHeight);
    if (FAILED(result) || sourceWidth <= 0 || sourceHeight <= 0)
        return 2;

    *width = sourceWidth;
    *height = sourceHeight;
    return 0;
}

extern "C" __declspec(dllexport) void __cdecl CameraCloud_StopVideo(void* opaqueSession)
{
    releaseCameraSession(static_cast<CameraSession*>(opaqueSession));
}

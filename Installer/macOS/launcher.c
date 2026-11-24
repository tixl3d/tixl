/*
 * The app's main executable. It runs the Editor (Resources/TiXL/TiXL.dll) on .NET inside its own process,
 * so the process stays the bundle's main executable. macOS ties privacy permissions (microphone, camera) to that:
 * a process that exec'd another binary is refused silently, without a prompt, under the hardened runtime.
 * .NET is found the same way the stock apphost finds it (DOTNET_ROOT, then the global install).
 */
#include <dlfcn.h>
#include <limits.h>
#include <mach-o/dyld.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "nethost.h"

typedef void* hostfxr_handle;

struct hostfxr_initialize_parameters
{
    size_t size;
    const char* host_path;
    const char* dotnet_root;
};

typedef int32_t (*hostfxr_initialize_for_dotnet_command_line_fn)(int argc, const char** argv,
    const struct hostfxr_initialize_parameters* parameters, hostfxr_handle* host_context_handle);
typedef int32_t (*hostfxr_run_app_fn)(const hostfxr_handle host_context_handle);
typedef int32_t (*hostfxr_close_fn)(const hostfxr_handle host_context_handle);

static void strip_last_component(char* path)
{
    char* slash = strrchr(path, '/');
    if (slash != NULL)
        *slash = 0;
}

int main(int argc, const char** argv)
{
    char executable[PATH_MAX];
    uint32_t executableSize = sizeof(executable);
    char host_path[PATH_MAX];
    if (_NSGetExecutablePath(executable, &executableSize) != 0 || realpath(executable, host_path) == NULL)
    {
        fprintf(stderr, "TiXL: cannot resolve the launcher's path.\n");
        return 1;
    }

    char resources[PATH_MAX];
    snprintf(resources, sizeof(resources), "%s", host_path);
    strip_last_component(resources); /* Contents/MacOS */
    strip_last_component(resources); /* Contents */
    strncat(resources, "/Resources", sizeof(resources) - strlen(resources) - 1);

    char app_path[PATH_MAX];
    snprintf(app_path, sizeof(app_path), "%s/TiXL/TiXL.dll", resources);

    /* MoltenVK ships in the bundle; an ICD set by the user wins. */
    char icd[PATH_MAX];
    snprintf(icd, sizeof(icd), "%s/vulkan/icd.d/MoltenVK_icd.json", resources);
    setenv("VK_DRIVER_FILES", icd, 0);

    char hostfxr_path[PATH_MAX];
    size_t hostfxr_path_size = sizeof(hostfxr_path);
    struct get_hostfxr_parameters hostfxr_query = { sizeof(struct get_hostfxr_parameters), app_path, NULL };
    if (get_hostfxr_path(hostfxr_path, &hostfxr_path_size, &hostfxr_query) != 0)
    {
        fprintf(stderr, "TiXL: the .NET runtime was not found next to %s.\n", app_path);
        return 1;
    }

    void* hostfxr = dlopen(hostfxr_path, RTLD_NOW | RTLD_LOCAL);
    hostfxr_initialize_for_dotnet_command_line_fn initialize = hostfxr != NULL
        ? (hostfxr_initialize_for_dotnet_command_line_fn)dlsym(hostfxr, "hostfxr_initialize_for_dotnet_command_line")
        : NULL;
    hostfxr_run_app_fn run_app = hostfxr != NULL ? (hostfxr_run_app_fn)dlsym(hostfxr, "hostfxr_run_app") : NULL;
    hostfxr_close_fn close_host = hostfxr != NULL ? (hostfxr_close_fn)dlsym(hostfxr, "hostfxr_close") : NULL;
    if (initialize == NULL || run_app == NULL || close_host == NULL)
    {
        fprintf(stderr, "TiXL: cannot load %s: %s\n", hostfxr_path, dlerror());
        return 1;
    }

    /* A self-contained app carries hostfxr next to itself and is its own root; a global install keeps it in
       <dotnet root>/host/fxr/<version>/. hostfxr looks for the runtime in this folder to tell the two apart. */
    char app_dir[PATH_MAX];
    snprintf(app_dir, sizeof(app_dir), "%s", app_path);
    strip_last_component(app_dir);
    char dotnet_root[PATH_MAX];
    snprintf(dotnet_root, sizeof(dotnet_root), "%s", hostfxr_path);
    strip_last_component(dotnet_root);
    if (strcmp(dotnet_root, app_dir) != 0)
    {
        for (int level = 0; level < 3; level++)
            strip_last_component(dotnet_root);
    }

    /* The command line hostfxr expects: the app's path in place of the program name, then the app's arguments. */
    const char** arguments = calloc((size_t)argc + 1, sizeof(char*));
    if (arguments == NULL)
        return 1;
    arguments[0] = app_path;
    for (int index = 1; index < argc; index++)
        arguments[index] = argv[index];

    struct hostfxr_initialize_parameters parameters = { sizeof(parameters), host_path, dotnet_root };
    hostfxr_handle context = NULL;
    int32_t result = initialize(argc, arguments, &parameters, &context);
    if (result != 0 || context == NULL)
    {
        fprintf(stderr, "TiXL: cannot start .NET for %s (error 0x%x).\n", app_path, (unsigned)result);
        if (context != NULL)
            close_host(context);
        return 1;
    }

    result = run_app(context);
    close_host(context);
    return result;
}

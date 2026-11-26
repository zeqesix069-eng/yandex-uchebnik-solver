#pragma once

#ifdef _WIN32
#  ifdef SOLVER_CORE_EXPORTS
#    define SOLVER_API __declspec(dllexport)
#  else
#    define SOLVER_API __declspec(dllimport)
#  endif
#else
#  define SOLVER_API
#endif

extern "C" {
SOLVER_API int ys_http_get(const char* url, char* outBuf, int outBufSize);
SOLVER_API int ys_solve_task(
    const char* provider,
    const char* baseUrl,
    const char* apiKey,
    const char* model,
    const char* promptUtf8,
    char* outBuf,
    int outBufSize);
SOLVER_API int ys_solve_task_ex(
    const char* provider,
    const char* baseUrl,
    const char* apiKey,
    const char* model,
    const char* systemPromptUtf8,
    const char* promptUtf8,
    double temperature,
    char* outBuf,
    int outBufSize);
}

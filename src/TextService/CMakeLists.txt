cmake_minimum_required(VERSION 3.20)
project(MeifengWubiTSF LANGUAGES CXX)

# TSF 文本服务 DLL。x64 与 x86 各编一份：64 位程序加载 MeifengWubiTSF64.dll，32 位程序加载 MeifengWubiTSF32.dll
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(CMAKE_SIZEOF_VOID_P EQUAL 8)
    if(CMAKE_GENERATOR_PLATFORM STREQUAL "ARM64" OR CMAKE_SYSTEM_PROCESSOR MATCHES "ARM64")
        set(TSF_SUFFIX "ARM64")
    else()
        set(TSF_SUFFIX "64")
    endif()
else()
    set(TSF_SUFFIX "32")
endif()

add_library(MeifengWubiTSF SHARED
    DllMain.cpp
    Register.cpp
    TextService.cpp
    LangBarButton.cpp
    DisplayAttribute.cpp
    PipeClient.cpp
    Protocol.cpp
    TextService.def
)

set_target_properties(MeifengWubiTSF PROPERTIES OUTPUT_NAME "MeifengWubiTSF${TSF_SUFFIX}")
target_compile_definitions(MeifengWubiTSF PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)

if(MSVC)
    # 静态 CRT：宿主进程五花八门，不依赖 VC 运行库
    set_property(TARGET MeifengWubiTSF PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    target_compile_options(MeifengWubiTSF PRIVATE /W4 /utf-8 /permissive- /guard:cf)
    target_link_options(MeifengWubiTSF PRIVATE /guard:cf /DYNAMICBASE /NXCOMPAT)
else()
    target_link_options(MeifengWubiTSF PRIVATE -static -static-libgcc -static-libstdc++)
endif()

target_link_libraries(MeifengWubiTSF PRIVATE ole32 oleaut32 uuid user32 advapi32 shell32)

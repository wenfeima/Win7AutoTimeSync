@echo off
chcp 65001 >nul 2>&1
setlocal
cd /d "%~dp0"
title 自动校时 - 一键编译
echo ============================================
echo   自动校时 一键编译（用系统自带的 .NET 3.5 编译器）
echo ============================================
echo.
set "CSC=%SystemRoot%\Microsoft.NET\Framework\v3.5\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v3.5\csc.exe"
if not exist "%CSC%" goto nocsc
if not exist "源码\TimeSyncSource.cs" goto nosrc
if not exist "源码\app.manifest" goto nosrc
echo 正在编译，请稍等...
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ /codepage:65001 /win32manifest:"源码\app.manifest" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:"自动校时.exe" "源码\TimeSyncSource.cs"
if errorlevel 1 goto fail
echo.
echo [成功] 已生成 自动校时.exe
for %%F in ("自动校时.exe") do echo         大小 %%~zF 字节    编译时间 %%~tF
echo.
echo 改完 源码\TimeSyncSource.cs 再跑一次这个脚本就行。
echo 发给别人：自动校时.exe + 使用说明.txt 两个文件一起给。
echo.
pause
exit /b 0

:nocsc
echo [错误] 没找到 csc.exe，请确认系统已安装 .NET Framework 3.5
pause
exit /b 1

:nosrc
echo [错误] 缺少 源码\TimeSyncSource.cs 或 源码\app.manifest
pause
exit /b 1

:fail
echo.
echo [失败] 编译报错了，看上面的提示信息
pause
exit /b 1

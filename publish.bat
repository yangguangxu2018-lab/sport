@echo off
REM SportTracker - IIS 发布脚本（适用于 Windows）
REM 用法：publish.bat [应用路径]
REM 例如：publish.bat C:\inetpub\wwwroot\SportTracker

setlocal enabledelayedexpansion

REM 检查参数
if "%~1"=="" (
    echo 用法: publish.bat [目标文件夹路径]
    echo 例如: publish.bat C:\inetpub\wwwroot\SportTracker
    exit /b 1
)

set TARGET_PATH=%~1

echo ================== SportTracker IIS 发布脚本 ==================
echo.
echo 1. 清理旧的发布文件...
if exist publish (
    rmdir /s /q publish
    echo   ✓ 已清理 publish 文件夹
)

echo.
echo 2. 发布应用（Release 配置）...
dotnet publish -c Release -o publish
if errorlevel 1 (
    echo   ✗ 发布失败！
    exit /b 1
)
echo   ✓ 发布完成

echo.
echo 3. 停止 IIS 应用程序池...
REM 提取应用程序池名称（假设与目标文件夹同名）
for %%F in (%TARGET_PATH%) do set POOL_NAME=%%~nF
echo   应用程序池: !POOL_NAME!
appcmd stop apppool /apppool.name:"!POOL_NAME!"

echo.
echo 4. 备份旧文件...
if exist "%TARGET_PATH%.backup" (
    rmdir /s /q "%TARGET_PATH%.backup"
)
if exist "%TARGET_PATH%" (
    move "%TARGET_PATH%" "%TARGET_PATH%.backup"
    echo   ✓ 已备份到: %TARGET_PATH%.backup
)

echo.
echo 5. 复制新文件到 %TARGET_PATH%...
move publish "%TARGET_PATH%"
echo   ✓ 文件复制完成

echo.
echo 6. 启动 IIS 应用程序池...
appcmd start apppool /apppool.name:"!POOL_NAME!"
echo   ✓ 应用程序池已启动

echo.
echo ================== 发布完成 ==================
echo 应用程序已更新，请稍等几秒钟让应用启动
echo.
pause

@echo off
chcp 65001 >nul
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo 没找到系统自带的 .NET Framework 编译器 (csc.exe)
  pause
  exit /b 1
)

if not exist "aquarium.ico" (
  echo 找不到 aquarium.ico，先运行 node make-icon.js 生成图标
  pause
  exit /b 1
)

"%~CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:aquarium.ico /out:AquariumWallpaper.exe /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll AquariumWallpaper.cs
if errorlevel 1 (
  echo.
  echo 编译失败，请把上面的报错内容发给我。
  pause
  exit /b 1
)

echo.
echo 编译完成：AquariumWallpaper.exe
echo 双击它，小鱼就会游到桌面壁纸层；右键托盘小鱼可换背景 / 退出。
pause

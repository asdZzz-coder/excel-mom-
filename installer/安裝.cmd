@echo off
chcp 65001 >nul
setlocal
rem ============================================================
rem  居服紀錄表 安裝程式
rem  ClickOnce 會記住「從哪個資料夾安裝」，換資料夾安裝同一個程式會被拒絕。
rem  所以一律先把安裝檔複製到固定的資料夾 %LOCALAPPDATA%\ServiceRecord-Setup，
rem  再從那裡安裝；程式內的「檢查更新」也會把新版放回這裡。
rem ============================================================
set "SRC=%~dp0"
set "DEST=%LOCALAPPDATA%\ServiceRecord-Setup"

if not exist "%SRC%ServiceRecord.application" (
    echo 找不到 ServiceRecord.application。請先把整個 zip 解壓縮，再執行「安裝.cmd」。
    pause
    exit /b 1
)

rem 已經在固定資料夾裡執行，就不用再複製
if /i "%SRC%"=="%DEST%\" goto install

echo 正在準備安裝檔...
rem 先移除固定資料夾裡舊版本的檔案（只動 ServiceRecord_* 版本資料夾）
if exist "%DEST%\Application Files" (
    for /d %%D in ("%DEST%\Application Files\ServiceRecord_*") do rmdir /s /q "%%D"
)
robocopy "%SRC%." "%DEST%" ServiceRecord.application /R:1 /W:1 /NJH /NJS /NFL /NDL /NP >nul
if errorlevel 8 goto copyfail
robocopy "%SRC%Application Files" "%DEST%\Application Files" /E /R:1 /W:1 /NJH /NJS /NFL /NDL /NP >nul
if errorlevel 8 goto copyfail

:install
echo 正在開啟安裝程式...
start "" "%DEST%\ServiceRecord.application"
exit /b 0

:copyfail
echo 複製安裝檔失敗，請確認磁碟空間或稍後再試。
pause
exit /b 1

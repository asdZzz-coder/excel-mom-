@echo off
chcp 65001 >nul
setlocal
rem ============================================================
rem  居服紀錄表 安裝程式（程式內的「檢查更新」也用它來更新）
rem  把 app 資料夾複製到 %LOCALAPPDATA%\Programs\ServiceRecord，再開啟程式；
rem  開始功能表、桌面捷徑和「設定 > 應用程式」的登記，由程式開啟時自己建立。
rem  不需要系統管理員權限；紀錄存在 %AppData%\ServiceRecord，重新安裝或更新都不受影響。
rem  不用 ClickOnce：它的 Launcher.exe 會被「智慧型應用程式控制」擋下而裝不起來。
rem  參數 /update：由「檢查更新」呼叫，不重建使用者自己刪掉的桌面捷徑。
rem ============================================================
set "SRC=%~dp0app"
set "DEST=%LOCALAPPDATA%\Programs\ServiceRecord"
set "ARG=--installed"
if /i "%~1"=="/update" set "ARG="

if not exist "%SRC%\ServiceRecord.exe" goto notfound

rem 程式還開著就等它關閉（更新時程式會自己結束），最多等 2 分鐘
set /a WAITED=0
:waitloop
tasklist /fi "imagename eq ServiceRecord.exe" /nh 2>nul | find /i "ServiceRecord.exe" >nul
if errorlevel 1 goto copy
if %WAITED%==0 echo 請先關閉「居服紀錄表」，關閉後會自動繼續安裝...
if %WAITED% geq 120 goto stillrunning
set /a WAITED+=1
timeout /t 1 /nobreak >nul
goto waitloop

:copy
echo 正在安裝「居服紀錄表」...
robocopy "%SRC%" "%DEST%" /MIR /R:2 /W:1 /NJH /NJS /NFL /NDL /NP >nul
if errorlevel 8 goto copyfail

start "" "%DEST%\ServiceRecord.exe" %ARG%
echo 安裝完成。
exit /b 0

:notfound
echo 找不到 app\ServiceRecord.exe。請先把整個 zip 解壓縮，再執行「安裝.cmd」。
pause
exit /b 1

:stillrunning
echo 「居服紀錄表」還開著，請關閉後再執行一次「安裝.cmd」。
pause
exit /b 1

:copyfail
echo 複製檔案失敗，請確認磁碟空間或稍後再試。
pause
exit /b 1

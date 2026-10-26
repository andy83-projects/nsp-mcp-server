@echo off
setlocal
cd /d "%~dp0"
echo Experimental NSP automation. Use an offline map copy and independently verify edits.
echo Keep this console open. Endpoint: http://127.0.0.1:6274/
"%~dp0Nsp.Mcp.Server.exe"
set "server_exit=%errorlevel%"
if not "%server_exit%"=="0" (
  echo Server exited with code %server_exit%.
  pause
)
exit /b %server_exit%

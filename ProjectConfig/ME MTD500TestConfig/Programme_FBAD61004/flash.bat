@ECHO off
SETLOCAL
JLink.exe -device STM32F405VG -CommanderScript load_commands.jlink -Log "jlink_program.log"
IF %ERRORLEVEL% NEQ 0 (
    EXIT /B 1
)

ENDLOCAL
EXIT /B 0
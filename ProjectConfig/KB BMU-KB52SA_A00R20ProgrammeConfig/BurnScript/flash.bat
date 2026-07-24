@ECHO off
SETLOCAL
JLink.exe -device GD32F303VC -CommanderScript load_commands.jlink -Log "jlink_program.log"
IF %ERRORLEVEL% NEQ 0 (
    EXIT /B 1
)
ENDLOCAL
EXIT /B 0
@ECHO off
SETLOCAL
SET SN=69705607
JLink.exe -SelectEmuBySN %SN% -device MIMXRT1024xxx4A -CommanderScript load_commands.jlink -Log "jlink_program.log"
IF %ERRORLEVEL% NEQ 0 (
    EXIT /B 1
)

ENDLOCAL
EXIT /B 0
@ECHO off
SETLOCAL
SET SN=607000546
JLink.exe -SelectEmuBySN %SN% -device nRF52833_xxAA -CommanderScript load_commands.jlink -Log "jlink_program.log"
IF %ERRORLEVEL% NEQ 0 (
    EXIT /B 1
)

ENDLOCAL
EXIT /B 0
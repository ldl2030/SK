@ECHO off
SETLOCAL
SET SN=607000751
JLink.exe -SelectEmuBySN %SN% -device EFR32MG21AxxxF1024 -CommanderScript load_commands.jlink -Log "jlink_program.log"
IF %ERRORLEVEL% NEQ 0 (
    EXIT /B 1
)

ENDLOCAL
EXIT /B 0
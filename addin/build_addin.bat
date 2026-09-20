@echo off
REM Сборка Add-in Этапа 2 (terminal_strip_addin/addin).
REM Компилирует ВСЕ .cs рекурсивно (AddIn.cs, Actions/, Diagnostics/, Data/, Geometry/, Configuration/).

set EPLAN_BIN=C:\Program Files\EPLAN\Platform\2.9.4\Bin
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

cd /d "%~dp0"

"%CSC%" /target:library ^
  /out:TerminalStripAddin.dll ^
  /reference:"%EPLAN_BIN%\Eplan.EplApi.AFu.dll" ^
  /reference:"%EPLAN_BIN%\Eplan.EplApi.Baseu.dll" ^
  /reference:"%EPLAN_BIN%\Eplan.EplApi.DataModelu.dll" ^
  /reference:"%EPLAN_BIN%\Eplan.EplApi.HEServicesu.dll" ^
  /reference:System.Windows.Forms.dll ^
  /recurse:*.cs

pause

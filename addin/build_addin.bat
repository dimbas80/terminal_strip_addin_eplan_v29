@echo off
REM Сборка Add-in Этапа 2 (terminal_strip_addin/addin).
REM Компилирует ВСЕ .cs рекурсивно (AddIn.cs, Actions/, Diagnostics/, Data/, Geometry/, Configuration/, UI/, Graphics/, Anchor/, Report/).
REM rev.13.1 (H-4): Eplan.EplApi.MasterDatau.dll — классы MD* (Eplan.EplApi.MasterData:
REM MDSymbolLibrary/MDSymbol/MDSymbolVariant) для SymbolBrowserDialog. Порядок
REM /out,/target до /recurse не трогать (урок CS2022 п.19).
REM rev.13.3 (H-4v2 SPIKE-2, throwaway): Eplan.EplApi.EServicesu.dll — Eplan.EplApi.EServices.Ged
REM (InsertInteraction/InteractionAttribute/InteractionContext) для SymbolPickInteraction;
REM при удалении спайка референс можно оставить (безвреден) или убрать вместе с кодом.

set EPLAN_BIN=C:\Program Files\EPLAN\Platform\2.9.4\Bin
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

cd /d "%~dp0"

"%CSC%" /target:library ^
  /out:TerminalStripAddin.dll ^
  /reference:"%EPLAN_BIN%\Eplan.EplApi.AFu.dll" ^
  /reference:"%EPLAN_BIN%\Eplan.EplApi.Baseu.dll" ^
  /reference:"%EPLAN_BIN%\Eplan.EplApi.DataModelu.dll" ^
  /reference:"%EPLAN_BIN%\Eplan.EplApi.HEServicesu.dll" ^
  /reference:"%EPLAN_BIN%\Eplan.EplApi.MasterDatau.dll" ^
  /reference:"%EPLAN_BIN%\Eplan.EplApi.EServicesu.dll" ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Drawing.dll ^
  /recurse:*.cs

pause

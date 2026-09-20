@echo off
REM Путь к папке Bin установленного EPLAN Electric P8 2.9
REM Поправьте путь, если у вас EPLAN установлен в другое место
set EPLAN_BIN=C:\Program Files\EPLAN\Platform\2.9.4\Bin

REM Путь к компилятору C# из .NET Framework
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

"%CSC%" /target:library ^
  /reference:"%EPLAN_BIN%\Eplan.EplApi.AFu.dll" ^
  /reference:"%EPLAN_BIN%\Eplan.EplApi.Baseu.dll" ^
  /reference:"%EPLAN_BIN%\Eplan.EplApi.DataModelu.dll" ^
  /reference:"%EPLAN_BIN%\Eplan.EplApi.HEServicesu.dll" ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Drawing.dll ^
  /out:ShowCablesInSegment.dll ^
  ShowCablesInSegment.cs

pause

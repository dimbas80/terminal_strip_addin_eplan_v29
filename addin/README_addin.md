# Add-in Этап 2 — TERMINAL_STRIP_ANALYZE

Порт логики spike rev.13 в полноценный Add-in (план: `../plan_stage2.md`).

## Сборка

1. Запустить `build_addin.bat` (правит `EPLAN_BIN`, если EPLAN в другом месте).
   Компилирует все `.cs` рекурсивно → `TerminalStripAddin.dll`.
2. Убедиться по окну компилятора, что ошибок нет ( предупреждения — нормально).

## Подключение

1. Скопировать `TerminalStripAddin.dll` в папку Addins EPLAN
   («Параметры → Настройки → Пользователь → Управление → Папки → Addins» —
   та же папка, где лежал рабочий `TerminalStripReportSpike.dll`).
2. **Перезапустить EPLAN** (DLL подхватывается из ShadowCopy — без перезапуска
   может исполняться старая копия).
3. Выполнить действие: командная строка EPLAN → `TERMINAL_STRIP_ANALYZE`
   (или кнопка с командой на панели/ленте).

## Лог

- Файл: `D:\YandexDisk\!EPLAN\Сценарии\terminal_strip_addin\terminal_strip_addin.log`
  (папка создаётся при первом прогоне; если диск недоступен — корневая «Сценарии»,
  крайний случай `%TEMP%`).
- Первая строка-штамп `[INFO] BUILD_STAMP: ...` обязана совпадать с ожидаемой
  версией (сейчас: `2026-09-20 Этап 2 rev.4.0 (DataModel-ридеры, дамп [DM])`).
  Если в логе старый штамп — исполняется DLL из ShadowCopy: перезапустить EPLAN.
- Копировать лог в workspace после каждого прогона (как в Этапе 1).

## Отладка подключения (если команда «молчит»)

Паттерн Этапа 1 (`spike/README_spike.md`): проверить папку Addins + перезапуск;
при тишине — собрать spike-TestPing тем же способом и сравнить.

## Структура кода (план §3, минимальная)

```text
addin/
├── AddIn.cs                      # IEplAddIn (порт из spike)
├── Actions/AnalyzeAction.cs      # TERMINAL_STRIP_ANALYZE: конвейер rev.13
├── Diagnostics/DiagnosticLogger.cs  # лог-файл, BUILD_STAMP, Summary/Fail
├── Configuration/AddInConfiguration.cs  # слои/допуски/форма/тип отчёта
├── Geometry/LeadGeometry.cs      # чистые типы Pt/Seg/LineComponent
├── Geometry/LeadDetector.cs      # порт детектора К1–К4 из rev.13
├── Report/EmbeddedReportReader.cs # форма→отчёт→дерево→слои (порт EPLAN-части)
└── Data/                         # (Задачи 4–5) ридеры DataModel, [DM]/[MATCH]
```

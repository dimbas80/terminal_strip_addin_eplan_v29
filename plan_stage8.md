# Этап 8 — Фаза H (UI): диалог + интерактивная точка вставки — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить захардкоженные значения `AddInConfiguration` (клеммник, форма,
ориентация, символ, точка вставки) на пользовательский ввод: WinForms-диалог →
«Создать» → клик по странице → генерация. Персистентность последних выборов;
авто-определение ориентации из формы.

**Architecture:** Чистый модуль настроек (`addin/UI/AddInSettings.cs`, без EPLAN) +
WinForms-диалог и браузер символов (`addin/UI/`) + `InsertInteraction`
(`addin/Interaction/`) для клика по странице. `AnalyzeAction` развиливается флагом
`UseUi`: headless-ветка — байт-в-байт текущее поведение (стендовые регрессы), UI-ветка —
диалог → интеракция → продолжение существующего пайплайна с `PointD` от клика.
Ориентация — детерминация из дерева отчёта (обе оси уже реализованы в `AnchorResolver`,
rev.6.2). Символ — один, два слота варианта (H/V), выбор по фактической ориентации;
компенсация смещения центра bbox для асимметричных символов.

**Tech Stack:** C# (csc.exe .NET 4, EPLAN 2.9.4 API: `Eplan.EplApi.DataModel`,
`.Graphics`, `.MasterData`, `Eplan.EplApi.Base`, `Eplan.EplApi.EServices` (Ged,
`Eplan.EplApi.EServices.dll` — ДОБАВИТЬ в build_addin.bat), `System.Windows.Forms`),
сборка `addin/build_addin.bat` только на Windows-стенде.

**Spec:** `docs/superpowers/specs/2026-09-25-fase-h-ui-design.md`

## Global Constraints

- Кодировка новых файлов: **UTF-8 без BOM**; `namespace MyEplanActions`; венгерская
  нотация, русские XML-doc комментарии, стиль как в соседних файлах.
- **НЕ КОММИТИТЬ** — коммиты по явному запросу пользователя.
- **BUILD_STAMP передирать при КАЖДОЙ ревизии** (`AnalyzeAction.cs:26`, урок п.47/п.54);
  Этап 8 стартует с `rev.12.0`.
- «Одна ГИПОТЕЗА на прогон» (урок п.47–48): каждая задача = отдельный прогон на стенде.
- Компилятора на Linux нет — самопроверка using-ов по компилирующемуся потребителю
  (урок п.30: `Terminal` живёт в `DataModel.EObjects`; для новых типов — reflection-проба
  или KB-проверка) + ревью.
- Using-и: существующий набор + `Eplan.EplApi.EServices.Ged` (Interaction) — референс
  `Eplan.EplApi.EServicesu.dll` (уточнить точное имя DLL в `%EPLAN_BIN%` при сборке:
  `Eplan.EplApi.EServicesu.dll`); `System.Windows.Forms.dll` уже в build_addin.bat;
  `System.Drawing.dll` добавить при необходимости (иконки/Color).
- Счётчики этапов 1–5 не меняются; WARN-бюджет headless-прогона: 10 известных + только
  новые от отказов UI-механизмов.
- Чистые модули (без EPLAN-типов) тестируются в `tests/` (csc-раннер, exit code = число
  провалов); EPLAN-слой — прогоном на стенде.
- Настройки: значения key=value БЕЗ trim (хвостовой пробел имени формы — урок п.33),
  разбор по ПЕРВОМУ `=` (полные имена клеммников начинаются с `=`).

---

### Task H-1: AddInSettings + headless-флаг (без изменения поведения)

**Files:**
- Create: `addin/UI/AddInSettings.cs`, `tests/AddInSettingsTests.cs`
- Modify: `addin/Configuration/AddInConfiguration.cs` (+`UseUi=false`,
  +`GhostPitchFallbackMm=10.0`), `addin/Diagnostics/DiagnosticLogger.cs`
  (кандидаты каталога — public static readonly, переиспользование настройками),
  `addin/Actions/AnalyzeAction.cs` (BUILD_STAMP rev.12.0; в BeginRun —
  `[SETTINGS] load: <путь|нет файла — дефолт>`), `tests/build_tests.bat`
  (+ AddInSettings.cs и тесты в список исходников)

**Interfaces:**
- `AddInSettings` (чистый, System.IO only): класс настроек (свойства с дефолтами из
  `AddInConfiguration`: TargetStrip, Form, SymbolLibrary, SymbolName, VariantH, VariantV,
  OrientationMode (enum Auto/Horizontal/Vertical), GridPitch per-form —
  `TryGetGridPitch(formName, out double)` / `SetGridPitch(formName, double)`);
  `static AddInSettings Load(string[] arrDirCandidates, out string strUsedPath)` — первый
  существующий каталог, файл `terminal_strip_addin.settings`, нет файла/каталога —
  дефолты, `strUsedPath=null`; `static bool Save(AddInSettings o, string[] arrDirCandidates,
  out string strUsedPath)` — в первый существующий каталог, все отказы — false.
- Формат: строки `key=value` (разбор по первому `=`), `#` — комментарий, пустые — пропуск;
  `GridPitch.<form>=<double InvariantCulture>`.

**Steps:**
- [x] TDD: сначала `tests/AddInSettingsTests.cs` — кейсы: roundtrip; значение с `=`;
      значение с хвостовым пробелом сохраняется посимвольно; нет файла → дефолты;
      повреждённая строка пропущена; GridPitch per-form set/get/перезапись; кандидаты —
      первый существующий каталог; ни один не существует → Load дефолты/Save false.
- [x] Реализация `AddInSettings.cs`; `build_tests.bat` расширить; прогон тестов на стенде
      — 0 failed (к существующим 19 кейсам добавились новые). **ИТОГ: 146/0 + 56/0.**
- [x] `UseUi`/`GhostPitchFallbackMm` в конфиг; публичные кандидаты каталога лога;
      BUILD_STAMP rev.12.0; `[SETTINGS] load`-строка в BeginRun.

**Ожидание прогона (headless):** счётчики == rev.11.15, WARN 10, новые строки
`[SETTINGS] load: <путь>` (первый прогон — «нет файла — дефолты», после прогона файл ещё
не создаётся — save появится в H-2) и штамп rev.12.0. Тесты: все кейсы 0 failed.

---

### Task H-2: MainDialog — клеммник/форма/ориентация, интеграция в AnalyzeAction

**Files:**
- Create: `addin/UI/MainDialog.cs`
- Modify: `addin/Actions/AnalyzeAction.cs` (развилка UseUi; сбор данных для диалога;
  `[MODE] ui|headless`; при провале создания отчёта на выбранной форме — сообщение и
  повторный диалог), `addin/Report/EmbeddedReportReader.cs` (список форм: все `*.f11`
  из `get_ProjectEntries`+`SystemEntries`, без фильтра; `AddToProjectEx` для выбранного
  имени; WARN «чужая форма» — только headless), `addin/Data/EplanTerminalStripReader.cs`
  (+ лёгкий проход «имя клеммника → список», если ещё не выделен), save настроек после
  успешного прогона.

**Interfaces:**
- `MainDialog : Form` — конструктор принимает: список клеммников (полные имена),
  список форм (f11 без расширения), текущие `AddInSettings`; свойства-результаты:
  `SelectedStripName`, `SelectedFormName`, `OrientationMode`, `SymbolLibrary`,
  `SymbolName`, `VariantH`, `VariantV`; `DialogResult` стандартный.
- Пайплайн в UI-режиме потребляет значения диалога (константы конфига — только headless).
- Дубликаты полных имён клеммников: первый побеждает + `[STRIPDUP]` WARN.

**Steps:**
- [x] `MainDialog.cs` (WinForms, программный layout без designer): ComboBox клеммника
      (предвыбор из настроек), ComboBox формы (все f11, предвыбор), ComboBox ориентации
      (Авто/H/V), подпись символа + кнопка «Выбрать символ…» (в H-2 — заглушка, активна
      в H-4; слоты варианта неактивны до H-4), «Создать»/«Отмена».
- [x] Анализ `AnalyzeAction.Run`: `UseUi=false` — ветка как сейчас; `UseUi=true` —
      собрать списки → `ShowDialog()` → «Отмена»/закрытие → выход без действий;
      «Создать» → диалог скрыт → пайплайн с выбранными значениями (точка пока
      `InsertX/InsertY` из конфига). Save настроек после успешной генерации
      (`[SETTINGS] save: <путь>`).
- [x] Headless-ветка не изменена (ревьюер сверяет против BASE).
- [x] Прогон стенда (UI + предвыбор + отмена + опц. headless-регресс). **rev.12.1:
      диалог/предвыбор/save/отмена работают. Х-2b (список=активная страница) ОТМЕНЁН
      (R5: блоки на многополюсных, прогон на однополюсной → диалог пустел) — список
      снова всего проекта (rev.12.3). Защита от чужого клеммника — гейт [CROSSGATE].**

**Ожидание прогона (headless):** == rev.12.0 (UseUi=false). **Ожидание UI-прогона:**
выбор X2 + горизонтальная форма + Авто → счётчики == rev.11.15, `[ORIENT]` пока из
конфига (детерминация — H-3), настройки сохранены, повторный запуск — предвыбор.

---

### Task H-3: Авто-ориентация + ручной override

**Files:**
- Modify: `addin/Anchor/AnchorResolver.cs` (+`DetectOrientation`),
  `addin/Actions/AnalyzeAction.cs` (потребление), лог `[ORIENT-AUTO]`.

**Interfaces:**
- `static ReportOrientation DetectOrientation(List<PhRow> lstRows, out string strEvidence)`
  — доминирующий ряд (Y-бакеты) vs доминирующий столбец (X-бакеты) по числовым
  дескрипторам, порог ≥2; ничья → null (каллер применяет fallback: аспект bbox дерева —
  `GetBoundingBox` SubPlacements; ничья → конфиг + WARN). `strEvidence` — числа в лог.
- Потребители (`AnchorResolver.Build`, `CheckK4Report`, `MatchBuilder`, выбор варианта
  символа после H-4) получают детерминированную ориентацию; `OrientationMode=Auto` —
  детекция, H/V — override (`[ORIENT] manual`).

**Steps:**
- [ ] `DetectOrientation` + unit-кейсы (чистые PhRow-фактуры: ряд→Horizontal,
      столбец→Vertical, ничья→null) в `tests/`.
- [ ] Интеграция в UI-режим (headless — конфиг-ориентация без изменений).

**Ожидание прогона:** UI-прогоны A (вертикальная форма → `[ORIENT-AUTO] Vertical`,
evidence «столбец 60 vs ряд 1») и B (горизонтальная → `Horizontal`, «ряд 60 vs столбец 1»);
счётчики == rev.11.15 в обоих; ручной override — `[ORIENT] manual`, работает.

---

### Task H-4: Браузер символов (2 слота H/V) + компенсация центра (dx,dy)

**Files:**
- Create: `addin/UI/SymbolBrowserDialog.cs`
- Modify: `addin/Graphics/SymbolSizeMeasurer.cs` (+`CenterOffsetDx/Dy` в результате),
  `addin/Graphics/CableSymbolCreator.cs` (выбор варианта по ориентации; компенсация
  центра при вставке), `addin/Configuration/AddInConfiguration.cs` (символ из настроек
  в UI-режиме; константы — headless), `MainDialog.cs` (активация кнопки и слотов),
  `tests/` (компенсация — чистая арифметика, если выделима).

**Interfaces:**
- `SymbolBrowserDialog : Form` — вход: `Project` (или список библиотек), текущий выбор;
  выход: `Library`, `Name`, `VariantH`, `VariantV`. Список: `Project.SymbolLibraries`
  → символы (проба: перечисление у DataModel `SymbolLibrary`; отказ → `MDSymbolLibrary.
  Symbols` по пути библиотеки → reflection-проба; урок rev.7). Поиск подстрокой.
  Варианты: `Symbol.Variants` count (0-based).
- Замер: при старте прогона — два пробных замера (вариант H и вариант V, Remove в
  finally, как `SymbolBoxProbe`): A×B + центр bbox (dx,dy) на каждый вариант.
- Вставка символа: `Location = желаемая_позиция − (dx,dy)` → визуальный центр на конце
  линии; зазор/лесенка — от A×B как в rev.10.7+ (габарит-по-оси).

**Steps:**
- [ ] `SymbolBrowserDialog` + проба перечисления символов (KB-цитаты в комментариях).
- [ ] Замер (dx,dy) в `SymbolSizeMeasurer` (два варианта, `[SYMSIZE]` + `[SYMSIZE-OFF]`).
- [ ] Выбор варианта по фактической ориентации (из H-3); компенсация в `CableSymbolCreator`.
- [ ] Headless — без изменений (константный символ, CABDCP2 центр (0;0) — поведение ==).

**Ожидание прогона:** UI: выбор CABDCP2 (0/0) — вывод == rev.11.15 (компенсация нулевая);
выбор GOST K (известно Δ(0;−4)) — символ смещён так, что визуальный центр на линии
(подтверждение визуально); WARN-бюджет не расширен.

---

### Task H-5: PING-спик InsertInteraction

**Files:**
- Create: `addin/Interaction/InsertPointInteraction.cs`
- Modify: `addin/build_addin.bat` (+`Eplan.EplApi.EServicesu.dll`), `AnalyzeAction.cs`
  (временный запуск спика — диагностическая секция, гейт константой).

**Steps:**
- [ ] Микро-спик: класс `PingInteraction : InsertInteraction` +
      `[InteractionAttribute(Name="TSA_PING_POINT")]`; `OnStart` →
      `PromptForStatusLine` + `RequestCode.Select`; `OnPoint` → лог `[PING] x,y` →
      конец; лог факта регистрации при старте аддина.
- [ ] Проба `SetStaticCursor` с временным графическим объектом (рамка-призрак) —
      ОТДЕЛЬНЫЙ прогон (одна гипотеза): сработало → механика в H-6; нет → промпт без
      рамки (решение зафиксировать).

**Ожидание прогона:** `[PING]` с координатами клика; ESC — без исключений; сработка/отказ
`SetStaticCursor` зафиксированы. Счётчики основного пайплайна == rev.11.15.

---

### Task H-6: Точка вставки в бою + рамка-призрак + кэш шага

**Files:**
- Modify: `addin/Interaction/InsertPointInteraction.cs` (боевой: имя
  `TSA_INSERT_POINT`, `Description` undo-шага, колбэк-продолжение),
  `addin/Actions/AnalyzeAction.cs` (диалог → интеракция → продолжение пайплайна в
  `PointD` клика; после успеха — save настроек + `GridPitch.<форма>`),
  `addin/Report/EmbeddedReportReader.cs` (`insertPoint` параметр),
  рамка-призрак по решению H-5.

**Steps:**
- [ ] Реструктуризация: «Создать» → диалог скрыт → запуск интеракции → `OnPoint` →
      весь существующий пайплайн (report → orient → detect → … → graphics) в точке
      клика; `InsertX/InsertY` — только headless. Ошибки пайплайна — в лог и
      сообщение, без падения EPLAN.
- [ ] Рамка-призрак: `nTerminals × pitch` (кэш `GridPitch.<форма>`, нет кэша —
      `GhostPitchFallbackMm`), механика из H-5.
- [ ] Кэш шага: нижняя медиана шага стубов из K4 → `SetGridPitch` после успеха.

**Ожидание прогона (UI):** клик в произвольной точке → отчёт+графика там; повторный
запуск: рамка-призрак соответствует факту (шаг из кэша); ESC — ничего не создано;
undo содержит именованный шаг; headless-регресс == rev.11.15.

---

## Финал этапа

- [ ] Whole-branch ревью SDD; фиксы Important; миноры — ledger.
- [ ] `summary.md` (п.62+, Handoff), мастер-план §Фаза H → ЗАВЕРШЕНА, MVP §24 п.1/2/15.
- [ ] Коммит — по явному запросу пользователя.

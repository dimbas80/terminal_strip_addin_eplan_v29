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
- [x] `DetectOrientation` + unit-кейсы (чистые PhRow-фактуры: ряд→Horizontal,
      столбец→Vertical, ничья→null) в `tests/`. *(rev.12.2, тесты 24/0, summary п.64)*
- [x] Интеграция в UI-режим (headless — конфиг-ориентация без изменений).
      *(rev.12.2–12.4; токены `[ORIENT-AUTO]`/`[ORIENT] manual` в RunPipeline)*

**Ожидание прогона:** UI-прогоны A (вертикальная форма → `[ORIENT-AUTO] Vertical`,
evidence «столбец 60 vs ряд 1») и B (горизонтальная → `Horizontal`, «ряд 60 vs столбец 1»);
счётчики == rev.11.15 в обоих; ручной override — `[ORIENT] manual`, работает.

**ИТОГ (25.09.2026):** AUTO — ch1–ch3 + A/B (rev.12.4, summary п.65–66): Vertical
(столбец X=99.0) / Horizontal (ряд Y=-81.0), счётчики == эталону; ручной override —
`[ORIENT] manual Vertical` подтверждён прогоном XT1-вертикаль (сборка rev.13.0,
summary п.71: `[CROSS]` 24==24, оверфлоу-слоты ×2 по Y, графика 14/14 + символ 1/1 +
ссылка 1/1, DT/видимое ОК, save ✓, WARN 14 = environmental 12 + сироты-K4 2).
**Task H-3 ЗАКРЫТ полностью.**

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
- [x] `SymbolBrowserDialog` + проба перечисления символов (KB-цитаты в комментариях).
- [x] Замер (dx,dy) в `SymbolSizeMeasurer` (два варианта, `[SYMSIZE]` + `[SYMSIZE-OFF]`).
- [x] Выбор варианта по фактической ориентации (из H-3); компенсация в `CableSymbolCreator`.
- [x] Headless — без изменений (константный символ, CABDCP2 центр (0;0) — поведение ==).

**ИТОГ (25.09.2026):** код+ревью готовы (rev.13.1, commits 09fb80d+9d6f2ee, ревью Approved,
fix-1: путь библиотеки отделён от имени; fix-2: pragma-пары CS0618; fix-3 (74ff36b):
pragma в форму «голый 618» для легаси-csc (CS1692), выход браузера `Name`→`SymbolName`
(CS0108 vs Control.Name) — выход диалога: Library/SymbolName/VariantH/VariantV);
**ЖДЁТ СТЕНДОВОГО ПРОГОНА** — ожидания ниже + заведомый CS-риск одной строки: имя
`Eplan.EplApi.MasterDatau.dll` в build_addin.bat.

**Дополнение (25.09, пост-прогон rev.13.1):** UX нашего браузера отвергнут пользователем —
нужен нативный диалог «Вставить символ» (дерево+графика). Закрыто через **H-4v2 SPIKE**
(throwaway, rev.13.2, коммит 63fe490, ревью Approved): вызов `XEGActionInsertSymRef`
через `ActionManager`+`ActionCallingContext` (пустой ctx = полный диалог) до MainDialog,
логи `[SYMDLG]`/`[ACTDUMP]`; продакшн-замена браузера — по фактам спайка
(ledger «Task H-4v2 SPIKE», PARK-лист: ID→resolve, валидация ctx, режим размещения).

**Ожидание прогона:** UI: выбор CABDCP2 (0/0) — вывод == rev.11.15 (компенсация нулевая);
выбор GOST K (известно Δ(0;−4)) — символ смещён так, что визуальный центр на линии
(подтверждение визуально); WARN-бюджет не расширен.

---

### Task H-4b: Браузер v2 — дерево категорий (FD) + превью DrawingService

**Реализация (26.09.2026, rev.14.0):** код готов — `addin/UI/SymbolCatalog.cs` (чистая
категоризация: FD-имена / «Прочее (FD N)» / fallback-префикс / «Прочие»),
`tests/SymbolCatalogTests.cs` (11 кейсов, раннер зарегистрирован в build_tests.bat И
Program.Main — ловушка п.66), `addin/UI/SymbolBrowserDialog.cs` переработан (рев.14.0,
контракт конструктора и выходов не менялся): TableLayoutPanel 840×620 — слева
библиотеки + поиск + TreeView («Библиотека → Категория → Символ», листья Tag=имя,
двойной клик = ОК после валидации, поиск фильтрует листья, предвыбор разворачивает путь
и подсвечивает), справа превью-Panel (260px, FixedSingle, Paint →
`DrawDisplayList(e, ClientRectangle)`) + карточка (имя/категория/«вариантов: N»/
NumericUpDown «Вариант превью», НЕ связан со слотами H/V); DrawingService — один
экземпляр на диалог, Dispose при закрытии. FD-ID — первично из MDSymbol
(`MDSymbolPropertyList(mdsym).SYMB_MAINFUNCTION` #16018, MDPropertyValue, извлечение
числа — reflection-проба ToInt64/ToInt/Value + fallback ToString+TryParse); словарь
FD-ID→имя — `Project.FunctionDefinitionLibrary.FunctionDefinitions` + reflection-проба
поверхности (GetProperty-существование — дамп [FD], без вызовов!). Превью-каскад:
(1) типизированный путь `Symbol(SymbolLibrary, name)` → `SymbolVariant(symbol, n)` →
`CreateDisplayList(SymbolVariant)`; (2) `CreateDisplayList(name, lib, n, project)` и
второй пробой с ""; (3) честная деградация «Превью недоступно» — RepresentationType-
перегрузка сознательно НЕ пробуется. После успешного списка — проба
`SetDefaultViewport` (подгон viewport по bbox — KB). Настройки перед CreateDisplayList:
DrawConnections=false, MacroPreview=false, DrawBackGround=false. Спайк-дампы
первого прогона: [FD] (счётчик + поверхность членов + ToString первого FD),
[SYMFDMAP] (10 первых символов: raw #16018, извлечённый id + через какое свойство, итог
«сопоставлено X из Y, словарь N»), [DSPROBE] (какая перегрузка CreateDisplayList
сработала / тип+сообщение каждой пробы, факт SetDefaultViewport).

**Решения пользователя (26.09.2026):** категории дерева — по определению функции
(`SYMB_MAINFUNCTION` #16018 → `FunctionDefinition`; fallback — группировка по префиксу
имени + «Прочие»); превью — **картинка прямо в диалоге через `DrawingService`**
(наводка пользователя; KB-подтверждение: класс-рендер display list с официальными
примерами); «Показать в EPLAN» из плана выпало (превью закрывает); кнопка
«Выбрать через EPLAN…» (нативный диалог, механизм SPIKE-7/8) — PARK в ledger.
Контекст: SPIKE-11 (rev.13.13) закрыл программную автоточку отрицательно —
`base.OnPoint(PointD(0,0))` → Success(1024), OnSuccess авто, но вставки нет
(0 останцев, коллекции null, 3 детерминированных прогона; KB `Interaction~OnPoint`:
«Is called after a point input by user» — callback реального ввода, не команда).

**Files:**
- Create: `addin/UI/SymbolCatalog.cs` (чистая категоризация, без EPLAN-типов),
  `tests/SymbolCatalogTests.cs`
- Modify: `addin/UI/SymbolBrowserDialog.cs` (2 колонки: слева библиотеки+поиск+TreeView,
  справа превью-панель+карточка), `addin/Actions/AnalyzeAction.cs` (BUILD_STAMP
  rev.14.0), `tests/build_tests.bat` (+ новые исходники, регистрация в Program.Main —
  ловушка п.66)

**KB-факты (www.eplan.help, API 2.9; проверено 26.09, коллекция eplan_api):**
- `Project.FunctionDefinitionLibrary : FunctionDefinitionLibrary` —
  `DataModelu~Eplan.EplApi.DataModel.Project~FunctionDefinitionLibrary.html`
- `FunctionDefinitionLibrary.FunctionDefinitions : FunctionDefinition[]` —
  `DataModelu~...MasterData.FunctionDefinitionLibrary~FunctionDefinitions.html`;
  члены `FunctionDefinition` (Name/IdentifyingName/Id/Category) НЕ доказаны —
  reflection-пробы (урок rev.7: имена членов не угадываем)
- `SYMB_MAINFUNCTION` #16018 на `MDSymbolPropertyList` (ctor `(MDSymbol)` доказан) —
  Int64 = ID определения функции; уровень `MDSymbolVariant` — проба (FD может
  отличаться по вариантам)
- `Eplan.EplApi.HEServices.DrawingService` — рендер превью: `CreateDisplayList`
  (перегрузки `(SymbolVariant)`, `(SymbolVariant, Boolean bReturnSymbolConnectionPointsData)`
  — «…return also a structure with information about the symbol variant's connection
  points», `(String,String,Int32,Project)`, `(Placement)`, `(StorableObject[])`,
  `(Page[])`, `(WindowMacro)`); `DrawDisplayList` — «Draws a display list on a window.
  The preview is fit to the window, while keeping its aspect ratio», пример
  `DrawDisplayList(e, oForm.ClientRectangle)` в Paint-обработчике; `SetWindow`/
  `SetDefaultWindow` (100×100)/`SetViewport`/`SetDefaultViewport` («Adjusts viewport to
  the bounding box of the objects from drawing list»)/`ZoomAll`; свойства
  `DrawConnections`/`DrawCrossReferences`/`DrawInvisibleObjects`/`DrawBackGround`/
  `DrawBlackAndWhite` (Remarks: «images are always colored, independently»)/
  `MacroPreview`/`UseThumbnail`/`CenterView`; `Reset`/`Dispose`.
  Примеры: `DrawingService.html` (WindowMacro → Panel.Paint → DrawDisplayList),
  `HE_Display.html` (`DrawConnections=true; MacroPreview=true;
  CreateDisplayList(strObj, "", 0, gProject); Picture1.Invalidate()`).
  Ранний вывод «рендера в 2.9 нет» (проверка по ExportBitmap/geometry) — опровергнут.
- Референс `Eplan.EplApi.HEServicesu.dll` уже в build_addin.bat (NameService, rev.13.0).

**Interfaces:**
- `SymbolCatalog` (чистый): вход — список имён символов, FD-ID по символу (или null),
  словарь FD-ID → имя категории; выход — упорядоченные категории → символы.
  Нет маппинга/не сошёлся — бакеты по префиксу имени (ведущая нецифровая группа,
  как SplitDeviceTagLetterCounter) + «Прочие»; пустой вход — одна категория.
- `SymbolBrowserDialog`: выход не меняется (`Library`/`SymbolName`/`VariantH`/
  `VariantV`), сигнатура конструктора та же (`MainDialog` передаёт `_oProject`).
  Слева: список библиотек (как есть) + поиск + TreeView «Библиотека → Категория →
  Символ» (поиск фильтрует листья, пустые категории скрываются; двойной клик по
  символу = ОК; предвыбор из настроек — разворот пути). Справа: превью-Panel
  (Paint → `DrawDisplayList(e, panel.ClientRectangle)`) + карточка (имя, категория,
  число вариантов, «Вариант превью» NumericUpDown 0..count−1 — независимо от слотов
  H/V) + статусная строка (как сейчас).
- Превью-каскад (одна гипотеза = пробы с логом): (1) `CreateDisplayList(strName,
  strLib, nVariant, project)` (паттерн HE_Display; второй пробой — с `""` вместо
  имени библиотеки), (2) сборка `DataModel.SymbolVariant` reflection-пробой +
  `CreateDisplayList(SymbolVariant)`, (3) деградация «превью недоступно» (карточка,
  честный статус). После создания списка при пустой картинке — проба
  `SetDefaultViewport`. Базовые настройки: `DrawConnections=false`,
  `MacroPreview=false`, `DrawBackGround=false` — фиксируются прогоном.
- Спайк-дампы (первый прогон): `[FD]` — счётчик `FunctionDefinitions` + поверхность
  членов; `[SYMFDMAP]` — чтение #16018 с MDSymbol, результат сопоставления с FD-ID;
  `[DSPROBE]` — какая перегрузка сработала/тип+сообщение отказа. Всё — Console +
  статусная строка (паттерн диалога).
- Диалог остаётся read-only (display list — внутренний рендер, мутаций проекта нет).
  Габарит A×B в карточку НЕ выносится: `SymbolSizeMeasurer` вставляет реальный
  символ — на каждый клик недопустимо; габариты измеряются в `RunPipeline` как
  сейчас.

**Steps:**
- [x] TDD: `tests/SymbolCatalogTests.cs` — кейсы: группировка по FD-именам; fallback
      по префиксу; FD неизвестен у части символов (смешанный); дубли имён; пустой
      вход; регистр/локаль — регистрация в `build_tests.bat` + `Program.Main`.
- [x] `SymbolCatalog.cs` + TreeView в диалоге (предвыбор, поиск, двойной клик).
- [x] Превью-панель DrawingService + карточка + дампы [FD]/[SYMFDMAP]/[DSPROBE];
      `Dispose` DrawingService при закрытии диалога.
- [x] BUILD_STAMP rev.14.0; headless-ветка не тронута.
- [x] SDD-ревью: Needs fixes (3 Important + 2 Minor) → fix-раунд r14.1 (ранние
      [FD]/[SYMFDMAP]-выходы, сброс превью/карточки при ручном вводе, чистое имя
      категории в карточке, чекбоксы) — все пункты закрыты.
- [ ] Стендовый прогон (ожидания — ниже).

**Ожидание прогона:** дерево строится по FD-именам (или честный fallback-префикс —
решение по `[SYMFDMAP]`); превью отрисовывает CABDCP2/0 и GOST K/0 (отказ — тип
исключения в `[DSPROBE]`, следующая гипотеза по нему); выбор символа в диалоге →
пайплайн без изменений (счётчики == эталону rev.13.x, WARN-бюджет не расширен);
тесты — новые кейсы 0 failed.

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

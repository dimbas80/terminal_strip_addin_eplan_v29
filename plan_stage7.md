# Этап 7 — Фаза G (Graphics): реальные линии и символы — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить отладочное превью Фазы F на создание реальных объектов: графические линии по `CableGeometryResult.Segments` (слой `EPLAN100`, красное перо) и символы кабеля `SPECIAL / 16 / CABDCP2` вариант 1 по `CableSymbolPlacement`.

**Architecture:** Два тонких создателя в `addin/Graphics/` (EPLAN-слой без логики) потребляют чистый результат Geometry Engine (Фаза F, без изменений); интеграция — секция 13 `AnalyzeAction` вместо гейта `PreviewDraw`. Слой линий резолвится из дерева отчёта (объект `GraphicalLayer` строки `EPLAN100`), отказы — WARN с продолжением.

**Tech Stack:** C# (csc.exe .NET 4, EPLAN 2.9.4 API: `Eplan.EplApi.DataModel`, `.Graphics`, `.MasterData`, `Eplan.EplApi.Base`), сборка `addin/build_addin.bat` только на Windows-стенде.

**Spec:** `docs/superpowers/specs/2026-09-22-fase-g-graphics-design.md`

## Global Constraints

- Кодировка новых файлов: **UTF-8 без BOM** — как все существующие файлы `addin/` (проверено: `AnalyzeAction.cs` и др. начинаются с `usi`, не с EF BB BF; кириллица в литералах компилируется корректно).
- `namespace MyEplanActions`; стиль кода — как в соседних файлах: венгерская нотация (`o`/`lst`/`n`/`b`/`d`/`str`), русские XML-doc комментарии, 4 пробела, K&R-скобки как в окружении.
- **НЕ КОММИТИТЬ** — коммиты только по явному запросу пользователя (конвенция проекта).
- Компилятора на Linux нет — «проверка» задач 1–3 = самопроверка using-ов по компилирующемуся потребителю (урок summary п.30) + ревью; реальная сборка — Task 4 на стенде.
- Using-и новых файлов — только из проверенного набора потребителя `AnalyzeAction.cs`: `Eplan.EplApi.Base` (PointD), `Eplan.EplApi.DataModel` (Page, Placement, SymbolReference, Project), `Eplan.EplApi.DataModel.Graphics` (Line, Pen, GraphicalPlacement, GraphicalLayer), `Eplan.EplApi.DataModel.MasterData` (SymbolLibrary, Symbol, SymbolVariant).
- Типы-потребители (существуют, не менять): `Pt{X,Y}`/`Seg{A,B,LayerName}` (`addin/Geometry/LeadGeometry.cs`), `CableGeometryResult{Segments:List<Seg>, Symbols:List<CableSymbolPlacement>, Warnings}` и `CableSymbolPlacement{CableName:string, CableIndex:int, Position:Pt}` (`addin/Geometry/CableGeometryBuilder.cs`), `DiagnosticLogger` — `Log(string)`, `Warn(string)`, `Fail(string)`, `Summarize(string)` (`addin/Diagnostics/DiagnosticLogger.cs`).
- Счётчики этапов 1–5 не меняются; WARN-бюджет прогона: 10 известных (9 lookup-проб S029153 + 1 «колонок К4 (61) != клемм (60)») + только новые от отказов создания.

---

### Task 1: GraphicLineCreator — линии по сегментам

**Files:**
- Create: `addin/Graphics/GraphicLineCreator.cs`

**Interfaces:**
- Consumes: `Seg`/`CableGeometryResult` (Global Constraints), `AddInConfiguration.GraphicsLayerName/GraphicsPenColorId/GraphicsPenWidthMm` (появятся в Task 3 — до него файл не компилируется, это норма: сборка только в Task 4).
- Produces: `static GraphicalLayer ResolveLayerFromTree(System.Collections.Generic.List<Placement> lstTree, string strLayerName, DiagnosticLogger log)`; `static int CreateLines(Page oPage, CableGeometryResult oGeom, GraphicalLayer oLayer, DiagnosticLogger log)`.

- [ ] **Step 1: Write the file**

Создать `addin/Graphics/GraphicLineCreator.cs` с содержимым:

```csharp
using System;
using System.Collections.Generic;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;

namespace MyEplanActions
{
    /// <summary>Создание графических линий кабельной разводки по сегментам Geometry Engine
    /// (Фаза G, spec docs/superpowers/specs/2026-09-22-fase-g-graphics-design.md;
    /// мастер-план §12 — Этап 8). Линии графические, не электрические Connection.
    /// Слой — объект GraphicalLayer из дерева отчёта (ридинг Layer.Name проверен rev.9,
    /// summary п.9; на стресс-стенде 67 линий на EPLAN100); перо — из AddInConfiguration.
    /// Отказ отдельной линии — WARN [GRAPH], остальные продолжают (паттерн [PREVIEW]).
    /// НЕ идемпотентно: повторный прогон дублирует линии (очистка — Фаза I).</summary>
    public static class GraphicLineCreator
    {
        /// <summary>Объект слоя strLayerName из дерева отчёта lstTree: первый
        /// GraphicalPlacement, чей Layer.Name совпал (без учёта регистра). Не найден —
        /// WARN и null (линии останутся на слое по умолчанию, spec §5).</summary>
        public static GraphicalLayer ResolveLayerFromTree(List<Placement> lstTree,
            string strLayerName, DiagnosticLogger log)
        {
            if (lstTree == null || string.IsNullOrEmpty(strLayerName)) return null;
            foreach (Placement oPlacement in lstTree)
            {
                GraphicalPlacement oGraphical = oPlacement as GraphicalPlacement;
                if (oGraphical == null) continue;
                try
                {
                    GraphicalLayer oLayer = oGraphical.Layer;
                    if (oLayer == null) continue;
                    if (string.Equals(oLayer.Name, strLayerName, StringComparison.OrdinalIgnoreCase))
                        return oLayer;
                }
                catch { /* слой отдельного объекта недоступен — пробуем следующий */ }
            }
            log.Warn("[GRAPH] слой '" + strLayerName +
                "' не найден в дереве отчёта — линии на слое по умолчанию");
            return null;
        }

        /// <summary>Линия на каждый Seg: Create + перо (конфиг) + слой oLayer
        /// (null — слой по умолчанию). Возвращает число созданных.</summary>
        public static int CreateLines(Page oPage, CableGeometryResult oGeom,
            GraphicalLayer oLayer, DiagnosticLogger log)
        {
            if (oPage == null || oGeom == null)
            {
                log.Warn("[GRAPH] CreateLines: page или geometry == null — линий не создаём");
                return 0;
            }
            int nTotal = oGeom.Segments.Count;
            if (nTotal == 0) return 0;

            // Красное перо — рабочий вывод по решению пользователя (spec §2.2);
            // паттерн Pen — example/ShowCablesInSegment.cs / превью Фазы F.
            Pen oPen = new Pen();
            oPen.ColorId = AddInConfiguration.GraphicsPenColorId;
            oPen.Width = AddInConfiguration.GraphicsPenWidthMm;
            oPen.StyleId = 0;

            int nCreated = 0;
            foreach (Seg oSeg in oGeom.Segments)
            {
                try
                {
                    Line oLine = new Line();
                    oLine.Create(oPage,
                        new PointD(oSeg.A.X, oSeg.A.Y),
                        new PointD(oSeg.B.X, oSeg.B.Y));
                    oLine.Pen = oPen;
                    if (oLayer != null) oLine.Layer = oLayer;
                    nCreated++;
                }
                catch (Exception oEx)
                {
                    log.Warn("[GRAPH] Line.Create бросил " + oEx.GetType().Name + ": " + oEx.Message);
                }
            }
            log.Log("[INFO] [GRAPH-SUM] линий " + nCreated + " из " + nTotal +
                (oLayer != null
                    ? " (слой '" + AddInConfiguration.GraphicsLayerName + "')"
                    : " (слой по умолчанию)") + ".");
            return nCreated;
        }
    }
}
```

- [ ] **Step 2: Самопроверка файла**

Проверить grep-ом: ровно один `namespace MyEplanActions`; using-и из допустимого набора (Global Constraints); нет ссылок на `PreviewDraw`; имена сигнатур совпадают с Interfaces байт-в-байт. Кодировка UTF-8 без BOM (`head -c 3` не должен давать `ef bb bf`).

- [ ] **Step 3: Ревью**

Ревью кода (reviewer-субагент): соответствие стилю `addin/Geometry/CableGeometryBuilder.cs`, обработка ошибок по spec §5, отсутствие логики вне EPLAN-привязки.

### Task 2: CableSymbolCreator — вставка символов 16/CABDCP2

**Files:**
- Create: `addin/Graphics/CableSymbolCreator.cs`

**Interfaces:**
- Consumes: `CableSymbolPlacement`/`CableGeometryResult` (Global Constraints), константы `AddInConfiguration.SymbolLibrary/SymbolName/SymbolVariant` (Task 3).
- Produces: `static int CreateSymbols(Page oPage, CableGeometryResult oGeom, DiagnosticLogger log)`.

- [ ] **Step 1: Write the file**

Создать `addin/Graphics/CableSymbolCreator.cs` с содержимым:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;

namespace MyEplanActions
{
    /// <summary>Вставка символов кабеля (Фаза G, spec
    /// docs/superpowers/specs/2026-09-22-fase-g-graphics-design.md; мастер-план §13 —
    /// Этап 9). KB 2.9 (проверено 22.09.2026): SymbolLibrary(Project, String) →
    /// Symbol = oLibrary[имя] → SymbolVariant = oSymbol[индекс]; SymbolReference.Create
    /// — ИНСТАНСНЫЙ (public virtual void Create(Page, SymbolVariant)); позиция —
    /// Placement.Location (get/set). Библиотека/имя/вариант — из AddInConfiguration
    /// (в Фазе H — выбор пользователя в UI). Подпись кабеля живёт в самом символе —
    /// свойства не выставляются (spec §2.3). Поворот 0° (spec §7). Отказ — WARN [SYMBOL].
    /// НЕ идемпотентно (очистка — Фаза I).</summary>
    public static class CableSymbolCreator
    {
        /// <summary>Символ на каждый CableSymbolPlacement. Возвращает число созданных.
        /// Библиотека/символ/вариант недоступны — один WARN, 0 созданных.</summary>
        public static int CreateSymbols(Page oPage, CableGeometryResult oGeom,
            DiagnosticLogger log)
        {
            if (oPage == null || oGeom == null)
            {
                log.Warn("[SYMBOL] CreateSymbols: page или geometry == null — символов не создаём");
                return 0;
            }
            int nTotal = oGeom.Symbols.Count;
            if (nTotal == 0) return 0;

            SymbolVariant oVariant;
            try
            {
                SymbolLibrary oLibrary = new SymbolLibrary(oPage.Project, AddInConfiguration.SymbolLibrary);
                Symbol oSymbol = oLibrary[AddInConfiguration.SymbolName];
                oVariant = oSymbol[AddInConfiguration.SymbolVariant];
            }
            catch (Exception oEx)
            {
                log.Warn("[SYMBOL] вариант '" + AddInConfiguration.SymbolLibrary + "'/" +
                    AddInConfiguration.SymbolName + "/" + AddInConfiguration.SymbolVariant +
                    " недоступен: " + oEx.GetType().Name + ": " + oEx.Message);
                return 0;
            }

            int nCreated = 0;
            foreach (CableSymbolPlacement oSym in oGeom.Symbols)
            {
                try
                {
                    SymbolReference oRef = new SymbolReference();
                    oRef.Create(oPage, oVariant);
                    oRef.Location = new PointD(oSym.Position.X, oSym.Position.Y);
                    nCreated++;
                    log.Log("[INFO] [SYMBOL] '" + (oSym.CableName ?? "<без имени>") + "' #" +
                        oSym.CableIndex.ToString(CultureInfo.InvariantCulture) + " @ (" +
                        oSym.Position.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                        oSym.Position.Y.ToString("F3", CultureInfo.InvariantCulture) + ")");
                }
                catch (Exception oEx)
                {
                    log.Warn("[SYMBOL] Create бросил " + oEx.GetType().Name + ": " + oEx.Message +
                        " (символ '" + (oSym.CableName ?? "<без имени>") + "' #" +
                        oSym.CableIndex.ToString(CultureInfo.InvariantCulture) + ")");
                }
            }
            log.Log("[INFO] [SYMBOL-SUM] символов " + nCreated + " из " + nTotal + ".");
            return nCreated;
        }
    }
}
```

- [ ] **Step 2: Самопроверка файла**

Аналогично Task 1 Step 2: namespace, using-и (допустимый набор + `MasterData`), сигнатуры по Interfaces, UTF-8 без BOM. Проверить, что `Create` вызывается на экземпляре (`oRef.Create`), не статически.

- [ ] **Step 3: Ревью**

Ревью кода (reviewer-субагент): единый WARN на недоступный вариант, WARN на каждый отказавший символ, отсутствие установки свойств символа (spec §2.3).

### Task 3: Конфиг + интеграция AnalyzeAction (секция 13)

**Files:**
- Modify: `addin/Configuration/AddInConfiguration.cs` (блок `PreviewDraw`, строки ~72–75)
- Modify: `addin/Actions/AnalyzeAction.cs` (строка 26 BUILD_STAMP, строка 27 BeginRun-текст, секция 13 строки 338–368)

**Interfaces:**
- Consumes: `ResolveLayerFromTree`/`CreateLines` (Task 1), `CreateSymbols` (Task 2).
- Produces: константы `GraphicsLayerName="EPLAN100"`, `GraphicsPenColorId=1`, `GraphicsPenWidthMm=0.35`, `SymbolLibrary="SPECIAL"`, `SymbolName="CABDCP2"`, `SymbolVariant=1` (Task 1–2 уже ссылаются).

- [ ] **Step 1: AddInConfiguration — заменить блок PreviewDraw**

Старый блок (целиком, включая комментарий над ним):

```csharp
        // Отладочное превью Фазы F: рисовать вычисленную геометрию Graphics.Line
        // на странице отчёта. НЕ идемпотентно: повторный прогон дублирует линии
        // (удалять вручную; идентификация объектов — Фаза I). Слой по умолчанию.
        public const bool PreviewDraw = false;
```

Заменить на:

```csharp
        // Фаза G (spec 2026-09-22-fase-g-graphics-design.md): реальные объекты кабельной
        // разводки вместо превью. Линии — слой EPLAN100 и красное перо (решение
        // пользователя, spec §2.2); слой резолвится из дерева отчёта
        // (GraphicLineCreator.ResolveLayerFromTree), не найден — слой по умолчанию.
        // НЕ идемпотентно: повторный прогон дублирует объекты (очистка — Фаза I).
        public const string GraphicsLayerName = "EPLAN100";
        public const int GraphicsPenColorId = 1;      // красный в штатной палитре EPLAN
        public const double GraphicsPenWidthMm = 0.35;

        // Символ кабеля: библиотека SPECIAL, 16 / CABDCP2, вариант 1 (= «A»; решение
        // пользователя 22.09.2026). В Фазе H выносится в UI (как TargetStripName).
        public const string SymbolLibrary = "SPECIAL";
        public const string SymbolName = "CABDCP2";
        public const int SymbolVariant = 1;
```

- [ ] **Step 2: AnalyzeAction — BUILD_STAMP и BeginRun**

Строку 26 заменить на:

```csharp
        private const string BUILD_STAMP = "2026-09-22 Этап 7 rev.10.0 (Фаза G: реальные линии EPLAN100 + символы CABDCP2)";
```

Строку 27 (текст BeginRun) заменить на:

```csharp
            _logger.BeginRun("TERMINAL_STRIP_ANALYZE — Этап 7 rev.10.0 (Фаза G: линии + символы)", BUILD_STAMP);
```

- [ ] **Step 3: AnalyzeAction — заменить секцию 13**

Старый блок (строки 338–368, целиком от `// --- 13. Фаза F: отладочное превью (PreviewDraw)...` до закрывающей скобки `if (AddInConfiguration.PreviewDraw)` включительно):

```csharp
            // --- 13. Фаза F: отладочное превью (PreviewDraw) — Graphics.Line по сегментам.
            // НЕ идемпотентно: повторный прогон дублирует линии (удалить вручную).
            if (AddInConfiguration.PreviewDraw)
            {
                int nDrawn = 0;
                // Красное перо превью (просьба пользователя): ColorId 1 = красный
                // в штатной палитре EPLAN; паттерн Pen — example/ShowCablesInSegment.cs.
                Pen oPreviewPen = new Pen();
                oPreviewPen.ColorId = 1;
                oPreviewPen.Width = 0.35;
                oPreviewPen.StyleId = 0;
                foreach (Seg oPrevSeg in oGeom.Segments)
                {
                    try
                    {
                        Line oNewLine = new Line();
                        oNewLine.Create(oPage,
                            new PointD(oPrevSeg.A.X, oPrevSeg.A.Y),
                            new PointD(oPrevSeg.B.X, oPrevSeg.B.Y));
                        oNewLine.Pen = oPreviewPen;
                        nDrawn++;
                    }
                    catch (Exception oPrevEx)
                    {
                        _logger.Warn("[PREVIEW] Line.Create бросил " +
                            oPrevEx.GetType().Name + ": " + oPrevEx.Message);
                    }
                }
                _logger.Summarize("Превью: нарисовано линий " + nDrawn + " из " +
                    oGeom.Segments.Count + " (слой по умолчанию; удалить вручную).");
            }
```

Заменить на:

```csharp
            // --- 13. Фаза G: реальные объекты по CableGeometryResult (spec
            // 2026-09-22-fase-g-graphics-design.md). Слой линий — GraphicalLayer из дерева
            // отчёта (строки на слое GraphicsLayerName); перо красное — рабочий вывод по
            // решению пользователя. НЕ идемпотентно: повтор — дубликаты (очистка — Фаза I).
            GraphicalLayer oCableLayer = GraphicLineCreator.ResolveLayerFromTree(
                lstAll, AddInConfiguration.GraphicsLayerName, _logger);
            int nLines = GraphicLineCreator.CreateLines(oPage, oGeom, oCableLayer, _logger);
            int nSymbols = CableSymbolCreator.CreateSymbols(oPage, oGeom, _logger);
            _logger.Summarize("Фаза G: линий " + nLines + "/" + oGeom.Segments.Count +
                ", символов " + nSymbols + "/" + oGeom.Symbols.Count +
                " (не идемпотентно: повторный прогон дублирует объекты).");
```

- [ ] **Step 4: Самопроверка интеграции**

Grep: в `addin/` не осталось ссылок на `PreviewDraw` (`grep -rn PreviewDraw addin/` — 0 строк); `GraphicLineCreator`/`CableSymbolCreator` упоминаются только в секции 13; секции 12 (геометрия) и 14 (`ReadCables`) не тронуты; BUILD_STAMP содержит `rev.10.0`. Проверить, что `GraphicalLayer` в зоне видимости AnalyzeAction (using `Eplan.EplApi.DataModel.Graphics` есть — строка 8).

- [ ] **Step 5: Ревью**

Ревью (reviewer-субагент): старый код превью удалён полностью; единственный Summarize Фазы G; счётчики/логика секций 1–12, 14 не изменены (diff-проверка).

### Task 4: Верификация на стенде + фиксация

**Files:**
- Modify: `summary.md` (новый пункт 45), `plan_implementation.md` (§16.4 Фаза G — статус после прогона)

**Interfaces:**
- Consumes: собранная DLL rev.10.0 (Tasks 1–3).

Выполняет пользователь на Windows-стенде (агент — готовит инструкцию и разбирает лог).

- [ ] **Step 1: Сборка на стенде**

Пользователь: запустить `addin\build_addin.bat`. Ожидание: компиляция без ошибок (CS-ошибки на этом шаге — вероятные точки: сигнатуры `SymbolLibrary`/`Item`/`Layer`; править по тексту ошибки, ревизия rev.10.1).

- [ ] **Step 2: Прогон rev.10.0**

Пользователь: перезапустить EPLAN, сделать активной страницу `&ЭМ2/8.1`, выполнить `TERMINAL_STRIP_ANALYZE`, сохранить лог `terminal_strip_addin.log` агенту.

- [ ] **Step 3: Разбор лога (агент)**

Критерии прогона (spec §6): BUILD_STAMP `rev.10.0`; `[ORIENT] Horizontal`; счётчики этапов 1–5 == rev.9.6 (77 точек; `[K4]` 61: 0×10/1×25/2×26; `[ANCHOR]` Y=-81.0 якорей 60; `[SPLIT]` 1; `[MATCH]` 77 сирот 0; `[CROSS]` СОВПАДАЕТ; `[CABGROUP]` 4; `[GEOM-SUM]` 4/28/0; `[DMERR]` 0); `[GRAPH-SUM]` 28 из 28 (слой 'EPLAN100'); `[SYMBOL]` 4 строки, `[SYMBOL-SUM]` 4 из 4; WARN ровно 10. Визуальная проверка пользователем: красные линии на EPLAN100, 4 символа за правым краем (X = 482.85 + idx·20: 482.85/502.85/522.85/542.85), в символах видны имена K140/K190/K190/K100.

- [ ] **Step 4: Фиксация**

Записать результаты в `summary.md` (п.45), обновить статус Фазы G в `plan_implementation.md` §16.4. Коммит — только по явному запросу пользователя.

## Открытые риски прогонa (спека §8)

1. `SymbolLibrary(Project, String)` / индексаторы `Item` — сигнатуры по KB, не компилировались: CS-ошибка Task 4 Step 1 покажет точное место.
2. Индекс варианта 1 != «A» → `[SYMBOL] вариант ... недоступен` в логе → сменить `SymbolVariant` в конфиге (одна константа).
3. `IncorrectSymbolTypeException` на `Create` → перегрузка `Create(SymbolVariant, Page)` (одна строка, ревизия).
4. Имя кабеля не видно в символе → сообщить пользователю, какая установка свойства нужна (отдельная ревизия).
5. Слой: `[GRAPH] слой 'EPLAN100' не найден` → линии на слое по умолчанию, работоспособность не ломается; выяснить фактическое имя слоя на странице.

---

## Ревизии по итогам прогона rev.10.0 (22.09.2026, спека §9)

Прогон rev.10.0 чист; визуальная проверка дала 5 ревизий (подходы 8 мм, колонка
символов +16 мм от последнего подхода, зазор 8 мм, DT-свойства, вариант 0) +
стопка 16 мм (подтверждено пользователем: кабель 1 сверху, последний на уровне
самой глубокой шины). Ожидания прогона rev.10.1 — в спеке §9.

### Task 5: Геометрия rev.10.1 — подходы/колонка/стопка/зазор (CableGeometryBuilder + конфиг + тесты)

**Files:**
- Modify: `addin/Geometry/CableGeometryBuilder.cs` (алгоритм Build + CableGeometryConfig)
- Modify: `addin/Configuration/AddInConfiguration.cs` (константы геометрии)
- Modify: `addin/Actions/AnalyzeAction.cs` (секция 12: проводка полей конфига; −CablePitchMm)
- Modify: `tests/CableGeometryTests.cs` (пересчёт ожиданий по новым правилам)

**Interfaces:**
- Consumes: CableLayoutModel/ReportOrientation (без изменений), oAnalysis.K4.Columns (без изменений).
- Produces: `CableGeometryResult` — та же структура; новые правила координат (спека §9). `CableGeometryConfig`: поля `ApproachOffsetMm=10, ApproachPitchMm=8, SymbolColumnOffsetMm=16, SymbolGapMm=8, SymbolStackPitchMm=16` (+ прежние BusOffsetMm/LevelPitchMm/BusLiftMm; −`SymbolOffsetMm`, −`PitchMm`).

**Правила алгоритма (H; perp/axis меняются местами для V существующими хелперами):**
1. Уровни шин сторон — без изменений (формулы dBusRight/dBusLeft/dBusOther).
2. Подход кабеля i: `X_i = dStripEndAxis + ApproachOffsetMm + i·ApproachPitchMm`.
   Шины кабеля заканчиваются на X_i (было: на оси символа).
3. Двусторонний кабель (обе группы непусты): одна вертикаль на X_i от нижнего
   уровня до верхнего (учитывая Y_i — расширить диапазон, если Y_i вне его);
   вырожденная (< EpsLen) — пропускается. Односторонний: без вертикали-подхода.
4. Колонка символов: `SymX = max(X_i по двусторонним) + SymbolColumnOffsetMm`;
   если двусторонних нет — `max(X_i)+SymbolColumnOffsetMm`; если кабелей нет —
   ранний выход как сейчас. Fallback dStripEndAxis=NaN — прежний (крайняя точка
   кабеля + WARN), подходы от него.
5. Стопка: `anchor = min по всем непустым уровням шин` (H: самая глубокая нижняя);
   `Y_i = anchor + (N−1−i)·SymbolStackPitchMm` (idx 0 сверху, idx N−1 на якоре).
6. Заход двустороннего: горизонталь на Y_i от X_i до `SymX − SymbolGapMm`
   (длина < EpsLen — пропуск). Односторонний: шина до `SymX − SymbolGapMm` на своём
   уровне, затем вертикаль на `SymX − SymbolGapMm` от уровня шины до Y_i
   (вырожденная — пропуск; в данных K100 даст длинную вертикаль, т.к. его символ
   внизу стопки, а шина сверху — семантика стороны из модели).
7. Symbols: `Position = (SymX, Y_i)`; CableName/CableIndex без изменений.
8. Контроль самосогласованности на данных стресс-стенда (спека §9): подходы
   482.85/490.85/498.85; SymX=514.85; линии до 506.85; Y = −97.875/−113.875/−129.875/
   −145.875; сегментов 29.

- [ ] **Step 1: CableGeometryConfig + Build по правилам 1–8** (полный код даёт имплементатор по спеке §9; структуру сегментов не менять — List<Seg>, Symbols, Warnings)
- [ ] **Step 2: AddInConfiguration** — −`CablePitchMm`, −`CableSymbolOffsetMm`; +`CableApproachOffsetMm=10.0`, `CableApproachPitchMm=8.0`, `CableSymbolColumnOffsetMm=16.0`, `CableSymbolGapMm=8.0` (коммент: по умолчанию для CABDCP2, в UI позже), `CableSymbolStackPitchMm=16.0` (коммент: из эталона rev.10.0); AnalyzeAction секция 12 — проводка новых полей.
- [ ] **Step 3: tests/CableGeometryTests.cs** — пересчитать ожидания всех кейсов по правилам 1–8 (кейсы: horizontal/vertical/stagger/Other/empty/NaN — состав сохраняется); добавить кейс «односторонний кабель не у якоря» (джамп).
- [ ] **Step 4: Самопроверка** — grep: CablePitchMm/SymbolOffsetMm не упоминаются; компиляция невозможна (Linux) — ревью.
- [ ] **Step 5: Ревью** (reviewer-субагент, против спеки §9).

### Task 6: Символы — вариант 0 + DT-свойства (CableSymbolCreator)

**Files:**
- Modify: `addin/Configuration/AddInConfiguration.cs` (SymbolVariant 1→0)
- Modify: `addin/Graphics/CableSymbolCreator.cs` (парсер DT + запись свойств)

**Interfaces:**
- Consumes: `CableSymbolPlacement.CableName` (полный DT, может быть null); `oRef.Properties[AnyPropertyId]` (ридер-паттерн проекта).
- Produces: без изменений сигнатуры `CreateSymbols`; новый дамп `[SYMDT]`.

- [ ] **Step 1: SymbolVariant = 0** (коммент: индексация Item 0-based; эмпирика rev.10.0: 1 → вариант B; вариант A = 0; в UI позже).
- [ ] **Step 2: Парсер DT** — `ParseDeviceTag(string strFullName)` → части (installation/mountingSite/userStruct/name; null = блок отсутствует). Правила: снять ведущий `=`; `установка` до `++`; `место сборки` после `++` до следующего `+`; `место установки` после `+` до `#` или `-`; `опред. структура` после `#` до `-`; `имя` после последнего `-` в хвосте `-K140`. Любая аномалия — вернуть что разобрано, части null.
- [ ] **Step 3: Запись свойств** после Create/Location каждого символа: хелпер `CreateAnyPropertyIdFromNumber` (reflection op_Implicit/op_Explicit → AnyPropertyId, порт из spike rev.7, private static); запись `oRef.Properties[oId].Value = str` для 1120/1220/1620 (непустые) и 20000 = имя; каждый — try/catch, отказ → WARN `[SYMDT]` (id, часть, тип+сообщение), продолжаем; пустая часть — пропуск без лога. Дамп `[SYMDT] 'имя': =… / ++… / #… / имя…` (что записано).
- [ ] **Step 4: Самопроверка** — grep SymbolVariant=0; парс на примерах пользователя (=HII-1.2++М+#3-K140 → HII-1.2/М/3/K140; =HII-1.1++М+#3-K190 → аналог); UTF-8 без BOM.
- [ ] **Step 5: Ревью** (reviewer-субагент: парсер, отказоустойчивость, WARN-бюджет).

### Task 7: Линия-ссылка от символа кабеля — rev.11.0 (решение пользователя 23.09.2026, спека §11)

**Files:**
- Modify: `tests/CableGeometryTests.cs` (Case16–19, TDD — тесты первыми)
- Modify: `addin/Geometry/CableGeometryBuilder.cs` (ReferenceElement, References, AddReference)
- Modify: `addin/Configuration/AddInConfiguration.cs` (4 константы CableReference*)
- Create: `addin/Graphics/ReferenceArrowCreator.cs` (Line + PolyLine Closed→IsSurfaceFilled)
- Modify: `addin/Actions/AnalyzeAction.cs` (проводка конфига, дамп [GEOM] ref, вызов в секции 13, BUILD_STAMP rev.11.0)

**Interfaces:**
- Consumes: `CableSymbolPlacement` (SymAxis/ряд символа из прохода (г) Build), `GraphicsLayerName`/перо конфига.
- Produces: `CableGeometryResult.References: List<ReferenceElement>` (НЕ в Segments); дампы `[REF]`/`[REF-SUM]`; `[GEOM-SUM]` расширен полем «, ссылок N».

**KB (проверено 23.09.2026):** `Graphics.PolyLine : GraphicalPlacement` — `Create(Page)`, `SetPointAt(int, ref PointD)` (свежая PolyLine — 4 точки), `Closed`, `IsSurfaceFilled` (сеттер бросает при незамкнутой — порядок Closed→IsSurfaceFilled); перо/слой наследуются.

**Геометрия:** старт = `SymAxis + sDir·(зазор габарит/2)`, уровень = ряд символа; линия 20 мм включая стрелку (остриё = конец линии); стрелка: остриё / (остриё−7, ±2) / вырез (остриё−4); H→+X, V→−Y через sDir. Ожидания прогона: счётчики 1–5 == rev.10.11 построчно; `[REF-SUM]` 4 из 4; WARN без изменений.

- [x] **Step 1: Тесты Case16–19** (H, V, skip-empty, кастомные длины; TDD).
- [x] **Step 2: Геометрия** (ReferenceElement + AddReference в проходе (г) после Symbols.Add).
- [x] **Step 3: Конфиг + ReferenceArrowCreator + секция 13 + BUILD_STAMP rev.11.0.**
- [x] **Step 4: Ревью** — Approved with minors; миноры исправлены (мёртвый using, guard 4 точек, знаменатель [REF-SUM] → nTotal, null-safe лог, doc «логическая единица»). Примечание: при коммите не забыть `git add addin/Graphics/ReferenceArrowCreator.cs` (untracked).
- [x] **Step 5: Прогон на стенде** (23.09.2026) — прогоны H+V чисты: `[GEOM-SUM]` 4/25/ссылок 4/0; `[REF-SUM]` 4 из 4, стрелок 4 из 4 (EPLAN100); счётчики 1–5 == rev.10.11 построчно; WARN 26 = 10 + 16 SYMDT (rev.10.12 ещё не прогонялся); визуально подтверждено на обеих ориентациях. Прогон rev.10.12 (DT) — отдельно, следующим.

## Открытые риски (после rev.11.0)

- rev.10.12 (DT, S063113 → индексатор-SET) — ждёт СВОЕГО прогона (после rev.11.0).
- Отложенные миноры п.46/49/50: тихий all-null парсер DT, кэш AnyPropertyId, проверка «пробег шины сквозь чужой символ» при уровнях < B/2, guard `CableSymbolColumnOffsetMm` при B ≥ 32.
- Идемпотентность (Фаза I): линии-ссылки дублируются при повторном прогоне, как остальные объекты.

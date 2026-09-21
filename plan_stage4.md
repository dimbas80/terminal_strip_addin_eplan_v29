# Этап 4 (Фаза D) — TerminalConnectionModel: связка точки с конкретным Connection

> **Для agentic-работы:** выполнять по `plan_stage4.md` задача-за-задачей (SDD, как Этап 3 —
> субагенты; компиляция и прогон — шаг пользователя на Windows). Чекбоксы `- [ ]`.
> Текущий BUILD_STAMP — rev.6.3 (AnalyzeAction.cs:26); правка логики = новая ревизия rev.7.0.

**Goal:** из уже построенных данных (геометрия `LeadAnalysis` + DataModel `DmReport` +
свод `MatchBuilder.MatchRow[]`) собрать `TerminalConnectionModel[]` (§4.4 мастер-плана) —
по точке подключения определить её конкретный `Connection` (DmRow) и геометрическую `Side`.

**Architecture:** новый чистый модуль `addin/Data/TerminalConnectionModel.cs` (модель +
builder-функция). Builder потребляет готовые `MatchRow[]` (точка ↔ клемма) + `DmReport` +
`LeadAnalysis` (для стубов → положение ряда клемм) + ориентацию и выдаёт список моделей
плюс дамп `[TCM]`. Встраивается в `AnalyzeAction.Run` после свода `[MATCH]` (rev.6.3).

**Tech Stack:** C# (.NET Framework 4.x, EPLAN 2.9 add-in, csc.exe на Windows-стенде),
чистая геометрия без EPLAN-типов (Pt/Seg — LeadGeometry.cs).

**Spec:** `plan_implementation.md` §4.4 (`TerminalConnectionModel`) + §23 Фаза D; вход —
`summary.md` п.35. Текущий код: `addin/Data/MatchBuilder.cs` (MatchRow), `addin/Data/
EplanTerminalStripReader.cs` (DmRow/DmReport), `addin/Geometry/LeadGeometry.cs`
(Pt/Seg/LeadAnalysis/K4Report), `addin/Actions/AnalyzeAction.cs` (Run).

## Global Constraints

- Кодировка addin/*.cs — UTF-8 без BOM (конвенция addin/, НЕ spike).
- Чистая геометрия: не вводить EPLAN-типы в Geometry/Data-модули (только Pt/Seg/string).
- Культурная гигиена: весь разбор/вывод чисел — `CultureInfo.InvariantCulture`.
- Никаких EPLAN-API в новых Data-модулях — только в Actions/Report (как существующие).
- Имена: венгерские префиксы (oRow/oAnalysis/lstPh/dRowCoord), русские комментарии.
- Коммиты — только по явному запросу пользователя; SDD-ledger вне git (.superpowers/).
- Верификация — прогон на Windows (`build_addin.bat`, BUILD_STAMP, дамп `[TCM]`), не
  юнит-тесты (тестовой инфраструктуры в проекте нет; чистые хелперы гоняются прогоном).

---

## Task 1: Модель `TerminalConnectionModel` + enum `TerminalSide` + чистый хелпер стороны

**Files:**
- Create: `addin/Data/TerminalConnectionModel.cs` (весь модуль Фазы D: модель + builder)

**Interfaces:**
- Consumes: `Pt` (LeadGeometry.cs:8), `MatchRow` (MatchBuilder.cs:11), `DmRow`/`DmReport`
  (EplanTerminalStripReader.cs:11/52), `K4Report`/`LeadAnalysis` (LeadGeometry.cs:42/77),
  `AnchorMap`/`ReportOrientation`/`ParseTerminalNumber` (AnchorResolver.cs), `MatchBuilder.IsCableRow`
  (MatchBuilder.cs:73), `Seg` (LeadGeometry.cs:22), `DiagnosticLogger`.
- Produces: `enum TerminalSide`, `class TerminalConnectionModel`,
  `static class TerminalGeometry.PointSide(bool, Pt, double)`,
  `static class TerminalConnectionModelBuilder.Build(...) → List<TerminalConnectionModel>`.

- [ ] **Step 1: Написать модель и enum (полный код)**

```csharp
using System.Collections.Generic;
using System.Globalization;

namespace MyEplanActions
{
    /// <summary>Геометрическая сторона подключения относительно ряда/столбца клеммы.
    /// Horizontal-форма: Top (Y < ряда), Bottom (Y > ряда); Vertical-форма: Left (X <
    /// столбца), Right (X > столбца). Unknown — ряд/столбец не определился (Этап 5
    /// уточняет Side по правилам §9).</summary>
    public enum TerminalSide { Top, Bottom, Left, Right, Unknown }

    /// <summary>Модель конкретного подключения (Фаза D, plan_implementation §4.4).
    /// Одна точка геометрии = одно подключение клеммы; связка с конкретным DmRow —
    /// эвристика по геометрической стороне («верх ↔ Ext, низ ↔ Int» для Horizontal;
    /// «лево ↔ Ext, право ↔ Int» для Vertical), валидируется дампом [TCM].</summary>
    public sealed class TerminalConnectionModel
    {
        public string Terminal;          // полное имя клеммы из DM (TerminalKey) или null
        public int TerminalNumber = -1;  // номер клеммы (суффикс после ':')
        public string ConnectionName;    // DmRow.ConnectionName (null = не сопоставлено)
        public string PinName;           // DmRow.FunctionPinName
        public int PinIndex = -1;        // DmRow.PinIndex
        public Pt ConnectionPoint;       // точка подключения (геометрия)
        public int ColumnIndex = -1;     // колонка К4 (BindIndex); -1 = сирота
        public TerminalSide Side = TerminalSide.Unknown;
        public bool IsCable;             // кабель/провод (DmRow: CableName != null || №31058)
        public string CableName;         // DmRow.CableName (null = провод)
        public bool HasConn;             // DmRow.HasConn
        public string BridgePeer;        // DmRow.PeerName (для мостов) или null
        public bool IsBridge;            // точка — лист моста К2 (из LeadAnalysis.PointIsBridge)
    }

    /// <summary>Чистая геометрия стороны (без EPLAN): точка сверху/снизу (Horizontal)
    /// или слева/справа (Vertical) относительно координаты ряда/столбца клеммы на
    /// перпендикулярной оси. Допуск TolPerpMm — толщина ряда клемм: точки ближе
    /// TolPerpMm к ряду считаются на нём (Unknown).</summary>
    public static class TerminalGeometry
    {
        public const double TolPerpMm = 1.0;

        /// <summary>Сторона точки относительно перпендикулярной координаты ряда.
        /// dPerp = Y ряда (Horizontal) или X столбца (Vertical); bVertical — ось
        /// ориентации. Возвращает Side в осях формы (Top/Bottom для Horizontal,
        /// Left/Right для Vertical), Unknown при |Δ| ≤ TolPerpMm.</summary>
        public static TerminalSide PointSide(bool bVertical, Pt oPoint, double dPerp)
        {
            double dDelta = bVertical ? (oPoint.X - dPerp) : (oPoint.Y - dPerp);
            if (dDelta > TolPerpMm) return bVertical ? TerminalSide.Right : TerminalSide.Bottom;
            if (dDelta < -TolPerpMm) return bVertical ? TerminalSide.Left : TerminalSide.Top;
            return TerminalSide.Unknown;
        }
    }
}
```

- [ ] **Step 2: Само-проверка (без компиляции на Linux):** enum/классы синтаксически
  корректны; `PointSide` — чистая функция без зависимостей; конвенции addin/ (без BOM);
  InvariantCulture не нужен (только сравнения double). Отметить «код самопроверен;
  сборка — шаг пользователя».

- [ ] **Step 3: Снапшот** — `.superpowers/sdd/plan_stage4/snapshots/TerminalConnectionModel.cs`
  (для диффа ревьюеру; коммитов нет до явного запроса).

---

## Task 2: Builder `TerminalConnectionModelBuilder.Build` — связка точка ↔ Connection + Side

**Files:**
- Modify: `addin/Data/TerminalConnectionModel.cs` (добавить builder в тот же namespace)

**Interfaces:**
- Consumes: `List<MatchRow>` (свод MatchBuilder.Build; lstMatch[i] ↔ Points[i] по индексу),
  `DmReport`, `LeadAnalysis` (Stubs/PointIsBridge), `K4Report`, `AnchorMap`, `string strTarget`,
  `DiagnosticLogger`.
- Produces: `List<TerminalConnectionModel>`; строки `[TCM]` + `[TCM-SUM]`.

- [ ] **Step 1: Написать builder (полный код ниже)**

```csharp
    /// <summary>Builder TerminalConnectionModel (Фаза D): по каждой точке свода [MATCH]
    /// определяет конкретный DmRow (Connection) эвристикой по геометрической стороне и
    /// геометрическую Side. Чистый модуль — только Pt/Seg/строка, без EPLAN-API.</summary>
    public static class TerminalConnectionModelBuilder
    {
        /// <summary>Перпендикулярная координата ряда клеммы для колонки (Horizontal —
        /// Y ряда; Vertical — X столбца). Из стубов-маркеров (EPLAN100) колонки: медиана
        /// середины стубов, чья координата оси совпадает с колонкой (в пределах
        /// полушага). NaN — ряд не определился. Для Horizontal стуб вертикален
        /// (ось=X, перпендикуляр=Y); для Vertical — горизонтален (ось=Y, перпендикуляр=X).</summary>
        private static double ColumnPerpRef(LeadAnalysis oAnalysis, K4Report oK4, int nCol)
        {
            if (oK4 == null || nCol < 0 || nCol >= oK4.Columns.Count) return double.NaN;
            bool bV = oK4.Orientation == ReportOrientation.Vertical;
            double dAxisCol = oK4.Columns[nCol];
            List<double> lstPerp = new List<double>();
            foreach (Seg oStub in oAnalysis.Stubs)
            {
                double dStubAxis = bV ? oStub.A.Y : oStub.A.X;      // ось стуба
                if (System.Math.Abs(dStubAxis - dAxisCol) > oK4.Half) continue;
                double dPerp = bV ? (oStub.A.X + oStub.B.X) / 2.0   // Vertical: перпендикуляр = X
                                   : (oStub.A.Y + oStub.B.Y) / 2.0; // Horizontal: перпендикуляр = Y
                lstPerp.Add(dPerp);
            }
            if (lstPerp.Count == 0) return double.NaN;
            lstPerp.Sort();
            return lstPerp[lstPerp.Count / 2];
        }

        /// <summary>Сборка моделей по точкам свода [MATCH]. Связка точка ↔ DmRow:
        /// Top/Left-точки ← Ext-пул, Bottom/Right-точки ← Int-пул (по порядку);
        /// Unknown/переполнение пула — по порядку из оставшихся DmRow; сироты (клемма
        /// не сопоставлена) — модель с пустым Connection. Валидируется дампом [TCM].</summary>
        public static List<TerminalConnectionModel> Build(
            List<MatchRow> lstMatch, DmReport oDm, LeadAnalysis oAnalysis, K4Report oK4,
            string strTarget, DiagnosticLogger log)
        {
            List<TerminalConnectionModel> lstModels = new List<TerminalConnectionModel>();
            if (lstMatch == null || lstMatch.Count == 0) return lstModels;
            bool bV = oK4 != null && oK4.Orientation == ReportOrientation.Vertical;

            // 1) Точки по клемме (полное имя); lstMatch[i] ↔ Points[i].
            Dictionary<string, List<int>> dicByTerm = new Dictionary<string, List<int>>();
            List<int> lstOrphans = new List<int>();
            for (int i = 0; i < lstMatch.Count; i++)
            {
                string strTerm = lstMatch[i].TerminalName;
                if (strTerm == null) lstOrphans.Add(i);
                else
                {
                    List<int> lst;
                    if (!dicByTerm.TryGetValue(strTerm, out lst)) { lst = new List<int>(); dicByTerm[strTerm] = lst; }
                    lst.Add(i);
                }
            }

            // 2) DmRow целевого клеммника (без Bridge), по клемме.
            Dictionary<string, List<DmRow>> dicRowsByTerm = new Dictionary<string, List<DmRow>>();
            foreach (DmRow oRow in oDm.Rows)
            {
                if (oRow.StripName != strTarget || oRow.Side == "Bridge") continue;
                List<DmRow> lst;
                if (!dicRowsByTerm.TryGetValue(oRow.TerminalName, out lst)) { lst = new List<DmRow>(); dicRowsByTerm[oRow.TerminalName] = lst; }
                lst.Add(oRow);
            }

            // 3) По клемме: стороны точек, пулы Ext/Int, попарное назначение.
            int nUnmatched = 0;
            foreach (KeyValuePair<string, List<int>> oEntry in dicByTerm)
            {
                List<DmRow> lstRows;
                dicRowsByTerm.TryGetValue(oEntry.Key, out lstRows);
                if (lstRows == null) lstRows = new List<DmRow>();

                List<TerminalSide> lstSides = new List<TerminalSide>();
                foreach (int nIdx in oEntry.Value)
                {
                    MatchRow oRow = lstMatch[nIdx];
                    double dRef = oRow.ColumnIndex >= 0
                        ? ColumnPerpRef(oAnalysis, oK4, oRow.ColumnIndex) : double.NaN;
                    lstSides.Add(double.IsNaN(dRef)
                        ? TerminalSide.Unknown
                        : TerminalGeometry.PointSide(bV, oRow.Point, dRef));
                }

                List<DmRow> lstExt = new List<DmRow>(), lstInt = new List<DmRow>();
                foreach (DmRow oRow in lstRows)
                    (oRow.Side == "Int" ? lstInt : lstExt).Add(oRow);

                List<DmRow>[] arrPool = { lstExt, lstInt };
                int[] arrPoolIdx = { 0, 0 };
                List<DmRow> lstRemain = new List<DmRow>(lstRows);
                for (int k = 0; k < oEntry.Value.Count; k++)
                {
                    int nIdx = oEntry.Value[k];
                    TerminalSide oSide = lstSides[k];
                    int nPool = (oSide == TerminalSide.Top || oSide == TerminalSide.Left) ? 0
                              : (oSide == TerminalSide.Bottom || oSide == TerminalSide.Right) ? 1 : -1;
                    DmRow oPicked = null;
                    if (nPool >= 0 && arrPoolIdx[nPool] < arrPool[nPool].Count)
                        oPicked = arrPool[nPool][arrPoolIdx[nPool]++];
                    if (oPicked == null)
                    {
                        foreach (DmRow oRow in lstRemain)
                            if (oRow != null) { oPicked = oRow; break; }
                    }
                    if (oPicked != null) lstRemain.Remove(oPicked);
                    if (oPicked == null) nUnmatched++;
                    lstModels.Add(MakeModel(lstMatch[nIdx], oPicked, oSide, IsBridge(oAnalysis, nIdx)));
                }
            }

            // 4) Сироты — модель с пустым Connection.
            int nOrphan = 0;
            foreach (int nIdx in lstOrphans)
            {
                nOrphan++;
                double dRef = lstMatch[nIdx].ColumnIndex >= 0
                    ? ColumnPerpRef(oAnalysis, oK4, lstMatch[nIdx].ColumnIndex) : double.NaN;
                lstModels.Add(MakeModel(lstMatch[nIdx], null,
                    double.IsNaN(dRef) ? TerminalSide.Unknown
                        : TerminalGeometry.PointSide(bV, lstMatch[nIdx].Point, dRef),
                    IsBridge(oAnalysis, nIdx)));
            }

            // 5) Дамп [TCM] + итог.
            int nBridgeLeaves = 0;
            foreach (TerminalConnectionModel oM in lstModels)
            {
                if (oM.IsBridge) nBridgeLeaves++;
                string strConn = oM.ConnectionName ?? (oM.IsBridge ? "<мост>" : "<нет сопоставления>");
                string strCable = oM.IsCable ? (oM.CableName ?? "№31058") : "<провод>";
                log.Log("[TCM] #" + oM.TerminalNumber.ToString(CultureInfo.InvariantCulture) + " '" +
                    (oM.Terminal ?? "-") + "' " + oM.Side + " (" +
                    oM.ConnectionPoint.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                    oM.ConnectionPoint.Y.ToString("F3", CultureInfo.InvariantCulture) +
                    ") -> conn='" + strConn + "' pin=" + oM.PinIndex.ToString(CultureInfo.InvariantCulture) +
                    " cable=" + strCable +
                    (oM.BridgePeer != null ? " bridge->" + oM.BridgePeer : "") +
                    (oM.ConnectionName == null && !oM.IsBridge ? " [СИРОТА]" : ""));
                if (oM.ConnectionName == null && !oM.IsBridge)
                    log.Warn("[TCM] точка без сопоставления (клемма '" + (oM.Terminal ?? "-") +
                        "'): связи с DmRow нет");
            }
            log.Log("[INFO] [TCM-SUM] моделей " + lstModels.Count + ": сирот " + nOrphan +
                ", без Connection " + nUnmatched + ", листьев моста " + nBridgeLeaves +
                " (связка точка↔Connection — эвристика по стороне, см. [TCM])");
            return lstModels;
        }

        private static bool IsBridge(LeadAnalysis oAnalysis, int nIdx)
        {
            return oAnalysis.PointIsBridge != null && nIdx < oAnalysis.PointIsBridge.Count
                && oAnalysis.PointIsBridge[nIdx];
        }

        private static TerminalConnectionModel MakeModel(MatchRow oRow, DmRow oDmRow,
            TerminalSide oSide, bool bIsBridge)
        {
            TerminalConnectionModel oM = new TerminalConnectionModel();
            oM.Terminal = oRow.TerminalName;
            oM.TerminalNumber = AnchorResolver.ParseTerminalNumber(oRow.TerminalName ?? "");
            oM.ConnectionPoint = oRow.Point;
            oM.ColumnIndex = oRow.ColumnIndex;
            oM.Side = oSide;
            oM.IsBridge = bIsBridge;
            if (oDmRow != null)
            {
                oM.ConnectionName = oDmRow.ConnectionName;
                oM.PinName = oDmRow.PinName;
                oM.PinIndex = oDmRow.PinIndex;
                oM.IsCable = MatchBuilder.IsCableRow(oDmRow);
                oM.CableName = oDmRow.CableName;
                oM.HasConn = oDmRow.HasConn;
                oM.BridgePeer = oDmRow.PeerName;
            }
            return oM;
        }
    }
}
```

- [ ] **Step 2: Само-проверка** — (а) `ColumnPerpRef`: H ось=X/перпендикуляр=Y, V ось=Y/
  перпендикуляр=X; (б) пулы Ext/Int по порядку, остаток по порядку из lstRemain;
  (в) `IsCableRow`/`ParseTerminalNumber` — публичные, переиспользованы; (г) числа —
  InvariantCulture; (д) нет EPLAN-типов; (е) `IsBridge` защищён границей PointIsBridge.
  Отметить «код самопроверен; сборка — шаг пользователя».

- [ ] **Step 3: Снапшот** — обновить `.superpowers/sdd/plan_stage4/snapshots/TerminalConnectionModel.cs`.

---

## Task 3: Встраивание в `AnalyzeAction.Run` + BUILD_STAMP rev.7.0

**Files:**
- Modify: `addin/Actions/AnalyzeAction.cs` — Run (вызвать builder после [MATCH], строка ~277);
  строка 26 — BUILD_STAMP.
- Reference: `MatchRow`/`MatchBuilder.Build` возвращают `List<MatchRow>` (MatchBuilder.cs:78).

**Interfaces:**
- Consumes: `TerminalConnectionModelBuilder.Build`, `TerminalConnectionModel`, `AnchorMap`
  (уже есть как oMap в Run? — в Run Builder вызывается через MatchBuilder; builder берёт
  `AnchorMap` не нужен по сигнатуре — принимает strTarget). `DmReport`, `LeadAnalysis`, `K4Report`.
- Produces: `[TCM]`-дамп + `[TCM-SUM]` в логе; `List<TerminalConnectionModel>` (заполняется).

- [ ] **Step 1: Встроить вызов в Run после свода [MATCH] (строки ~275-280)**

```csharp
            // --- 9. Свод [MATCH] (Задача 5): точки ↔ клеммы ↔ кабель/провод + Bridge ---
            _logger.Log("[INFO] --- Свод [MATCH]: точки ↔ клеммы ↔ кабель/провод ---");
            List<MatchRow> lstMatch = MatchBuilder.Build(oAnalysis, oDm, lstPh, _logger);

            // --- 10. Фаза D: TerminalConnectionModel — связка точки с конкретным Connection ---
            _logger.Log("[INFO] --- Фаза D: TerminalConnectionModel (точка ↔ Connection по стороне) ---");
            List<TerminalConnectionModel> lstTcm = TerminalConnectionModelBuilder.Build(
                lstMatch, oDm, oAnalysis, oAnalysis.K4, AddInConfiguration.TargetStripName, _logger);

            _logger.Summarize("Готово: точек подключения " + oAnalysis.Points.Count +
                "; строк [DM] " + oDm.Rows.Count + "; строк [MATCH] " + lstMatch.Count +
                "; моделей [TCM] " + lstTcm.Count + ".");
```

- [ ] **Step 2: BUILD_STAMP → rev.7.0**

```csharp
        private const string BUILD_STAMP = "2026-09-21 Этап 4 rev.7.0 (Фаза D: TerminalConnectionModel — связка точки с Connection по геометрической стороне, дамп [TCM])";
```

- [ ] **Step 3: Само-проверка** — oAnalysis.K4 != null в Run (детектор построил К4; при
  K4 невалиден MatchBuilder уже вернул [MATCH-SKIP] → lstMatch пуст → builder вернёт пусто,
  без падения — builder принимает oK4 и проверяет null); все имена/типы совпадают с Task 1-2.

- [ ] **Step 4: Снапшот** — `.superpowers/sdd/plan_stage4/snapshots/AnalyzeAction.cs` (только
  секции 10 + BUILD_STAMP).

---

## Критерии готовности Этапа 4 (Фаза D)

1. `TerminalConnectionModelBuilder.Build` выдаёт `List<TerminalConnectionModel>` — по одной
   модели на точку, `ConnectionName` заполнен для сопоставленных (эвристика по стороне).
2. `Side` — Top/Bottom (Horizontal) или Left/Right (Vertical) по геометрии относительно ряда
   из стуба; Unknown — при неопределённом ряде.
3. Дамп `[TCM]` + `[TCM-SUM]` в логе; `[TCM]`-WARN для точек без сопоставления.
4. Счётчики существующих этапов НЕ изменились: геометрия/`[MATCH]`/`[K4]`/`[CROSS]`/`[DMERR]`
   — как в rev.6.3 (верификация на прогоне: BUILD_STAMP rev.7.0, счётчики == rev.6.3).
5. Пользователь визуально валидирует связку точка↔Connection по `[TCM]` (эвристика «верх ↔
   Ext» может быть откорректирована на Этапе 5 / следующей ревизией).

## Handoff (после выполнения кода)

- **Ждём прогона пользователя** (`build_addin.bat`, `TERMINAL_STRIP_ANALYZE` на `&ЭМ2/8.1`):
  BUILD_STAMP rev.7.0; счётчики этапов 1-3 == rev.6.3; дамп `[TCM]` (77 строк) + `[TCM-SUM]`;
  сирот/без-Connection — ожидается 0 (связка должна покрыть все 77 точек; фантом 472.85 —
  колонка №60, раздвоение №2 — как в [MATCH]).
- Действия агента по приходу лога: критерии 1-4 выше; разбор `[TCM]`-WARN; зафиксировать в
  summary.md (п.37) + план; фикс эвристики (если пользователь укажет) — следующая ревизия.
- Minor-долги Этапа 3 («Minors deferred» из ledger Этапа 3) — в Фазу D при касании файлов:
  на этой ревизии берём только «два источника оси» НЕ трогаем (builder не использует
  AddInConfiguration.Orientation напрямую — берёт oK4.Orientation); остальные — по отдельности.

## Статус (21.09.2026)

- [x] Task 1 — модель/enum/`TerminalGeometry` — реализовано (coder-субагент), ревьюер Approved.
- [x] Task 2 — builder — реализовано, ревьюер Approved.
- [x] Task 3 — встраивание в Run + BUILD_STAMP rev.7.0 — реализовано.
- Ревью Фазы D: **Approved with minors** (0 Critical, 1 Major non-blocking, 7 Minor).
  Major исправлен (добавлены поля `Destination`/`ConnectionDesignation` из §4.4).
  Миноры исправлены: заголовок `BeginRun` → rev.7.0; единый счётчик `nNoConn`
  (совпадает с меткой `[СИРОТА]`); null-guard на `oDm`/`oAnalysis`; кэш `dicPerpCache`
  (ColumnPerpRef не пересчитывает ряд на точку); doc-контракт порядка вывода
  (группировка по клеммам, НЕ `lstTcm[i]↔Points[i]`).
  Миноры отложены (документировано в ledger): `IsCable` — три-стейт (bool схлопывает
  Unknown→провод; Этап 5); `BridgePeer` всегда null (Bridge-строки отфильтрованы из
  модели; мосты — Этап 5); порядок выбора DmRow при нескольких точках одной стороны —
  по порядку DM (эвристика, валидируется [TCM]); семантика Side vs §9 — Этап 5.
- **Ждём прогона пользователя** (`build_addin.bat`, `TERMINAL_STRIP_ANALYZE` на `&ЭМ2/8.1`):
  BUILD_STAMP rev.7.0; счётчики этапов 1-3 == rev.6.3 (геометрия/`[MATCH]`/`[K4]`/
  `[CROSS]`/`[DMERR]` не должны измениться); дамп `[TCM]` (77 строк) + `[TCM-SUM]`
  (ожидается «без сопоставления Connection 0», листьев моста 1); визуальная валидация
  пользователем связки точка↔Connection (эвристика «верх↔Ext, низ↔Int»).

- [x] **rev.7.1 (21.09.2026, фикс по данным пользователя):** инверсия Top/Bottom.
  Визуально: клемма 1 — вверх, провод; клемма 31 — вниз кабель, вверх провод, а код
  помечал наоборот (Bottom/Top). Причина — на странице EPLAN верх = БОЛЬШАЯ Y. Исправлено:
  (а) `TerminalGeometry.PointSide` — dDelta>0 = Top, dDelta<0 = Bottom (Horizontal);
  (б) паринг в Build — Top↔Int, Bottom↔Ext (сохранена валидированная связка кабель/провод:
  клемма 31 — кабель на Ext/низ, провод на Int/верх). Left/Right (Vertical) — без данных,
  не тронуты. BUILD_STAMP → rev.7.1. Ожидание прогона: Side в [TCM] совпадает с визуалом
  (клемма 1 — Top/провод, клемма 31 — кабель Bottom, провод Top), Connection/кабель не
  изменились.

# Этап 6 (Фаза F) — Geometry Engine: CableGeometryBuilder + превью

> **Для agentic-работы:** выполнять по `plan_stage6.md` задача-за-задачей (SDD, как
> Этапы 3–5 — субагенты; компиляция, unit-раннер и прогон — шаг пользователя на Windows).
> Чекбоксы `- [ ]`. Текущий BUILD_STAMP — rev.8.1 (`addin/Actions/AnalyzeAction.cs:26`);
> правка логики = новая ревизия **rev.9.0**. SDD-ledger: `.superpowers/sdd/plan_stage6/`.

**Goal:** из `CableLayoutModel` (Фаза E, rev.8.1) вычислить чистую геометрию кабельной
разводки — ветви + шины + позиции SHCP2 (`CableGeometryResult`), без создания объектов
EPLAN, с unit-тестами и отладочным превью (`Graphics.Line` по константе конфига).

**Architecture:** новый чистый модуль `addin/Geometry/CableGeometryBuilder.cs`
(результат + builder, без EPLAN-типов и без логгера — дампы `[GEOM]` печатает
`AnalyzeAction` из результата). Попутно — два pure-move (выделение
`TerminalConnectionModelBuilder` и `ReportOrientation` в отдельные файлы), чтобы
unit-раннер компилировался без EPLAN-зависимостей. Интеграция — секции 12–13
`AnalyzeAction.Run`; превью — по константе `PreviewDraw`.

**Tech Stack:** C# 5 (.NET Framework 4.x, синтаксис под csc 4.0.30319 — без
интерполяций/expression-bodied), EPLAN 2.9 add-in (csc.exe на Windows-стенде),
чистая геометрия Pt/Seg (LeadGeometry.cs). Тесты — консольный раннер `tests/`
(csc, exit code = число провалов; EPLAN-ссылок нет).

**Spec:** `docs/superpowers/specs/2026-09-21-fase-f-cable-geometry-design.md`
(согласован пользователем 21.09.2026) + `plan_implementation.md` §11/§23 Фаза F;
эталон `example/Кабель с двух сторон.pdf`; вход — `summary.md` п.38.

## Global Constraints

- Кодировка addin/*.cs, tests/*.cs — UTF-8 без BOM (конвенция addin/, НЕ spike).
- Синтаксис C# 5: без `$"..."`, без `?.`, без expression-bodied — csc 4.0.30319.
- Чистая геометрия: EPLAN-типы НЕ вводить в `Geometry/` и новые `Data/`-модули
  (только Pt/Seg/string); builder без DiagnosticLogger — дампы в AnalyzeAction.
- Культурная гигиена: весь вывод чисел — `CultureInfo.InvariantCulture`.
- Имена: венгерские префиксы (oCable/oRes/lstGroup/dValue/bFlag/nCount), русские
  комментарии, `/// <summary>` на публичных членах.
- Топология/параметры — только из spec: шины сторон (Right → max(perp)+Off,
  Left → min(perp)−Off, Other — как Right + WARN), вынос H → +X / V → −Y (s=±1),
  `SymbolAxis = max(s*axis)*s + s*Offset + idx*Pitch`, символ = среднее уровней шин
  задействованных групп; Off/Offset/Pitch = 10.0/10.0/20.0 (AddInConfiguration).
- Верх страницы = большая Y (урок rev.7.1); бакеты CableModel: Right = Top/Right,
  Left = Bottom/Left, Other = Unknown.
- Коммиты — только по явному запросу пользователя; вместо коммитов — снапшоты
  changed-файлов в `.superpowers/sdd/plan_stage6/snapshots/` (для диффа ревьюеру).
- Верификация: unit-раннер и прогон addin — на Windows-стенде
  (`tests/build_tests.bat`, `addin/build_addin.bat`, BUILD_STAMP rev.9.0 в логе);
  локально (Linux) компилятора нет — самопроверка статическая.
- `build_addin.bat` рекурсивно собирает ВСЕ .cs под `addin/` — новые файлы в
  `addin/` не требуют правки bat; файлы в `tests/` в сборку addin НЕ попадают.

---

## Task 1: Pure-move — `TerminalConnectionModelBuilder` и `ReportOrientation` в отдельные файлы

Зачем: unit-раннер (Task 3) компилирует чистые файлы без EPLAN; сейчас enum
`ReportOrientation` живёт в `AnchorResolver.cs` (тянет `PhRow` → `MatchBuilder` →
EPLAN), а файл `TerminalConnectionModel.cs` смешивает чистую модель с билдером,
зависящим от `DmReport` (EPLAN). Оба переноса — дословные, поведение не меняется.

**Files:**
- Create: `addin/Data/TerminalConnectionModelBuilder.cs`
- Modify: `addin/Data/TerminalConnectionModel.cs` (удалить класс билдера, строки 61–255)
- Create: `addin/Geometry/ReportOrientation.cs`
- Modify: `addin/Anchor/AnchorResolver.cs` (удалить enum, строки 7–10)

**Interfaces:**
- Produces: `enum ReportOrientation { Horizontal, Vertical }` в
  `addin/Geometry/ReportOrientation.cs` (namespace `MyEplanActions` — тот же);
  `static class TerminalConnectionModelBuilder` в своём файле (сигнатуры без
  изменений: `Build(List<MatchRow>, DmReport, LeadAnalysis, K4Report, string,
  DiagnosticLogger) → List<TerminalConnectionModel>`).

- [ ] **Step 1: Создать `addin/Data/TerminalConnectionModelBuilder.cs` — ДОСЛОВНЫЙ
  перенос класса билдера из `TerminalConnectionModel.cs:61-255`**

```csharp
using System.Collections.Generic;
using System.Globalization;

namespace MyEplanActions
{
    /// <summary>Builder TerminalConnectionModel (Фаза D): по каждой точке свода [MATCH]
    /// определяет конкретный DmRow (Connection) эвристикой по геометрической стороне и
    /// геометрическую Side. Чистый модуль — только Pt/Seg/строка, без EPLAN-API.
    /// (Файл выделен из TerminalConnectionModel.cs, Этап 6 / Задача 1 — дословный
    /// перенос, поведение не менялось.)</summary>
    public static class TerminalConnectionModelBuilder
    {
        // ... ВСЁ содержимое класса из TerminalConnectionModel.cs (ColumnPerpRef,
        // Build, IsBridge, MakeModel) — БЕЗ ИЗМЕНЕНИЙ, байт-в-байт ...
    }
}
```

Важно: перенести методы `ColumnPerpRef`, `Build`, `IsBridge`, `MakeModel` и ВСЕ
комментарии к ним дословно; usings файла-источника — `System.Collections.Generic`,
`System.Globalization` (в модели после переноса они не нужны).

- [ ] **Step 2: Оставить в `addin/Data/TerminalConnectionModel.cs` только чистую модель**

Итоговое содержимое файла (enum `TerminalSide`, класс `TerminalConnectionModel`,
класс `TerminalGeometry` — дословно из текущего файла, без билдера):

```csharp
namespace MyEplanActions
{
    /// <summary>Геометрическая сторона подключения относительно ряда/столбца клеммы.
    /// Horizontal-форма: Top (Y < ряда), Bottom (Y > ряда); Vertical-форма: Left (X <
    /// столбца), Right (X > столбца). Unknown — ряд/столбец не определился (Этап 5
    /// уточняет Side по правилам §9).</summary>
    public enum TerminalSide { Top, Bottom, Left, Right, Unknown }

    /// <summary>Модель конкретного подключения (Фаза D, plan_implementation §4.4).
    /// ... (поля и комментарий — дословно, строки 12-35 текущего файла) ...</summary>
    public sealed class TerminalConnectionModel
    {
        // ... поля Terminal/TerminalNumber/ConnectionName/PinName/PinIndex/
        // Destination/ConnectionDesignation/ConnectionPoint/ColumnIndex/Side/
        // IsCable/CableName/HasConn/BridgePeer/IsBridge — БЕЗ ИЗМЕНЕНИЙ ...
    }

    /// <summary>Чистая геометрия стороны ...</summary>
    public static class TerminalGeometry
    {
        // ... TolPerpMm + PointSide — БЕЗ ИЗМЕНЕНИЙ ...
    }
}
```

Usings в начале файла УДАЛИТЬ (`System.Collections.Generic`/`System.Globalization`
после переноса билдера не нужны — проверить отсутствие других использований).

- [ ] **Step 3: Создать `addin/Geometry/ReportOrientation.cs` — enum из
  `AnchorResolver.cs:7-10` дословно**

```csharp
namespace MyEplanActions
{
    /// <summary>Ориентация формы отчёта: вдоль колонок якоря идут по X (Horizontal —
    /// текущая горизонтальная форма) или по Y (Vertical — вертикальная форма,
    /// Задача 0/3 Этапа 3). (Выделен из AnchorResolver.cs, Этап 6 / Задача 1 — чтобы
    /// чистые модули геометрии компилировались без EPLAN-зависимостей.)</summary>
    public enum ReportOrientation { Horizontal, Vertical }
}
```

- [ ] **Step 4: Удалить enum из `addin/Anchor/AnchorResolver.cs` (строки 7–10)**

Удалить doc-комментарий и `public enum ReportOrientation { Horizontal, Vertical }`.
Остальное содержимое файла (TerminalAnchor, AnchorMap, AnchorResolver) — без
изменений; компиляция тот же namespace.

- [ ] **Step 5: Статическая самопроверка (Linux, без компиляции)**

Проверить: (а) в `TerminalConnectionModel.cs` не осталось `DiagnosticLogger`,
`MatchRow`, `DmReport`, `LeadAnalysis`; (б) в `AnchorResolver.cs` не осталось
`enum ReportOrientation`, при этом `ReportOrientation`/`DiagnosticLogger` используются
и резолвятся в том же namespace; (в) перенесённые члены байт-в-байт равны исходным
(дифф снапшота против текущего файла).

- [ ] **Step 6: Снапшот** — скопировать все 4 файла в
  `.superpowers/sdd/plan_stage6/snapshots/` (для диффа ревьюеру; коммитов нет).

---

## Task 2: `CableGeometryBuilder` — модели результата + алгоритм + константы конфига

**Files:**
- Create: `addin/Geometry/CableGeometryBuilder.cs`
- Modify: `addin/Configuration/AddInConfiguration.cs` (добавить 4 константы)

**Interfaces:**
- Consumes: `Pt`/`Seg` (LeadGeometry.cs), `CableLayoutModel`/`CableModel`
  (Data/CableLayoutModel.cs, Data/CableModel.cs — бакеты `LeftConnections`/
  `RightConnections`/`OtherConnections` из Фазы E), `ReportOrientation`
  (Geometry/ReportOrientation.cs, Task 1).
- Produces (для Task 3 и Task 4):
  `sealed class CableSymbolPlacement` (поля `string CableName`, `int CableIndex`, `Pt Position`);
  `sealed class CableGeometryResult` (`List<Seg> Segments`, `List<CableSymbolPlacement> Symbols`,
  `List<string> Warnings`);
  `sealed class CableGeometryConfig` (`double BusOffsetMm/SymbolOffsetMm/PitchMm`, дефолты 10/10/20);
  `static CableGeometryResult CableGeometryBuilder.Build(CableLayoutModel oLayout,
  ReportOrientation eOrientation, CableGeometryConfig oCfg)`;
  побочный эффект: `Build` заполняет `CableModel.SymbolPosition` каждого кабеля с точками.
  `Seg.LayerName` остаётся null (слой — Фаза G).

- [ ] **Step 1: Создать `addin/Geometry/CableGeometryBuilder.cs` (полный код)**

```csharp
using System;
using System.Collections.Generic;

namespace MyEplanActions
{
    /// <summary>Позиция символа кабеля (SHCP2) — результат Geometry Engine (Фаза F,
    /// plan_implementation §11/§13). Создание самого символа — Фаза G.</summary>
    public sealed class CableSymbolPlacement
    {
        public string CableName;         // имя кабеля или null (бинарный случай)
        public int CableIndex = -1;      // 0-based индекс в CableLayoutModel.Cables
        public Pt Position;              // SymbolPosition, страничные координаты
    }

    /// <summary>Результат Geometry Engine: сегменты (ветви + шины + подходы; LayerName
    /// остаётся null — слой назначит Фаза G) и позиции символов. Warnings — побудительный
    /// список для log.Warn в AnalyzeAction (builder без логгера — чистый модуль).</summary>
    public sealed class CableGeometryResult
    {
        public readonly List<Seg> Segments = new List<Seg>();
        public readonly List<CableSymbolPlacement> Symbols = new List<CableSymbolPlacement>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>Параметры разводки. В проде значения — из AddInConfiguration
    /// (CableBusOffsetMm/CableSymbolOffsetMm/CablePitchMm), в unit-тестах — свои.</summary>
    public sealed class CableGeometryConfig
    {
        public double BusOffsetMm = 10.0;     // отступ шины от крайних точек группы (perp)
        public double SymbolOffsetMm = 10.0;  // вынос SymbolAxis за крайнюю точку кабеля (axis)
        public double PitchMm = 20.0;         // разнос SymbolAxis нескольких кабелей
    }

    /// <summary>Geometry Engine (Фаза F, spec 2026-09-21-fase-f-cable-geometry-design.md):
    /// чистая геометрия кабельной разводки по эталону «example/Кабель с двух сторон.pdf».
    /// Оси: Horizontal — axis=X (ряд), perp=Y; Vertical — axis=Y, perp=X; верх страницы —
    /// большая Y (урок rev.7.1). Группы стороны из CableModel: Right (Top/Right, большие
    /// perp) — шина за max(perp)+Off; Left (Bottom/Left) — за min(perp)−Off; Other
    /// (Unknown) — как Right, с WARN. Вынос по оси: H → +X, V → −Y (s=±1);
    /// SymbolAxis = max(s*axis)*s + s*Offset + idx*Pitch. Символ — среднее уровней шин
    /// задействованных групп (односторонний кабель — на уровне своей шины, подход
    /// вырождается). Без EPLAN-типов и без логгера; дампы печатает AnalyzeAction.</summary>
    public static class CableGeometryBuilder
    {
        private const double EpsLen = 1e-9;   // порог вырожденного (нулевого) подхода

        public static CableGeometryResult Build(CableLayoutModel oLayout,
            ReportOrientation eOrientation, CableGeometryConfig oCfg)
        {
            CableGeometryResult oRes = new CableGeometryResult();
            if (oLayout == null || oCfg == null)
            {
                oRes.Warnings.Add("[GEOM] Build: layout или config == null — вычислять нечего");
                return oRes;
            }
            bool bV = eOrientation == ReportOrientation.Vertical;
            double sDir = bV ? -1.0 : 1.0;    // H → +X (как на эталоне), V → −Y

            for (int nCable = 0; nCable < oLayout.Cables.Count; nCable++)
            {
                CableModel oCable = oLayout.Cables[nCable];
                if (oCable == null) continue;
                string strName = oCable.Name ?? "<без имени>";

                List<TerminalConnectionModel> oRight = oCable.RightConnections;
                List<TerminalConnectionModel> oLeft = oCable.LeftConnections;
                List<TerminalConnectionModel> oOther = oCable.OtherConnections;
                int nTotal = CountOf(oRight) + CountOf(oLeft) + CountOf(oOther);
                if (nTotal == 0)
                {
                    oRes.Warnings.Add("[GEOM] кабель '" + strName +
                        "': 0 точек — пропущен (сегменты и символ не созданы)");
                    continue;
                }
                if (CountOf(oOther) > 0)
                    oRes.Warnings.Add("[GEOM] кабель '" + strName + "': " + CountOf(oOther) +
                        " точек Unknown-стороны (Other) — шина по общему правилу, правило не валидировано данными");

                // Уровни шин групп: Right/Other — за max(perp)+Off, Left — за min(perp)−Off.
                double dBusRight = CountOf(oRight) > 0 ? MaxPerpOf(oRight, bV) + oCfg.BusOffsetMm : double.NaN;
                double dBusLeft = CountOf(oLeft) > 0 ? MinPerpOf(oLeft, bV) - oCfg.BusOffsetMm : double.NaN;
                double dBusOther = CountOf(oOther) > 0 ? MaxPerpOf(oOther, bV) + oCfg.BusOffsetMm : double.NaN;

                // Вынос по оси: max(s*axis)*s + s*Offset + idx*Pitch (s=+1 вправо, s=-1 вниз).
                double dMaxSigned = double.NegativeInfinity;
                CollectMaxSigned(oRight, bV, sDir, ref dMaxSigned);
                CollectMaxSigned(oLeft, bV, sDir, ref dMaxSigned);
                CollectMaxSigned(oOther, bV, sDir, ref dMaxSigned);
                double dSymbolAxis = dMaxSigned * sDir + sDir * oCfg.SymbolOffsetMm + nCable * oCfg.PitchMm;

                // Символ — среднее уровней шин задействованных групп.
                double dSymbolPerp = MeanBus(dBusRight, dBusLeft, dBusOther);

                AddGroup(oRes, oRight, dBusRight, bV, dSymbolAxis, dSymbolPerp);
                AddGroup(oRes, oLeft, dBusLeft, bV, dSymbolAxis, dSymbolPerp);
                AddGroup(oRes, oOther, dBusOther, bV, dSymbolAxis, dSymbolPerp);

                oCable.SymbolPosition = PtOf(bV, dSymbolAxis, dSymbolPerp);
                CableSymbolPlacement oSym = new CableSymbolPlacement();
                oSym.CableName = oCable.Name;
                oSym.CableIndex = nCable;
                oSym.Position = oCable.SymbolPosition;
                oRes.Symbols.Add(oSym);
            }
            return oRes;
        }

        /// <summary>Сегменты одной группы стороны: ветвь на каждую точку (перпендикулярно
        /// ряду до уровня шины), шина от крайней точки группы, ПРОТИВОПОЛОЖНОЙ направлению
        /// выноса (H: min axis; V: max axis), до SymbolAxis, подход к символу
        /// (нулевой подход одностороннего кабеля не создаётся).</summary>
        private static void AddGroup(CableGeometryResult oRes, List<TerminalConnectionModel> lstGroup,
            double dBusPerp, bool bV, double dSymbolAxis, double dSymbolPerp)
        {
            if (lstGroup == null || lstGroup.Count == 0 || double.IsNaN(dBusPerp)) return;
            double dBusStart = 0.0;
            bool bFirst = true;
            foreach (TerminalConnectionModel oM in lstGroup)
            {
                if (oM == null) continue;
                double dAxis = AxisOf(oM.ConnectionPoint, bV);
                if (bFirst) { dBusStart = dAxis; bFirst = false; }
                else dBusStart = bV ? Math.Max(dBusStart, dAxis) : Math.Min(dBusStart, dAxis);
                Seg oBranch = new Seg();
                oBranch.A = oM.ConnectionPoint;
                oBranch.B = PtOf(bV, dAxis, dBusPerp);
                oRes.Segments.Add(oBranch);
            }
            Seg oBus = new Seg();
            oBus.A = PtOf(bV, dBusStart, dBusPerp);
            oBus.B = PtOf(bV, dSymbolAxis, dBusPerp);
            oRes.Segments.Add(oBus);
            if (Math.Abs(dBusPerp - dSymbolPerp) > EpsLen)
            {
                Seg oApproach = new Seg();
                oApproach.A = PtOf(bV, dSymbolAxis, dBusPerp);
                oApproach.B = PtOf(bV, dSymbolAxis, dSymbolPerp);
                oRes.Segments.Add(oApproach);
            }
        }

        private static int CountOf(List<TerminalConnectionModel> lst)
        {
            // Только non-null — согласовано с null-guard'ами хелперов: группа из
            // одних null не должна давать CountOf > 0 (иначе -Inf-шина без WARN).
            if (lst == null) return 0;
            int nCount = 0;
            foreach (TerminalConnectionModel oM in lst)
                if (oM != null) nCount++;
            return nCount;
        }

        private static double AxisOf(Pt oPt, bool bV) { return bV ? oPt.Y : oPt.X; }
        private static double PerpOf(Pt oPt, bool bV) { return bV ? oPt.X : oPt.Y; }

        /// <summary>Страничная точка из осевых координат: H — (axis, perp), V — (perp, axis).</summary>
        private static Pt PtOf(bool bV, double dAxis, double dPerp)
        {
            return bV ? new Pt(dPerp, dAxis) : new Pt(dAxis, dPerp);
        }

        private static double MaxPerpOf(List<TerminalConnectionModel> lst, bool bV)
        {
            double dBest = double.NegativeInfinity;
            foreach (TerminalConnectionModel oM in lst)
                if (oM != null) dBest = Math.Max(dBest, PerpOf(oM.ConnectionPoint, bV));
            return dBest;
        }

        private static double MinPerpOf(List<TerminalConnectionModel> lst, bool bV)
        {
            double dBest = double.PositiveInfinity;
            foreach (TerminalConnectionModel oM in lst)
                if (oM != null) dBest = Math.Min(dBest, PerpOf(oM.ConnectionPoint, bV));
            return dBest;
        }

        private static void CollectMaxSigned(List<TerminalConnectionModel> lst, bool bV, double sDir,
            ref double dMaxSigned)
        {
            if (lst == null) return;
            foreach (TerminalConnectionModel oM in lst)
            {
                if (oM == null) continue;
                double dSigned = sDir * AxisOf(oM.ConnectionPoint, bV);
                if (dSigned > dMaxSigned) dMaxSigned = dSigned;
            }
        }

        private static double MeanBus(double dBusRight, double dBusLeft, double dBusOther)
        {
            double dSum = 0.0;
            int nCount = 0;
            if (!double.IsNaN(dBusRight)) { dSum += dBusRight; nCount++; }
            if (!double.IsNaN(dBusLeft)) { dSum += dBusLeft; nCount++; }
            if (!double.IsNaN(dBusOther)) { dSum += dBusOther; nCount++; }
            return nCount > 0 ? dSum / nCount : double.NaN;
        }
    }
}
```

- [ ] **Step 2: Добавить константы в `addin/Configuration/AddInConfiguration.cs`**

Вставить ПЕРЕД комментарием «Точка вставки отчёта на странице» (после
`LayerPropertyNumber`):

```csharp
        // Фаза F (rev.9.0): параметры геометрии кабельной разводки (spec
        // 2026-09-21-fase-f-cable-geometry-design.md): отступ шины от крайних точек
        // стороны (перпендикулярно ряду), вынос SymbolAxis за крайнюю точку кабеля
        // (вдоль ряда), разнос SymbolAxis нескольких кабелей.
        public const double CableBusOffsetMm = 10.0;
        public const double CableSymbolOffsetMm = 10.0;
        public const double CablePitchMm = 20.0;

        // Отладочное превью Фазы F: рисовать вычисленную геометрию Graphics.Line
        // на странице отчёта. НЕ идемпотентно: повторный прогон дублирует линии
        // (удалять вручную; идентификация объектов — Фаза I). Слой по умолчанию.
        public const bool PreviewDraw = false;
```

- [ ] **Step 3: Статическая самопроверка** — файл не использует ничего кроме
  System/System.Collections.Generic/Pt/Seg/CableLayout/CableModel/ReportOrientation;
  синтаксис C#5 (нет интерполяций/`?.`); константы встали в класс конфига.

- [ ] **Step 4: Снапшот** — оба файла в `.superpowers/sdd/plan_stage6/snapshots/`.

---

## Task 3: Unit-тесты — `tests/CableGeometryTests.cs` + `tests/build_tests.bat`

**Files:**
- Create: `tests/CableGeometryTests.cs`
- Create: `tests/build_tests.bat`

**Interfaces:**
- Consumes: файлы из Task 1–2 (компилируются ВМЕСТЕ: ReportOrientation.cs,
  LeadGeometry.cs, TerminalConnectionModel.cs, CableModel.cs, CableLayoutModel.cs,
  CableGeometryBuilder.cs) — без EPLAN-ссылок.
- Produces: консольный `GeometryTests.exe`; exit code = число провалов (0 = успех).

- [ ] **Step 1: Создать `tests/CableGeometryTests.cs` (полный код; 8 кейсов из spec,
  координаты — расчётные значения алгоритма Task 2)**

```csharp
using System;
using System.Collections.Generic;

namespace MyEplanActions
{
    /// <summary>Unit-тесты Geometry Engine (Фаза F) — консольный раннер (csc .NET 4.x,
    /// без EPLAN). Exit code = число провалов. Прогон — Windows-стенд
    /// (tests/build_tests.bat). Координаты кейсов — расчётные значения алгоритма
    /// CableGeometryBuilder (spec 2026-09-21-fase-f-cable-geometry-design.md).</summary>
    internal static class GeometryTests
    {
        private static int _nFailed;
        private static int _nPassed;

        private static void Check(bool bCond, string strWhat)
        {
            if (bCond) { _nPassed++; Console.WriteLine("  PASS " + strWhat); }
            else { _nFailed++; Console.WriteLine("  FAIL " + strWhat); }
        }

        private static bool NumEq(double dA, double dB) { return Math.Abs(dA - dB) <= 1e-6; }

        private static bool PtEq(Pt oPt, double dX, double dY)
        {
            return NumEq(oPt.X, dX) && NumEq(oPt.Y, dY);
        }

        private static bool SegHas(Seg oSeg, double dX1, double dY1, double dX2, double dY2)
        {
            return (PtEq(oSeg.A, dX1, dY1) && PtEq(oSeg.B, dX2, dY2)) ||
                   (PtEq(oSeg.A, dX2, dY2) && PtEq(oSeg.B, dX1, dY1));
        }

        private static bool HasSeg(List<Seg> lst, double dX1, double dY1, double dX2, double dY2)
        {
            foreach (Seg oSeg in lst)
                if (SegHas(oSeg, dX1, dY1, dX2, dY2)) return true;
            return false;
        }

        private static TerminalConnectionModel MkPt(double dX, double dY)
        {
            TerminalConnectionModel oM = new TerminalConnectionModel();
            oM.ConnectionPoint = new Pt(dX, dY);
            return oM;
        }

        private static CableModel MkCable()
        {
            return new CableModel();   // Name = null — бинарный случай
        }

        public static int RunAll()
        {
            Console.WriteLine("=== CableGeometryBuilder unit tests ===");
            RunCase(Case1_TwoSidedHorizontal);
            RunCase(Case2_OneSidedHorizontal);
            RunCase(Case3_SinglePoint);
            RunCase(Case4_TwoSidedVertical);
            RunCase(Case5_TwoCables);
            RunCase(Case6_OtherGroup);
            RunCase(Case7_EmptyCable);
            RunCase(Case8_NanSentinel);
            Console.WriteLine("=== " + _nPassed + " passed, " + _nFailed + " failed ===");
            return _nFailed;
        }

        /// <summary>Изоляция кейсов: исключение в одном кейсе (например, регрессия,
        /// давшая меньше символов, чем ожидает индексный ассерт) не должно обрывать
        /// остальные кейсы — иначе exit code перестаёт быть «числом провалов».</summary>
        private static void RunCase(Action oCase)
        {
            try { oCase(); }
            catch (Exception oEx)
            {
                _nFailed++;
                Console.WriteLine("  FAIL <исключение в кейсе> " + oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        // Двусторонний кабель, Horizontal (сценарий эталона): R (0,10),(20,10);
        // L (5,0),(15,0). busR=20, busL=-10, SymbolAxis=30, SymbolPerp=5.
        private static void Case1_TwoSidedHorizontal()
        {
            Console.WriteLine("[case 1] two-sided horizontal");
            CableModel oCable = MkCable();
            oCable.RightConnections.Add(MkPt(0, 10));
            oCable.RightConnections.Add(MkPt(20, 10));
            oCable.LeftConnections.Add(MkPt(5, 0));
            oCable.LeftConnections.Add(MkPt(15, 0));
            CableLayoutModel oLayout = new CableLayoutModel();
            oLayout.Cables.Add(oCable);

            CableGeometryResult oRes = CableGeometryBuilder.Build(oLayout,
                ReportOrientation.Horizontal, new CableGeometryConfig());

            Check(oRes.Segments.Count == 8, "сегментов 8 (4 ветви + 2 шины + 2 подхода), факт " + oRes.Segments.Count);
            Check(oRes.Symbols.Count == 1, "символов 1, факт " + oRes.Symbols.Count);
            Check(PtEq(oRes.Symbols[0].Position, 30, 5), "символ (30;5)");
            Check(oRes.Symbols[0].CableIndex == 0, "CableIndex 0");
            Check(HasSeg(oRes.Segments, 0, 10, 0, 20), "ветвь (0;10)-(0;20)");
            Check(HasSeg(oRes.Segments, 20, 10, 20, 20), "ветвь (20;10)-(20;20)");
            Check(HasSeg(oRes.Segments, 5, 0, 5, -10), "ветвь (5;0)-(5;-10)");
            Check(HasSeg(oRes.Segments, 15, 0, 15, -10), "ветвь (15;0)-(15;-10)");
            Check(HasSeg(oRes.Segments, 0, 20, 30, 20), "шина R (0;20)-(30;20)");
            Check(HasSeg(oRes.Segments, 5, -10, 30, -10), "шина L (5;-10)-(30;-10)");
            Check(HasSeg(oRes.Segments, 30, 20, 30, 5), "подход R (30;20)-(30;5)");
            Check(HasSeg(oRes.Segments, 30, -10, 30, 5), "подход L (30;-10)-(30;5)");
            Check(oRes.Warnings.Count == 0, "предупреждений 0, факт " + oRes.Warnings.Count);
        }

        // Односторонний кабель (R), Horizontal: подход вырождается, символ на шине.
        private static void Case2_OneSidedHorizontal()
        {
            Console.WriteLine("[case 2] one-sided horizontal");
            CableModel oCable = MkCable();
            oCable.RightConnections.Add(MkPt(0, 10));
            oCable.RightConnections.Add(MkPt(10, 10));
            CableLayoutModel oLayout = new CableLayoutModel();
            oLayout.Cables.Add(oCable);

            CableGeometryResult oRes = CableGeometryBuilder.Build(oLayout,
                ReportOrientation.Horizontal, new CableGeometryConfig());

            Check(oRes.Segments.Count == 3, "сегментов 3 (2 ветви + 1 шина, подхода нет), факт " + oRes.Segments.Count);
            Check(oRes.Symbols.Count == 1 && PtEq(oRes.Symbols[0].Position, 20, 20), "символ (20;20) на шине");
        }

        // Одна точка (L), Horizontal: bus=-10, SymbolAxis=15, SymbolPerp=-10.
        private static void Case3_SinglePoint()
        {
            Console.WriteLine("[case 3] single point");
            CableModel oCable = MkCable();
            oCable.LeftConnections.Add(MkPt(5, 0));
            CableLayoutModel oLayout = new CableLayoutModel();
            oLayout.Cables.Add(oCable);

            CableGeometryResult oRes = CableGeometryBuilder.Build(oLayout,
                ReportOrientation.Horizontal, new CableGeometryConfig());

            Check(oRes.Segments.Count == 2, "сегментов 2 (ветвь + шина), факт " + oRes.Segments.Count);
            Check(HasSeg(oRes.Segments, 5, 0, 5, -10), "ветвь (5;0)-(5;-10)");
            Check(HasSeg(oRes.Segments, 5, -10, 15, -10), "шина (5;-10)-(15;-10)");
            Check(oRes.Symbols.Count == 1 && PtEq(oRes.Symbols[0].Position, 15, -10), "символ (15;-10)");
        }

        // Двусторонний кабель, Vertical (зеркальные оси): R (30,100),(30,120);
        // L (20,110). busR=40, busL=10, вынос -Y: SymbolAxis=90, SymbolPerp=25.
        private static void Case4_TwoSidedVertical()
        {
            Console.WriteLine("[case 4] two-sided vertical");
            CableModel oCable = MkCable();
            oCable.RightConnections.Add(MkPt(30, 100));
            oCable.RightConnections.Add(MkPt(30, 120));
            oCable.LeftConnections.Add(MkPt(20, 110));
            CableLayoutModel oLayout = new CableLayoutModel();
            oLayout.Cables.Add(oCable);

            CableGeometryResult oRes = CableGeometryBuilder.Build(oLayout,
                ReportOrientation.Vertical, new CableGeometryConfig());

            Check(oRes.Segments.Count == 7, "сегментов 7 (3 ветви + 2 шины + 2 подхода), факт " + oRes.Segments.Count);
            Check(oRes.Symbols.Count == 1 && PtEq(oRes.Symbols[0].Position, 25, 90), "символ (25;90) — ниже всех точек");
            Check(HasSeg(oRes.Segments, 30, 100, 40, 100), "ветвь (30;100)-(40;100)");
            Check(HasSeg(oRes.Segments, 30, 120, 40, 120), "ветвь (30;120)-(40;120)");
            Check(HasSeg(oRes.Segments, 20, 110, 10, 110), "ветвь (20;110)-(10;110)");
            Check(HasSeg(oRes.Segments, 40, 120, 40, 90), "шина R (40;120)-(40;90)");
            Check(HasSeg(oRes.Segments, 10, 110, 10, 90), "шина L (10;110)-(10;90)");
            Check(HasSeg(oRes.Segments, 40, 90, 25, 90), "подход R (40;90)-(25;90)");
            Check(HasSeg(oRes.Segments, 10, 90, 25, 90), "подход L (10;90)-(25;90)");
        }

        // Два кабеля, Horizontal: SymbolAxis разнесены на Pitch (10 и 35).
        private static void Case5_TwoCables()
        {
            Console.WriteLine("[case 5] two cables stagger");
            CableModel oC0 = MkCable();
            oC0.RightConnections.Add(MkPt(0, 10));
            CableModel oC1 = MkCable();
            oC1.LeftConnections.Add(MkPt(5, 0));
            CableLayoutModel oLayout = new CableLayoutModel();
            oLayout.Cables.Add(oC0);
            oLayout.Cables.Add(oC1);

            CableGeometryResult oRes = CableGeometryBuilder.Build(oLayout,
                ReportOrientation.Horizontal, new CableGeometryConfig());

            Check(oRes.Segments.Count == 4, "сегментов 4, факт " + oRes.Segments.Count);
            Check(oRes.Symbols.Count == 2, "символов 2");
            Check(PtEq(oRes.Symbols[0].Position, 10, 20), "символ #0 (10;20) — база без Pitch");
            Check(PtEq(oRes.Symbols[1].Position, 35, -10), "символ #1 (35;-10) — база 15 + Pitch 20");
            Check(oRes.Symbols[0].CableIndex == 0 && oRes.Symbols[1].CableIndex == 1, "индексы 0/1");
        }

        // Other-группа (Unknown), Horizontal: своя шина (как Right) + WARN.
        private static void Case6_OtherGroup()
        {
            Console.WriteLine("[case 6] other group warns");
            CableModel oCable = MkCable();
            oCable.OtherConnections.Add(MkPt(0, 5));
            CableLayoutModel oLayout = new CableLayoutModel();
            oLayout.Cables.Add(oCable);

            CableGeometryResult oRes = CableGeometryBuilder.Build(oLayout,
                ReportOrientation.Horizontal, new CableGeometryConfig());

            Check(oRes.Warnings.Count == 1, "WARN 1 (Other), факт " + oRes.Warnings.Count);
            Check(oRes.Segments.Count == 2, "сегментов 2 (ветвь + шина, подход вырожден), факт " + oRes.Segments.Count);
            Check(oRes.Symbols.Count == 1 && PtEq(oRes.Symbols[0].Position, 10, 15), "символ (10;15) — шина Other на 15");
        }

        // Пустой кабель: WARN, пропуск, символ не создаётся (SymbolPosition остаётся NaN).
        private static void Case7_EmptyCable()
        {
            Console.WriteLine("[case 7] empty cable skipped");
            CableModel oEmpty = MkCable();
            CableModel oOk = MkCable();
            oOk.RightConnections.Add(MkPt(0, 10));
            CableLayoutModel oLayout = new CableLayoutModel();
            oLayout.Cables.Add(oEmpty);
            oLayout.Cables.Add(oOk);

            CableGeometryResult oRes = CableGeometryBuilder.Build(oLayout,
                ReportOrientation.Horizontal, new CableGeometryConfig());

            Check(oRes.Warnings.Count == 1, "WARN 1 (пустой кабель), факт " + oRes.Warnings.Count);
            Check(oRes.Symbols.Count == 1, "символ только у непустого, факт " + oRes.Symbols.Count);
            Check(oRes.Symbols[0].CableIndex == 1, "индекс символа 1 (пустой — 0)");
            Check(double.IsNaN(oEmpty.SymbolPosition.X), "SymbolPosition пустого остался NaN");
        }

        // NaN-сентинел: до Build SymbolPosition.X == NaN, после — число.
        private static void Case8_NanSentinel()
        {
            Console.WriteLine("[case 8] nan sentinel writeback");
            CableModel oCable = MkCable();
            Check(double.IsNaN(oCable.SymbolPosition.X), "до Build X == NaN");
            oCable.RightConnections.Add(MkPt(0, 10));
            oCable.RightConnections.Add(MkPt(20, 10));
            oCable.LeftConnections.Add(MkPt(5, 0));
            oCable.LeftConnections.Add(MkPt(15, 0));
            CableLayoutModel oLayout = new CableLayoutModel();
            oLayout.Cables.Add(oCable);
            CableGeometryBuilder.Build(oLayout, ReportOrientation.Horizontal, new CableGeometryConfig());
            Check(NumEq(oCable.SymbolPosition.X, 30) && NumEq(oCable.SymbolPosition.Y, 5),
                "после Build SymbolPosition == (30;5)");
        }
    }

    internal class Program
    {
        private static int Main()
        {
            return GeometryTests.RunAll();
        }
    }
}
```

- [ ] **Step 2: Создать `tests/build_tests.bat`**

```bat
@echo off
REM Unit-тесты геометрии Фазы F (чистые модули, БЕЗ EPLAN API).
REM Прогон: tests\build_tests.bat -> GeometryTests.exe; exit code = число провалов.
REM Урок CS2022 (п.19): /out и /target — ДО списка исходников.

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

cd /d "%~dp0"

"%CSC%" /target:exe ^
  /out:GeometryTests.exe ^
  ..\addin\Geometry\ReportOrientation.cs ^
  ..\addin\Geometry\LeadGeometry.cs ^
  ..\addin\Data\TerminalConnectionModel.cs ^
  ..\addin\Data\CableModel.cs ^
  ..\addin\Data\CableLayoutModel.cs ^
  ..\addin\Geometry\CableGeometryBuilder.cs ^
  CableGeometryTests.cs

if errorlevel 1 (
  echo COMPILE FAILED
  exit /b 999
)

GeometryTests.exe
pause
```

- [ ] **Step 3: Статическая самопроверка** — список исходников покрывает все
  зависимости (никакой файл из набора не тянет EPLAN/DiagnosticLogger/MatchRow/
  DmReport); кейсы 1–8 соответствуют spec (двусторонний/односторонний/1 точка/
  Vertical/два кабеля/Other/пустой/NaN); синтаксис C#5.

- [ ] **Step 4: Снапшот** — оба файла в `.superpowers/sdd/plan_stage6/snapshots/`.

---

## Task 4: Интеграция — секции 12–13 в `AnalyzeAction.Run` + BUILD_STAMP rev.9.0

**Files:**
- Modify: `addin/Actions/AnalyzeAction.cs` (штамп :26, заголовок :32, секции 12–13
  после секции 11)

**Interfaces:**
- Consumes: `CableLayoutModel oLayout` (локальная секции 11), `AddInConfiguration.Orientation`
  (конст), `CableGeometryBuilder/CableGeometryResult/CableGeometryConfig/CableSymbolPlacement`
  (Task 2), `Page oPage` (локальная секции 1 — для превью), `Line`/`PointD`
  (EPLAN: using Graphics/Base уже в файле).
- Produces: дампы `[GEOM]` (символы + сегменты) и `[GEOM-SUM]`; WARN из
  `result.Warnings`; превью `PreviewDraw` — `Graphics.Line.Create(Page, PointD, PointD)`.

- [ ] **Step 1: Обновить BUILD_STAMP (AnalyzeAction.cs:26)**

oldString:

```csharp
        private const string BUILD_STAMP = "2026-09-21 Этап 5 rev.8.1 (Фаза E: фикс двойного назначения DmRow в TCM + маркеры [CABGROUP]; прогон rev.8.0: K140 у точек Unknown и Bottom клеммы №2)";
```

newString:

```csharp
        private const string BUILD_STAMP = "2026-09-21 Этап 6 rev.9.0 (Фаза F: CableGeometryBuilder — чистая геометрия кабельной разводки, [GEOM], превью PreviewDraw)";
```

- [ ] **Step 2: Обновить заголовок прогона (AnalyzeAction.cs:32)**

oldString:

```csharp
            _logger.BeginRun("TERMINAL_STRIP_ANALYZE — Этап 5 rev.8.1 (Фаза E: фикс double-assignment в TCM + маркеры [CABGROUP])", BUILD_STAMP);
```

newString:

```csharp
            _logger.BeginRun("TERMINAL_STRIP_ANALYZE — Этап 6 rev.9.0 (Фаза F: геометрия кабельной разводки)", BUILD_STAMP);
```

- [ ] **Step 3: Вставить секции 12–13 в конец `Run()` (после секции 11)**

oldString:

```csharp
            _logger.Summarize("Фаза E: кабелей " + oLayout.Cables.Count +
                ", проводных " + oLayout.NoCableConnections.Count + ".");
        }
```

newString:

```csharp
            _logger.Summarize("Фаза E: кабелей " + oLayout.Cables.Count +
                ", проводных " + oLayout.NoCableConnections.Count + ".");

            // --- 12. Фаза F: CableGeometryBuilder — чистая геометрия кабельной разводки ---
            _logger.Log("[INFO] --- Фаза F: геометрия кабельной разводки ---");
            CableGeometryConfig oGeomCfg = new CableGeometryConfig();
            oGeomCfg.BusOffsetMm = AddInConfiguration.CableBusOffsetMm;
            oGeomCfg.SymbolOffsetMm = AddInConfiguration.CableSymbolOffsetMm;
            oGeomCfg.PitchMm = AddInConfiguration.CablePitchMm;
            CableGeometryResult oGeom = CableGeometryBuilder.Build(
                oLayout, AddInConfiguration.Orientation, oGeomCfg);

            foreach (CableSymbolPlacement oSym in oGeom.Symbols)
                _logger.Log("[GEOM] символ '" + (oSym.CableName ?? "<без имени>") + "' #" +
                    oSym.CableIndex.ToString(CultureInfo.InvariantCulture) + ": (" +
                    oSym.Position.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                    oSym.Position.Y.ToString("F3", CultureInfo.InvariantCulture) + ")");
            foreach (Seg oGeomSeg in oGeom.Segments)
                _logger.Log("[GEOM] seg: (" +
                    oGeomSeg.A.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                    oGeomSeg.A.Y.ToString("F3", CultureInfo.InvariantCulture) + ") -> (" +
                    oGeomSeg.B.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                    oGeomSeg.B.Y.ToString("F3", CultureInfo.InvariantCulture) + ")");
            foreach (string strGeomWarn in oGeom.Warnings)
                _logger.Warn(strGeomWarn);
            _logger.Log("[INFO] [GEOM-SUM] кабелей " + oGeom.Symbols.Count +
                ", сегментов " + oGeom.Segments.Count +
                ", предупреждений " + oGeom.Warnings.Count);
            _logger.Summarize("Фаза F: сегментов " + oGeom.Segments.Count +
                ", символов " + oGeom.Symbols.Count + ".");

            // --- 13. Фаза F: отладочное превью (PreviewDraw) — Graphics.Line по сегментам.
            // НЕ идемпотентно: повторный прогон дублирует линии (удалить вручную).
            if (AddInConfiguration.PreviewDraw)
            {
                int nDrawn = 0;
                foreach (Seg oPrevSeg in oGeom.Segments)
                {
                    try
                    {
                        Line oNewLine = new Line();
                        oNewLine.Create(oPage,
                            new PointD(oPrevSeg.A.X, oPrevSeg.A.Y),
                            new PointD(oPrevSeg.B.X, oPrevSeg.B.Y));
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
        }
```

- [ ] **Step 4: Статическая самопроверка** — `oPage`/`oLayout` в области видимости
  (секции 1 и 11 того же метода); `Line` = `Eplan.EplApi.DataModel.Graphics.Line`
  (using :8), `PointD` = `Eplan.EplApi.Base` (using :6); `CultureInfo` using :3;
  синтаксис C#5; штамп в двух местах (:26 и :32).

- [ ] **Step 5: Снапшот** — `AnalyzeAction.cs` и `AddInConfiguration.cs` в
  `.superpowers/sdd/plan_stage6/snapshots/`.

---

## Task 5: Верификация на Windows-стенде (шаг пользователя)

**Files:** без изменений кода; прогоны + запись результатов.

- [x] **Step 1: Unit-раннер.** Запустить `tests\build_tests.bat`.
  Ожидание: компиляция без ошибок; вывод — 8 блоков `[case N]`, каждый чек `PASS`,
  итог `=== N passed, 0 failed ===`; код возврата 0. Если FAIL — лог кейса в отчёт.
  ✅ 22.09.2026: **56 passed / 0 failed** (8 кейсов; счётчики выросли от исходных
  8×N по ходу ревизий 1–4 и rev.9.6).

- [x] **Step 2: Сборка add-in.** `addin\build_addin.bat` — компиляция без ошибок
  (новые файлы подхватываются `/recurse`). Возможный warning CS0162 (unreachable
  code) на гейте `PreviewDraw` — штатно (const-гейт), не дефект.
  ✅ Сборки rev.9.0–rev.9.6 без ошибок (прогоны 21–22.09.2026).

- [x] **Step 3: Прогон EPLAN (горизонталь, `&ЭМ2/8.1`, PreviewDraw=false).**
  Ожидание в логе:
  - `BUILD_STAMP: 2026-09-21 Этап 6 rev.9.0 (...)`;
  - счётчики этапов 1–5 == rev.8.1: 77 точек, `[K4]` 61 колонка (X)
    0×10/1×25/2×26, `[ANCHOR]` Y=-81.0 якорей 60, `[SPLIT]` 1, `[MATCH]` 77,
    `[TCM]` 77/0/2, `[CABGROUP]` 1×15 (Left 8/Right 7/Other 0), `[CROSS]`
    СОВПАДАЕТ, `[DMERR]` 0;
  - `[GEOM] символ '<без имени>' #0: (X;Y)` — X = max X точек кабеля (по `[TCM]`)
    + 10, Y между уровнями шин; `[GEOM] seg:` ровно 19 строк (15 ветвей + 2 шины +
    2 подхода); `[GEOM-SUM] кабелей 1, сегментов 19, предупреждений 0`;
  - WARN ровно 10 (9 lookup-проб S029153 + «колонок К4 (61) != клемм (60)»).
  ✅ 22.09.2026 (rev.9.6, ожидания уточнены ревизиями 1–4 + реальной группировкой):
  `BUILD_STAMP rev.9.6`; счётчики этапов 1–5 == rev.8.1; `[CABGROUP]` 4 кабеля
  (K140 ×9 L6/R3, K190 1.1 ×2, K190 1.2 ×2, K100 ×2 R2), `[CABGROUP-SUM]` 4/62/0;
  `[GEOM-SUM]` кабелей 4, сегментов 28 (15 ветвей + 13 шин/подходов), предупреждений 0;
  символы X = 482.850 + idx·20, сход за правым краем ряда (K4 max X 472.85); шины
  ±(18 + idx·8) от кончиков выводов; WARN ровно 10. Прогон 21.09 (rev.9.0/9.4,
  бинарная группировка) — промежуточные ожидания сошлись (19 сегментов, символ
  279.850/482.850).

- [x] **Step 4: Превью.** В `AddInConfiguration.cs` переключить
  `PreviewDraw = false → true`, пересобрать, прогон на `&ЭМ2/8.1`.
  Ожидание: в сводке «Превью: нарисовано линий 19 из 19»; на странице — картинка
  как на эталоне `example/Кабель с двух сторон.pdf` (ветви от кончиков выводов,
  две шины, сходящиеся в точку символа справа). Вернуть `PreviewDraw = false`.
  Дубликаты при повторных прогонах удалять вручную.
  ✅ 21–22.09.2026: превью отрисовывалось на каждой ревизии; ревизии 1–4 (разнос
  уровней шин, SymbolAxis от края ряда, подъём шины целиком) — по замечаниям
  пользователя к превью. Финальное превью rev.9.6 (4 кабеля, 4 пары шин с разносом
  8 мм, сход за правым краем) подтверждено пользователем визуально.

- [x] **Step 5: Результаты** — отметки в этом файле, summary.md (новый пункт),
  ledger `.superpowers/sdd/plan_stage6/progress.md`; коммит — по явному запросу.
  ✅ summary п.40–44, мастер-план §23 Фаза F → ЗАВЕРШЕНА. Коммит — по явному
  запросу (rev.9.0–9.6 в рабочем дереве).

---

## Самопроверка плана (выполнена при написании)

- Spec coverage: модель разводки (Task 2), конфиг-константы (Task 2 Step 2),
  интеграция [GEOM] (Task 4 Step 3), превью (Task 4 Step 3, блок 13), unit-тесты
  8 кейсов (Task 3), чистые файлы для тестов (Task 1), верификация (Task 5) — все
  секции spec покрыты.
- Type consistency: `Build(CableLayoutModel, ReportOrientation, CableGeometryConfig)`
  — Task 2 ↔ Task 3 ↔ Task 4 совпадают; поля `CableSymbolPlacement`/`CableGeometryResult`
  — совпадают; `SymbolPosition` writeback — Case 8.
- Формула `SymbolAxis` соответствует spec (`+ s*Offset + idx*Pitch`) — кейсы 1/4/5
  просчитаны вручную (30/90/10+35).

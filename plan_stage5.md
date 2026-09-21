# Этап 5 (Фаза E) — CableLayoutBuilder: группировка соединений по кабелям

> **Для agentic-работы:** выполнять по `plan_stage5.md` задача-за-задачей (SDD, как Этап 3/4 —
> субагенты; компиляция и прогон — шаг пользователя на Windows). Чекбоксы `- [ ]`.
> Текущий BUILD_STAMP — rev.7.1 (AnalyzeAction.cs:26); правка логики = новая ревизия rev.8.0.

**Goal:** из уже построенных данных `TerminalConnectionModel[]` (Фаза D, rev.7.1) собрать
`CableLayoutModel` — сгруппировать соединения клеммника по кабелям (§10 «Этап 6» мастер-плана).

**Architecture:** новый чистый модуль `addin/Data/CableLayoutBuilder.cs` (+ `CableModel.cs`,
`CableLayoutModel.cs`). Builder потребляет готовый `List<TerminalConnectionModel>` и выдаёт
`CableLayoutModel` плюс дампы `[CABLE]`/`[CABLE-SUM]`. Встраивается в `AnalyzeAction.Run`
после секции 10 (свод `[TCM]`).

**Tech Stack:** C# (.NET Framework 4.x, EPLAN 2.9 add-in, csc.exe на Windows-стенде),
чистая геометрия без EPLAN-типов (Pt — LeadGeometry.cs).

**Spec:** `docs/superpowers/specs/2026-09-21-fase-e-cablelayout-design.md` (согласован
пользователем 21.09.2026) + `plan_implementation.md` §4.5/§4.6/§10/§23 Фаза E; вход —
`summary.md` п.37. Текущий код: `addin/Data/TerminalConnectionModel.cs` (TerminalSide/
TerminalConnectionModel/TerminalConnectionModelBuilder), `addin/Data/EplanTerminalStripReader.cs`
(DmRow), `addin/Actions/AnalyzeAction.cs` (Run), `addin/Diagnostics/DiagnosticLogger.cs`.

## Global Constraints

- Кодировка addin/*.cs — UTF-8 без BOM (конвенция addin/, НЕ spike).
- Чистая геометрия: не вводить EPLAN-типы в Geometry/Data-модули (только Pt/Seg/string).
- Культурная гигиена: весь разбор/вывод чисел — `CultureInfo.InvariantCulture`.
- Никаких EPLAN-API в новых Data-модулях — только в Actions/Report (как существующие).
- Имена: венгерские префиксы (oModel/oCable/lstModels/log), русские комментарии.
- Коммиты — только по явному запросу пользователя; SDD-ledger вне git (.superpowers/).
- Верификация — прогон на Windows (`build_addin.bat`, BUILD_STAMP, дампы `[CABLE]`/
  `[CABLE-SUM]`), не юнит-тесты (тестовой инфраструктуры в проекте нет).
- Бинарная группировка: в проекте `CableName == null` у всех (summary п.21б/п.24) —
  только №31058 `IsCable`. Реальная группировка по `CableName` — позже (ключ заложен).
- Семантика лево/право = `TerminalSide` из модели (сторона ряда, валидирована rev.7.1):
  `Top/Right → Right`, `Bottom/Left → Left`, `Unknown → Other`.

---

## Task 1: Модели `CableModel` + `CableLayoutModel`

**Files:**
- Create: `addin/Data/CableModel.cs` (модель `CableModel`)
- Create: `addin/Data/CableLayoutModel.cs` (модель `CableLayoutModel`)

**Interfaces:**
- Consumes: `Pt` (LeadGeometry.cs:8), `TerminalConnectionModel` (TerminalConnectionModel.cs:17).
- Produces: `sealed class CableModel` (поля `Name`, `Connections`, `LeftConnections`,
  `RightConnections`, `OtherConnections`, `SymbolPosition`), `sealed class CableLayoutModel`
  (поля `Cables`, `NoCableConnections`). Оба — пустые контейнеры, готовые для Task 2.

- [x] **Step 1: Создать `addin/Data/CableModel.cs` (полный код)**

```csharp
using System.Collections.Generic;

namespace MyEplanActions
{
    /// <summary>Группировка подключений одного кабеля (Фаза E, plan_implementation §4.5).
    /// Name — имя кабеля (CableName из DM) или null, если кабель без имени (в проекте
    /// объектов-определений кабелей нет, только №31058 IsCable — бинарный случай).
    /// Left/Right — разбивка по стороне ряда (TerminalSide: Top/Right → Right,
    /// Bottom/Left → Left, Unknown → Other); SymbolPosition — точка вставки символа
    /// (NaN до Фазы F/Geometry).</summary>
    public sealed class CableModel
    {
        public string Name;                              // null = кабель без имени
        public readonly List<TerminalConnectionModel> Connections = new List<TerminalConnectionModel>();
        public readonly List<TerminalConnectionModel> LeftConnections = new List<TerminalConnectionModel>();
        public readonly List<TerminalConnectionModel> RightConnections = new List<TerminalConnectionModel>();
        public readonly List<TerminalConnectionModel> OtherConnections = new List<TerminalConnectionModel>();
        public Pt SymbolPosition;                        // X=NaN до Фазы F
    }
}
```

- [x] **Step 2: Создать `addin/Data/CableLayoutModel.cs` (полный код)**

```csharp
using System.Collections.Generic;

namespace MyEplanActions
{
    /// <summary>Свод группировки соединений клеммника по кабелям (Фаза E, §4.6).
    /// Cables — кабельные группы (IsCable == true); NoCableConnections — провода
    /// (IsCable == false), не входящие ни в один кабель.</summary>
    public sealed class CableLayoutModel
    {
        public readonly List<CableModel> Cables = new List<CableModel>();
        public readonly List<TerminalConnectionModel> NoCableConnections = new List<TerminalConnectionModel>();
    }
}
```

- [x] **Step 3: Само-проверка (без компиляции на Linux):** классы синтаксически корректны,
  чистые контейнеры без зависимостей; конвенции addin/ (без BOM). Отметить «код
  самопроверен; сборка — шаг пользователя».

- [x] **Step 4: Снапшот** — `.superpowers/sdd/plan_stage5/snapshots/CableModel.cs` и
  `.superpowers/sdd/plan_stage5/snapshots/CableLayoutModel.cs` (для диффа ревьюеру;
  коммитов нет до явного запроса).

---

## Task 2: Builder `CableLayoutBuilder.Build` — группировка кабель/провод + стороны

**Files:**
- Create: `addin/Data/CableLayoutBuilder.cs`

**Interfaces:**
- Consumes: `List<TerminalConnectionModel>` (Task 1 из Фазы D), `CableModel`/`CableLayoutModel`
  (Task 1), `DiagnosticLogger` (Diagnostics/DiagnosticLogger.cs), `CultureInfo`.
- Produces: `static class CableLayoutBuilder.Build(List<TerminalConnectionModel>, DiagnosticLogger)
  → CableLayoutModel`. Дампы `[CABLE]` (на кабель) и `[CABLE-SUM]` (итог).

- [x] **Step 1: Написать `addin/Data/CableLayoutBuilder.cs` (полный код)**

```csharp
using System.Collections.Generic;
using System.Globalization;

namespace MyEplanActions
{
    /// <summary>Builder CableLayoutModel (Фаза E, plan_implementation §10 «Этап 6»):
    /// группирует соединения клеммника по кабелям. Чистый модуль — только модели, без
    /// EPLAN-API. Семантика стороны = TerminalSide из модели (сторона ряда, rev.7.1):
    /// Top/Right → Right, Bottom/Left → Left, Unknown → Other. Бинарный случай: в проекте
    /// CableName == null (summary п.21б/п.24, только №31058 IsCable) → все кабельные
    /// соединения в один CableModel без имени; при появлении CableName — группировка
    /// по имени (ключ расширения заложен).</summary>
    public static class CableLayoutBuilder
    {
        public static CableLayoutModel Build(List<TerminalConnectionModel> lstModels, DiagnosticLogger log)
        {
            CableLayoutModel oLayout = new CableLayoutModel();
            if (lstModels == null || lstModels.Count == 0)
            {
                if (log != null) log.Log("[CABLE-SUM] соединений 0 — layout пуст");
                return oLayout;
            }

            // Группировка кабельных соединений по имени (CableName); null-ключ — «без имени».
            Dictionary<string, CableModel> dicCables = new Dictionary<string, CableModel>();
            foreach (TerminalConnectionModel oM in lstModels)
            {
                if (oM == null || !oM.IsCable)
                {
                    if (oM != null) oLayout.NoCableConnections.Add(oM);
                    continue;
                }
                string strKey = oM.CableName;   // null = кабель без имени
                CableModel oCable;
                if (!dicCables.TryGetValue(strKey, out oCable))
                {
                    oCable = new CableModel();
                    oCable.Name = oM.CableName;
                    dicCables[strKey] = oCable;
                    oLayout.Cables.Add(oCable);
                }
                oCable.Connections.Add(oM);
                switch (oM.Side)
                {
                    case TerminalSide.Top:
                    case TerminalSide.Right: oCable.RightConnections.Add(oM); break;
                    case TerminalSide.Bottom:
                    case TerminalSide.Left: oCable.LeftConnections.Add(oM); break;
                    default: oCable.OtherConnections.Add(oM); break;
                }
            }

            // Дамп [CABLE] на каждый кабель + WARN для Unknown-сторон.
            foreach (CableModel oCable in oLayout.Cables)
            {
                if (log != null)
                {
                    string strName = oCable.Name ?? "<без имени>";
                    log.Log("[CABLE] '" + strName + "': подключений " +
                        oCable.Connections.Count.ToString(CultureInfo.InvariantCulture) +
                        " (Left " + oCable.LeftConnections.Count.ToString(CultureInfo.InvariantCulture) +
                        ", Right " + oCable.RightConnections.Count.ToString(CultureInfo.InvariantCulture) +
                        ", Other " + oCable.OtherConnections.Count.ToString(CultureInfo.InvariantCulture) + ")");
                }
                foreach (TerminalConnectionModel oM in oCable.OtherConnections)
                    if (log != null)
                        log.Warn("[CABLE] кабель '" + (oCable.Name ?? "<без имени>") +
                            "': подключение клеммы '" + (oM.Terminal ?? "-") +
                            "' без определённой стороны (Unknown) — в OtherConnections");
            }

            // Итог [CABLE-SUM].
            if (log != null)
            {
                int nUnknown = 0;
                foreach (CableModel oCable in oLayout.Cables) nUnknown += oCable.OtherConnections.Count;
                log.Log("[CABLE-SUM] кабелей " + oLayout.Cables.Count.ToString(CultureInfo.InvariantCulture) +
                    ", проводных (NoCable) " + oLayout.NoCableConnections.Count.ToString(CultureInfo.InvariantCulture) +
                    ", кабельных подключений с Unknown-стороной " + nUnknown.ToString(CultureInfo.InvariantCulture) +
                    " (both-sides/multiple не воспроизведены: CableName в проекте отсутствует)");
            }
            return oLayout;
        }
    }
}
```

- [x] **Step 2: Само-проверка (без компиляции на Linux):** логика чистая; группировка по
  `CableName` (null-ключ работает через `Dictionary<string, CableModel>` — null допустим);
  сторона `Top/Right→Right`, `Bottom/Left→Left`, `default→Other`; провода в
  `NoCableConnections`; InvariantCulture в дампах; конвенции addin/ (без BOM). Отметить
  «код самопроверен; сборка — шаг пользователя».

- [x] **Step 3: Снапшот** — `.superpowers/sdd/plan_stage5/snapshots/CableLayoutBuilder.cs`.

---

## Task 3: Интеграция в `AnalyzeAction.Run` (секция 11) + BUILD_STAMP rev.8.0

**Files:**
- Modify: `addin/Actions/AnalyzeAction.cs` (секция 10/11, строка 26)

**Interfaces:**
- Consumes: `CableLayoutBuilder.Build` (Task 2), `lstTcm` (секция 10, rev.7.1).
- Produces: секция 11 после `[TCM]` — построение layout + дампы `[CABLE]`/`[CABLE-SUM]`;
  обновлённый BUILD_STAMP rev.8.0.

- [x] **Step 1: Обновить BUILD_STAMP (AnalyzeAction.cs:26)**

```csharp
private const string BUILD_STAMP = "2026-09-21 Этап 5 rev.8.0 (Фаза E: CableLayoutBuilder — группировка соединений по кабелям; бинарный случай, CableName в проекте нет)";
```

- [x] **Step 2: Добавить секцию 11 после секции 10** (после строки `_logger.Summarize(...)` в
  конце секции 10, перед закрывающей скобкой `Run()`):

```csharp
// --- 11. Фаза E: CableLayoutBuilder — группировка соединений по кабелям ---
_logger.Log("[INFO] --- Фаза E: CableLayout (группировка соединений по кабелям) ---");
CableLayoutModel oLayout = CableLayoutBuilder.Build(lstTcm, _logger);
_logger.Summarize("Фаза E: кабелей " + oLayout.Cables.Count +
    ", проводных " + oLayout.NoCableConnections.Count + ".");
```

- [x] **Step 3: Обновить строку BeginRun** (AnalyzeAction.cs:32) — «Этап 4 rev.7.1» →
  «Этап 5 rev.8.0 (Фаза E: CableLayoutBuilder)»:

```csharp
_logger.BeginRun("TERMINAL_STRIP_ANALYZE — Этап 5 rev.8.0 (Фаза E: CableLayoutBuilder)", BUILD_STAMP);
```

- [x] **Step 4: Само-проверка:** `lstTcm` объявлен в секции 10 до секции 11 (порядок корректен);
  `CableLayoutBuilder` в том же namespace `MyEplanActions` (using не нужен); счётчики
  этапов 1–4 не изменены (только добавлена секция 11 и штампы). Отметить «код
  самопроверен; сборка — шаг пользователя».

- [x] **Step 5: Снапшот** — `.superpowers/sdd/plan_stage5/snapshots/AnalyzeAction.cs`.

---

## Task 4: Прогон на Windows-стенде (верификация)

**Files:**
- Run: `build_addin.bat` (Windows-стенд EPLAN 2.9) — сборка DLL + запуск действия
  `TERMINAL_STRIP_ANALYZE` на стресс-клеммнике `&ЭМ2/8.1` (горизонтальная форма
  `Клемник_ОУ(горизонтально)_addin`).

**Interfaces:**
- Consumes: собранный addin (Tasks 1–3), лог `terminal_strip_addin.log`.
- Produces: BUILD_STAMP rev.8.0 в логе; счётчики; дампы `[CABLE]`/`[CABLE-SUM]`.

- [x] **Step 1: Собрать и прогнать** на Windows-стенде (`build_addin.bat`, запуск действия).
  Ожидание в логе `terminal_strip_addin.log` (rev.8.1, обновлено после прогона rev.8.0
  21.09.2026 — найден и исправлен Critical-баг Фазы D: двойное назначение DmRow
  (K140 у точек Unknown и Bottom клеммы №2, провод `#5` pin=1 потерян); маркеры
  `[CABLE]`→`[CABGROUP]` — коллизия с дампом точек LeadDetector):
  - `BUILD_STAMP` = **rev.8.1**; счётчики этапов 1–4 **без изменений** vs rev.7.1: 77 точек,
    `[K4]` 61 колонка, `[MATCH]` 77, `[DMERR]` 0, lookup-пробы S029153 9 шт + 1 К4;
  - `[TCM]` 77 моделей; клемма №2: Top/провод (pin=0), Bottom/кабель (K140),
    Unknown/провод (`#5` pin=1) — каждый DmRow назначен ровно один раз;
  - `[TCM-SUM]` — моделей 77, без сопоставления 0, листьев моста 2;
  - `[CABGROUP]` — **1 строка**: `'<без имени>': подключений 15 (Left 8, Right 7, Other 0)`;
  - `[CABGROUP-SUM]` — «кабелей **1**, проводных (NoCable) **62**, кабельных подключений с
    Unknown-стороной **0**» (== [MATCH-SUM]: кабельных 15 / проводных 62);
  - WARN ровно **10** (9 lookup-проб + 1 «колонок К4 (61) != клемм (60)»); Unknown-WARN нет;
  - ВАЖНО: Left>0 и Right>0 у одного агрегата — НЕ «both-sides воспроизведён», это
    артефакт бинарной группировки (все кабели проекта в одной группе); настоящий
    both-sides/multiple ждёт реальных CableName.

- [x] **Step 2: Сверить результат** с ожиданием Step 1. При расхождении — вернуться к
  Tasks 2–3 (диагностика по дампам), повторно прогнать.

- [x] **Step 3: Зафиксировать прогон** в `summary.md` (п.38) и отметить чекбоксы
  `plan_stage5.md` закрытыми. Обновить `plan_implementation.md` (§23 Фаза E — статус
  ЗАВЕРШЕНА, вход Фазы F сформулирован).

---

## Self-Review (проверка плана против spec)

**Покрытие spec:**
- `CableModel` (Name/Connections/Left/Right/Other/SymbolPosition) — Task 1. ✅
- `CableLayoutModel` (Cables/NoCableConnections) — Task 1. ✅
- `CableLayoutBuilder.Build` (стороны, кабель/провод, группировка по CableName, дампы) — Task 2. ✅
- Интеграция секция 11 + BUILD_STAMP rev.8.0 — Task 3. ✅
- Верификация прогоном + фиксация — Task 4. ✅

**Placeholder scan:** кода нет, все шаги с полным кодом. ✅

**Type consistency:** `CableLayoutBuilder.Build(List<TerminalConnectionModel>, DiagnosticLogger)
→ CableLayoutModel` одинаков во всех задачах; `TerminalSide` значения (Top/Right/Bottom/Left)
совпадают с `TerminalConnectionModel.cs:10`. `Pt.SymbolPosition` — `X=NaN` сентинел (Task 1),
согласовано с «NaN до Фазы F». ✅

**Риск (задокументирован в spec):** на текущих данных 15 кабелей по одному подключению —
left/right по стороне; both-sides/multiple не воспроизводятся (нет `CableName`) —
фиксируются «не воспроизведено», ключ расширения (группировка по `CableName`) заложен в
Task 2. ✅

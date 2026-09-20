# План Этапа 3 — Фаза C (AnchorResolver) + сопоставление строк

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (шаги с чекбоксами). Специфично для этой среды: **прогоны в EPLAN делает пользователь** (Windows), агент пишет код, разбирает логи и правит. Верификация — по чек-листам ожидаемого лога (паттерн Фаз A/B), не по unit-тестам.

**Goal:** Выделить якорную логику в независимый модуль `AnchorResolver` (`ReportBlockReference` → `Dictionary<TerminalKey, AnchorPos>`), перевести формы на уникальные слои `TSA_LEAD`/`TSA_MARKER` (урок rev.9), добавить запасной якорь `SourceObject=Terminal` и поддержку вертикальной ориентации отчёта (§8.4: сопоставление по X вместо Y).

**Architecture:** Новый каталог `addin/Anchor/`: `AnchorResolver` — чистая функция из списка дескрипторов формы (`PhRow`) в `AnchorMap` (доминирующий ряд/столбец номеров + якоря «номер ↔ позиция вдоль колонок» + карта «номер → полное имя клеммы из DM»). `MatchBuilder` теряет якорный код и потребляет `AnchorMap`. Ориентация — один параметр `ReportOrientation`, протянутый в `AnchorResolver` и `CheckK4Report`/`K4Report.BindIndex` (колонки К4 вдоль оси X при Horizontal, вдоль Y при Vertical). К1–К3' не меняются (топология и 2D-расстояния осенезависимы).

**Tech stack:** C# (.NET Framework), EPLAN 2.9.4 API, сборка `csc.exe` через `addin/build_addin.bat` (`/recurse` — новые подкаталоги подхватываются сами).

**Spec:** `plan_implementation.md` §8 (Этап 4 — разрешение якорей), §23 (Фаза C/D); `summary.md` п.28 + «Следующие шаги» п.7 (вход Этапа 3); `plan_stage2.md` (итог Этапа 2, эталонные счётчики rev.5.6).

## Global Constraints

- API-имена — **только из KB** (www.eplan.help API 2.9 / kb-чанки). Неподтверждённое — диагностическая проба + лог, не фантазия. Уже подтверждено: `PlaceHolderText.SourceObject` → `Terminal` (проба `[PHPROBE]` rev.5.5); `Terminal.Name` — ПОЛНОЕ имя `=…-X2:1` (урок rev.5.2); текст дескриптора — `Contents`/`GetDisplayString` (rev.5.5).
- Эталон сравнения — **прогон rev.5.6** (summary п.28): 298 линий → 143 компоненты, 60 стубов + 7 перемычек, 76 компонент (1 мост) = 77 точек, `[K4]` 61 колонка (0×10 / 1×25 / 2×26), `[ANCHOR]` Y=-81.0, якорей 60 (коллизий 0, дальних 0), `[SPLIT]` 1, `[MATCH]` 77 / сирот 0, WARN ровно 7 (6 lookup-проб + «колонок≠клемм»). Регрессионные прогоны Задач 1–3 обязаны повторить эти счётчики.
- Позиционный fallback якорей ЗАПРЕЩЁН (решение пользователя 20.09.2026, summary п.26): нет якорей — нет сопоставления, WARN.
- UI отложен (решение 20.09.2026): `TargetStripName` остаётся в `AddInConfiguration`. Имя формы/слоёв/ориентация меняются ТОЛЬКО в конфиге.
- Длины отрезков в критериях не использовать. Arc игнорируются осознанно (п.18).
- Тестовый стенд — стресс-клеммник `&ЭМ2/8.1` (горизонталь) + его копия-страница для вертикали (Задача 0).
- Каждый прогон: `BUILD_STAMP` (rev.6.x) + путь/дата сборки; лог в папке сценариев. Файлы с кириллицей — UTF-8 c BOM.
- WARN-пробы `TerminalDiagram` → S029153 (6 шт.) — известный шум lookup-цикла.

---

### Задача 0: Стенды *(делает пользователь)* — ЗАВЕРШЕНА с изменениями (решения пользователя 20.09.2026)

**Files:** нет (работа в EPLAN: мастер-данные форм).

- [x] ~~**Шаги 1–2:** TSA-формы~~ — **ОТМЕНЕНЫ пользователем**: TSA-слои не нужны, автонумерация `EPLANnnn` устраивает на текущем проекте (Задача 2 исключена из плана; к TSA можно вернуться при переносе на другой проект — урок rev.9 остаётся в силе).
- [x] ~~**Шаг 3:** копия страницы `&ЭМ2/8.2`~~ — **ОТМЕНЕН пользователем**: вертикальный отчёт вставлять на ту же страницу `&ЭМ2/8.1` («не критично»).
- [x] Вертикальная форма создана, проверена в EPLAN и переименована пользователем в **`Клемник_ОУ(вертикально)_addin.f11`** (содержит подстроку `addin` — фильтр `FormNameFilter` доволен).

**Критерий:** ✅ вертикальная форма `_addin` в мастере-данных проекта.

### Задача 1: AnchorResolver — выделение модуля (rev.6.0, чистый рефакторинг)

**Files:**
- Create: `addin/Anchor/AnchorResolver.cs`
- Modify: `addin/Data/MatchBuilder.cs` (удалить `FindDominantAnchorRowY`, якорный цикл §3, перенести `ParseTerminalNumber`/`SuffixAfterColon`; потреблять `AnchorMap`)
- Modify: `addin/Actions/AnalyzeAction.cs` (`ResolvePlaceholderText` → `AnchorResolver.ParseTerminalNumber`)

**Interfaces (produces):**
```csharp
enum ReportOrientation { Horizontal, Vertical }   // Задача 1: используется только Horizontal

sealed class TerminalAnchor {
    public int Number;            // номер клеммы (суффикс после ':')
    public double Pos;            // позиция ВДОЛЬ колонок: X (Horizontal) / Y (Vertical)
    public double RowCoord;       // координата ряда: Y (Horizontal) / X (Vertical)
    public string Text;           // исходный текст дескриптора
    public bool FromSourceObject; // Задача 3: якорь из SourceObject, не из текста
}

sealed class AnchorMap {
    public ReportOrientation Orientation;
    public bool HasRow;                    // доминирующий ряд найден (>=2 числовых дескрипторов)
    public double RowCoord;                // NaN если !HasRow
    public List<TerminalAnchor> Anchors;   // только ряд; коллизии/дальние — считает MatchBuilder
    public Dictionary<int,string> NumberToTerminalKey; // номер -> полное имя клеммы из DM (Задача 3)
}

static class AnchorResolver {
    // Задача 1: Build(PhRow[], orientation, log) — доминирующий ряд + парсинг чисел.
    // Задача 3: + параметры (stripTerminalNames, targetStripName) для NumberToTerminalKey и [PHFB].
    public static AnchorMap Build(List<PhRow> phRows, ReportOrientation orientation,
        DiagnosticLogger log);
    public static int ParseTerminalNumber(string name);  // перенос из MatchBuilder (публичный)
}
```

- [x] **Шаг 1:** написать `AnchorResolver.cs`: перенос `FindDominantAnchorRowY` (бакет 0.1 мм по Y, порог ≥2) и якорного цикла сбора (фильтр ряда, парсинг числа) из `MatchBuilder.cs:136-180`; `Pos = Location.X`, `RowCoord = Y`. Лог `[ANCHOR]` — из `Build`. Сбор [`PHCOL`]-коллизий/дальних остаётся в `MatchBuilder` (нужны колонки К4) — цикл `foreach (TerminalAnchor in map.Anchors)` вместо `foreach (PhRow)`.
- [x] **Шаг 2:** сборка (`addin/build_addin.bat`), компиляция = проверка; поведение не меняется.
- [x] **Шаг 3 (пользователь):** прогон rev.6.0 на `&ЭМ2/8.1`, форма и слои как в rev.5.6 (старая горизонтальная форма без TSA). *(выполнен 20.09.2026 20:24: все счётчики == rev.5.6)*
- [x] **Шаг 4 (агент):** критерии: все счётчики == rev.5.6 байт-в-байт по значениям (эталон в Global Constraints); новых WARN нет. ✅ Рефакторинг без изменения поведения. **Задача 1 ЗАКРЫТА.**

### ~~Задача 2: Уникальные слои TSA_LEAD / TSA_MARKER (rev.6.1)~~ — ОТМЕНЕНА

> **Решение пользователя (20.09.2026):** TSA-слои не нужны — автонумерация `EPLANnnn` устраивает на текущем проекте. Нумерация ревизий сдвинута: Задача 3 → **rev.6.1**, Задача 4 → **rev.6.2**. К TSA можно вернуться при переносе на другой проект.

### Задача 3: TerminalKey + запасной якорь SourceObject=Terminal (rev.6.1)

**Files:**
- Modify: `addin/Data/MatchBuilder.cs` (`PhRow` (строка 26) + поле `public string SourceTerminalName;` — полное имя Terminal или `""`)
- Modify: `addin/Actions/AnalyzeAction.cs` (заполнение `SourceTerminalName` при построении `PhRow`: `oPh.SourceObject as Terminal` → `SafeText(() => oTerm.Name)`, всё в try/catch)
- Modify: `addin/Anchor/AnchorResolver.cs` (`Build` + параметры `Dictionary<string, List<string>> stripTerminalNames, string targetStripName`; fallback-логика; `NumberToTerminalKey`)

**Interfaces:** `AnchorResolver.Build(phRows, orientation, stripTerminalNames, targetStripName, log)`; поведение:
1. Якори из текста (как rev.6.0); если текст не числовой, а `SourceTerminalName` парсится в номер → якорь с `FromSourceObject=true`, строка `[PHFB] дескриптор '<text>' (@X;Y): текста-номера нет, якорь из SourceObject '<name>' → №N`.
2. `NumberToTerminalKey`: из `stripTerminalNames[targetStripName]` — `ParseTerminalNumber(полное имя) → полное имя`; дубль номера → `log.Warn("[KEYDUP] номер N: два имени '<a>' и '<b>' — взят первый")`; клеммник не найден → пустой словарь (без падения).

**Requires:** Задача 1.

- [x] **Шаг 1:** реализация по интерфейсу выше; `MatchBuilder` при потреблении якоря берёт `TerminalKey` из `NumberToTerminalKey` (нет ключа → существующий путь через `dicByNumber`). Попутно (отложенный Minor ревью Задачи 1): переименовать `FindDominantAnchorRowY` → `FindDominantAnchorRow` (поведение не меняется).
- [x] **Шаг 2 (пользователь):** прогон rev.6.1 на `&ЭМ2/8.1`, горизонтальная форма (как rev.6.0). **Выполнен 20.09.2026 21:02.**
- [x] **Шаг 3 (агент):** критерии: счётчики == rev.6.0; `[PHFB]` 0 строк (все тексты ряда числовые — запасной путь готов, но не задействован); `[KEYDUP]` нет; WARN ровно 7. Реальная проверка fallback — Задача 4 (вертикальная форма). ✅ **Все критерии выполнены (summary п.31); `[PHPROBE]`: дескрипторы несут `SourceObject=Terminal`. Задача 3 ЗАКРЫТА.**

### Задача 4: Вертикальная ориентация (rev.6.2)

**Files:**
- Modify: `addin/Anchor/AnchorResolver.cs` — ось из `Orientation`: Horizontal → ряд по Y (как есть), `Pos = X`; Vertical → доминирующий **столбец** по X (бакет 0.1 мм по X, порог ≥2), `Pos = Y`, `RowCoord = X`; `[ANCHOR]`: «доминирующий столбец номеров формы: X=…»; NaN-guard по оси `Pos` (отложенный Minor ревью Задачи 1)
- Modify: `addin/Geometry/LeadDetector.cs` — `CheckK4Report(..., ReportOrientation orientation)`: центры стубов и виртуальные колонки проектируются на ось (X при H, Y при V), привязка точек по |Δ оси| ≤ полушага
- Modify: `addin/Geometry/LeadGeometry.cs` — `K4Report` + поле `Orientation`; `BindIndex(Pt)` сравнивает координату оси (`p.X` при H, `p.Y` при V); `[K4]`-лог печатает ось
- Modify: `addin/Configuration/AddInConfiguration.cs` — `public const ReportOrientation Orientation = ReportOrientation.Horizontal;`
- Modify: `addin/Actions/AnalyzeAction.cs` — строка `[ORIENT] ориентация отчёта: …` (из конфига), прокидывание ориентации в `AnchorResolver`/`LeadDetector`

**Requires:** Задача 0, Задачи 1, 3.

- [x] **Шаг 1:** реализация осей (перечисленное выше). К1–К3' не трогать: топология и расстояния до маркеров 2D-осенезависимы. **Готово (rev.6.2): код + ревью spec ✅ / Approved, 0 Critical/Important; NaN-guard З1 закрыт; сверх Files затронут MatchBuilder (хардкод Horizontal в каллере Build + ось в метках логов) — justified, см. ledger.**
- [ ] **Шаг 2 (пользователь):** прогон A — вертикальный стенд: активная страница `&ЭМ2/8.1`, конфиг `Orientation = Vertical`, `ReportFormName = "Клемник_ОУ(вертикально)_addin"`.
- [ ] **Шаг 3 (агент):** критерии прогона A (расклад заранее не предсказывается — форма другая; фиксируется фактический лог): `[SUCCESS-TYPE]` получен; `[ORIENT] Vertical`; `[CROSS]` точки == Ext+Int целевого клеммника — СОВПАДАЕТ; сирот 0; конфликтов классификации 0; якоря: ряд/столбец номеров из `[ANCHOR]` ИЛИ все через `[PHFB]` (первый реальный тест запасного пути); если якорей 0 и `[PHFB]` 0 — **STOP**, разбор `[PH]`/`[PHPROBE]` с пользователем; любые WARN помимо 6 lookup-проб — разобрать и зафиксировать.
- [ ] **Шаг 4 (пользователь):** прогон B — контрольный регресс: конфиг `Orientation = Horizontal`, форма `Клемник_ОУ(горизонтально)_addin`, страница `&ЭМ2/8.1`.
- [ ] **Шаг 5 (агент):** критерии прогона B: все счётчики == rev.6.1. ✅ Ось инкапсулирована, горизонталь не задета.

### Задача 5: Закрытие этапа

- [ ] Обновить `summary.md` (прогоны rev.6.0–6.2, уроки; решение об отмене TSA), зафиксировать правку мастер-плана при следующем согласовании: §8.2 (фильтр якорей — `PlaceHolderText` с числовым текстом / `SourceObject=Terminal`, НЕ `Line+EPLAN430+Invisible`), §8.4 (доминирующий ряд/столбец — реализованный вариант).
- [ ] Сформулировать вход Фазы D (TerminalConnectionModel = DataModel + Anchor: `TerminalConnectionModel[]` — клемма → якорная позиция → подключения с типом кабель/провод), что подтверждено, что осталось (UI-выбор клеммника — отложен решением 20.09.2026).

**Критерии готовности Этапа 3:** (1) `AnchorResolver` — независимый модуль, `MatchBuilder` потребляет `AnchorMap`, счётчики горизонтального стенда не изменились; (2) запасной якорь `SourceObject=Terminal` реализован и отработал минимум на одном стенде (или разобран, если не потребовался); (3) вертикальная форма проходит критерии прогона A + регресс B чист; (4) вход Фазы D сформулирован.

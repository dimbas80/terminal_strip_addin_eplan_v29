# План Этапа 2 — Фаза B (Data Model spike) + скелет Add-in

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (шаги с чекбоксами). Специфично для этой среды: **прогоны в EPLAN делает пользователь** (Windows), агент пишет код, разбирает логи и правит. Верификация — по чек-листам ожидаемого лога (паттерн Фазы A), не по unit-тестам.

**Goal:** Прочитать электрику из Data Model (`TerminalStrip → Terminal → Connection → Cable`), перенести детектор выводов К1–К4 из rev.13 в скелет Add-in и сверить геометрию отчёта с DataModel на стресс-клеммнике `&ЭМ2/8.1`.

**Architecture:** Add-in (`addin/`) минимальной структуры по §3: Diagnostics (логгер из spike) + Configuration (слои/допуски/форма) + Geometry (чистые функции детектора, порт из rev.13) + Data (ридеры DataModel) + один диагностический Action `TERMINAL_STRIP_ANALYZE`, который строит embedded-отчёт как rev.13, детектирует выводы, читает DataModel и сводит `[MATCH]` в лог.

**Tech stack:** C# (.NET Framework), EPLAN 2.9.4 API (`Eplan.EplApi.DataModel` и др.), сборка `csc.exe` через bat (паттерн `spike/build_TerminalStripReportSpike.bat`).

**Spec:** `plan_implementation.md` §2.4, §21, §23 (Фаза B), §25; `summary.md` (К1–К4, решения п.14–17).

## Global Constraints

- API-имена — **только из kb.py-чанков** (навык eplan-arduino-kb; бюджет 3–5 запросов на вопрос). Неподтверждённое — диагностическая проба + лог, не фантазия.
- Подтверждено KB (eplan.help API 2.9): `Page.TerminalStrips: TerminalStrip[]`; `Terminal.TerminalStrip`; `Terminal.ExternalConnections` / `InternalConnections: Terminal.ConnectionInfo[]`; `Terminal.ConnectionInfo` — поля `Conn: Connection`, `ConnectionName`, `Function`, `FunctionName`, `FunctionPinName`, `PinIndex`, `FormPos`; `Terminal.Bridges: Terminal.Bridge[]`, `Bridge.BridgeSegments: BridgeInfo[]` (`BridgedFunction: Terminal`, `Conn: Connection`); `Terminal.ConnectionSide {External=0, Internal=1}`; `Cable.CableConnections: Connection[]`; `Properties.Cable.*` №35100/35101/35108; `Properties.TerminalStrip.TERMINALSTRIP_COUNTOFTERMINALS`.
- **Не подтверждено KB** (обязательные пробы в Задаче 4): перечисление клеммников уровня проекта (кандидаты: итерация страниц → `Page.TerminalStrips`; `TerminalStrip.Terminals`), связь `Connection` → `Cable` (кандидат: обратная карта через `Cable.CableConnections`).
- Длины отрезков в критериях выводов не использовать. Слои/имя формы/тип отчёта/допуски — только из конфига (дефолты: выводы `EPLAN450`, маркеры `EPLAN100`, `STUB_MAX_LENGTH_MM=1.0`, допуск вершин 0.001 мм, отчёт `TerminalConnectiondiagram`, форма `Клемник_ОУ(горизонтально)_addin`).
- Тестовый стенд — стресс-клеммник `&ЭМ2/8.1` (298 линий, 60 стубов + 7 перемычек, мост 45°, Arc-«точки», 77 точек).
- Каждый прогон: `BUILD_STAMP` в логе + путь/дата сборки (анти-ShadowCopy), лог в папку сценария (паттерн `LOG_DIR_CANDIDATES` из rev.9/13). Файлы с кириллицей — UTF-8 c BOM.
- WARN-пробы `TerminalDiagram` → S029153 (6 шт.) — известный шум lookup-цикла, не ошибка.

---

### Задача 0: Прогон rev.13 на форме с перемычкой стубов *(параллельно с Задачей 1; делает пользователь)*

**Files:** нет новых; разбор лога `terminal_strip_spike_rev13_jumper.log`.

- [x] **Шаг 1 (пользователь):** сделать вариант формы, где при перемычке соседних клемм стуб рисуется «2 точки + линия между ними» вместо 2 стубов; вставить отчёт на копию стресс-клеммника; прогнать rev.13.
- [x] **Шаг 2 (агент):** разобрать лог — **ИТОГ (20.09.2026, лог 11:06): содержательно идентичен rev.13 от 10:30** (512 строк diff = только timestamp/object-ID; стубов 60, перемычек 7, `[K4]` 61 колонка, `[RESULT]` 77). Пользователь разобрал устройство формы напрямую: при перемычке **стуб остаётся**, «точка» — **круг (Arc `EPLAN100`) поверх стуба**. Feared-случай «2 точки + линия вместо 2 стубов» не реализуется.
- [x] **Шаг 3 (агент):** зафиксировано в `summary.md` (п.18): **маркеры = только стубы, круги не используем**; К3'/К4 rev.13 без изменений; Arc игнорируются осознанно; отдельный прогон «точечного» варианта формы не требуется.

**Критерий:** ✅ вывод зафиксирован; Задача 3 сохраняет поведение rev.13 один-в-один (переработка маркерной геометрии НЕ нужна).

### Задача 1: Скелет Add-in (AddIn.cs + PING)

**Files:**
- Create: `addin/AddIn.cs` (класс add-in — порт из `spike/TerminalStripReportSpike.cs`, секция add-in)
- Create: `addin/Actions/AnalyzeAction.cs` (заглушка `TERMINAL_STRIP_ANALYZE`: пишет `PING-ANALYZE` в лог)
- Create: `addin/Diagnostics/DiagnosticLogger.cs` (порт логгера rev.13: `Log`, `Section`, `LOG_DIR_CANDIDATES`, BUILD_STAMP)
- Create: `addin/build_addin.bat` (копия bat spike; референсы — тот же список из `spike/build_TerminalStripReportSpike.bat`)
- Create: `addin/README_addin.md` (подключение: папка Addins + перезапуск; как снять лог)

**Interfaces (produces):** `DiagnosticLogger.Log(string)`, `DiagnosticLogger.Section(string)`; имя команды `TERMINAL_STRIP_ANALYZE`.

- [x] **Шаг 1:** написать файлы (порт из spike, без новой логики).
- [x] **Шаг 2 (пользователь):** собрать, подключить, выполнить команду; лог в workspace. *(сборка: исправлены порядок параметров в bat (CS2022), `using Eplan.EplApi.Base` для PointD, убран несуществующий `LockSelection`; лог 11:52 — в корневой «Сценарии», подпапка не создалась)*
- [x] **Шаг 3 (агент):** проверено: `BUILD_STAMP rev.3.0`, лог читается. ✅ Задача 1 закрыта.

### Задача 2: Конфигурация

**Files:** Create `addin/Configuration/AddInConfiguration.cs`.

- [x] **Шаг 1:** статический класс: `LeadLayer="EPLAN450"`, `MarkerLayer="EPLAN100"`, `VertexTolMm=0.001`, `StubMaxLengthMm=1.0`, `ReportTypeName="TerminalConnectiondiagram"`, `ReportFormName="Клемник_ОУ(горизонтально)_addin"`, `MaxPointsPerColumn=2`. Все обращения к слоям/порогам в последующих задачах — только через него.
- [x] **Шаг 2:** включить в bat; компиляция = проверка.

### Задача 3: Порт детектора выводов (К1–К4) как чистых функций

**Files:** Create `addin/Geometry/LeadGeometry.cs` (структуры `Pt`, `Seg`, `Component`), `addin/Geometry/LeadDetector.cs`, `addin/Geometry/MarkerGeometry.cs`.

**Interfaces (produces):**
```csharp
static Component[] AnalyzeLineComponents(Seg[] segs, double tol);           // К1: слияние по вершинам
static Lead[] DetectLeads(Component[] comps, bool[] collinear);             // К1/К2: цепи и мосты
static Pt[] CablePoints(Lead[] leads, MarkerGeometry markers);              // К3': внешние концы,
                                                                            //   расстояние до ГЕОМЕТРИИ слоя маркеров (проекция + концы; по итогам Задачи 0)
static K4Report CheckK4(Pt[] pts, MarkerGeometry markers, double maxLen);   // К4 rev.13: колонки-стабы +
                                                                            //   виртуальные, полушаг, сироты/перегруз
```
Источник порта — `AnalyzeLineComponents` / `DetectLeads` / `CheckK4` из `spike/TerminalStripReportSpike.cs` rev.13; изменения: входы/выходы без EPLAN-типов; К3' — маркерная геометрия (не только 1-сегментные Line, см. Задачу 0); Arc осознанно игнорируются.

- [x] **Шаг 1:** перенесён (BUILD_STAMP `rev.3.0`); логи `[TREE]`, `[ALLLAYERS]`, `[LINE]`/`[LAYER]`, `[COMP]`, `[JUMPER]`, `[LEADSKIP]`, `[CABLE]`, `[RESULT]`, `[K4]` сохранены. Диагностика spike, закрывшие свои вопросы (§5.2 п.4, §5.3 якоря, `[PROP]`/`[GLAYER]` дампы), сознательно не перенесены. Круги-«точки» не используются (summary п.18).
- [x] **Шаг 2 (пользователь):** прогон на `&ЭМ2/8.1` (лог 11:52, `rev.3.0`).
- [x] **Шаг 3 (агент):** сверка с rev.13 выполнена — ✅ всё сошлось: 298 линий → 143 компоненты; 60 стубов + 7 перемычек (координаты байт-в-байт); 77 точек; 61 колонка (0×10 / 1×25 / 2×26); 0 сирот; 0 перегруженных; `[LEADSKIP]` 0; WARN ровно 6 lookup-проб. **Задача 3 закрыта.**

### Задача 4: Data Model-ридеры + дамп `[DM]` (Фаза B)

**Files:** Create `addin/Data/EplanTerminalStripReader.cs`, `addin/Data/EplanCableReader.cs`, Modify `addin/Actions/AnalyzeAction.cs` (флаг `[DM-ONLY]`).

- [x] **Шаг 1 (kb.py, до кода):** ✅ всё подтверждено чанками KB (www.eplan.help API 2.9): `Project.Pages: Page[]`; `TerminalStrip.Terminals: Terminal[]` (sem, sim 0.73); `Connection.CableDefinitionLine: Cable` — прямая связь Conn→Cable (лучше обратной карты); бонус: `CONNECTION_IS_CABLE` №31058 (признак кабельного подключения — пригодится Задаче 5).
- [x] **Шаг 2:** ридеры написаны (`rev.4.0`): дамп `[DM] Terminal | Side | Conn | Pin | cable`, `[DMSTRIP]` (+№35006 кросс-чек числа клемм), `[DMCABLE]`, `[DMSUM]`, `[DMERR]` без прерывания обхода; `[CROSS]` — сверка 77 точек с Ext/Int/Bridge из DataModel.
- [x] **Шаг 3 (пользователь):** прогон `TERMINAL_STRIP_ANALYZE` (`rev.4.0`/`rev.4.1`) на `&ЭМ2/8.1` → лог в workspace. *(20.09.2026, summary п.21/п.22: 588 строк [DM], [DMERR]=0)*
- [x] **Шаг 4 (агент):** сверка: 12 клеммников/348 клемм; сумма Ext+Int целевого = 77 == 77 точек геометрии; 10 клемм с 0 подключений ↔ 10 колонок К4 с 0 точек; 1×25 + 2×26 = 77; `[DMERR]`=0. **Критерий выполнен, Задача 4 закрыта** (rev.4.1: `[CROSS]` по-клеммнику, СОВПАДАЕТ; открытые вопросы — кабели=0 и Bridge 11 vs 12 → Задача 5).

### Задача 5: Свод `[MATCH]` — точки ↔ клеммы ↔ кабель/провод

**Files:** Modify `addin/Actions/AnalyzeAction.cs`, Create `addin/Data/MatchBuilder.cs`.

**Interfaces:** `MatchRow[] Build(Pt[] pts, DmTerminal[] dm, Func<Connection,Cable> cableOf)` — строка: точка (X,Y) ↔ Terminal ↔ Cable|null (кабель/провод).

- [x] **Шаг 1 (rev.5.0, 20.09.2026):** реализация ОТЛИЧАЕТСЯ от исходного замысла — KB (www.eplan.help 2.9) показал: `Connection.CableDefinitionLine` бросает `BaseException` при ≠1 CDP (причина «кабелей 0» в rev.4.x); основной путь — `ConnectionDefPoints` → `ConnectionDefinitionPoint.CableDefinitionLine` → `Cable.Name`, кросс-чек — №31058 «Соединение: Принадлежность=Кабель» (docs: legacy) на Connection и CDP; №31058-vs-CDP расхождения — `[CABX]`. Точка↔клемма — через колонки К4 (K4Report/BindIndex, извлечён из CheckK4), клемма↔колонка — по порядку (условное при 61≠60, WARN). Файлы: `Data/MatchBuilder.cs` (новый), ридер (`FillCable`, CdpCount/IsCableConn/IsCableCdp в [DM]), `Actions/AnalyzeAction.cs` (rev.5.0). Интерфейс: `MatchRow[] Build(LeadAnalysis, DmReport, DiagnosticLogger)`.
- [x] **Шаг 2 (пользователь):** полный прогон `TERMINAL_STRIP_ANALYZE` на `&ЭМ2/8.1` — выполнен серией ревизий rev.5.0→rev.5.6 (summary п.24–28; финальный прогон rev.5.6, 20.09.2026 18:48).
- [x] **Шаг 3 (агент):** критерии выполнены (в ходе ревизий уточнены — см. summary п.24–27): `[MATCH]` 77 строк, все привязаны — через якоря-дескрипторы (доминирующий ряд Y=-81) + раздвоение моста `[SPLIT]` (слот 66.850 → №2); сирот 0, перегруженных колонок 0, конфликтов классификации 0; WARN ровно 7 — все предсказаны (6 lookup-проб + «колонок К4 (61) != клемм (60)» — виртуальная колонка под перемычкой); кабель/провод: №31058, 15/62 — подтверждено пользователем (summary п.24). **Задача 5 закрыта.**

### Задача 6: Закрытие этапа

- [x] Обновить `summary.md` (прогоны п.24–28, уроки, решение по «точке» из Задачи 0 — п.18); расхождения API с мастер-планом зафиксированы в `summary.md` «Следующие шаги» п.7 (CableDefinitionLine бросает при ≠1 CDP; PlaceHolderText без `.Text`; BridgedFunction→BridgedTerminal; LockSelection отсутствует; якоря = PlaceHolderText, не линии спецслоя) — правка §2.4/§23/§25 при следующем согласовании.
- [x] Сформулировать вход Этапа 3 (Фаза C AnchorResolver / сопоставление строк) — `summary.md` «Следующие шаги» п.7: подтверждено (якоря-дескрипторы + доминирующий ряд + [SPLIT], 77/77, №31058, запасной путь SourceObject=Terminal); осталось (выделение `AnchorResolver` в независимый модуль, TerminalKey из DataModel, вертикальная форма, UI-выбор клеммника, конфиг слоёв с уникальными именами).

**Критерии готовности Этапа 2:** ✅ все 5 выполнены (подтверждение — summary п.28): (1) rev.13-детектор воспроизводит rev.13-счётчики из addin-сборки; (2) `[DM]`-дамп согласован с геометрией (77 = сумма подключений, 0 сирот); (3) каждая точка имеет клемму (включая раздвоение №2); (4) кабель/провод классифицирован (№31058, 15/62) и подтверждён пользователем; (5) вывод по «точке» перемычки зафиксирован (п.18: круг поверх стуба, маркеры = только стубы).

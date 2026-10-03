namespace MyEplanActions
{
    /// <summary>
    /// Конфигурация Этапа 2 (план: plan_implementation.md §21, plan_stage2.md Задача 2).
    /// Все обращения к слоям/порогам/имени формы в коде Add-in — только через этот класс.
    /// Значения подтверждены прогонами rev.11–rev.13 (summary.md, К1–К4).
    /// </summary>
    public static class AddInConfiguration
    {
        // Слой выводов и слой стубов-маркеров клемм (rev.11: 72 линии на EPLAN450,
        // 12 стубов на EPLAN100). rev.9: имя автослоя EPLANnnn при слиянии таблиц
        // слоёв формы и страницы не гарантировано — для новых форм использовать
        // уникальные имена; здесь — фактические имена тестовой формы.
        public const string LeadLayerName = "EPLAN450";
        public const string MarkerLayerName = "EPLAN100";

        // rev.13: короткий маркер-стаб; длиннее — линия перемычки стубов между
        // соседними клеммами (стресс-прогон rev.12: перемычки L=7/14/28).
        // «Точки»-круги (Arc) поверх стубов НЕ используются (summary п.18).
        public const double StubMaxLengthMm = 1.0;

        // Допуск совпадения вершин при слиянии отрезков (К1).
        public const double VertexTolMm = 0.001;

        // К4: максимум точек подключения на колонку клеммы (WARN при превышении).
        public const int MaxPointsPerColumn = 2;

        // Тип отчёта и форма (rev.12: §5.1–§5.3 закрыты на этой паре).
        public const string ReportTypeName = "TerminalConnectiondiagram";
        public const string ReportFormName = "Клемник_ОУ(горизонтально)_addin";   // rev.10.11: вертикальная форма (прогон A)
        public const string ReportFilterSchemaName = "";

        // Ориентация отчёта (Этап 3, Задача 4, rev.6.2): ось, вдоль которой идут
        // колонки/якоря — Horizontal — колонки по X (текущая горизонтальная форма),
        // Vertical — по Y (вертикальная форма). Переключается вручную между прогонами:
        // A (Vertical — вертикальный стенд) / B (Horizontal — регресс rev.6.1).
        // Потребители: AnchorResolver.Build, LeadDetector.CheckK4Report, MatchBuilder.
        // rev.10.11: прогон A — вертикальный стенд; для регресса B переключить
        // обратно на Horizontal.
        public const ReportOrientation Orientation = ReportOrientation.Horizontal;

        // Целевой клеммник: отчёт строится по ОДНОМУ клеммнику, сверка [CROSS]/[MATCH]
        // идёт с его подключениями (не с суммой по проекту — урок rev.4.0, summary п.21).
        // Этап 2: захардкоден; позже значение подставляется из выбора пользователя
        // в интерфейсе Add-in. Полное имя (не «X2» из отчёта — оно может не совпасть).
        public const string TargetStripName = "=HII-1.1++ЯЧ67+#2-X2";

        // Фильтр «своих» форм: динамические кандидаты из проекта берём только
        // среди имён, содержащих эту подстроку (урок rev.4 — чужие формы).
        public const string FormNameFilter = "addin";

        // Свойство «Слой» графического объекта (№19019) хранит ЧИСЛОВОЙ LayerId,
        // а не имя слоя; имя достаётся через GraphicalPlacement.Layer.Name (урок rev.7/9).
        public const int LayerPropertyNumber = 19019;

        // Фаза F (rev.9.0): параметры шин кабельной разводки (spec
        // 2026-09-21-fase-f-cable-geometry-design.md): отступ шины от крайних точек
        // стороны (перпендикулярно ряду). Ревизия 2 (22.09.2026): разнос уровней
        // шин кабелей (перпендикулярно ряду, кабель 1 ближе). Ревизия 4: подъём
        // шины целиком дополнительно к отступу от кончиков выводов.
        // rev.10.7 (23.09.2026, правило 2): CableLevelPitchMinMm — МИНИМУМ шага
        // уровней шин; расчётный шаг = max(min, B·min/14) — растёт от высоты
        // символа B (замер [SYMSIZE]).
        public const double CableBusOffsetMm = 10.0;
        public const double CableLevelPitchMinMm = 8.0;
        public const double CableBusLiftMm = 8.0;

        // Ревизия rev.10.1 (22.09.2026, spec 2026-09-22-fase-g-graphics-design.md §9,
        // эталон «Кабель с двух сторон.pdf»): подходы и символы. Подход кабеля i —
        // вертикаль за краем ряда (по оси выноса) с шагом 8 мм; шины заканчиваются
        // на подходе. Колонка символов — за последним подходом двусторонних кабелей.
        // rev.10.7: зазор линия–символ и шаг расстановки символов из конфига УДАЛЕНЫ —
        // выводятся из размера символа (gap = (габарит по оси выноса)/2: H — A/2,
        // V — B/2, rev.10.11; rev.10.8: шаг уровней шин = max(min, B·min/14),
        // замер [SYMSIZE]; rev.10.10: ряды символов — на уровнях шин).
        public const double CableApproachOffsetMm = 10.0;
        public const double CableApproachPitchMm = 8.0;
        public const double CableSymbolColumnOffsetMm = 16.0;

        // rev.11.0 (23.09.2026): линия-ссылка от символа кабеля наружу от клеммника —
        // продолжение шины на уровне ряда символа; длина ВКЛЮЧАЯ стрелку (конец
        // линии = остриё стрелки). Размеры стрелки из замера пользователя
        // 23.09.2026: остриё−7 зад, ±2 полуширина, вырез −3 от зада (= −4 от
        // острия). KB API 2.9 (проверено по eplan.help): PolyLine :
        // GraphicalPlacement, Create(Page), точки — SetPointAt(int, ref PointD)
        // (ref!); свойства Closed и IsSurfaceFilled — сеттер заливки бросает
        // исключение на незамкнутой полилинии, присваивать СТРОГО после Closed=true.
        public const double CableReferenceLineLengthMm = 20.0;
        public const double CableReferenceArrowLengthMm = 7.0;
        public const double CableReferenceArrowHalfWidthMm = 2.0;
        public const double CableReferenceArrowNotchDepthMm = 3.0;

        // Фаза G (spec 2026-09-22-fase-g-graphics-design.md): реальные объекты кабельной
        // разводки вместо превью. Линии — слой EPLAN100 и красное перо (решение
        // пользователя, spec §2.2); слой резолвится из дерева отчёта
        // (GraphicLineCreator.ResolveLayerFromTree), не найден — слой по умолчанию.
        // НЕ идемпотентно: повторный прогон дублирует объекты (очистка — Фаза I).
        public const string GraphicsLayerName = "EPLAN100";
        public const int GraphicsPenColorId = 1;      // красный в штатной палитре EPLAN
        public const double GraphicsPenWidthMm = 0.35;

        // Символ кабеля: библиотека SPECIAL, 16 / CABDCP2, вариант 0 (= «A»; решение
        // пользователя 22.09.2026). Индексация Symbol.Item 0-based (эмпирика rev.10.0:
        // индекс 1 дал вариант B); вариант A = 0. rev.13.1 (Этап 8, H-4): эти константы —
        // ТОЛЬКО headless-путь (обёртки SymbolSizeMeasurer/CableSymbolCreator, ruling R8)
        // и дефолты AddInSettings; UI-режим берёт символ из настроек
        // (SymbolLibrary/SymbolName/VariantH/VariantV — браузер MainDialog/H-4).
        public const string SymbolLibrary = "SPECIAL";
        public const string SymbolName = "CABDCP2";
        public const int SymbolVariant = 0;
        // rev.10.7: фолбэк замера [SYMSIZE] для A и B (14×14 — фактический размер
        // CABDCP2); применяется, если пробная вставка/GetBoundingBox не удались.
        public const double SymbolFallbackSizeMm = 14.0;

        // Проба [SYMBOX] (rev.10.6): второй символ пробы — кандидат на Фазу H (UI,
        // выбор символа пользователем); размеры нужны, чтобы считать зазор/шаги из
        // геометрии символа, а не хардкодить.
        public const string SymbolProbeLibrary = "GOST_single_symbol";
        public const string SymbolProbeName = "K";
        public const int SymbolProbeVariant = 0;
        // Точка вставки пробных символов (за колонкой символов аддина по оси
        // выноса; после замера символы удаляются — урок п.48: не засорять страницу).
        public const double SymbolProbeX = 600.0;
        public const double SymbolProbeY = 150.0;

        // Точка вставки отчёта на странице.
        public const double InsertX = 20.0;
        public const double InsertY = 20.0;

        // Фаза H (rev.12.1, H-2; spec 2026-09-25-fase-h-ui-design.md §2 п.10):
        // UI-режим диалога вставки. Переключается вручную: true — UI-режим
        // (стенд H-2), false — headless-регресс A/B (счётчики == rev.11.15
        // обязательны на каждом шаге).
        // rev16.8 (03.10, решение заказчика): поле больше НЕ const — static readonly.
        // const позволял компилятору свернуть ветку и доказать недостижимость её тела
        // (8× CS0162 «Обнаружен недостижимый код» в сборке на Windows); static readonly —
        // нет. Код гейта остаётся на месте и включается сменой значения.
        public static readonly bool UseUi = true;

        // SPIKE (throwaway, H-4v2): гейты диагностики нативного диалога «Вставить
        // символ». УДАЛИТЬ вместе с addin/UI/NativeSymbolDialogSpike.cs и хуком
        // 1b в RunUi и этим блоком после вердикта (SymbolPickInteraction.cs и хук
        // 1c удалены rev.15.4; brief .superpowers/sdd/plan_stage8/
        // task-h4v2-spike-brief.md / task-h4v2-spike2-brief.md). В headless-прогоне
        // гейты не читаются. Один сценарий за прогон:
        // spike-1 (rev.13.2) ЗАКРЫТ фактами 25.09 (диалог синхронен полному циклу,
        // ctx пуст, ActionManager перечисления не даёт) — гейты выключены, код цел.
        // spike-2 (rev.13.3): производный InsertInteraction + дампы OnSuccess.
        // rev16.8 (03.10, решение заказчика): оба поля больше НЕ const — static
        // readonly. Причина — ровно та же, что у UseUi: const позволял компилятору
        // свернуть ветку и доказать недостижимость её тела, и CS0162 «Обнаружен
        // недостижимый код» на выключенных гейтах был ОЖИДАЕМЫМ и снимается этой
        // правкой (AnalyzeAction.cs:688 — тело NativeSymbolDialogSpike.Run,
        // NativeSymbolDialogSpike.cs:53 — DumpActions). Код за гейтами НЕ потерян и
        // НЕ удалён: он остаётся на месте и включается сменой значения.
        public static readonly bool SpikeNativeInsertSymbol = false;
        public static readonly bool SpikeActionDump = false;

        // rev.14.15 (диагностика крэшей, summary п.108-109): шторм проб
        // rev.14.12/13 ([VARPROP]/[VARPROP-EXC]/[VARPROP-MEMS]/[SYMPL] —
        // per-variant Invoke-пробы + полные дампы значений ~115 членов × 4
        // поверхности) валит CLR (Event Viewer 29.09 13:23: сбойный модуль
        // clr.dll, 0x80131506 Fatal Execution Engine Error). Гейт OFF —
        // код остаётся для точечных ре-проб, исполнения нет. Классификация
        // идёт через ридер-скан (TryGetMemberValueViaScan), а не через шторм.
        // rev16.8 (03.10, решение заказчика): поле больше НЕ const — static readonly.
        // Причина — ровно та же, что у UseUi: const позволял компилятору свернуть
        // ветку и доказать недостижимость её тела; 4× CS0162 «Обнаружен недостижимый
        // код» на выключенном шторме (SymbolBrowserDialog.cs 1637, 1815, 1936, 2035)
        // были ОЖИДАЕМЫМИ и снимаются этой правкой. Код проб НЕ потерян: он остаётся
        // на месте для точечных ре-проб и включается сменой значения.
        public static readonly bool ProbeStormEnabled = false;

        // Фаза H (rev.12.0, spec §6): рамка-призрак — записи GridPitch.<форма>
        // в настройках нет → фиксированный шаг 10 мм.
        public const double GhostPitchFallbackMm = 10.0;

        // rev.15.5 (решение пользователя 30.09.2026): зазор ПОСЛЕ футера ВДОЛЬ
        // направления данных для символов кабелей (мм). Входит в ALONG-размер
        // призрака: GhostFrameMath.ComputeFromTemplateMetrics
        // (шапка + N×колонка_данных + футер + GhostCableGapMm).
        public const double GhostCableGapMm = 100.0;

        // H-5/H-6 (rev.15.0): интерактивная точка вставки отчёта — после «Создать»
        // пользователь кликает точку на странице (рамка-призрак под курсором;
        // Esc — возврат в диалог). Headless не затронут: интеракция запускается
        // только в UI-ветке (RunUi). Отказ запуска/таймаут — MessageBox + заново
        // диалог, ничего не создаётся (безопасная деградация).
        // rev16.8 (03.10, решение заказчика): поле больше НЕ const — static readonly.
        // Причина — ровно та же, что у UseUi: const позволял компилятору свернуть
        // ветку и доказать недостижимость её тела (ветка фиксированной точки,
        // AnalyzeAction.cs:980). Код гейта остаётся на месте и включается сменой
        // значения.
        public static readonly bool UseInsertPointPick = true;

        // H-5/H-6 (rev.15.0): кап ожидания клика вставки точки (сек) — модель
        // ожидания SPIKE-8 (AnalyzeAction, кап 120 с на интеракцию спайка).
        public const int SelectPointTimeoutSec = 120;

        // rev.16.2 (решение 01.10, стенд): фича «Формат блока» (запись 20202[x]
        // на символ кабеля; строки BlockFormat1/2, слот BlockFormatIndex,
        // сверка 1429 BlockCompareProp) УДАЛЕНА целиком — «теперь не
        // используется». Чтение 20376/20377 (BlockCabSourceProp/BlockCabTargetProp)
        // ОСТАЛОСЬ — на нём сидит классификация BP (BreakPointResolver).

        // Fix round 4 (rev.16.x, справка EPLAN 20376/20377): у кабеля есть
        // свойства «Кабели: источник» (№20376) и «Кабели: цель» (№20377) —
        // строки с ПОЛНОЙ структурой концов (К190: 20376='=HII-1.1++ЯЧ67+#1-X2',
        // 20377='=ТСН-1++ЯЧ17+#1-X2'). Механика выбора: сверяем №1429 нашего
        // клеммника (BlockCompareProp) со строками 20376/20377 (Contains,
        // Ordinal); содержится в 20376 → наш конец = источник → показываем
        // ДРУГОЙ конец (цель) → строка 20211,2 (fmt2); в 20377 → наш = цель →
        // показываем источник → 20211,1 (fmt1). В строке формата: 20211,1 =
        // источник кабеля, 20211,2 = цель кабеля. GetSourcesAndTargets больше
        // НЕ используется (кабель может быть «развёрнут»).
        public const int BlockCabSourceProp = 20376;  // «Кабели: источник» — полная структура источника
        public const int BlockCabTargetProp = 20377;  // «Кабели: цель» — полная структура цели

        // rev.16.2 (решение пользователя 30.09.2026): точка разрыва BP «48 / BPIN»
        // на обратном конце линии-ссылки. Символы СИСТЕМНЫЕ (SPECIAL «48 / BPIN»,
        // работают во всех EPLAN) — константы, НЕ настройки; фича всегда
        // включена (BreakPointEnabled не заводим). Варианты СИМВОЛА BPIN
        // (реш. 01.10.2026, смена BP→BPIN): прямые A(0)-горизонт / D(3)-вертик,
        // мульти-клеммные E(4)-горизонт / B(1)-вертик.
        // Наборы отображения .emc применяются к вставленному SymbolReference
        // через SymbolReference.PropertyPlacementsSchemasList.Import (KB 2.9)
        // — файл берётся по фиксированному пути: папка point/ рядом со сборкой
        // аддина (вариант (а), решение пользователя); UI-выбор файлов — позже.
        // Подмена свойства 1450→1250 НЕ выполняется (решение пользователя:
        // пользователь сам редактирует свой .emc).
        public const string BreakPointSymbolLibrary = "SPECIAL";
        public const string BreakPointSymbolName = "BPIN";
        public const int BreakPointVariantStraightH = 0;   // A(0), горизонтальный отчёт
        public const int BreakPointVariantStraightV = 3;   // D(3), вертикальный отчёт (BPIN)
        public const int BreakPointVariantMultiH = 4;      // E(4), горизонтальный отчёт (BPIN)
        public const int BreakPointVariantMultiV = 1;      // B(1), вертикальный отчёт (BPIN)
        public const string PointSchemeFolder = "point";
        public const string EmcStraightStripH = "ТР_Кабель(горизонт).emc";
        public const string EmcStraightStripV = "ТР_Кабель(вертик).emc";
        public const string EmcStraightDeviceH = "ТР_Устройство(горизонт).emc";
        public const string EmcStraightDeviceV = "ТР_Устройство(вертик).emc";
        public const string EmcMultiStripH = "ТР_кабель(int_горизонт).emc";
        public const string EmcMultiStripV = "ТР_кабель(int_вертик).emc";
        public const string BreakPointSuffix = "(EXT)";

        /// <summary>rev.16.2 (стенд 01.10, решение пользователя): шинный BP
        /// (multi) выводится ЗА пределы клеммника — в шапку: хвост шины тянется
        /// до точки «не доходя BreakPointHeaderGapMm (20)» до крайней клеммной
        /// колонки блока (BreakPointHeaderGapMm-зазор от подписей выводов).
        /// Фолбэк (ось блока не передана/вырождена) — короткий хвост
        /// BreakPointBusTailMm (3.25 = полширины символа BP 2.25 + зазор 1).</summary>
        public const double BreakPointHeaderGapMm = 20.0;
        public const double BreakPointBusTailMm = 3.25;

    }
}

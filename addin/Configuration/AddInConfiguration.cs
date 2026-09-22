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
        public const string ReportFormName = "Клемник_ОУ(горизонтально)_addin";
        public const string ReportFilterSchemaName = "";

        // Ориентация отчёта (Этап 3, Задача 4, rev.6.2): ось, вдоль которой идут
        // колонки/якоря — Horizontal — колонки по X (текущая горизонтальная форма),
        // Vertical — по Y (вертикальная форма). Переключается вручную между прогонами:
        // A (Vertical — вертикальный стенд) / B (Horizontal — регресс rev.6.1).
        // Потребители: AnchorResolver.Build, LeadDetector.CheckK4Report, MatchBuilder.
        public const ReportOrientation Orientation = ReportOrientation.Horizontal;

        // Имя формы вертикального стенда (прогон A): «Клемник_ОУ(вертикально)_addin».
        // ReportFormName НЕ меняется и кодом по ориентации не подставляется (текущее
        // поведение не меняем): перед Vertical-прогоном подменить вручную.

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
        public const double CableBusOffsetMm = 10.0;
        public const double CableLevelPitchMm = 8.0;
        public const double CableBusLiftMm = 8.0;

        // Ревизия rev.10.1 (22.09.2026, spec 2026-09-22-fase-g-graphics-design.md §9,
        // эталон «Кабель с двух сторон.pdf»): подходы и символы. Подход кабеля i —
        // вертикаль за краем ряда (по оси выноса) с шагом 8 мм; шины заканчиваются
        // на подходе. Колонка символов — за последним подходом двусторонних кабелей.
        public const double CableApproachOffsetMm = 10.0;
        public const double CableApproachPitchMm = 8.0;
        public const double CableSymbolColumnOffsetMm = 16.0;
        // Зазор линия–круг 7 мм от ЦЕНТРА (требование пользователя 22.09.2026).
        // rev.10.4: центр круга CABDCP2 = точке вставки — офсет (+8.00;−14.245) из
        // замера п.47 опровергнут прогоном rev.10.3 (круг ушёл 1:1 с Location;
        // замер был загрязнён останцами старых прогонов — аддин неидемпотентен).
        public const double CableSymbolGapMm = 7.0;
        // Шаг стопки символов по вертикали — только двусторонние кабели (rev.10.5; из эталона rev.10.0).
        public const double CableSymbolStackPitchMm = 16.0;

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
        // индекс 1 дал вариант B); вариант A = 0; в UI позже.
        public const string SymbolLibrary = "SPECIAL";
        public const string SymbolName = "CABDCP2";
        public const int SymbolVariant = 0;

        // Точка вставки отчёта на странице.
        public const double InsertX = 20.0;
        public const double InsertY = 20.0;
    }
}

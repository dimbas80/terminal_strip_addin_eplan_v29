using System;

namespace MyEplanActions
{
    /// <summary>Чистый модуль арифметики рамки-призрака (Этап 8, H-5/H-6, rev.15.0;
    /// spec 2026-09-25-fase-h-ui-design.md §6): ориентация призрака и его размеры
    /// в мм. БЕЗ EPLAN-типов — компилируется в консольный тест-раннер
    /// (tests/build_tests.bat, ловушка п.66: кейсы в build_tests.bat И Program.Main).
    /// Решение пользователя 29.09.2026: явный выбор ориентации в диалоге
    /// (SettingsOrientation) приоритетнее формы; только Auto → парсинг имени формы.
    /// Имя формы НЕ триммится (хвостовой пробел значим — урок п.33): подсказки
    /// ищутся подстрокой (Contains). Дефолт при любых промахах — Horizontal.
    /// rev.15.5 (решение пользователя 30.09.2026): ResolveAnchorSpans — пролёты
    /// якоря рамки от курсора: курсор интеракции = ЛЕВЫЙ ВЕРХНИЙ угол (+X вправо,
    /// −Y вниз; в EPLAN Y растёт вверх). Ориентация НЕ параметр — вызыватель
    /// (GhostFrameCreator) резолвит dX/dY из ориентации до вызова.
    /// rev.15.5 (решение пользователя 30.09.2026, размер из .f11):
    /// ComputeFromTemplateMetrics — ALONG (вдоль направления данных шаблона) =
    /// шапка + N_строк×колонка_данных + футер + GhostCableGapMm (зазор для
    /// символов кабелей); ACROSS = размах шаблонных блоков поперёк данных
    /// (высота шаблона; в эталоне Y-размах O128 = 180).
    /// X3-факт эталона example/Клемник_ОУ(горизонтально)_addin.f11:
    /// 29 + 20×7 + 1.5 + 100 = 270.5 × 180. Метрики отдаёт
    /// EmbeddedReportReader.TryGetFormTemplateMetrics (парсинг файла формы).</summary>
    public static class GhostFrameMath
    {
        // Подсказки ориентации в имени формы. Поиск — OrdinalIgnoreCase.
        // Скобки НЕ требуются: допускаются и «голые» слова, справедливы и русские,
        // и английские подсказки (см. тесты (O-в)..(O-и)).
        private static readonly string[] _arrVerticalHints = new string[]
        {
            "вертикально", "vertical"
        };
        private static readonly string[] _arrHorizontalHints = new string[]
        {
            "горизонтально", "horizontal"
        };

        /// <summary>Ориентация рамки-призрака: явный выбор пользователя (eMode)
        /// приоритетнее формы; Auto → поиск подсказок в имени формы
        /// (OrdinalIgnoreCase, Contains; имя НЕ триммится — урок п.33).
        /// Если найдены ОБЕ подсказки — Vertical побеждает (детерминировано:
        /// горизонтальная проверка пропускается при найденной вертикальной).
        /// Ничего не найдено (в т.ч. null/пустое имя) → Horizontal (дефолт).</summary>
        public static ReportOrientation ResolveOrientation(SettingsOrientation eMode, string strFormName)
        {
            // 1. Явный выбор в диалоге — подсказки формы не читаются вовсе.
            if (eMode == SettingsOrientation.Horizontal) return ReportOrientation.Horizontal;
            if (eMode == SettingsOrientation.Vertical) return ReportOrientation.Vertical;

            // 2. Auto: имена подсказок могут быть null/пустыми — нормальный вход
            //    (дефолт ниже); Contains по OrdinalIgnoreCase.
            string strName = strFormName ?? string.Empty;
            bool bVerticalFound = false;
            for (int i = 0; i < _arrVerticalHints.Length; i++)
            {
                if (strName.IndexOf(_arrVerticalHints[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bVerticalFound = true;   // обе — Vertical побеждает: дальше не ищем H
                    break;
                }
            }
            if (bVerticalFound) return ReportOrientation.Vertical;

            for (int i = 0; i < _arrHorizontalHints.Length; i++)
            {
                if (strName.IndexOf(_arrHorizontalHints[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ReportOrientation.Horizontal;
                }
            }

            // 3. Ничего не найдено — дефолт Horizontal.
            return ReportOrientation.Horizontal;
        }

        /// <summary>Длинная сторона призрака (мм): nTerminals × dPitchMm;
        /// nTerminals < 1 → 1 × dPitchMm (крайние случаи без исключений).</summary>
        public static double ComputeWidthMm(int nTerminals, double dPitchMm)
        {
            if (nTerminals < 1) nTerminals = 1;
            return nTerminals * dPitchMm;
        }

        /// <summary>Короткая сторона призрака (мм): 2 × AddInConfiguration.
        /// SymbolFallbackSizeMm (14.0) = 28 мм. AddInConfiguration компилируется
        /// в тест-раннер (см. tests/build_tests.bat) — прямой доступ.</summary>
        public static double ComputeHeightMm()
        {
            return 2.0 * AddInConfiguration.SymbolFallbackSizeMm;
        }

        /// <summary>Якорные пролёты рамки-призрака от курсора (rev.15.5, решение
        /// пользователя 30.09.2026): курсор интеракции = ЛЕВЫЙ ВЕРХНИЙ угол рамки
        /// (был левый нижний). Локальная система призрака: курсор = (0,0)
        /// (SetStaticCursor); в EPLAN Y растёт вверх, поэтому «вниз от курсора» =
        /// отрицательный Y: dSpanX = +dSizeXMm (рамка тянется вправо),
        /// dSpanY = −dSizeYMm (вниз). Ориентация НЕ параметр — GhostFrameCreator
        /// резолвит dX/dY из ориентации ДО вызова. Чистые присваивания — без
        /// исключений при любых входах (0/отрицательные проходят как есть).</summary>
        public static void ResolveAnchorSpans(double dSizeXMm, double dSizeYMm,
            out double dSpanX, out double dSpanY)
        {
            dSpanX = dSizeXMm;
            dSpanY = -dSizeYMm;
        }

        /// <summary>Размер призрака из метрик шаблона формы (rev.15.5, решение
        /// пользователя 30.09.2026): ALONG = dHeaderMm + N_строк×dDataColMm +
        /// dFooterMm + dGapMm (AddInConfiguration.GhostCableGapMm — зазор ПОСЛЕ
        /// футера вдоль направления данных для символов кабелей); ACROSS =
        /// dTotalAcrossMm (высота шаблонных блоков). nRows &lt; 1 → 0 строк
        /// (БЕЗ клампа к 1 — в отличие от ComputeWidthMm: пустой клеммник даёт
        /// призрак шапка+футер+зазор, а не одну фиктивную строку). Без
        /// исключений при любых входах. X3-факт: 29 + 20×7 + 1.5 + 100 =
        /// 270.5 × 180. Вызыватель (AnalyzeAction 3b) маппит ALONG/ACROSS в
        /// dLong/dShort призрака (dLong=ALONG, dShort=ACROSS — итог dX=ALONG
        /// для Horizontal, dX=ACROSS для Vertical).</summary>
        public static void ComputeFromTemplateMetrics(double dHeaderMm,
            double dDataColMm, double dFooterMm, double dTotalAcrossMm,
            int nRows, double dGapMm, out double dAlongMm, out double dAcrossMm)
        {
            int nRowsSafe = nRows < 1 ? 0 : nRows;
            dAlongMm = dHeaderMm + nRowsSafe * dDataColMm + dFooterMm + dGapMm;
            dAcrossMm = dTotalAcrossMm;
        }
    }
}

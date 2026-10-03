using System;
using System.Collections.Generic;

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
    /// EmbeddedReportReader.TryGetFormTemplateMetrics (парсинг файла формы).
    /// rev.16.6 (замер прогона 14:19, клеммник =++ШОБ+#-XT1.1, форма
    /// Клемник_ОУ(горизонтально)_addin, курсор (22;−136), Horizontal): призрак
    /// печатался 683.5 × 180.0 (шапка 29 + 79 строк × 7 + футер 1.5 + зазор 100),
    /// а графика из [GEOM] заняла Y −339.375…−100.5 = 238.875 мм — то есть ACROSS
    /// шаблона (180) шины НЕ вмещает, графика вылезает за рамку. Причина: шины
    /// раскладываются по уровням с шагом 8 мм (CableGeometryBuilder.cs:206-207 →
    /// теперь ComputeLevelPitchMm), верх = Int, низ = Ext
    /// (TerminalConnectionModelBuilder.cs:186-194), а про шины в высоте блока
    /// формы ничего нет. Решение заказчика: ACROSS = высота блока формы + разнос
    /// шин с ОБЕИХ сторон (консервативно — вертикальные поля формы не вычитаются),
    /// зазор ALONG — от числа кабелей, но не меньше GhostCableGapMm. Отсюда
    /// CountBusLevels / ComputeBusSpreadMm / ComputeCableAlongGapMm: вход — строки
    /// [DM] (уже прочитанный DmReport), выход — четыре числа и две оценки в мм.</summary>
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

        /// <summary>rev.16.6: сколько УРОВНЕЙ ШИН наберётся по строкам [DM]
        /// целевого клеммника — четыре числа из уже прочитанного DmReport (второго
        /// Read НЕ делается: отчёт читается ради ConnCount = nRows).
        /// ВЕРХ = Side "Int", НИЗ = "Ext" — правило стороны
        /// TerminalConnectionModelBuilder.cs:186-194 (Top/Right ↔ Int,
        /// Bottom/Left ↔ Ext), в терминах шин CableGeometryBuilder.cs:261-266 это
        /// nRankRight (верх) и nRankLeft (низ). Считаются РАЗНЫЕ КАБЕЛИ, а не
        /// строки: ключ кабеля = ConnectionName ?? CableName ?? "" — тот же, что
        /// в CableLayoutBuilder.cs:34, иначе в одну группу слиплись бы чужие кабели
        /// проекта (на стенде 03.10 — 19 клеммников, 504 клемы).
        /// Фильтр строки: целевой клеммник по Ordinal (как
        /// TerminalConnectionModelBuilder.cs:83), Side != "Bridge" (перемычка не
        /// кабель) и MatchBuilder.IsCableRow (строка без CableName и без №31058 —
        /// внутреннее соединение, на стенде 02.10 их 420 из 1398).
        /// nCables = |верх ∪ низ| — инвариант nRight + nLeft − nBilateral (его и
        /// держит кейс (B-б)); nBilateral = |верх ∩ ниж| — по нему выбирается
        /// ветка «двусторонних нет» при оценке зазора ALONG.
        /// Любая другая строка Side игнорируется (в данных только Ext/Int/Bridge).
        /// Отказ на пустых входах (oDm == null, Rows == null, пустое имя
        /// клеммника) — ВСЕ out = 0, БЕЗ исключений: шаг 3bAnalyzeAction на этом
        /// не падает и сохраняет прежний размер призрака.</summary>
        public static void CountBusLevels(DmReport oDm, string strTargetStrip,
            out int nRight, out int nLeft, out int nBilateral, out int nCables)
        {
            nRight = 0;
            nLeft = 0;
            nBilateral = 0;
            nCables = 0;
            if (oDm == null || oDm.Rows == null || string.IsNullOrEmpty(strTargetStrip))
                return;

            HashSet<string> setTop = new HashSet<string>();
            HashSet<string> setBottom = new HashSet<string>();
            for (int i = 0; i < oDm.Rows.Count; i++)
            {
                DmRow oRow = oDm.Rows[i];
                if (oRow == null) continue;
                if (!string.Equals(oRow.StripName, strTargetStrip, StringComparison.Ordinal))
                    continue;
                if (oRow.Side == "Bridge") continue;
                if (!MatchBuilder.IsCableRow(oRow)) continue;
                string strKey = oRow.ConnectionName ?? oRow.CableName ?? string.Empty;
                if (oRow.Side == "Int") setTop.Add(strKey);
                else if (oRow.Side == "Ext") setBottom.Add(strKey);
            }

            nRight = setTop.Count;
            nLeft = setBottom.Count;
            int nBoth = 0;
            foreach (string strKey in setTop)
                if (setBottom.Contains(strKey)) nBoth++;
            nBilateral = nBoth;
            nCables = nRight + nLeft - nBilateral;
        }

        /// <summary>rev.16.6: разнос шин ОДНОЙ стороны по вертикали (мм) —
        /// столько призрак обязан добавить к ACROSS сверху (Int) или снизу (Ext).
        /// Формула повторяет раскладку CableGeometryBuilder.cs:261-266: уровень
        /// nRank = BusOffset + BusLift + nRank·LevelPitch, поэтому весь блок от
        /// края ряда до последнего уровня плюс ПОЛОВИНА габарита символа (символ
        /// стоит на уровне шины и наружу вылезает на B/2 — иначе рамка срежет
        /// верхушку символа). nLevels &lt; 1 → 0 (кабелей с этой стороны нет).
        /// Нулевые/отрицательные входы идут как есть, без исключений.
        /// Стенд 14:19: 9 уровней (Int) → 10+8+8×8+7 = 89, 8 уровней (Ext) →
        /// 10+8+7×8+7 = 81; обе стороны суммируются ⇒ ACROSS 180 → 350.</summary>
        public static double ComputeBusSpreadMm(int nLevels, double dBusOffsetMm,
            double dBusLiftMm, double dLevelPitchMm, double dSymbolHalfMm)
        {
            if (nLevels < 1) return 0.0;
            return dBusOffsetMm + dBusLiftMm + (nLevels - 1) * dLevelPitchMm + dSymbolHalfMm;
        }

        /// <summary>rev.16.6: оценка зазора ALONG (мм) по числу кабелей — резерв
        /// ПОСЛЕ футера для символов/линий кабелей. Считается ВЕРХНЕЙ ГРАНИЦЕЙ
        /// (последний двусторонний = индекс nCables−1): порядок кабелей из строк
        /// [DM] не восстанавливается намеренно (это оценка, дальше вызывающий
        /// берёт max() с GhostCableGapMm = 100 мм), поэтому недооценить нельзя.
        /// Состав — от последнего подхода к стрелке: отступ подхода + шаг подходов
        /// × индекс + отступ колонки символа + половина символа + опорная линия +
        /// стрелка (те же слагаемые, что в dLastSigned/symX ревизии 16.5-бис-ж).
        /// nBilateral &lt; 1 → только отступ подхода: ветка rev16.5-бис-ж
        /// «двусторонних нет» — подходы не рисуются, символ встаёт у ряда, и весь
        /// остаток резерва был бы пустым местом. nCables &lt; 1 → 0.
        /// Стенд 14:19: 10 кабелей / 8 двусторонних → 10+9×8+16+7+20+7 = 132;
        /// вызывающий берёт max(100, 132) = 132 ⇒ ALONG 683.5 → 715.5.</summary>
        public static double ComputeCableAlongGapMm(int nCables, int nBilateral,
            double dApproachOffsetMm, double dApproachPitchMm, double dColumnOffsetMm,
            double dSymbolHalfMm, double dRefLineMm, double dArrowMm)
        {
            if (nCables < 1) return 0.0;
            if (nBilateral < 1) return dApproachOffsetMm;
            return dApproachOffsetMm + dApproachPitchMm * (nCables - 1) +
                dColumnOffsetMm + dSymbolHalfMm + dRefLineMm + dArrowMm;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace MyEplanActions
{
    /// <summary>Дескриптор строки формы — PlaceHolderText (rev.5.3): текст + позиция.
    /// Якорь соответствия «номер клеммы ↔ колонка»: порядковое сопоставление колонок
    /// и клемм неверно, когда нумерация клеммника не совпадает с раскладкой по X
    /// (урок rev.5.2: мосты 7-8/8-9 стоят под перемычкой колонок 8/9/10).
    /// rev.12.2 (H-3): перенесён из Data/MatchBuilder.cs — AnchorResolver компилируется
    /// в чистый тест-раннер tests/ (MatchBuilder тянет EPLAN-типы DmRow/DmReport);
    /// сам класс чистый: Pt (LeadGeometry) + строки. Поле SourceTerminalName — ""
    /// если данных нет (rev.6.1).</summary>
    public sealed class PhRow
    {
        public Pt Location;
        public string Text;
        public string SourceTerminalName; // rev.6.1: полное имя Terminal-источника (SourceObject) или "" — запасной якорь
    }

    /// <summary>Якорь соответствия «номер клеммы ↔ колонка»: номер, распознанный из
    /// текста дескриптора, и его позиция. Порядковое сопоставление колонок и клемм
    /// неверно, когда нумерация клеммника не совпадает с раскладкой (урок rev.5.2:
    /// мосты 7-8/8-9 стоят под перемычкой колонок 8/9/10).</summary>
    public sealed class TerminalAnchor
    {
        public int Number;            // номер клеммы (суффикс после ':')
        public double Pos;            // позиция ВДОЛЬ колонок: X (Horizontal) / Y (Vertical)
        public double RowCoord;       // координата ряда: Y (Horizontal) / X (Vertical)
        public string Text;           // исходный текст дескриптора
        public bool FromSourceObject; // Задача 3: якорь из SourceObject, не из текста
    }

    /// <summary>Результат разбора дескрипторов формы: доминирующий ряд + якоря ряда.
    /// Коллизии/дальние якоря здесь НЕ считаются — для них нужна геометрия К4,
    /// их считает MatchBuilder (раздел 3 свода).</summary>
    public sealed class AnchorMap
    {
        public ReportOrientation Orientation;
        public bool HasRow;                    // доминирующий ряд найден (>=2 числовых дескрипторов)
        public double RowCoord;                // NaN если !HasRow
        public List<TerminalAnchor> Anchors;   // только ряд; коллизии/дальние — считает MatchBuilder
        public Dictionary<int, string> NumberToTerminalKey; // номер -> полное имя клеммы из DM (Задача 3)
    }

    /// <summary>Якорная логика (Этап 3): поиск доминирующего ряда номеров формы и
    /// разбор номеров клемм из имён/текстов. rev.6.0 — чистый перенос из MatchBuilder
    /// без изменения поведения; rev.6.1 — NumberToTerminalKey (номер -> имя клеммы
    /// из DM) и запасной якорь [PHFB] из SourceObject=Terminal. От геометрии К4 не
    /// зависит — работает только с дескрипторами PhRow. Проверки «дальше
    /// шага»/«конфликт» [PHCOL] остаются в MatchBuilder: им нужны колонки К4.
    /// rev.12.2 (H-3): DetectOrientation — авто-детекция ориентации отчёта по
    /// раскладке числовых дескрипторов (спека §5); чистый PhRow перенесён сюда.</summary>
    public static class AnchorResolver
    {
        /// <summary>Разбор дескрипторов формы: (а) доминирующий ряд номеров (бакет
        /// 0.1 мм, порог >=2 числовых дескрипторов), (б) якоря ряда — номер из текста +
        /// позиция вдоль колонок; rev.6.1 (Задача 3): если текст ряда не парсится как
        /// номер, а SourceTerminalName (SourceObject → Terminal) парсится — запасной
        /// якорь [PHFB] с FromSourceObject=true; (в) NumberToTerminalKey — номер ->
        /// полное имя клеммы целевого клеммника (по [KEYDUP] — первое имя).
        /// rev.6.2 (Задача 4): ориентация приходит из конфигурации — Horizontal —
        /// доминирующий ряд по Y (Pos = X, RowCoord = Y — как в MatchBuilder
        /// rev.5.6), Vertical — доминирующий столбец по X (Pos = Y, RowCoord = X).
        /// Строка [ANCHOR] печатается здесь с формулировкой «ряд»/«столбец» по оси.
        /// Проверки «дальше шага»/«конфликт» [PHCOL] остаются в MatchBuilder: им нужны
        /// колонки К4.</summary>
        public static AnchorMap Build(List<PhRow> phRows, ReportOrientation orientation,
            Dictionary<string, List<string>> stripTerminalNames, string targetStripName,
            DiagnosticLogger log)
        {
            AnchorMap oMap = new AnchorMap();
            oMap.Orientation = orientation;
            oMap.Anchors = new List<TerminalAnchor>();
            oMap.NumberToTerminalKey = BuildNumberToKey(stripTerminalNames, targetStripName, log);

            bool bVertical = orientation == ReportOrientation.Vertical;

            // Доминирующий ряд номеров (rev.5.6): якорим только ряд с максимумом
            // числовых дескрипторов. Прогон rev.5.5: без фильтра числоподобные тексты
            // прочих строк (этажи Y=-51, уровни Y=-91, позиции «-XP1:10», «-SF1:2»)
            // дали 106 коллизий [PHCOL] и паразитный якорь на виртуальной колонке 66.85.
            double dAnchorRowCoord = FindDominantAnchorRow(phRows, bVertical);
            oMap.HasRow = !double.IsNaN(dAnchorRowCoord);
            oMap.RowCoord = oMap.HasRow ? dAnchorRowCoord : double.NaN;
            long nAnchorRowKey = oMap.HasRow ? (long)Math.Round(dAnchorRowCoord * 10.0) : long.MinValue;
            // rev.6.2: формулировка по оси — Vertical — «доминирующий столбец» по X,
            // Horizontal — прежняя строка rev.6.1 без изменений.
            if (oMap.HasRow)
                log.Log("[INFO] [ANCHOR] " +
                    (bVertical ? "доминирующий столбец номеров формы: X=" :
                                 "доминирующий ряд номеров формы: Y=") +
                    dAnchorRowCoord.ToString("F1", CultureInfo.InvariantCulture) + " — якорим только его");

            foreach (PhRow oPh in phRows)
            {
                if (oPh == null) continue;
                // rev.6.2 (отложенный Minor Задачи 1): NaN-guard по оси Pos —
                // Horizontal — X (как в rev.6.1), Vertical — Y.
                if (double.IsNaN(bVertical ? oPh.Location.Y : oPh.Location.X)) continue;
                double dRowCoord = bVertical ? oPh.Location.X : oPh.Location.Y;
                if (oMap.HasRow && (long)Math.Round(dRowCoord * 10.0) != nAnchorRowKey) continue;
                int nNumber = ParseTerminalNumber(oPh.Text);
                bool bFromText = nNumber >= 0;
                if (!bFromText)
                {
                    // rev.6.1: текста-номера нет — запасной якорь из имени клеммы-
                    // источника (SourceObject → Terminal). Нет номера и там — не якорь.
                    nNumber = ParseTerminalNumber(oPh.SourceTerminalName);
                    if (nNumber < 0) continue;
                    // Уровень INFO (решение контроллера, 20.09.2026): сработавший
                    // запасной путь — штатный механизм, а не аномалия; на форме без
                    // ряда номеров через [PHFB] пойдут ВСЕ якоря — они не должны
                    // раздувать WARN-бюджет прогона.
                    log.Log("[INFO] [PHFB] дескриптор '" + oPh.Text + "' (@" +
                        oPh.Location.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                        oPh.Location.Y.ToString("F3", CultureInfo.InvariantCulture) +
                        "): текста-номера нет, якорь из SourceObject '" + oPh.SourceTerminalName +
                        "' → №" + nNumber);
                }
                TerminalAnchor oAnchor = new TerminalAnchor();
                oAnchor.Number = nNumber;
                oAnchor.Pos = bVertical ? oPh.Location.Y : oPh.Location.X;
                oAnchor.RowCoord = dRowCoord;
                oAnchor.Text = oPh.Text;
                oAnchor.FromSourceObject = !bFromText;
                oMap.Anchors.Add(oAnchor);
            }
            return oMap;
        }

        /// <summary>Число клеммы из полного имени: суффикс после последнего ':' как
        /// число, -1 если не число. Публичный: используется чтением якорей [PH]
        /// (AnalyzeAction.ResolvePlaceholderText, rev.5.5).</summary>
        public static int ParseTerminalNumber(string strName)
        {
            int nNumber;
            if (int.TryParse(SuffixAfterColon(strName).Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out nNumber)) return nNumber;
            return -1;
        }

        /// <summary>H-3 (rev.12.2, спека §5): авто-детекция ориентации отчёта по
        /// раскладке числовых дескрипторов (те же, что якорят ряд: текст парсится
        /// ParseTerminalNumber). Бакеты 0.1 мм по обеим осям — как доминирующий ряд
        /// (FindDominantAnchorRow); nRow = максимум по Y-бакетам, nCol = максимум по
        /// X-бакетам. Порог ≥2 по лучшей оси (спека §5 п.1; фикс-волна MINOR-3):
        /// max(nRow,nCol) &lt; 2 — обе оси неубедительны → null (иначе одиночный
        /// дескриптор с NaN-координатой по одной оси давал ложный Horizontal 1&gt;0).
        /// nRow &gt; nCol — Horizontal (ряд номеров вдоль X), nCol &gt; nRow —
        /// Vertical (номера в столбец), равенство (в т.ч. оба 0 — числовых нет) —
        /// null: вызывающий идёт на bbox-fallback. NaN-координаты пропускаются по
        /// каждой оси отдельно (урок rev.12.1: дескриптор с NaN в локации).
        /// strEvidence — всегда обе стороны: «числовых дескрипторов: ряд(Y) максимум
        /// N на Y=..., столбец(X) максимум M на X=...» (без «на ...» при нулевом
        /// максимуме — координаты нет).</summary>
        public static ReportOrientation? DetectOrientation(List<PhRow> lstPh, out string strEvidence)
        {
            Dictionary<long, int> dicYCount = new Dictionary<long, int>();
            Dictionary<long, double> dicYCoord = new Dictionary<long, double>();
            Dictionary<long, int> dicXCount = new Dictionary<long, int>();
            Dictionary<long, double> dicXCoord = new Dictionary<long, double>();
            if (lstPh != null)
            {
                foreach (PhRow oPh in lstPh)
                {
                    if (oPh == null) continue;
                    if (ParseTerminalNumber(oPh.Text) < 0) continue;
                    if (!double.IsNaN(oPh.Location.Y))
                    {
                        long nKeyY = (long)Math.Round(oPh.Location.Y * 10.0);
                        if (!dicYCount.ContainsKey(nKeyY))
                        {
                            dicYCount[nKeyY] = 0;
                            dicYCoord[nKeyY] = oPh.Location.Y;
                        }
                        dicYCount[nKeyY]++;
                    }
                    if (!double.IsNaN(oPh.Location.X))
                    {
                        long nKeyX = (long)Math.Round(oPh.Location.X * 10.0);
                        if (!dicXCount.ContainsKey(nKeyX))
                        {
                            dicXCount[nKeyX] = 0;
                            dicXCoord[nKeyX] = oPh.Location.X;
                        }
                        dicXCount[nKeyX]++;
                    }
                }
            }
            int nRow = 0, nCol = 0;
            double dRowY = double.NaN, dColX = double.NaN;
            foreach (KeyValuePair<long, int> oPair in dicYCount)
                if (oPair.Value > nRow) { nRow = oPair.Value; dRowY = dicYCoord[oPair.Key]; }
            foreach (KeyValuePair<long, int> oPair in dicXCount)
                if (oPair.Value > nCol) { nCol = oPair.Value; dColX = dicXCoord[oPair.Key]; }
            strEvidence = "числовых дескрипторов: ряд(Y) максимум " + nRow.ToString(CultureInfo.InvariantCulture) +
                (nRow > 0 ? " на Y=" + dRowY.ToString("F1", CultureInfo.InvariantCulture) : "") +
                ", столбец(X) максимум " + nCol.ToString(CultureInfo.InvariantCulture) +
                (nCol > 0 ? " на X=" + dColX.ToString("F1", CultureInfo.InvariantCulture) : "");
            // Порог ≥2 по лучшей оси (спека §5 п.1, фикс-волна MINOR-3): один
            // числовой дескриптор — ни ряд, ни столбец (тот же порог, что
            // FindDominantAnchorRow: nBest старует с 1). Без него одиночный
            // дескриптор с NaN по одной оси давал ложный Horizontal (1 > 0).
            if (nRow < 2 && nCol < 2)
                return null; // обе оси неубедительны → bbox-fallback у вызывающего
            if (nRow > nCol) return ReportOrientation.Horizontal;
            if (nCol > nRow) return ReportOrientation.Vertical;
            return null; // ничья (в т.ч. 0/0 — числовых дескрипторов нет)
        }

        /// <summary>Координата доминирующего ряда номеров формы (rev.5.6): бакет
        /// 0.1 мм по ряду с максимумом дескрипторов, чей текст парсится как номер
        /// клеммы; NaN — числовых дескрипторов меньше 2 (одиночное число рядом не
        /// «ряд»). Прогон rev.5.5: у тестовой формы ряд «1…60» на Y=-81 (60 шт.),
        /// прочие строки — этажи/уровни/позиции (-XP1:10, -SF1:2), давшие 106
        /// коллизий [PHCOL]. Этап 3: ось ряда выбирается ориентацией (Vertical —
        /// бакет по X, Задача 4).</summary>
        private static double FindDominantAnchorRow(List<PhRow> lstPh, bool bVertical)
        {
            Dictionary<long, int> dicCount = new Dictionary<long, int>();
            Dictionary<long, double> dicY = new Dictionary<long, double>();
            foreach (PhRow oPh in lstPh)
            {
                if (oPh == null) continue;
                // rev.6.2: тот же NaN-guard по оси Pos, что в главном цикле Build
                // (Horizontal — X, как в rev.6.1; Vertical — Y): дескриптор с NaN
                // на оси не может ни якорить ряд, ни дать корректную позицию.
                if (double.IsNaN(bVertical ? oPh.Location.Y : oPh.Location.X)) continue;
                if (ParseTerminalNumber(oPh.Text) < 0) continue;
                double dRowCoord = bVertical ? oPh.Location.X : oPh.Location.Y;
                long nKey = (long)Math.Round(dRowCoord * 10.0);
                if (!dicCount.ContainsKey(nKey))
                {
                    dicCount[nKey] = 0;
                    dicY[nKey] = dRowCoord;
                }
                dicCount[nKey]++;
            }
            int nBest = 1; // порог: ряд — минимум 2 числовых дескриптора
            double dBestY = double.NaN;
            foreach (KeyValuePair<long, int> oPair in dicCount)
            {
                if (oPair.Value > nBest)
                {
                    nBest = oPair.Value;
                    dBestY = dicY[oPair.Key];
                }
            }
            return dBestY;
        }

        /// <summary>Номер -> полное имя клеммы целевого клеммника (rev.6.1, Задача 3):
        /// источник — список имён клемм из DM (stripTerminalNames[targetStripName]),
        /// номер — суффикс после ':' имени. Дубль номера — берётся первое имя
        /// ([KEYDUP]); клеммник не найден — пустой словарь, без падения.</summary>
        private static Dictionary<int, string> BuildNumberToKey(
            Dictionary<string, List<string>> dicStripNames, string strTargetStrip,
            DiagnosticLogger log)
        {
            Dictionary<int, string> dicKey = new Dictionary<int, string>();
            List<string> lstNames;
            if (dicStripNames == null || string.IsNullOrEmpty(strTargetStrip) ||
                !dicStripNames.TryGetValue(strTargetStrip, out lstNames))
            {
                return dicKey;
            }
            foreach (string strName in lstNames)
            {
                int nNumber = ParseTerminalNumber(strName);
                if (nNumber < 0) continue;
                string strPrev;
                if (dicKey.TryGetValue(nNumber, out strPrev))
                {
                    log.Warn("[KEYDUP] номер " + nNumber + ": два имени '" + strPrev +
                        "' и '" + strName + "' — взят первый");
                    continue;
                }
                dicKey[nNumber] = strName;
            }
            return dicKey;
        }

        /// <summary>Суффикс имени после последнего ':' (номер клеммы в полном имени
        /// «=HII-1.1++ЯЧ67+#2-X2:1»); если ':' нет — имя целиком.</summary>
        private static string SuffixAfterColon(string strName)
        {
            if (strName == null) return "";
            int nPos = strName.LastIndexOf(':');
            return nPos >= 0 ? strName.Substring(nPos + 1) : strName;
        }
    }
}

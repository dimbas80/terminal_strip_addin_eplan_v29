using System;
using System.Collections.Generic;
using System.Globalization;

namespace MyEplanActions
{
    /// <summary>
    /// Детектор выводов К1–К4 — побайтный порт rev.13 из spike/TerminalStripReportSpike.cs
    /// (AnalyzeLineComponents / DetectLeads / CheckK4), переведённый на чистые типы
    /// Pt/Seg без EPLAN-типов (план: plan_stage2.md, Задача 3).
    ///
    /// Критерии (summary.md, подтверждены 20.09.2026):
    ///   К1: вывод = связная компонента-цепь на слое выводов (внутренние точки степени 2,
    ///       ровно 2 листа); слияние по совпадающим концам с допуском 0.001 мм.
    ///   К2: неколлинеарная компонента-цепь = мост (2 вывода одной клеммы).
    ///   К3': точка подключения одиночного вывода = лист, дальний от ближайшего
    ///       маркера (стуб ∪ перемычка); у моста — оба листа.
    ///   К4: привязка точек к колонкам клемм (стубы + виртуальные под перемычками,
    ///       шаг = нижняя медиана шага стубов); WARN — сироты и >2 точек на клемму.
    /// Длины отрезков в критериях НЕ используются. Круги-«точки» (Arc) поверх стубов
    /// не используются (summary п.18) — сюда они не попадают (детектор получает только Line).
    /// </summary>
    public static class LeadDetector
    {
        private static bool IsLayer(Seg oSeg, string strExpected)
        {
            return oSeg.LayerName != null &&
                string.Equals(oSeg.LayerName, strExpected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>rev.11: разбор линий на компоненты связности по совпадающим концам.
        /// НИКАКИХ привязок к длинам — только топология и ориентации. Для каждой компоненты:
        /// число сегментов, ориентации, коллинеарность, макс. степень вершины, листья
        /// (точки степени 1 — кандидаты в точку подключения кабеля), bbox, длина, слои.
        /// Логи [COMP] сохранены как в rev.13.</summary>
        public static List<LineComponent> AnalyzeLineComponents(List<Seg> lstLines, DiagnosticLogger log)
        {
            List<LineComponent> lstComponents = new List<LineComponent>();
            try
            {
                int nCount = lstLines.Count;
                if (nCount == 0) return lstComponents;

                // DSU: объединяем линии, имеющие общую точку-конец.
                int[] arrParent = new int[nCount];
                for (int i = 0; i < nCount; i++) arrParent[i] = i;
                Func<int, int> find = delegate(int x)
                {
                    while (arrParent[x] != x)
                    {
                        arrParent[x] = arrParent[arrParent[x]];
                        x = arrParent[x];
                    }
                    return x;
                };

                Dictionary<string, int> dicEndpointOwner = new Dictionary<string, int>();
                Dictionary<string, Pt> dicPointCoord = new Dictionary<string, Pt>();
                for (int i = 0; i < nCount; i++)
                {
                    string strStart = LeadGeometry.PointKey(lstLines[i].A);
                    string strEnd = LeadGeometry.PointKey(lstLines[i].B);
                    dicPointCoord[strStart] = lstLines[i].A;
                    dicPointCoord[strEnd] = lstLines[i].B;

                    int nOwner;
                    if (dicEndpointOwner.TryGetValue(strStart, out nOwner))
                    {
                        int nA = find(i), nB = find(nOwner);
                        if (nA != nB) arrParent[nB] = nA;
                    }
                    else dicEndpointOwner[strStart] = i;
                    if (dicEndpointOwner.TryGetValue(strEnd, out nOwner))
                    {
                        int nA = find(i), nB = find(nOwner);
                        if (nA != nB) arrParent[nB] = nA;
                    }
                    else dicEndpointOwner[strEnd] = i;
                }

                Dictionary<int, List<int>> dicComponents = new Dictionary<int, List<int>>();
                for (int i = 0; i < nCount; i++)
                {
                    int nRoot = find(i);
                    if (!dicComponents.ContainsKey(nRoot)) dicComponents[nRoot] = new List<int>();
                    dicComponents[nRoot].Add(i);
                }
                log.Log("[INFO] Компонентов связности: " + dicComponents.Count + " (линий: " + nCount + ")");

                int nCompIndex = 0;
                foreach (KeyValuePair<int, List<int>> oEntry in dicComponents)
                {
                    nCompIndex++;
                    List<int> lstIdx = oEntry.Value;
                    LineComponent oComponent = new LineComponent();
                    lstComponents.Add(oComponent);

                    int nHorizontal = 0, nVertical = 0, nAngled = 0, nDegenerate = 0;
                    double dTotalLen = 0;
                    double dMinX = double.MaxValue, dMaxX = double.MinValue;
                    double dMinY = double.MaxValue, dMaxY = double.MinValue;
                    Dictionary<string, int> dicDegree = new Dictionary<string, int>();
                    Dictionary<string, int> dicCompLayers = new Dictionary<string, int>();
                    List<string> lstSegDesc = new List<string>();

                    foreach (int i in lstIdx)
                    {
                        Seg oSeg = lstLines[i];
                        oComponent.Segments.Add(oSeg);
                        Pt oStart = oSeg.A;
                        Pt oEnd = oSeg.B;
                        double dDx = oEnd.X - oStart.X;
                        double dDy = oEnd.Y - oStart.Y;
                        double dLen = Math.Sqrt(dDx * dDx + dDy * dDy);
                        dTotalLen += dLen;

                        double dAbsX = Math.Abs(dDx), dAbsY = Math.Abs(dDy);
                        string strOrient;
                        if (dAbsX <= 0.001 && dAbsY <= 0.001) { strOrient = "P"; nDegenerate++; }
                        else if (dAbsY <= 0.001) { strOrient = "H"; nHorizontal++; }
                        else if (dAbsX <= 0.001) { strOrient = "V"; nVertical++; }
                        else
                        {
                            double dAngle = Math.Atan2(dDy, dDx) * 180.0 / Math.PI;
                            if (dAngle < 0) dAngle += 180.0;
                            strOrient = "A" + Math.Round(dAngle, 1).ToString(CultureInfo.InvariantCulture);
                            nAngled++;
                        }
                        lstSegDesc.Add(strOrient + ":" + dLen.ToString("F3", CultureInfo.InvariantCulture));

                        string strLayerKey = oSeg.LayerName == null ? "<n/a>" : oSeg.LayerName;
                        if (!dicCompLayers.ContainsKey(strLayerKey)) dicCompLayers[strLayerKey] = 0;
                        dicCompLayers[strLayerKey]++;

                        if (oStart.X < dMinX) dMinX = oStart.X;
                        if (oStart.X > dMaxX) dMaxX = oStart.X;
                        if (oStart.Y < dMinY) dMinY = oStart.Y;
                        if (oStart.Y > dMaxY) dMaxY = oStart.Y;
                        if (oEnd.X < dMinX) dMinX = oEnd.X;
                        if (oEnd.X > dMaxX) dMaxX = oEnd.X;
                        if (oEnd.Y < dMinY) dMinY = oEnd.Y;
                        if (oEnd.Y > dMaxY) dMaxY = oEnd.Y;

                        string strStartKey = LeadGeometry.PointKey(oStart);
                        string strEndKey = LeadGeometry.PointKey(oEnd);
                        if (strStartKey == strEndKey)
                        {
                            // Вырожденный отрезок (start==end): степень точки +1, а не +2.
                            if (!dicDegree.ContainsKey(strStartKey)) dicDegree[strStartKey] = 0;
                            dicDegree[strStartKey]++;
                        }
                        else
                        {
                            if (!dicDegree.ContainsKey(strStartKey)) dicDegree[strStartKey] = 0;
                            if (!dicDegree.ContainsKey(strEndKey)) dicDegree[strEndKey] = 0;
                            dicDegree[strStartKey]++;
                            dicDegree[strEndKey]++;
                        }
                    }

                    int nMaxDegree = 0;
                    List<string> lstLeaves = new List<string>();
                    foreach (KeyValuePair<string, int> oDegree in dicDegree)
                    {
                        if (oDegree.Value > nMaxDegree) nMaxDegree = oDegree.Value;
                        if (oDegree.Value == 1)
                        {
                            Pt oLeaf;
                            if (dicPointCoord.TryGetValue(oDegree.Key, out oLeaf))
                                lstLeaves.Add("(" + oLeaf.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                                    oLeaf.Y.ToString("F3", CultureInfo.InvariantCulture) + ")");
                        }
                    }

                    bool bAllCollinear = nAngled == 0 && nDegenerate == 0 &&
                        ((nHorizontal == 0) || (nVertical == 0));

                    oComponent.AllCollinear = bAllCollinear;
                    oComponent.MaxDegree = nMaxDegree;
                    foreach (string strLeafKey in dicDegree.Keys)
                    {
                        if (dicDegree[strLeafKey] == 1)
                        {
                            Pt oLeaf;
                            if (dicPointCoord.TryGetValue(strLeafKey, out oLeaf))
                                oComponent.Leaves.Add(oLeaf);
                        }
                    }

                    List<string> lstLayerDesc = new List<string>();
                    foreach (KeyValuePair<string, int> oLayerPair in dicCompLayers)
                        lstLayerDesc.Add(oLayerPair.Key + "x" + oLayerPair.Value);

                    log.Log("[COMP] #" + nCompIndex + ": segs=" + lstIdx.Count +
                        " [H:" + nHorizontal + " V:" + nVertical + " A:" + nAngled + " P:" + nDegenerate + "]" +
                        " allCollinear=" + (bAllCollinear ? "yes" : "no") +
                        " maxDeg=" + nMaxDegree + " leaves=" + lstLeaves.Count +
                        " L=" + dTotalLen.ToString("F3", CultureInfo.InvariantCulture) +
                        " bbox=(" + dMinX.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                        dMinY.ToString("F3", CultureInfo.InvariantCulture) + ")-(" +
                        dMaxX.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                        dMaxY.ToString("F3", CultureInfo.InvariantCulture) + ")" +
                        " layers=[" + string.Join(" ", lstLayerDesc.ToArray()) + "]");
                    log.Log("[COMP]     segs: " + string.Join(" ", lstSegDesc.ToArray()));
                    if (lstLeaves.Count > 0)
                        log.Log("[COMP]     leaves: " + string.Join(" ", lstLeaves.ToArray()));
                }
                return lstComponents;
            }
            catch (Exception oException)
            {
                log.Warn("Разбор компонент не удался: " + oException.GetType().Name + ": " + oException.Message);
            }
            return lstComponents;
        }

        /// <summary>Выводы и точки подключения кабеля по решениям пользователя (20.09.2026).
        /// Компонента-цепь (maxDeg&lt;=2, ровно 2 листа) на слое выводов = вывод. Неколлинеарная
        /// компонента-цепь = мост: 2 вывода одной клеммы, соприкоснувшиеся в общей точке:
        /// ОБА её листа — точки подключения. У одиночного (коллинеарного) вывода точка
        /// подключения = внешний конец — лист, ДАЛЬНИЙ от ближайшего маркера клеммы
        /// (стуб/перемычка). Длины отрезков не используются. Порт DetectLeads rev.13.</summary>
        public static LeadAnalysis DetectLeads(List<LineComponent> lstComponents, List<Seg> lstLines,
            DiagnosticLogger log)
        {
            LeadAnalysis oResult = new LeadAnalysis();
            try
            {
                // Маркеры клемм на слое маркеров: стубы (короткие) и перемычки стубов —
                // длинные линии между соседними клеммами (rev.13). Круги-«точки» (Arc)
                // не используются (summary п.18).
                List<Seg> lstStubs = oResult.Stubs;
                List<Seg> lstJumpers = oResult.Jumpers;
                foreach (Seg oSeg in lstLines)
                {
                    if (!IsLayer(oSeg, AddInConfiguration.MarkerLayerName)) continue;
                    if (LeadGeometry.PointDistance(oSeg.A, oSeg.B) <= AddInConfiguration.StubMaxLengthMm)
                        lstStubs.Add(oSeg);
                    else
                        lstJumpers.Add(oSeg);
                }
                log.Log("[INFO] Стубы-маркеры клемм (слой '" + AddInConfiguration.MarkerLayerName +
                    "', L<=" + AddInConfiguration.StubMaxLengthMm.ToString("F1", CultureInfo.InvariantCulture) +
                    " мм): " + lstStubs.Count + "; перемычек стубов: " + lstJumpers.Count);
                foreach (Seg oJumper in lstJumpers)
                    log.Log("[JUMPER] перемычка стубов: (" +
                        oJumper.A.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                        oJumper.A.Y.ToString("F3", CultureInfo.InvariantCulture) + ")-(" +
                        oJumper.B.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                        oJumper.B.Y.ToString("F3", CultureInfo.InvariantCulture) + ") L=" +
                        LeadGeometry.PointDistance(oJumper.A, oJumper.B).ToString("F3", CultureInfo.InvariantCulture));

                // Для К3' расстояние меряем до ближайшего маркера — стуба ИЛИ перемычки
                // (под перемычкой бывают клеммы без собственного стуба).
                List<Seg> lstMarkers = new List<Seg>();
                foreach (Seg oSeg in lstStubs) lstMarkers.Add(oSeg);
                foreach (Seg oSeg in lstJumpers) lstMarkers.Add(oSeg);

                int nLeads = 0, nBridges = 0;
                List<Pt> lstPoints = oResult.Points;
                for (int nComp = 0; nComp < lstComponents.Count; nComp++)
                {
                    LineComponent oComponent = lstComponents[nComp];
                    // Только компоненты на слое выводов.
                    bool bOnLeadLayer = true;
                    foreach (Seg oSeg in oComponent.Segments)
                    {
                        if (!IsLayer(oSeg, AddInConfiguration.LeadLayerName))
                        {
                            bOnLeadLayer = false;
                            break;
                        }
                    }
                    if (!bOnLeadLayer) continue;

                    // Критерий К1: простая цепь (все внутренние точки степени 2, ровно 2 листа).
                    if (oComponent.MaxDegree > 2 || oComponent.Leaves.Count != 2)
                    {
                        log.Log("[LEADSKIP] компонента: segs=" + oComponent.Segments.Count +
                            " leaves=" + oComponent.Leaves.Count + " maxDeg=" + oComponent.MaxDegree +
                            " — не цепь, пропущена");
                        continue;
                    }

                    nLeads++;
                    Pt oLeaf1 = oComponent.Leaves[0];
                    Pt oLeaf2 = oComponent.Leaves[1];
                    double dDist1 = NearestMarkerDistance(oLeaf1, lstMarkers);
                    double dDist2 = NearestMarkerDistance(oLeaf2, lstMarkers);

                    if (oComponent.AllCollinear)
                    {
                        // Одиночный вывод: точка подключения = внешний (дальний от маркера) конец.
                        Pt oCable = dDist1 >= dDist2 ? oLeaf1 : oLeaf2;
                        double dDist = dDist1 >= dDist2 ? dDist1 : dDist2;
                        LogCablePoint(log, oCable, dDist, "одиночный вывод, внешний конец");
                        lstPoints.Add(oCable);
                        oResult.PointComponent.Add(nComp);
                        oResult.PointIsBridge.Add(false);
                    }
                    else
                    {
                        // Мост = 2 вывода одной клеммы: оба листа — точки подключения.
                        nBridges++;
                        LogCablePoint(log, oLeaf1, dDist1, "мост, лист 1");
                        LogCablePoint(log, oLeaf2, dDist2, "мост, лист 2");
                        lstPoints.Add(oLeaf1);
                        oResult.PointComponent.Add(nComp);
                        oResult.PointIsBridge.Add(true);
                        lstPoints.Add(oLeaf2);
                        oResult.PointComponent.Add(nComp);
                        oResult.PointIsBridge.Add(true);
                    }
                }
                oResult.Leads = nLeads;
                oResult.Bridges = nBridges;

                // К4 (rev.13): точки подключения = ВСЕ подключения клемм (кабель + провод;
                // кабельность определяется из DataModel на Задаче 5), клеммы без
                // подключений рисуются без выводов — «ожидаемое число» не вычисляется.
                // Контроли — в CheckK4Report: привязка точек к колонкам клемм и «не более 2
                // подключений на клемму» (две стороны клеммы).
                log.Log("[INFO] [RESULT] выводов (компонент-цепей): " + nLeads +
                    " (из них мостов: " + nBridges + ", т.е. физических выводов " + (nLeads + nBridges) +
                    "), точек подключения (кабель+провод): " + lstPoints.Count);
                if (lstStubs.Count == 0)
                    log.Log("[INFO] [RESULT] стубы-маркеры клемм не найдены — контроль К4 пропущен");
                else
                    oResult.K4 = CheckK4Report(lstPoints, lstStubs, lstJumpers,
                        AddInConfiguration.Orientation, log);
                log.Summarize("Выводов: " + nLeads + ", точек подключения (кабель+провод): " +
                    lstPoints.Count + ".");
            }
            catch (Exception oException)
            {
                log.Warn("Поиск выводов не удался: " + oException.GetType().Name + ": " + oException.Message);
            }
            return oResult;
        }

        /// <summary>К4 (rev.13): привязка точек подключения к колонкам клемм и контроль
        /// «не более 2 подключений на клемму» (две стороны клеммы). Колонки = проекции
        /// центров стубов на ось ориентации + виртуальные позиции под перемычками
        /// стубов (шаг сетки клемм — медиана шага соседних стубов вдоль оси, НЕ длина
        /// сегментов формы). Привязка по |Δ оси| ≤ полушага (rev.6.2, Задача 4: ось
        /// задаётся параметром orientation — X при Horizontal, Y при Vertical; путь
        /// Horizontal байт-в-байт как в rev.6.1). Клемма без точек — норма (нет
        /// подключений → нет выводов);
        /// WARN — только точки-«сироты» и клеммы с &gt;2 точками. Возвращает K4Report
        /// (колонки/шаг/привязка/ось) для свода [MATCH] (Задача 5); логи — как в
        /// rev.13, строки [K4] печатают ось.</summary>
        public static K4Report CheckK4Report(List<Pt> lstPoints, List<Seg> lstStubs, List<Seg> lstJumpers,
            ReportOrientation orientation, DiagnosticLogger log)
        {
            K4Report oReport = new K4Report();
            oReport.Orientation = orientation;
            bool bVertical = orientation == ReportOrientation.Vertical;
            string strAxis = bVertical ? "Y" : "X";
            List<double> lstCols = oReport.Columns;
            foreach (Seg oStub in lstStubs)
                lstCols.Add(bVertical ? (oStub.A.Y + oStub.B.Y) / 2.0 : (oStub.A.X + oStub.B.X) / 2.0);
            lstCols.Sort();
            if (lstCols.Count < 2)
            {
                log.Log("[INFO] [K4] стубов меньше 2 — контроль привязки пропущен");
                return oReport;
            }

            // Шаг сетки клемм: НИЖНЯЯ медиана разностей оси соседних колонок-стубов
            // (при чётном числе берём меньшую из двух средних — завышенный шаг
            // расставил бы виртуальные колонки реже нужного).
            List<double> lstDiffs = new List<double>();
            for (int i = 1; i < lstCols.Count; i++)
                lstDiffs.Add(lstCols[i] - lstCols[i - 1]);
            lstDiffs.Sort();
            double dPitch = lstDiffs[(lstDiffs.Count - 1) / 2];
            if (dPitch <= 0)
            {
                log.Log("[INFO] [K4] шаг клемм не определён — контроль привязки пропущен");
                return oReport;
            }
            double dHalf = dPitch / 2.0;
            oReport.Pitch = dPitch;
            oReport.Half = dHalf;
            oReport.Valid = true;

            // Виртуальные колонки под перемычками: позиции от начала перемычки с шагом
            // сетки (под перемычкой могут быть клеммы без собственного стуба).
            foreach (Seg oJumper in lstJumpers)
            {
                double dA = bVertical ? Math.Min(oJumper.A.Y, oJumper.B.Y) : Math.Min(oJumper.A.X, oJumper.B.X);
                double dB = bVertical ? Math.Max(oJumper.A.Y, oJumper.B.Y) : Math.Max(oJumper.A.X, oJumper.B.X);
                // Запас 0.25*dPitch — float-гвард: колонка на dB не должна потеряться
                // из-за накопления погрешности dPos += dPitch вдоль оси.
                for (double dPos = dA; dPos <= dB + dPitch * 0.25; dPos += dPitch)
                {
                    bool bExists = false;
                    foreach (double dCol in lstCols)
                        if (Math.Abs(dCol - dPos) <= dHalf) { bExists = true; break; }
                    if (!bExists)
                    {
                        lstCols.Add(dPos);
                        log.Log("[K4] колонка клеммы без стуба (под перемычкой): " + strAxis + "=" +
                            dPos.ToString("F3", CultureInfo.InvariantCulture));
                    }
                }
            }
            lstCols.Sort();

            int[] arrCounts = new int[lstCols.Count];
            oReport.Counts = arrCounts;
            int nOrphan = 0, nOver = 0;
            foreach (Pt oPoint in lstPoints)
            {
                double dPos = bVertical ? oPoint.Y : oPoint.X;
                int nBest = -1;
                double dBest = double.MaxValue;
                for (int i = 0; i < lstCols.Count; i++)
                {
                    double d = Math.Abs(lstCols[i] - dPos);
                    if (d < dBest) { dBest = d; nBest = i; }
                }
                if (nBest >= 0 && dBest <= dHalf)
                {
                    arrCounts[nBest]++;
                    if (arrCounts[nBest] == 3) // WARN один раз на колонку
                    {
                        nOver++;
                        log.Warn("[K4] на клемму " + strAxis + "=" + lstCols[nBest].ToString("F3", CultureInfo.InvariantCulture) +
                            " привязано более 2 точек подключения (если у клеммы есть раздвоение — см. [SPLIT] в [MATCH])");
                    }
                }
                else
                {
                    nOrphan++;
                    log.Warn("[K4] точка подключения (" +
                        oPoint.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                        oPoint.Y.ToString("F3", CultureInfo.InvariantCulture) +
                        ") не привязалась к колонке клемм (ближайшая " + strAxis + "=" +
                        (nBest >= 0 ? lstCols[nBest].ToString("F3", CultureInfo.InvariantCulture) : "?") +
                        ", Δ" + strAxis + "=" + dBest.ToString("F3", CultureInfo.InvariantCulture) + ")");
                }
            }
            oReport.Orphans = nOrphan;
            oReport.Over = nOver;

            int n0 = 0, n1 = 0, n2 = 0;
            foreach (int nCount in arrCounts)
            {
                if (nCount == 0) n0++;
                else if (nCount == 1) n1++;
                else if (nCount == 2) n2++;
            }
            log.Log("[INFO] [K4] клемм-колонок: " + lstCols.Count + " (" + strAxis + ", стубов " + lstStubs.Count +
                ", виртуальных под перемычками " + (lstCols.Count - lstStubs.Count) +
                "); распределение точек на клемму: 0×" + n0 + ", 1×" + n1 + ", 2×" + n2 +
                ", >2×" + nOver + "; точек без клеммы: " + nOrphan);
            log.Summarize("К4: точек " + lstPoints.Count + ", клемм-колонок " + lstCols.Count +
                ", точек без клеммы " + nOrphan + ", клемм с >2 точками " + nOver + ".");
            return oReport;
        }

        /// <summary>Минимальное расстояние от точки до ближайшего маркера клеммы (стуб
        /// или перемычка стубов) — по концам и середине маркера. Порт NearestStubDistance.</summary>
        public static double NearestMarkerDistance(Pt oPoint, List<Seg> lstMarkers)
        {
            double dBest = double.MaxValue;
            foreach (Seg oMarker in lstMarkers)
            {
                Pt oStart = oMarker.A;
                Pt oEnd = oMarker.B;
                Pt oMid = new Pt((oStart.X + oEnd.X) / 2.0, (oStart.Y + oEnd.Y) / 2.0);
                double d = LeadGeometry.PointDistance(oPoint, oStart);
                if (d < dBest) dBest = d;
                d = LeadGeometry.PointDistance(oPoint, oEnd);
                if (d < dBest) dBest = d;
                d = LeadGeometry.PointDistance(oPoint, oMid);
                if (d < dBest) dBest = d;
            }
            return dBest;
        }

        private static void LogCablePoint(DiagnosticLogger log, Pt oPoint, double dDistToMarker, string strKind)
        {
            log.Log("[CABLE] точка подключения (" + strKind + "): (" +
                oPoint.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                oPoint.Y.ToString("F3", CultureInfo.InvariantCulture) +
                ") расст. до ближайшего маркера=" + dDistToMarker.ToString("F3", CultureInfo.InvariantCulture));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
// rev.6.1: Terminal живёт в EObjects (как в EplanTerminalStripReader.cs); алиас —
// чтобы не тянуть весь EObjects в файл и избежать двусмысленностей с Graphics.
using Terminal = Eplan.EplApi.DataModel.EObjects.Terminal;
using Eplan.EplApi.HEServices;

namespace MyEplanActions
{
    /// <summary>
    /// Действие TERMINAL_STRIP_ANALYZE — Этап 2 (план: plan_stage2.md).
    /// Задача 3: конвейер rev.13 — форма → встроенный отчёт → дерево SubPlacements →
    /// линии → детектор К1–К4 (addin/Geometry, чистый порт rev.13).
    /// Задачи 4–5 добавят ридеры DataModel ([DM]) и свод [MATCH].
    /// </summary>
    public class AnalyzeTerminalStripAction : IEplAction
    {
        // Штамп сборки: должен совпадать в логе с ожидаемой версией кода.
        // Меняется при каждой правке логики — так видно, что исполняется не старый DLL.
        private const string BUILD_STAMP = "2026-09-23 Этап 7 rev.11.0 (Фаза G: линия-ссылка от символа кабеля — References в геометрии + PolyLine-стрелка)";

        private readonly DiagnosticLogger _logger = new DiagnosticLogger();

        public bool Execute(ActionCallingContext oActionCallingContext)
        {
            _logger.BeginRun("TERMINAL_STRIP_ANALYZE — Этап 7 rev.11.0 (Фаза G: линия-ссылка от символа кабеля — References в геометрии + PolyLine-стрелка)", BUILD_STAMP);
            try
            {
                Run();
            }
            catch (Exception oException)
            {
                _logger.Log("[ERROR] Необработанное исключение: " + oException.GetType().Name + ": " + oException.Message);
                _logger.Log("[ERROR] " + oException.StackTrace);
                _logger.Fail("ДЕЙСТВИЕ УПАЛО С ИСКЛЮЧЕНИЕМ — см. лог.");
            }

            string strLogPath = _logger.SaveLog();
            MessageBox.Show(
                _logger.SummaryText + Environment.NewLine + Environment.NewLine + "Полный лог: " + strLogPath,
                "TERMINAL_STRIP_ANALYZE — результат",
                MessageBoxButtons.OK,
                _logger.Failed ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            return !_logger.Failed;
        }

        private void Run()
        {
            // --- 1. Открытый проект и активная страница (как в spike rev.13) ---
            SelectionSet oSelectionSet = new SelectionSet();
            Project oProject = oSelectionSet.GetCurrentProject(false);
            if (oProject == null)
            {
                _logger.Fail("Нет открытого проекта. Откройте проект и повторите.");
                return;
            }
            _logger.Log("[INFO] Проект: " + SafeText("<нет имени>", () => oProject.ProjectName));

            Page oPage = oSelectionSet.CurrentlyEdited as Page;
            if (oPage == null)
            {
                _logger.Fail("Нет активной страницы. Сделайте активной страницу типа «Однополюсная схема соединения».");
                return;
            }
            DocumentTypeManager.DocumentType ePageType = oPage.PageType;
            _logger.Log("[INFO] Активная страница: " + oPage.IdentifyingName + ", тип: " + ePageType);
            // rev.6.2 (Задача 4): ориентация отчёта из конфига — критерий прогонов:
            // A — [ORIENT] Vertical, B — [ORIENT] Horizontal (регресс rev.6.1).
            _logger.Log("[INFO] [ORIENT] ориентация отчёта: " + AddInConfiguration.Orientation);
            if (ePageType != DocumentTypeManager.DocumentType.CircuitSingleLine)
            {
                _logger.Fail("Активная страница \"" + oPage.IdentifyingName + "\" имеет тип " + ePageType +
                     ". Требуется «Однополюсная схема соединения» (CircuitSingleLine).");
                return;
            }

            // --- 2. Форма: мастер-данные -> проект, список «своих» форм ---
            EmbeddedReportReader oReader = new EmbeddedReportReader(_logger);
            List<string> lstFormNames = oReader.TryAddFormToProject(oProject);

            // --- 3. Создание встроенного отчёта ---
            ReportBlockReference oReportRef = oReader.TryCreateEmbeddedReport(oProject, oPage, lstFormNames);
            if (oReportRef == null)
            {
                _logger.Fail("Не удалось создать встроенный отчёт ни с одной комбинацией " +
                     "(имя формы × Type × FilterSchemaName). Проверьте, что форма «" +
                     AddInConfiguration.ReportFormName + ".f11» добавлена в проект (Основные данные → Формы).");
                return;
            }
            _logger.Summarize("Встроенный отчёт создан, ReportBlockReference получен.");

            // --- 4. Дерево SubPlacements + перепись типов ---
            List<Placement> lstAll = new List<Placement>();
            try
            {
                _logger.Log("[INFO] --- Дерево SubPlacements отчёта ---");
                oReader.CollectSubPlacementsTree(oReportRef.SubPlacements, lstAll,
                    new HashSet<Placement>(), 0);
            }
            catch (Exception oException)
            {
                _logger.Fail("SubPlacements недоступен: " + oException.GetType().Name + ": " + oException.Message);
                return;
            }
            _logger.Log("[INFO] SubPlacements (рекурсивно): " + lstAll.Count + " объектов");

            Dictionary<string, int> dicTypeCounts = new Dictionary<string, int>();
            List<Line> lstLines = new List<Line>();
            foreach (Placement oPlacement in lstAll)
            {
                string strTypeName = oPlacement.GetType().Name;
                if (!dicTypeCounts.ContainsKey(strTypeName)) dicTypeCounts[strTypeName] = 0;
                dicTypeCounts[strTypeName]++;

                Line oLine = oPlacement as Line;
                if (oLine != null) lstLines.Add(oLine);
            }
            foreach (KeyValuePair<string, int> oPair in dicTypeCounts)
                _logger.Log("[INFO]   " + oPair.Key + " x " + oPair.Value);
            _logger.Summarize("Объектов в отчёте: " + lstAll.Count + ", из них линий: " + lstLines.Count + ".");

            // --- 4a. Перепись слоёв по ВСЕМ графическим объектам дерева (rev.11) ---
            Dictionary<string, int> dicAllLayerObjects = new Dictionary<string, int>();
            Dictionary<string, int> dicAllLayerLines = new Dictionary<string, int>();
            foreach (Placement oPlacement in lstAll)
            {
                GraphicalPlacement oGraphical = oPlacement as GraphicalPlacement;
                if (oGraphical == null) continue;
                string strKey = EmbeddedReportReader.LayerNameOf(oGraphical);
                if (strKey == null) strKey = "<LayerId " + SafeLayerId(oGraphical) + ">";
                if (!dicAllLayerObjects.ContainsKey(strKey))
                {
                    dicAllLayerObjects[strKey] = 0;
                    dicAllLayerLines[strKey] = 0;
                }
                dicAllLayerObjects[strKey]++;

                if (oPlacement is Line) dicAllLayerLines[strKey]++;
            }
            _logger.Log("[INFO] --- Перепись слоёв по всем графическим объектам (GraphicalPlacement) дерева ---");
            foreach (KeyValuePair<string, int> oPair in dicAllLayerObjects)
                _logger.Log("[ALLLAYERS] слой '" + oPair.Key + "': объектов " + oPair.Value +
                    ", из них Line: " + dicAllLayerLines[oPair.Key]);

            // --- 4b. PlaceHolderText — дескрипторы строк формы (якорь «номер ↔ колонка»,
            //         rev.5.3): порядковое сопоставление колонок и клемм неверно, когда
            //         нумерация клеммника не совпадает с раскладкой по X (п.24, клеммы 22/23).
            //         rev.5.5 (docs API 2.9: PlaceHolderText : Graphics.Text): текст —
            //         это Text.Contents / GetDisplayString(); TEXT-свойства из Properties
            //         дескриптора текст не хранят (прогон rev.5.4: 270 дескрипторов — 0) ---
            List<PhRow> lstPh = new List<PhRow>();
            List<PlaceHolderText> lstPhObjects = new List<PlaceHolderText>();
            foreach (Placement oPlacement in lstAll)
            {
                PlaceHolderText oPh = oPlacement as PlaceHolderText;
                if (oPh == null) continue;
                lstPhObjects.Add(oPh);
                PhRow oPhRow = new PhRow();
                string strContents, strDisplay;
                oPhRow.Text = ResolvePlaceholderText(oPh, out strContents, out strDisplay);
                // rev.6.1: запасной якорь — полное имя клеммы-источника заполнителя
                // (SourceObject → Terminal, проба [PHPROBE] rev.5.5; Terminal.Name —
                // ПОЛНОЕ имя, урок rev.5.2). Пустая строка = данных нет.
                oPhRow.SourceTerminalName = "";
                try
                {
                    Terminal oTerm = oPh.SourceObject as Terminal;
                    if (oTerm != null)
                        oPhRow.SourceTerminalName = SafeText("", () => oTerm.Name) ?? "";
                }
                catch { oPhRow.SourceTerminalName = ""; }
                try
                {
                    PointD oLoc = oPh.Location;
                    oPhRow.Location = new Pt(oLoc.X, oLoc.Y);
                }
                catch { oPhRow.Location = new Pt(double.NaN, double.NaN); }
                lstPh.Add(oPhRow);
                _logger.Log("[PH] (" + oPhRow.Location.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                    oPhRow.Location.Y.ToString("F3", CultureInfo.InvariantCulture) + ") '" + oPhRow.Text +
                    "' | contents='" + strContents + "' display='" + strDisplay + "'");
            }
            _logger.Log("[INFO] PlaceHolderText (дескрипторы строк): " + lstPh.Count);

            // Проба членов дескриптора: SourceObject может вернуть клемму-источник
            // (прямой якорь «объект ↔ колонка» без парсинга текста), PropDescr —
            // какое свойство отображает заполнитель, PlaceHolderType — тип заполнителя.
            LogPlaceHolderProbes(lstPhObjects, lstPh);

            // --- 5. Геометрия линий ---
            _logger.Log("[INFO] --- Линии в отчёте ---");
            List<Seg> lstSegs = new List<Seg>();
            int nIndex = 0;
            foreach (Line oLine in lstLines)
            {
                nIndex++;
                PointD oStart = oLine.StartPoint;
                PointD oEnd = oLine.EndPoint;
                double dDx = oEnd.X - oStart.X;
                double dDy = oEnd.Y - oStart.Y;
                double dLength = Math.Sqrt(dDx * dDx + dDy * dDy);
                _logger.Log(string.Format(CultureInfo.InvariantCulture,
                    "[LINE] #{0:00}: ({1:F3}; {2:F3}) -> ({3:F3}; {4:F3})  len={5:F3}  dx={6:F3}  dy={7:F3}",
                    nIndex, oStart.X, oStart.Y, oEnd.X, oEnd.Y, dLength, dDx, dDy));

                short nLayerId = SafeLayerId(oLine);
                string strLayerName = EmbeddedReportReader.LayerNameOf(oLine);
                _logger.Log("[LAYER] #" + nIndex.ToString("00") + ": LayerId=" + nLayerId +
                    ", Name=" + (strLayerName == null ? "<n/a>" : "'" + strLayerName + "'"));

                Seg oSeg = new Seg();
                oSeg.A = new Pt(oStart.X, oStart.Y);
                oSeg.B = new Pt(oEnd.X, oEnd.Y);
                oSeg.LayerName = strLayerName;
                lstSegs.Add(oSeg);
            }

            // --- 6. Компоненты связности (К1) и выводы (К1–К4) — чистый порт rev.13 ---
            _logger.Log("[INFO] --- Компоненты связности линий (критерии «вывода») ---");
            List<LineComponent> lstComponents = LeadDetector.AnalyzeLineComponents(lstSegs, _logger);

            _logger.Log("[INFO] --- Выводы и точки подключения (порт rev.13) ---");
            LeadAnalysis oAnalysis = LeadDetector.DetectLeads(lstComponents, lstSegs, _logger);

            // --- 7. Data Model (Фаза B, Задача 4): TerminalStrip -> Terminal -> Connection -> Cable ---
            _logger.Log("[INFO] --- Data Model: клеммники/клеммы/подключения ---");
            EplanTerminalStripReader oDmReader = new EplanTerminalStripReader(_logger);
            DmReport oDm = oDmReader.Read(oProject);

            int nExt = 0, nInt = 0, nBridge = 0;
            foreach (DmRow oRow in oDm.Rows)
            {
                if (oRow.Side == "Ext") nExt++;
                else if (oRow.Side == "Int") nInt++;
                else nBridge++;
            }
            _logger.Log("[INFO] [DMSUM] клеммников: " + oDm.StripCount + ", клемм: " + oDm.TerminalCount +
                ", строк [DM]: " + oDm.Rows.Count + " (Ext " + nExt + ", Int " + nInt + ", Bridge " + nBridge +
                "), кабелей: " + oDm.CableWireCounts.Count + ", ошибок [DMERR]: " + oDm.ErrCount);
            _logger.Log("[INFO] [DMSUM] CDP у соединений: 0×" + oDm.ConnCdpZero + ", 1×" + oDm.ConnCdpOne +
                ", >1×" + oDm.ConnCdpMulti + " (ошибок пробы " + oDm.ConnCdpErr + "); №31058=true: conn " +
                oDm.IsCable31058ConnTrue + ", cdp " + oDm.IsCable31058CdpTrue);
            foreach (KeyValuePair<string, int> oPair in oDm.CableWireCounts)
                _logger.Log("[DMSUM] кабель '" + oPair.Key + "': жил в подключениях клемм: " + oPair.Value);

            // --- 8. Сверка геометрии и DataModel (контроль К4; свод [MATCH] — Задача 5) ---
            // rev.4.1 (summary п.21): сверка ПО-КЛЕММНИКУ — отчёт показывает один клеммник,
            // сравнивать с его Ext+Int, а не с суммой по всем клеммникам проекта.
            DmStripStats oTarget;
            if (oDm.PerStrip.TryGetValue(AddInConfiguration.TargetStripName, out oTarget))
            {
                _logger.Log("[CROSS] целевой клеммник '" + AddInConfiguration.TargetStripName +
                    "': клемм " + oTarget.TerminalCount + ", Ext=" + oTarget.ExtCount +
                    ", Int=" + oTarget.IntCount + ", Ext+Int=" + oTarget.ConnCount +
                    ", Bridge=" + oTarget.BridgeCount + " | точек из геометрии: " + oAnalysis.Points.Count +
                    (oAnalysis.Points.Count == oTarget.ConnCount
                        ? " — точки == Ext+Int клеммника, СОВПАДАЕТ"
                        : " — РАСХОЖДЕНИЕ, см. [DM]/[K4]"));
            }
            else
            {
                _logger.Log("[CROSS] целевой клеммник '" + AddInConfiguration.TargetStripName +
                    "' НЕ НАЙДЕН в проекте — сверка по-клеммнику невозможна (проверить имя; AddInConfiguration.TargetStripName)");
            }
            _logger.Log("[CROSS] справочно по всем клеммникам проекта (" + oDm.StripCount +
                " шт.): Ext=" + nExt + ", Int=" + nInt + ", Ext+Int=" + (nExt + nInt) +
                ", Bridge=" + nBridge);

            // --- 9. Свод [MATCH] (Задача 5): точки ↔ клеммы ↔ кабель/провод + Bridge ---
            _logger.Log("[INFO] --- Свод [MATCH]: точки ↔ клеммы ↔ кабель/провод ---");
            List<MatchRow> lstMatch = MatchBuilder.Build(oAnalysis, oDm, lstPh, _logger);

            // --- 10. Фаза D: TerminalConnectionModel — связка точки с конкретным Connection ---
            _logger.Log("[INFO] --- Фаза D: TerminalConnectionModel (точка ↔ Connection по стороне) ---");
            List<TerminalConnectionModel> lstTcm = TerminalConnectionModelBuilder.Build(
                lstMatch, oDm, oAnalysis, oAnalysis.K4, AddInConfiguration.TargetStripName, _logger);

            _logger.Summarize("Готово: точек подключения " + oAnalysis.Points.Count +
                "; строк [DM] " + oDm.Rows.Count + "; строк [MATCH] " + lstMatch.Count +
                "; моделей [TCM] " + lstTcm.Count + ".");

            // --- 11. Фаза E: CableLayoutBuilder — группировка соединений по кабелям ---
            _logger.Log("[INFO] --- Фаза E: CableLayout (группировка соединений по кабелям) ---");
            CableLayoutModel oLayout = CableLayoutBuilder.Build(lstTcm, _logger);
            _logger.Summarize("Фаза E: кабелей " + oLayout.Cables.Count +
                ", проводных " + oLayout.NoCableConnections.Count + ".");

            // --- 12. Фаза F: CableGeometryBuilder — чистая геометрия кабельной разводки ---
            // rev.10.7 (шаг 4 Фазы G, решение пользователя 23.09.2026): размер рабочего
            // символа A×B замеряется ПЕРЕД конфигом (пробная вставка [SYMSIZE] →
            // GetBoundingBox → Remove; отказ — фолбэк 14×14, источник виден по логу
            // [SYMSIZE]); из размера выводятся зазор (габарит по оси выноса /2:
            // H — A/2, V — B/2, rev.10.11) и расчётный шаг уровней
            // шин — по нему стоят и ряды символов (rev.10.8/10.10; расчёт в builder'е).
            _logger.Log("[INFO] --- Фаза F: геометрия кабельной разводки ---");
            double dSymW = AddInConfiguration.SymbolFallbackSizeMm;
            double dSymH = AddInConfiguration.SymbolFallbackSizeMm;
            SymbolSizeMeasurer.TryMeasure(oPage, _logger, out dSymW, out dSymH);
            _logger.Log("[INFO] [SYMSIZE] конфиг: " + dSymW.ToString("F3", CultureInfo.InvariantCulture) +
                "×" + dSymH.ToString("F3", CultureInfo.InvariantCulture) + " мм");
            CableGeometryConfig oGeomCfg = new CableGeometryConfig();
            oGeomCfg.BusOffsetMm = AddInConfiguration.CableBusOffsetMm;
            oGeomCfg.LevelPitchMinMm = AddInConfiguration.CableLevelPitchMinMm;
            oGeomCfg.BusLiftMm = AddInConfiguration.CableBusLiftMm;
            oGeomCfg.ApproachOffsetMm = AddInConfiguration.CableApproachOffsetMm;
            oGeomCfg.ApproachPitchMm = AddInConfiguration.CableApproachPitchMm;
            oGeomCfg.SymbolColumnOffsetMm = AddInConfiguration.CableSymbolColumnOffsetMm;
            oGeomCfg.SymbolWidthMm = dSymW;
            oGeomCfg.SymbolHeightMm = dSymH;
            // rev.11.0: параметры линии-ссылки от символа кабеля (решение
            // пользователя 23.09.2026) — длина ВКЛЮЧАЯ стрелку, размеры стрелки.
            oGeomCfg.ReferenceLineLengthMm = AddInConfiguration.CableReferenceLineLengthMm;
            oGeomCfg.ReferenceArrowLengthMm = AddInConfiguration.CableReferenceArrowLengthMm;
            oGeomCfg.ReferenceArrowHalfWidthMm = AddInConfiguration.CableReferenceArrowHalfWidthMm;
            oGeomCfg.ReferenceArrowNotchDepthMm = AddInConfiguration.CableReferenceArrowNotchDepthMm;
            // Край ряда по оси выноса (ревизия 2): H — max X колонок К4, V — min Y.
            // К4 невалиден — NaN, builder уйдёт в fallback на точки кабеля (WARN).
            double dStripEndAxis = double.NaN;
            if (oAnalysis.K4 != null && oAnalysis.K4.Valid && oAnalysis.K4.Columns.Count > 0)
            {
                bool bVerticalK4 = AddInConfiguration.Orientation == ReportOrientation.Vertical;
                double dExtreme = bVerticalK4 ? double.PositiveInfinity : double.NegativeInfinity;
                foreach (double dCol in oAnalysis.K4.Columns)
                {
                    if (bVerticalK4) { if (dCol < dExtreme) dExtreme = dCol; }
                    else if (dCol > dExtreme) dExtreme = dCol;
                }
                if (!double.IsInfinity(dExtreme)) dStripEndAxis = dExtreme;
            }
            CableGeometryResult oGeom = CableGeometryBuilder.Build(
                oLayout, AddInConfiguration.Orientation, oGeomCfg, dStripEndAxis);

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
            // rev.11.0: дамп линий-ссылок (start → tip; стрелка — на слое Graphics).
            foreach (ReferenceElement oRefGeom in oGeom.References)
                _logger.Log("[GEOM] ref '" + (oRefGeom.CableName ?? "<без имени>") + "' #" +
                    oRefGeom.CableIndex.ToString(CultureInfo.InvariantCulture) + ": (" +
                    oRefGeom.Line.A.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                    oRefGeom.Line.A.Y.ToString("F3", CultureInfo.InvariantCulture) + ")-(" +
                    oRefGeom.Line.B.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                    oRefGeom.Line.B.Y.ToString("F3", CultureInfo.InvariantCulture) + ")");
            foreach (string strGeomWarn in oGeom.Warnings)
                _logger.Warn(strGeomWarn);
            _logger.Log("[INFO] [GEOM-SUM] кабелей " + oGeom.Symbols.Count +
                ", сегментов " + oGeom.Segments.Count +
                ", ссылок " + oGeom.References.Count +
                ", предупреждений " + oGeom.Warnings.Count);
            _logger.Summarize("Фаза F: сегментов " + oGeom.Segments.Count +
                ", символов " + oGeom.Symbols.Count + ".");

            // --- 13. Фаза G: реальные объекты по CableGeometryResult (spec
            // 2026-09-22-fase-g-graphics-design.md). Слой линий — GraphicalLayer из дерева
            // отчёта (строки на слое GraphicsLayerName); перо красное — рабочий вывод по
            // решению пользователя. НЕ идемпотентно: повтор — дубликаты (очистка — Фаза I).
            GraphicalLayer oCableLayer = GraphicLineCreator.ResolveLayerFromTree(
                lstAll, AddInConfiguration.GraphicsLayerName, _logger);
            int nLines = GraphicLineCreator.CreateLines(oPage, oGeom, oCableLayer, _logger);
            int nSymbols = CableSymbolCreator.CreateSymbols(oPage, oGeom, _logger);
            // rev.11.0: линии-ссылки от символов — линия + замкнутая
            // PolyLine-стрелка с заливкой (решение пользователя 23.09.2026);
            // слой и перо — как у линий разводки.
            int nRefs = ReferenceArrowCreator.CreateReferences(oPage, oGeom, oCableLayer, _logger);
            _logger.Summarize("Фаза G: линий " + nLines + "/" + oGeom.Segments.Count +
                ", символов " + nSymbols + "/" + oGeom.Symbols.Count +
                ", ссылок " + nRefs + "/" + oGeom.References.Count +
                " (не идемпотентно: повторный прогон дублирует объекты).");

            // --- 14. Проба чтения реальных кабелей проекта (для реальной группировки) ---
            oDmReader.ReadCables(oProject);

            // --- 15. Проба [SYMBOX]: размеры символов кабеля (GetBoundingBox/GetLogicalArea).
            // Диагностика Фазы H: символы вставляются у пробной точки и сразу удаляются
            // (урок п.48 — не засорять страницу); на счётчики этапов 1–5 не влияет.
            SymbolBoxProbe.Probe(oPage, oProject, _logger);
        }

        private static short SafeLayerId(GraphicalPlacement oPlacement)
        {
            try { return oPlacement.LayerId; }
            catch { return (short)-1; }
        }

        // --- PlaceHolderText: текст через Text.Contents / GetDisplayString (rev.5.5) ---

        /// <summary>Текст дескриптора: якорем считается значение, парсящееся как номер
        /// клеммы (Contents → GetDisplayString); если ни одно не парсится, но непусто —
        /// возвращается как есть (не-числа отфильтрует AnchorResolver). Сырые значения
        /// обоих членов — в out-параметрах для хвоста строки [PH]; "&lt;err&gt;" = чтение бросило.
        /// Прогон rev.5.4 доказал: в Properties дескриптора текста нет (рефлексия по
        /// TEXT-id — 270 пустых чтений), docs API 2.9: текст живёт в Text.Contents
        /// (тип MultiLangString — берём .ToString(); GetDisplayString возвращает string).</summary>
        private static string ResolvePlaceholderText(PlaceHolderText oPh, out string strContents, out string strDisplay)
        {
            strContents = SafeText("<err>", () => oPh.Contents == null ? "" : oPh.Contents.ToString());
            strDisplay = SafeText("<err>", () => oPh.GetDisplayString());
            if (strContents == null) strContents = "";
            if (strDisplay == null) strDisplay = "";
            if (AnchorResolver.ParseTerminalNumber(strContents) >= 0) return strContents;
            if (AnchorResolver.ParseTerminalNumber(strDisplay) >= 0) return strDisplay;
            if (strContents.Length > 0 && strContents != "<err>") return strContents;
            if (strDisplay.Length > 0 && strDisplay != "<err>") return strDisplay;
            return "<текст не найден>";
        }

        /// <summary>Проба членов PlaceHolderText (rev.5.5) на трёх дескрипторах ряда
        /// номеров формы (Y=-81: у тестовой формы ряд «1…60», прогон rev.5.4 + пример
        /// пользователя: текст «1» @ (52.85;-81)): SourceObject — объект-источник
        /// заполнителя (запасной путь якоря: Terminal → имя → номер, если у другой
        /// формы текст ряда не прочитается), PropDescr — отображаемое свойство,
        /// PlaceHolderType — тип заполнителя.
        /// TODO(rev.5.7+): проба диагностическая с захардкоденными координатами
        /// тестовой формы — удалить или перевести на конфигурацию, когда станет
        /// ясно, нужен ли запасной якорь через SourceObject.</summary>
        private void LogPlaceHolderProbes(List<PlaceHolderText> lstPhObjects, List<PhRow> lstPh)
        {
            double[] arrProbeX = new double[] { 52.85, 199.85, 472.85 };
            double dRowY = -81.0;
            _logger.Log("[INFO] --- Проба PlaceHolderText: SourceObject/PropDescr/PlaceHolderType (ряд Y=" +
                dRowY.ToString("F1", CultureInfo.InvariantCulture) + ") ---");
            foreach (double dProbeX in arrProbeX)
            {
                int nBest = -1;
                double dBest = double.MaxValue;
                for (int i = 0; i < lstPhObjects.Count; i++)
                {
                    if (double.IsNaN(lstPh[i].Location.X)) continue;
                    if (Math.Abs(lstPh[i].Location.Y - dRowY) > 0.5) continue; // только ряд номеров
                    double d = Math.Abs(lstPh[i].Location.X - dProbeX);
                    if (d < dBest) { dBest = d; nBest = i; }
                }
                string strProbe = "[PHPROBE] X=" + dProbeX.ToString("F2", CultureInfo.InvariantCulture);
                if (nBest < 0)
                {
                    _logger.Log(strProbe + ": в ряду номеров дескриптора нет");
                    continue;
                }
                PlaceHolderText oPh = lstPhObjects[nBest];
                string strAt = " (дескриптор X=" +
                    lstPh[nBest].Location.X.ToString("F3", CultureInfo.InvariantCulture) + ")";
                try
                {
                    object oSource = oPh.SourceObject;
                    if (oSource == null)
                        _logger.Log(strProbe + strAt + ": SourceObject=null");
                    else
                        _logger.Log(strProbe + strAt + ": SourceObject=" + oSource.GetType().Name + " '" +
                            SafeText("<n/a>", () => ((StorableObject)oSource).ToStringIdentifier()) + "'");
                }
                catch (Exception oException)
                {
                    _logger.Log(strProbe + strAt + ": SourceObject бросил " +
                        oException.GetType().Name + ": " + oException.Message);
                }
                _logger.Log(strProbe + strAt + ": PropDescr=" +
                    SafeText("<err>", () => oPh.PropDescr.ToString()) +
                    ", PlaceHolderType=" + SafeText("<err>", () => oPh.PlaceHolderType.ToString()));
            }
        }

        private static string SafeText(string strFallback, Func<string> oGetter)
        {
            try { return oGetter(); }
            catch { return strFallback; }
        }

        public bool OnRegister(ref string ActionName, ref int Ordinal)
        {
            ActionName = "TERMINAL_STRIP_ANALYZE";
            Ordinal = 0;
            return true;
        }

        public void GetActionProperties(ref ActionProperties actionProperties)
        {
        }
    }
}

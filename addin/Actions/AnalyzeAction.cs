using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;   // SPIKE-2 fix-1: reflection-проба сигнатуры/возврата Execute
using System.Windows.Forms;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
// rev.6.1: Terminal живёт в EObjects (как в EplanTerminalStripReader.cs); алиас —
// чтобы не тянуть весь EObjects в файл и избежать двусмысленностей с Graphics.
using Terminal = Eplan.EplApi.DataModel.EObjects.Terminal;
// rev.12.1 (H-2): тот же урок п.30 для TerminalStrip (CollectStripNames).
using TerminalStrip = Eplan.EplApi.DataModel.EObjects.TerminalStrip;
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
        // rev.13.0 (Этап 8): видимое ОУ — по примеру пользователя первичным путём
        // 2-арг NameService.SetFullNameAndAdjustVisibleName(oFunc, oParts) с
        // предварительным oNames.Page (список без 1800); false → fallback
        // rev.11.15-присваивание + шаг AdjustVisibleName (rev.12.9). rev.12.8
        // (3-арг, список с 1800) — прогон false×4; rev.12.9 (только AVN) —
        // см. summary. Арбитр — readback 20002 + Name.
        // rev.13.1 (Этап 8, H-4): UI-режим — символ из настроек (браузер
        // SymbolBrowserDialog, слоты H/V), два пробных замера [SYMSIZE] (по слоту),
        // компенсация визуального центра [SYMSIZE-OFF] dx,dy при вставке (R9);
        // headless-Run() — побайтно без изменений (обёртки: константы + (0,0), R8).
        // rev.13.2 (Этап 8, H-4v2 SPIKE, throwaway): в UI-ветке ДО показа MainDialog —
        // вызов нативного диалога «Вставить символ» (XEGActionInsertSymRef) + [ACTDUMP];
        // логи [SYMDLG]; гейты AddInConfiguration.Spike*; провал спайка пайплайн не
        // останавливает; H-4-браузер/замеры/DT — не тронуты (fallback до вердикта).
        // rev.13.3 (Этап 8, H-4v2 SPIKE-2, throwaway): производный InsertInteraction
        // TERMINAL_STRIP_PICK_SPIKE (от XEGedIaInsertSymRef) через CommandLineInterpreter;
        // факты OnSuccess — статика-буфер → [PICK-EXEC]/[PICK-DUMP]/[PICK-ADAPT]/
        // [PICK-CLEAN]; гейт SpikeSymbolPick, spike-1 гейты выключены (факты 25.09);
        // InsertedPlacements vs InsertedItems — только reflection; уборка IsValid→Remove.
        // rev.13.4 (Этап 8, H-4v2 SPIKE-3, throwaway): запуск интеракции по KB-канону —
        // ActionManager.FindAction("XGedStartInteractionAction") + ctx.AddParameter("Name",
        // <имя>) + Execute (reflection-проба возврата); лог [PICK-EXEC3]; вариант базового
        // имени — SpikePickVariant (1=XEGedIaInsertSymRef, 2=XEGActionInsertSymRef; классы
        // TERMINAL_STRIP_PICK_SPIKE/…2 зарегистрированы одновременно); legacy cli.Execute —
        // fallback сравнения. Факт rev.13.3: cli.Execute(<имя>) вернул False без диалога.
        private const string BUILD_STAMP = "2026-09-25 Этап 8 rev.13.4 (H-4v2 SPIKE-3: запуск через XGedStartInteractionAction(Name=...) + SpikePickVariant 1|2, [PICK-EXEC3]; spike-2 захват — общий статический; H-4 production — без изменений)";

        // Заголовок MessageBox'ов UI-ветки — как Text диалога (MainDialog).
        private const string UI_CAPTION = "Генерация схемы подключений клеммника";

        private readonly DiagnosticLogger _logger = new DiagnosticLogger();

        // Фаза H (rev.12.0, H-1): настройки, загруженные при старте действия.
        // Пайплайном пока НЕ потребляются (поведение headless без изменений);
        // читаются сводкой [SETTINGS] после загрузки; потребители
        // появятся в H-2 (диалог/режим UI).
        private AddInSettings _oSettings;

        // H-2: кандидаты каталога настроек (те же, что у AddInSettings.Load в
        // Execute) — нужны UI-ветке для сохранения после успешной генерации.
        private string[] _arrSettingsDirs;

        public bool Execute(ActionCallingContext oActionCallingContext)
        {
            // rev.13.4: SPIKE-3 — запуск PICK-интеракции через XGedStartInteractionAction
            // (Name=..., SpikePickVariant 1|2, [PICK-EXEC3]); захват/дампы SPIKE-2 — общие.
            _logger.BeginRun("TERMINAL_STRIP_ANALYZE — Этап 8 rev.13.4 (H-4v2 SPIKE-3: запуск XGedStartInteractionAction(Name=TERMINAL_STRIP_PICK_SPIKE/…2) + [PICK-EXEC3]; дампы [PICK-DUMP]/[PICK-ADAPT]/[PICK-CLEAN])", BUILD_STAMP);

            // H-1: загрузка персистентных настроек (файл в каталоге лога —
            // ruling R1). Файла/каталога нет — дефолты из AddInConfiguration,
            // путь = null (Load исключений не бросает). Кандидаты каталога:
            // лог-каталоги + %TEMP% fallback (спека §2 п.9: лог-каталоги + %TEMP% fallback).
            string[] arrSettingsDirs = new string[DiagnosticLogger.LOG_DIR_CANDIDATES.Length + 1];
            DiagnosticLogger.LOG_DIR_CANDIDATES.CopyTo(arrSettingsDirs, 0);
            arrSettingsDirs[arrSettingsDirs.Length - 1] = System.IO.Path.GetTempPath();
            _arrSettingsDirs = arrSettingsDirs;   // для [SETTINGS] save в UI-ветке (H-2)
            string strSettingsPath;
            _oSettings = AddInSettings.Load(arrSettingsDirs, out strSettingsPath);
            _logger.Log("[SETTINGS] load: " + (strSettingsPath ?? "нет файла — дефолты"));
            // rev.12.0: сводка настроек одной строкой — диагностика прогона и
            // потребление _oSettings (до появления потребителей в H-2 — иначе CS0414).
            _logger.Log("[SETTINGS] TargetStrip='" + _oSettings.TargetStrip +
                "' Form='" + _oSettings.Form +
                "' Символ " + _oSettings.SymbolLibrary + "/" + _oSettings.SymbolName +
                " (вариант H/V " + _oSettings.VariantH.ToString(CultureInfo.InvariantCulture) + "/" +
                _oSettings.VariantV.ToString(CultureInfo.InvariantCulture) + ")" +
                " ориентация=" + _oSettings.OrientationMode);

            try
            {
                if (!AddInConfiguration.UseUi)
                {
                    // Headless-режим (спека §2 п.10): код прежний — заморожен для
                    // регресса A/B, ревьюер сверяет ветку против BASE; отличие
                    // headless-пути — только строка [MODE].
                    _logger.Log("[MODE] headless");
                    Run();
                }
                else
                {
                    RunUi();
                }
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
            // rev.12.1 (H-2): целевой клеммник — явная константа конфига (headless).
            EplanTerminalStripReader oDmReader = new EplanTerminalStripReader(_logger,
                AddInConfiguration.TargetStripName);
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
            List<MatchRow> lstMatch = MatchBuilder.Build(oAnalysis, oDm, lstPh, _logger,
                AddInConfiguration.TargetStripName);

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

        // --- Фаза H, H-2: UI-ветка (спека 2026-09-25-fase-h-ui-design.md §2 п.1–3, 9) ---

        /// <summary>UI-ветка: сбор списков → диалог → генерация со значениями
        /// диалога → сохранение настроек. Отмена/закрытие диалога — выход без
        /// каких-либо действий (Execute вернёт true). Провал создания отчёта на
        /// выбранной форме — сообщение и ПОВТОРНЫЙ показ диалога (спека §2 п.3);
        /// прочие сбои пайплайна — текущая обработка ([ERROR]/Fail) + краткое
        /// сообщение и выход. Точка вставки отчёта пока из конфига
        /// (AddInConfiguration.InsertX/InsertY — интеракция клика H-5/H-6).
        /// Пайплайн после отчёта — RunPipeline: копия headless-Run() с
        /// подстановкой РЕЖИМА ориентации из диалога (разрешение и детекция —
        /// внутри RunPipeline, rev.12.2 H-3); headless-Run() намеренно не
        /// меняется (заморожен для регресса A/B — план plan_stage8.md H-2).</summary>
        private void RunUi()
        {
            _logger.Log("[MODE] ui");

            // --- 1. Открытый проект и активная страница — тот же доступ, что в headless ---
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
            if (ePageType != DocumentTypeManager.DocumentType.CircuitSingleLine)
            {
                _logger.Fail("Активная страница \"" + oPage.IdentifyingName + "\" имеет тип " + ePageType +
                     ". Требуется «Однополюсная схема соединения» (CircuitSingleLine).");
                return;
            }

            // --- 1b. SPIKE (throwaway, H-4v2, rev.13.2): нативный диалог «Вставить
            //        символ» (XEGActionInsertSymRef) ДО показа нашего MainDialog —
            //        строго в UI-ветке (headless Run() не затронут). Провал спайка
            //        не останавливает пайплайн (WARN/INFO внутри + продолжение).
            //        Удалить вместе с NativeSymbolDialogSpike.cs после вердикта. ---
            if (AddInConfiguration.SpikeNativeInsertSymbol)
                NativeSymbolDialogSpike.Run(oProject, _logger);

            // --- 1c. SPIKE-2/3 (throwaway, H-4v2, rev.13.4): запуск PICK-интеракции по
            //        KB-канону — FindAction("XGedStartInteractionAction") +
            //        AddParameter("Name", TERMINAL_STRIP_PICK_SPIKE|…2) (вариант базового
            //        имени — SpikePickVariant); факты OnSuccess — общий статику-буфер
            //        SymbolPickInteraction. Строго до MainDialog; headless не затронут;
            //        провал — INFO [PICK-EXEC3], пайплайн жив; размещённое убирается
            //        [PICK-CLEAN]. Удалить вместе с addin/Interaction/
            //        SymbolPickInteraction.cs и гейтами после вердикта. ---
            if (AddInConfiguration.SpikeSymbolPick)
                RunSymbolPickSpike(oProject);

            // --- 2. Данные для диалога: (а) клеммники ВСЕГО ПРОЕКТА (rev.12.3: клеммники
            //        размещены на многополюсных страницах, активная под прогон —
            //        однополюсная, постраничный список блокировал диалог; защита от
            //        «чужого» клеммника — гейт [CROSSGATE]), (б) формы. Отказ шага —
            //        сообщение пользователю и выход ([UIERR]) ---
            List<string> lstStripNames;
            try { lstStripNames = CollectStripNames(oProject); }
            catch (Exception oException)
            {
                _logger.Log("[UIERR] Сбор списка клеммников не удался: " +
                    oException.GetType().Name + ": " + oException.Message);
                MessageBox.Show("Не удалось собрать список клеммников проекта:\n" + oException.Message,
                    UI_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            List<string> lstFormNames;
            try { lstFormNames = EmbeddedReportReader.CollectAvailableFormNames(oProject, _logger); }
            catch (Exception oException)
            {
                _logger.Log("[UIERR] Сбор списка форм не удался: " +
                    oException.GetType().Name + ": " + oException.Message);
                MessageBox.Show("Не удалось собрать список форм отчёта:\n" + oException.Message,
                    UI_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (lstStripNames.Count == 0)
            {
                _logger.Fail("Клеммники в проекте не найдены — диалог невозможен.");
                MessageBox.Show("В проекте не найдено ни одного клеммника.",
                    UI_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (lstFormNames.Count == 0)
            {
                _logger.Fail("Формы *.f11 (проект+система) не найдены — диалог невозможен.");
                MessageBox.Show("Не найдено ни одной формы (*.f11) ни в проекте, ни в системе.",
                    UI_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _logger.Log("[INFO] Данные для диалога: клеммников " + lstStripNames.Count +
                ", форм " + lstFormNames.Count + ".");

            // --- 3. Цикл диалога: Отмена/закрытие — выход; «Создать» — пайплайн;
            //        провал создания отчёта — сообщение и заново диалог ---
            EmbeddedReportReader oReader = new EmbeddedReportReader(_logger);
            // rev.13.1 (H-4, R10): проект — браузеру символа (кнопка активна);
            // null — кнопка осталась бы выключенной (не наш проект — не наш случай).
            MainDialog oDialog = new MainDialog(lstStripNames, lstFormNames, _oSettings, oProject);
            try
            {
                while (true)
                {
                    // rev.12.1 (H-2): сброс DialogResult перед КАЖДЫМ показом (в т.ч.
                    // первым): после «Создать» с провалившимся отчётом диалог показывается
                    // повторно с залипшим DialogResult.OK — крестик вернул бы OK.
                    oDialog.DialogResult = DialogResult.None;
                    if (oDialog.ShowDialog() != DialogResult.OK)
                    {
                        _logger.Log("[MODE] ui: отмена пользователем");
                        return;   // без создания чего-либо
                    }
                    string strStrip = oDialog.SelectedStripName;
                    string strForm = oDialog.SelectedFormName;
                    SettingsOrientation eMode = oDialog.SelectedOrientation;

                    // rev.12.2 (H-3): разрешение ориентации (override из диалога ИЛИ
                    // геометр. детекция для Auto) и маркерные логи [ORIENT] manual /
                    // [ORIENT-AUTO] — внутри RunPipeline: детекции нужны дескрипторы из
                    // дерева отчёта, а отчёт создаётся до RunPipeline и ориентации не
                    // требует.
                    _logger.Log("[INFO] Выбор: клеммник '" + strStrip + "', форма '" + strForm + "'.");

                    // --- 3b. rev.12.5 (H-3b): разрешение ЦЕЛИ отчёта — выбранного
                    //        TerminalStrip. Без целей EPLAN строит встроенный отчёт по
                    //        умолчанию (контекст активной страницы) — геометрия выходила
                    //        по клеммнику X2, а не по выбранному. Обход — тот же, что в
                    //        CollectStripNames. Не найден/исключение — повторный показ
                    //        диалога (тот же приём, что при oReportRef == null ниже). ---
                    _logger.Log("[REPORT-TARGET] поиск цели отчёта: клеммник '" + strStrip + "'");
                    TerminalStrip oTargetStrip;
                    try
                    {
                        oTargetStrip = ResolveTargetStrip(oProject, strStrip);
                    }
                    catch (Exception oException)
                    {
                        _logger.Log("[UIERR] Разрешение цели отчёта не удалось: " +
                            oException.GetType().Name + ": " + oException.Message);
                        MessageBox.Show(oDialog, "Не удалось разрешить выбранный клеммник '" + strStrip +
                            "' в объекте проекта:\n" + oException.Message +
                            "\n\nВыберите другой клеммник либо отмените генерацию.",
                            UI_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;   // заново показать диалог
                    }
                    if (oTargetStrip == null)
                    {
                        // null — не только «не найден», но и сбои чтения страниц/клеммников
                        // (детали уже в [UIERR]-логах выше внутри ResolveTargetStrip).
                        _logger.Log("[UIERR] Цель отчёта не разрешена для клеммника '" + strStrip +
                            "' (не найден или доступ к страницам неудачен — см. [UIERR] выше)");
                        MessageBox.Show(oDialog, "Не удалось сопоставить выбранный клеммник '" + strStrip +
                            "' с объектом проекта (см. [UIERR]/[REPORT-TARGET] в логе).\n\n" +
                            "Выберите другой клеммник либо отмените генерацию.",
                            UI_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;   // заново показать диалог
                    }

                    try
                    {
                        // --- 4. Отчёт на ВЫБРАННОЙ форме (override; «чужая форма»
                        //        в UI-режиме не проверяется); rev.12.5 (H-3b): цель —
                        //        выбранный TerminalStrip (4-арг. CreateEmbeddedReport) ---
                        ReportBlockReference oReportRef =
                            oReader.TryCreateEmbeddedReport(oProject, oPage, null, strForm, false,
                                new StorableObject[] { oTargetStrip });
                        if (oReportRef == null)
                        {
                            _logger.Log("[UIERR] Не удалось создать отчёт для формы '" + strForm +
                                "' ни одной комбинацией тип×схема — повторный показ диалога.");
                            MessageBox.Show(oDialog, "Не удалось создать отчёт для формы '" + strForm +
                                "'.\n\nВыберите другую форму либо отмените генерацию.",
                                UI_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            continue;   // заново показать диалог
                        }
                        _logger.Summarize("Встроенный отчёт создан, ReportBlockReference получен.");

                        // --- 5–16. Пайплайн со значениями диалога; false — ранний
                        //        выход по Fail (настройки НЕ сохраняем) ---
                        if (RunPipeline(oProject, oPage, oReportRef, oReader, eMode, strStrip))
                        {
                            // --- Успешная генерация: сохранение настроек (спека §2 п.9).
                            //     Символ/слоты варианта (H-4) применяются браузером
                            //     MainDialog напрямую к _oSettings (он и есть
                            //     внутренний settings-объект диалога) — здесь не трогаем. ---
                            _oSettings.TargetStrip = strStrip;
                            _oSettings.Form = strForm;
                            _oSettings.OrientationMode = eMode;
                            string strSavePath;
                            AddInSettings.Save(_oSettings, _arrSettingsDirs, out strSavePath);
                            _logger.Log("[SETTINGS] save: " +
                                (strSavePath ?? "НЕ УДАЛОСЬ — каталог не найден"));
                        }
                        else
                        {
                            _logger.Log("[SETTINGS] save: настройки не сохранены (прогон не завершён)");
                        }
                        return;
                    }
                    catch (Exception oException)
                    {
                        // Текущая обработка (как catch в Execute) + краткое сообщение; выход.
                        _logger.Log("[ERROR] Необработанное исключение: " + oException.GetType().Name + ": " + oException.Message);
                        _logger.Log("[ERROR] " + oException.StackTrace);
                        _logger.Fail("ДЕЙСТВИЕ УПАЛО С ИСКЛЮЧЕНИЕМ — см. лог.");
                        MessageBox.Show("Ошибка генерации: " + oException.GetType().Name + "\n" +
                            oException.Message + "\n\nПодробности — в логе.",
                            UI_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
            }
            finally
            {
                oDialog.Dispose();
            }
        }

        /// <summary>SPIKE-2/3 (throwaway, H-4v2, rev.13.4; brief task-h4v2-spike2-brief.md
        /// + устная SPIKE-3): запуск PICK-интеракции по KB-канону —
        /// ActionManager.FindAction("XGedStartInteractionAction") + ActionCallingContext.
        /// AddParameter("Name", &lt;имя по SpikePickVariant&gt;) + Execute (reflection-проба,
        /// фактический возврат в [PICK-EXEC3]). Факт rev.13.3: прямой cli.Execute(<имя>)
        /// возвращал False без диалога — при недоступности канона вызывается и он
        /// (TryLegacyCliLaunch) для сравнения. Execute синхронен полному циклу
        /// «диалог+размещение» — факт spike-1; факты читаются из статики-буфера
        /// SymbolPickInteraction после возврата (класс-захватчик различим в дампе).
        /// Провал — INFO [PICK-EXEC3], пайплайн и настройки не трогает (ruling: Warn не
        /// фейлит). Размещённое убирается по IsValid ([PICK-CLEAN], урок п.48). Удалить
        /// вместе с addin/Interaction/SymbolPickInteraction.cs, блоком 1c в RunUi и
        /// гейтами SpikeSymbolPick/SpikePickVariant после вердикта.</summary>
        private void RunSymbolPickSpike(Project oProject)
        {
            // Сброс статики-буфера: OnSuccess «вернуть» нечего, контракт — статика.
            SymbolPickInteraction.PickDump.Clear();
            SymbolPickInteraction.PlacedObjects.Clear();
            SymbolPickInteraction.PlacedCount = 0;
            SymbolPickInteraction.CollectedVia = "<не собрано>";

            // SPIKE-3 (rev.13.4): гипотеза запуска — KB-канон: интеракции стартуют НЕ
            // cli.Execute(<имя интеракции>), а экшеном XGedStartInteractionAction с
            // параметром Name (KB-пример: acc.AddParameter("Name","XGedIaFormatText")).
            // Факт rev.13.3: cli.Execute("<имя>",ctx) существует (2-арг, bool), но вернул
            // False без диалога — проверяем канон. Вариант базового имени — гейт
            // SpikePickVariant (1=TERMINAL_STRIP_PICK_SPIKE на XEGedIaInsertSymRef —
            // пример пользователя; 2=TERMINAL_STRIP_PICK_SPIKE2 на XEGActionInsertSymRef —
            // имя, доказанное spike-1). Fallback при недоступности канона: прежний прямой
            // cli.Execute (TryLegacyCliLaunch) для сравнения. Провал — INFO, пайплайн жив.
            string strIaName = AddInConfiguration.SpikePickVariant == 2
                ? "TERMINAL_STRIP_PICK_SPIKE2" : "TERMINAL_STRIP_PICK_SPIKE";
            _logger.Log("[INFO] [PICK-EXEC3] --- SPIKE-3: запуск через XGedStartInteractionAction, " +
                "Name='" + strIaName + "' (SpikePickVariant=" +
                AddInConfiguration.SpikePickVariant.ToString(CultureInfo.InvariantCulture) + ") ---");
            try
            {
                // ActionManager/FindAction — паттерн доказан spike-1 (NativeSymbolDialogSpike).
                ActionManager oManager = new ActionManager();
                var oStartAction = oManager.FindAction("XGedStartInteractionAction");
                if (oStartAction == null)
                {
                    _logger.Log("[INFO] [PICK-EXEC3] экшен XGedStartInteractionAction не найден — " +
                        "fallback: прежний прямой cli.Execute для сравнения");
                    TryLegacyCliLaunch(strIaName);
                }
                else
                {
                    var oAcc = new ActionCallingContext();
                    // AddParameter(string,string) — KB-канон (параметр Name интеракции).
                    oAcc.AddParameter("Name", strIaName);
                    // Возврат Execute по сигнатуре не доказан — reflection-проба (стиль
                    // SPIKE-2 fix-1): логируем фактический boolean запуска.
                    MethodInfo oExecStart = oStartAction.GetType().GetMethod("Execute",
                        new Type[] { typeof(ActionCallingContext) });
                    if (oExecStart == null)
                    {
                        _logger.Log("[INFO] [PICK-EXEC3] Execute(ActionCallingContext) не найден у " +
                            "XGedStartInteractionAction (reflection) — fallback: прямой cli.Execute");
                        TryLegacyCliLaunch(strIaName);
                    }
                    else
                    {
                        object oRes = oExecStart.Invoke(oStartAction, new object[] { oAcc });
                        _logger.Log("[INFO] [PICK-EXEC3] Name='" + strIaName + "' возврат=" +
                            DescribeCliReturn(oRes));
                    }
                }
            }
            catch (Exception oException)
            {
                // Invoke оборачивает исключениеcallee в TargetInvocationException —
                // разворачиваем (C#6 exception-filters нет, разворот ручной).
                Exception oReal = oException;
                TargetInvocationException oTie = oException as TargetInvocationException;
                if (oTie != null && oTie.InnerException != null) oReal = oTie.InnerException;
                // Провал/не-найдено — INFO (ruling: Warn не фейлит).
                _logger.Log("[INFO] [PICK-EXEC3] исключение: " + oReal.GetType().Name + ": " + oReal.Message);
            }

            // [PICK-DUMP] — слив буфера OnSuccess, кап 200 строк (ruling).
            int nDumped = 0;
            foreach (string strLine in SymbolPickInteraction.PickDump)
            {
                nDumped++;
                if (nDumped > 200)
                {
                    _logger.Log("[INFO] [PICK-DUMP] кап 200 — опущено строк буфера: " +
                        (SymbolPickInteraction.PickDump.Count - 200).ToString(CultureInfo.InvariantCulture));
                    break;
                }
                _logger.Log("[INFO] [PICK-DUMP] " + strLine);
            }
            _logger.Log("[INFO] [PICK-DUMP] коллекция: '" + SymbolPickInteraction.CollectedVia +
                "', размещено " + SymbolPickInteraction.PlacedCount.ToString(CultureInfo.InvariantCulture) +
                ", буфер " + SymbolPickInteraction.PickDump.Count.ToString(CultureInfo.InvariantCulture) + ".");

            // [PICK-ADAPT] — попытка восстановить (библиотека/имя/вариант) сверкой каталогом.
            PickAdaptDump(oProject);

            // [PICK-CLEAN] — уборка пробы: страница не засоряется (урок п.48).
            int nRemoved = 0;
            int nCleanFailed = 0;
            foreach (Placement oPlaced in SymbolPickInteraction.PlacedObjects)
            {
                try
                {
                    if (oPlaced != null && oPlaced.IsValid)
                    {
                        oPlaced.Remove();
                        nRemoved++;
                    }
                }
                catch (Exception oException)
                {
                    nCleanFailed++;
                    _logger.Log("[INFO] [PICK-CLEAN] Remove бросил " + oException.GetType().Name + ": " +
                        oException.Message + " — возможен останец (урок п.48)");
                }
            }
            _logger.Log("[INFO] [PICK-CLEAN] удалено " +
                nRemoved.ToString(CultureInfo.InvariantCulture) + "/" +
                SymbolPickInteraction.PlacedObjects.Count.ToString(CultureInfo.InvariantCulture) +
                ", отказов " + nCleanFailed.ToString(CultureInfo.InvariantCulture) + ".");
        }

        /// <summary>SPIKE-3: legacy-дорожка — прежний прямой запуск cli.Execute(<имя
        /// интеракции>) (reflection-проба сигнатур из SPIKE-2 fix-1: 2-арг
        /// (string,ActionCallingContext), иначе 1-арг (string), иначе дамп public-методов
        /// интерпретатора в буфер). Вызывается только когда XGedStartInteractionAction
        /// недоступен — «для сравнения» двух механизмов запуска в одном прогоне.</summary>
        private void TryLegacyCliLaunch(string strIaName)
        {
            try
            {
                CommandLineInterpreter oInterpreter = new CommandLineInterpreter();
                Type oCliType = typeof(CommandLineInterpreter);
                MethodInfo oExec2 = oCliType.GetMethod("Execute",
                    new Type[] { typeof(string), typeof(ActionCallingContext) });
                if (oExec2 != null)
                {
                    object oRes = oExec2.Invoke(oInterpreter,
                        new object[] { strIaName, new ActionCallingContext() });
                    _logger.Log("[INFO] [PICK-EXEC] fallback cli.Execute('" + strIaName +
                        "', 2 арг.) возврат=" + DescribeCliReturn(oRes));
                }
                else
                {
                    MethodInfo oExec1 = oCliType.GetMethod("Execute", new Type[] { typeof(string) });
                    if (oExec1 != null)
                    {
                        object oRes = oExec1.Invoke(oInterpreter, new object[] { strIaName });
                        _logger.Log("[INFO] [PICK-EXEC] fallback cli.Execute('" + strIaName +
                            "', 1 арг.) возврат=" + DescribeCliReturn(oRes));
                    }
                    else
                    {
                        _logger.Log("[INFO] [PICK-EXEC] fallback: Execute-сигнатура не найдена (ни " +
                            "(string,ActionCallingContext), ни (string)) — интеракция НЕ запускалась; " +
                            "public-методы CommandLineInterpreter — в [PICK-DUMP]");
                        DumpCliMethods(oInterpreter);
                    }
                }
            }
            catch (Exception oException)
            {
                Exception oReal = oException;
                TargetInvocationException oTie = oException as TargetInvocationException;
                if (oTie != null && oTie.InnerException != null) oReal = oTie.InnerException;
                _logger.Log("[INFO] [PICK-EXEC] fallback исключение: " + oReal.GetType().Name + ": " +
                    oReal.Message);
            }
        }

        /// <summary>SPIKE-2 fix-1: описание фактического возврата Execute — bool как
        /// True/False, не-bool/null — «возврат &lt;тип&gt;» (+ToString скаляра безопасно).</summary>
        private static string DescribeCliReturn(object oRes)
        {
            if (oRes == null) return "<null>";
            if (oRes is bool) return ((bool)oRes) ? "True" : "False";
            string strValue;
            try { strValue = " '" + oRes.ToString() + "'"; }
            catch { strValue = ""; }
            return "возврат <" + oRes.GetType().Name + ">" + strValue;
        }

        /// <summary>SPIKE-2 fix-1: дамп public instance-методов CommandLineInterpreter
        /// (имена+арность+тип возврата, кап 30) в статику-буфер — сольётся в [PICK-DUMP]
        /// общим циклом (PickDump-стиль). Только чтение reflection, без вызовов.</summary>
        private static void DumpCliMethods(CommandLineInterpreter oInterpreter)
        {
            try
            {
                MethodInfo[] arrMethods = oInterpreter.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.Instance);
                int n = 0;
                for (int i = 0; i < arrMethods.Length; i++)
                {
                    if (n >= 30)
                    {
                        SymbolPickInteraction.PickDump.Add("cli.method: кап 30 — дальше обрезано по капу (" +
                            (arrMethods.Length - 30).ToString(CultureInfo.InvariantCulture) + " не показаны)");
                        break;
                    }
                    n++;
                    SymbolPickInteraction.PickDump.Add("cli.method " + arrMethods[i].Name + "(" +
                        arrMethods[i].GetParameters().Length.ToString(CultureInfo.InvariantCulture) +
                        " арг.) : " + arrMethods[i].ReturnType.Name);
                }
            }
            catch (Exception oException)
            {
                SymbolPickInteraction.PickDump.Add("cli.method дамп бросил " +
                    oException.GetType().Name + ": " + oException.Message);
            }
        }

        /// <summary>SPIKE-2 [PICK-ADAPT]: сверка SymbolVariant размещённого с каталогом —
        /// доказанный паттерн SymbolLibrary(project, lib) → library[name] → symbol[idx]
        /// (0-based) и сравнение ToString() Ordinal (бриф, п.«Каталог сверки»). Кандидаты —
        /// текущие настройки + probe-константы конфигу (не полное перечисление — спайк);
        /// нечитаемые индексы — штатный пропуск. Ничего в настройки НЕ пишем: сначала
        /// facts (критерий — [PICK-ADAPT] строки в логе).</summary>
        private void PickAdaptDump(Project oProject)
        {
            if (SymbolPickInteraction.PlacedObjects.Count == 0)
            {
                _logger.Log("[INFO] [PICK-ADAPT] размещённого нет — сверка не нужна");
                return;
            }
            if (oProject == null)
            {
                _logger.Log("[INFO] [PICK-ADAPT] проект null — сверка пропущена");
                return;
            }
            List<string> lstLibs = new List<string>();
            if (!string.IsNullOrEmpty(_oSettings.SymbolLibrary)) lstLibs.Add(_oSettings.SymbolLibrary);
            if (!string.IsNullOrEmpty(AddInConfiguration.SymbolProbeLibrary) &&
                lstLibs.IndexOf(AddInConfiguration.SymbolProbeLibrary) < 0)
                lstLibs.Add(AddInConfiguration.SymbolProbeLibrary);
            List<string> lstNames = new List<string>();
            if (!string.IsNullOrEmpty(_oSettings.SymbolName)) lstNames.Add(_oSettings.SymbolName);
            if (!string.IsNullOrEmpty(AddInConfiguration.SymbolProbeName) &&
                lstNames.IndexOf(AddInConfiguration.SymbolProbeName) < 0)
                lstNames.Add(AddInConfiguration.SymbolProbeName);

            int nPlaced = 0;
            foreach (Placement oPlaced in SymbolPickInteraction.PlacedObjects)
            {
                nPlaced++;
                string strPl = nPlaced.ToString(CultureInfo.InvariantCulture);
                Function oFunc = oPlaced as Function;
                if (oFunc == null)
                {
                    _logger.Log("[INFO] [PICK-ADAPT] placed#" + strPl + " — не Function, сверка пропущена");
                    continue;
                }
                string strVariant;
                try
                {
                    var oVariant = oFunc.SymbolVariant;   // KB-доказан (бриф); тип не именуем
                    strVariant = oVariant == null ? null : oVariant.ToString();
                }
                catch (Exception oException)
                {
                    _logger.Log("[INFO] [PICK-ADAPT] placed#" + strPl + ": чтение SymbolVariant бросило " +
                        oException.GetType().Name + " — сверка пропущена");
                    continue;
                }
                if (string.IsNullOrEmpty(strVariant))
                {
                    _logger.Log("[INFO] [PICK-ADAPT] placed#" + strPl + ": SymbolVariant пуст — сверка пропущена");
                    continue;
                }
                bool bFound = false;
                for (int iLib = 0; iLib < lstLibs.Count && !bFound; iLib++)
                {
                    for (int iName = 0; iName < lstNames.Count && !bFound; iName++)
                    {
                        for (int iVar = 0; iVar <= 3 && !bFound; iVar++)
                        {
                            try
                            {
                                var oLibrary = new Eplan.EplApi.DataModel.MasterData.SymbolLibrary(oProject, lstLibs[iLib]);
                                var oSymbol = oLibrary[lstNames[iName]];
                                var oCandidate = oSymbol[iVar];
                                if (oCandidate == null) continue;
                                if (string.Equals(oCandidate.ToString(), strVariant, StringComparison.Ordinal))
                                {
                                    _logger.Log("[INFO] [PICK-ADAPT] placed#" + strPl + " → '" + lstLibs[iLib] +
                                        "/" + lstNames[iName] + "/" +
                                        iVar.ToString(CultureInfo.InvariantCulture) +
                                        "' (ToString совпал с каталогом)");
                                    bFound = true;
                                }
                            }
                            catch
                            {
                                // Библиотека/имя/индекс недоступны — штатный пробы-пропуск (спайк).
                            }
                        }
                    }
                }
                if (!bFound)
                    _logger.Log("[INFO] [PICK-ADAPT] placed#" + strPl +
                        ": отображение среди кандидатов (настройки+константы, варианты 0..3) не найдено" +
                        " — ToString-факт уже в [PICK-DUMP]");
            }
        }

        /// <summary>Список полных имён клеммников ВСЕГО ПРОЕКТА: обход Project.Pages →
        /// Page.TerminalStrips (тот же доступ, что в EplanTerminalStripReader.Read,
        /// маркер [DMSTRIP]). rev.12.3 (откат H-2b): клеммники размещены на
        /// многополюсных страницах, а прогон делается на однополюсной (требование
        /// CircuitSingleLine) — постраничный список блокировал диалог (вывод
        /// &ЭМ2/8.1 пуст). Отчёт на однополюсной странице показывает подключения
        /// размещённого на ней клеммника; выбор «чужого» блокирует гейт [CROSSGATE]
        /// (жёсткий стоп с сообщением). Дубликаты полного имени: первый побеждает +
        /// WARN [STRIPDUP]. Нечитаемые чтения — [UIERR], обход не прерывается;
        /// наружу отдаются только строки (диалог не держит EPLAN-объекты).</summary>
        private List<string> CollectStripNames(Project oProject)
        {
            List<string> lstResult = new List<string>();
            Page[] arrPages;
            try { arrPages = oProject.Pages; }
            catch (Exception oException)
            {
                _logger.Log("[UIERR] Project.Pages: " +
                    oException.GetType().Name + ": " + oException.Message);
                return lstResult;
            }
            if (arrPages == null) return lstResult;
            foreach (Page oPage in arrPages)
            {
                string strPageName = SafeText("<нет имени>", () => oPage.IdentifyingName);
                TerminalStrip[] arrStrips;
                try { arrStrips = oPage.TerminalStrips; }
                catch (Exception oException)
                {
                    _logger.Log("[UIERR] Page.TerminalStrips ('" + strPageName + "'): " +
                        oException.GetType().Name + ": " + oException.Message);
                    continue;
                }
                if (arrStrips == null) continue;
                foreach (TerminalStrip oStrip in arrStrips)
                {
                    string strStripName = SafeText("<n/a>", () => oStrip.Name);
                    if (strStripName == null || strStripName.Length == 0 || strStripName == "<n/a>")
                    {
                        _logger.Log("[UIERR] TerminalStrip.Name ('страница " + strPageName +
                            "') не читается — клеммник пропущен");
                        continue;
                    }
                    if (lstResult.Contains(strStripName))
                    {
                        _logger.Warn("[STRIPDUP] дубликат полного имени клеммника '" + strStripName +
                            "' (страница " + strPageName + ") — пропущен");
                        continue;
                    }
                    lstResult.Add(strStripName);
                }
            }
            _logger.Log("[INFO] клеммников в проекте: " + lstResult.Count +
                " (обход " + arrPages.Length + " стр.)");
            return lstResult;
        }

        /// <summary>rev.12.5 (H-3b, «отчёт по выбранному клеммнику»): разрешение имени
        /// из диалога в объект TerminalStrip — цель 4-арг. перегрузки
        /// Reports.CreateEmbeddedReport(..., StorableObject[]). Обход — тот же, что в
        /// CollectStripNames: Project.Pages → Page.TerminalStrips; нечитаемые чтения —
        /// [UIERR] и пропуск (обход не прерывается). Сравнение через Trim() с обеих
        /// сторон: имя клеммника в EPLAN может нести хвостовой пробел (урок п.33),
        /// а ComboBox отдаёт текст как есть. Найден — лог [REPORT-TARGET] со страницей;
        /// null — клеммник не найден ИЛИ доступ к страницам/клеммникам нечитаем
        /// (детали — [UIERR]-логи внутри метода); вызывающий показывает диалог
        /// повторно нейтральной формулировкой.</summary>
        private TerminalStrip ResolveTargetStrip(Project oProject, string strStrip)
        {
            string strWant = (strStrip ?? "").Trim();
            Page[] arrPages;
            try { arrPages = oProject.Pages; }
            catch (Exception oException)
            {
                _logger.Log("[UIERR] Project.Pages (цель отчёта): " +
                    oException.GetType().Name + ": " + oException.Message);
                return null;
            }
            if (arrPages == null) return null;
            foreach (Page oPage in arrPages)
            {
                string strPageName = SafeText("<нет имени>", () => oPage.IdentifyingName);
                TerminalStrip[] arrStrips;
                try { arrStrips = oPage.TerminalStrips; }
                catch (Exception oException)
                {
                    _logger.Log("[UIERR] Page.TerminalStrips (цель отчёта, '" + strPageName + "'): " +
                        oException.GetType().Name + ": " + oException.Message);
                    continue;
                }
                if (arrStrips == null) continue;
                foreach (TerminalStrip oStrip in arrStrips)
                {
                    string strStripName = SafeText("<n/a>", () => oStrip.Name);
                    if (strStripName == null || strStripName.Trim() != strWant) continue;
                    _logger.Log("[REPORT-TARGET] TerminalStrip найден, страница '" +
                        strPageName + "'");
                    return oStrip;
                }
            }
            return null;
        }

        /// <summary>Пайплайн после создания отчёта — КОПИЯ headless-Run() (шаги 4–15)
        /// с подстановкой значений диалога UI-режима: режим ориентации eMode
        /// (rev.12.2, H-3: Horizontal/Vertical — override из диалога; Auto —
        /// геометрическая детекция AnchorResolver.DetectOrientation по числовым
        /// дескрипторам, fallback — аспект bbox дерева, ничья — конфиг; лог маркерами
        /// [ORIENT] manual / [ORIENT-AUTO] по месту разрешения — здесь, до первого
        /// потребителя) и целевой клеммник strTargetStripName (потребители — сверка
        /// [CROSS] и гейт [CROSSGATE]; допущение гейта: полное имя клеммника уникально
        /// в проекте — при имени на 2+ страницах [DMSTRIPDUP] гейт смягчается,
        /// MatchBuilder.Build → strTarget → словарь якорей
        /// AnchorResolver/[KEYDUP], TerminalConnectionModelBuilder, фильтр пробы
        /// [CBLPROP] в EplanTerminalStripReader). Потребители разрешённой
        /// ориентации — DetectLeads/CheckK4Report, AnchorResolver.Build через
        /// MatchBuilder, край ряда К4, CableGeometryBuilder. В headless-Run() те же
        /// значения — явные константы AddInConfiguration в местах вызова (то же
        /// значение — логи прежние); сам Run() больше не меняется.
        /// Возврат: true — пайплайн дошёл до конца (настройки можно сохранять);
        /// false — ранний выход по _logger.Fail (сбой шага — прогон не завершён).</summary>
        private bool RunPipeline(Project oProject, Page oPage, ReportBlockReference oReportRef,
            EmbeddedReportReader oReader, SettingsOrientation eMode, string strTargetStripName)
        {
            // ВНИМАНИЕ: тело — копия шагов 4–15 headless-Run() с подстановками
            // eMode→eOrientation/strTargetStripName. При правке пайплайна менять
            // СИНХРОННО в обоих местах (заморозка headless-регресса — до Фазы I).

            // rev.12.2 (H-3; фикс-волна MAJOR-1 — токены спеки §5 п.1–4): режим
            // ориентации из диалога. Override разбирается сразу и логируется маркером
            // «[ORIENT] manual»; Auto разрешится на шаге 4в после чтения дескрипторов
            // с маркером «[ORIENT-AUTO]» (в RunUi ориентация не логируется).
            bool bAutoOrient = eMode == SettingsOrientation.Auto;
            ReportOrientation eOrientation = AddInConfiguration.Orientation;
            if (!bAutoOrient)
            {
                eOrientation = eMode == SettingsOrientation.Vertical
                    ? ReportOrientation.Vertical : ReportOrientation.Horizontal;
                _logger.Log("[ORIENT] manual " +
                    (eOrientation == ReportOrientation.Vertical ? "Vertical" : "Horizontal") +
                    " (override из диалога)");
            }
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
                return false;
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

            // --- 4в. H-3 (rev.12.2; фикс-волна MAJOR-1 — токены спеки §5 п.1–3):
            // разрешение Auto-ориентации после чтения дескрипторов (шаг 4b), перед
            // первым потребителем (DetectLeads). Первый сигнал — раскладка числовых
            // дескрипторов (AnchorResolver.DetectOrientation: ряд по Y против столбца
            // по X, бакет 0.1 мм, порог ≥2) → INFO «[ORIENT-AUTO] <H|V> — evidence»;
            // ничья/нет чисел — аспект bbox всего дерева → INFO «[ORIENT-AUTO] <H|V> —
            // fallback bbox W×H»; и тут ничья/0/не измеримо — конфиг + WARN
            // «[ORIENT-AUTO] не уверен — конфиг». Маркеры grep-able, прежняя строка
            // «[INFO] [ORIENT] ориентация отчёта ...» UI-ветвью не печатается.
            if (bAutoOrient)
            {
                string strEvidence;
                ReportOrientation? eDetected = AnchorResolver.DetectOrientation(lstPh, out strEvidence);
                if (eDetected.HasValue)
                {
                    eOrientation = eDetected.Value;
                    _logger.Log("[ORIENT-AUTO] " +
                        (eOrientation == ReportOrientation.Horizontal ? "Horizontal" : "Vertical") +
                        " — " + strEvidence);
                }
                else
                {
                    double dBoxW, dBoxH;
                    bool bBoxAny = TryTreeBoundingBoxExtent(lstAll, out dBoxW, out dBoxH);
                    bool bBoxDecides = bBoxAny && dBoxW != dBoxH &&
                        !double.IsNaN(dBoxW) && !double.IsNaN(dBoxH);
                    if (bBoxDecides)
                    {
                        eOrientation = dBoxW > dBoxH
                            ? ReportOrientation.Horizontal : ReportOrientation.Vertical;
                        _logger.Log("[ORIENT-AUTO] " +
                            (eOrientation == ReportOrientation.Horizontal ? "Horizontal" : "Vertical") +
                            " — fallback bbox " +
                            dBoxW.ToString("F1", CultureInfo.InvariantCulture) + "×" +
                            dBoxH.ToString("F1", CultureInfo.InvariantCulture) + " мм");
                    }
                    else
                    {
                        eOrientation = AddInConfiguration.Orientation;
                        _logger.Warn("[ORIENT-AUTO] не уверен — конфиг (детекция: " + strEvidence +
                            "; bbox " + (bBoxAny
                                ? dBoxW.ToString("F1", CultureInfo.InvariantCulture) + "×" +
                                  dBoxH.ToString("F1", CultureInfo.InvariantCulture)
                                : "не измерим") + ")");
                    }
                }
            }

            // --- 6. Компоненты связности (К1) и выводы (К1–К4) — чистый порт rev.13 ---
            // rev.12.2 (H-3): ориентация — РАЗРЕШЁННОЕ значение (override диалога или
            // авто-детекция шага 4в; headless — конфиг).
            _logger.Log("[INFO] --- Компоненты связности линий (критерии «вывода») ---");
            List<LineComponent> lstComponents = LeadDetector.AnalyzeLineComponents(lstSegs, _logger);

            _logger.Log("[INFO] --- Выводы и точки подключения (порт rev.13) ---");
            LeadAnalysis oAnalysis = LeadDetector.DetectLeads(lstComponents, lstSegs, _logger, eOrientation);

            // --- 7. Data Model (Фаза B, Задача 4): TerminalStrip -> Terminal -> Connection -> Cable ---
            _logger.Log("[INFO] --- Data Model: клеммники/клеммы/подключения ---");
            EplanTerminalStripReader oDmReader = new EplanTerminalStripReader(_logger,
                strTargetStripName);
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
            // rev.12.1 (H-2): целевой клеммник — выбор диалога (в headless — константа).
            // rev.12.2 (H-2c): результат сравнения — в гейт bCrossOk (тексты логов не
            // меняются). oTarget инициализируется null: в отличие от прямого
            // `if (TryGetValue(...))`, взнос bool-флага old-csc (C#5) не считает
            // условно присвоенным — иначе CS0165 в ветках ниже.
            DmStripStats oTarget = null;
            bool bCrossFound = oDm.PerStrip.TryGetValue(strTargetStripName, out oTarget);
            bool bCrossOk = false;
            if (bCrossFound)
            {
                bCrossOk = oAnalysis.Points.Count == oTarget.ConnCount;
                _logger.Log("[CROSS] целевой клеммник '" + strTargetStripName +
                    "': клемм " + oTarget.TerminalCount + ", Ext=" + oTarget.ExtCount +
                    ", Int=" + oTarget.IntCount + ", Ext+Int=" + oTarget.ConnCount +
                    ", Bridge=" + oTarget.BridgeCount + " | точек из геометрии: " + oAnalysis.Points.Count +
                    (oAnalysis.Points.Count == oTarget.ConnCount
                        ? " — точки == Ext+Int клеммника, СОВПАДАЕТ"
                        : " — РАСХОЖДЕНИЕ, см. [DM]/[K4]"));
            }
            else
            {
                _logger.Log("[CROSS] целевой клеммник '" + strTargetStripName +
                    "' НЕ НАЙДЕН в проекте — сверка по-клеммнику невозможна (проверить имя; AddInConfiguration.TargetStripName)");
            }
            _logger.Log("[CROSS] справочно по всем клеммникам проекта (" + oDm.StripCount +
                " шт.): Ext=" + nExt + ", Int=" + nInt + ", Ext+Int=" + (nExt + nInt) +
                ", Bridge=" + nBridge);

            // rev.12.2 (H-2c; фикс-волна MAJOR-2, MINOR-4; rev.12.4 — кандидаты).
            // МОДЕЛЬ ПОЛЬЗОВАТЕЛЯ (25.09.2026): клеммник в диалоге — из всего проекта,
            // отчёт вставляется на ТЕКУЩУЮ однополюсную страницу; какие подключения
            // оказываются на странице — решает пользователь (размещение). Поэтому
            // расхождение [CROSS] означает «выбран не тот клеммник, что размещён на
            // текущей странице» — ЖЁСТКИЙ СТОП: WARN + Fail + MessageBox со списком
            // клеммников проекта, чьё число подключений совпадает с точками геометрии
            // (в стенде = 77 → четыре клона X2), графика НЕ создаётся, прогон считается
            // упавшим (настройки не сохраняются: save только при true).
            // ДОПУЩЕНИЕ: полное имя уникально в проекте; имя на 2+ страницах
            // ([DMSTRIPDUP]) — гейт смягчается (суммирование дубликатов может давать
            // ложное расхождение), продолжаем без гарантии.
            if (!bCrossOk)
            {
                bool bDupName = oDmReader.TargetNameMultiPage(strTargetStripName);
                List<string> lstCrossCandidates = new List<string>();
                foreach (KeyValuePair<string, DmStripStats> oKv in oDm.PerStrip)
                {
                    if (oKv.Value.ConnCount == oAnalysis.Points.Count)
                        lstCrossCandidates.Add(oKv.Key);
                }
                string strCandidates = lstCrossCandidates.Count > 0
                    ? string.Join("; ", lstCrossCandidates.GetRange(0,
                        Math.Min(5, lstCrossCandidates.Count)).ToArray())
                    : "нет (ни один клеммник проекта не имеет " + oAnalysis.Points.Count +
                      " подключений — возможно, на странице смешано несколько)";
                if (bDupName)
                {
                    _logger.Warn("[CROSSGATE] смягчён (дублирование имени клеммника в проекте) — " +
                        "продолжаем без гарантии");
                }
                else
                {
                    _logger.Warn("[CROSSGATE] отчёт не соответствует выбранному клеммнику (точки " +
                        oAnalysis.Points.Count + " != Ext+Int " +
                        (bCrossFound ? oTarget.ConnCount.ToString(CultureInfo.InvariantCulture) : "н/д") +
                        ") — графика НЕ создаётся; числу точек соответствуют клеммники: " +
                        strCandidates);
                    _logger.Fail("Генерация прервана: сверка [CROSS] не пройдена (см. [CROSSGATE]) — " +
                        "графика не создана, настройки не сохранены.");
                    // MINOR-4: RunPipeline вызывается только из UI-ветки (headless —
                    // отдельная ветка Run() без гейта), MessageBox здесь уместен.
                    MessageBox.Show("Отчёт на активной странице не соответствует выбранному клеммнику:\n" +
                        "точек геометрии " + oAnalysis.Points.Count + ", подключений клеммника " +
                        (bCrossFound ? oTarget.ConnCount.ToString(CultureInfo.InvariantCulture) : "н/д") + ".\n\n" +
                        "Числу точек соответствуют клеммники:\n  " + strCandidates + "\n\n" +
                        "Выберите один из них, либо разместите подключения выбранного\n" +
                        "клеммника на этой однополюсной странице и повторите.",
                        UI_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            // --- 9. Свод [MATCH] (Задача 5): точки ↔ клеммы ↔ кабель/провод + Bridge ---
            _logger.Log("[INFO] --- Свод [MATCH]: точки ↔ клеммы ↔ кабель/провод ---");
            List<MatchRow> lstMatch = MatchBuilder.Build(oAnalysis, oDm, lstPh, _logger,
                strTargetStripName, eOrientation);

            // --- 10. Фаза D: TerminalConnectionModel — связка точки с конкретным Connection ---
            _logger.Log("[INFO] --- Фаза D: TerminalConnectionModel (точка ↔ Connection по стороне) ---");
            List<TerminalConnectionModel> lstTcm = TerminalConnectionModelBuilder.Build(
                lstMatch, oDm, oAnalysis, oAnalysis.K4, strTargetStripName, _logger);

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
            // rev.13.1 (Этап 8, H-4, ruling R8): UI-режим — символ и слоты варианта
            // из настроек; ДВА пробных замера (слот H, слот V) + компенсация центра
            // (dx,dy) по R9; конфиг геометрии получает A×B варианта ФАКТИЧЕСКОЙ
            // ориентации (eOrientation разрешена выше — до секции, H-3); CreateSymbols
            // — индекс варианта фактической ориентации и его (dx,dy).
            _logger.Log("[INFO] --- Фаза F: геометрия кабельной разводки ---");
            bool bVerticalSym = eOrientation == ReportOrientation.Vertical;
            double dSizeWH, dSizeHH, dOffXH, dOffYH;
            bool bMeasH = SymbolSizeMeasurer.TryMeasure(oPage, _logger,
                _oSettings.SymbolLibrary, _oSettings.SymbolName, _oSettings.VariantH,
                out dSizeWH, out dSizeHH, out dOffXH, out dOffYH);
            double dSizeWV, dSizeHV, dOffXV, dOffYV;
            bool bMeasV = SymbolSizeMeasurer.TryMeasure(oPage, _logger,
                _oSettings.SymbolLibrary, _oSettings.SymbolName, _oSettings.VariantV,
                out dSizeWV, out dSizeHV, out dOffXV, out dOffYV);
            // [SYMSIZE-OFF] — новая INFO-строка замера (R8), только при успешном
            // замере (отказ: offset (0;0) — компенсация нулевая, WARN уже в [SYMSIZE]).
            if (bMeasH)
                _logger.Log("[INFO] [SYMSIZE-OFF] '" + _oSettings.SymbolLibrary + "/" +
                    _oSettings.SymbolName + "/" + _oSettings.VariantH.ToString(CultureInfo.InvariantCulture) +
                    "': dx=" + dOffXH.ToString("F3", CultureInfo.InvariantCulture) + ", dy=" +
                    dOffYH.ToString("F3", CultureInfo.InvariantCulture));
            if (bMeasV)
                _logger.Log("[INFO] [SYMSIZE-OFF] '" + _oSettings.SymbolLibrary + "/" +
                    _oSettings.SymbolName + "/" + _oSettings.VariantV.ToString(CultureInfo.InvariantCulture) +
                    "': dx=" + dOffXV.ToString("F3", CultureInfo.InvariantCulture) + ", dy=" +
                    dOffYV.ToString("F3", CultureInfo.InvariantCulture));
            double dSymW, dSymH;
            SymbolPlacementMath.SizeForOrientation(dSizeWH, dSizeHH, dSizeWV, dSizeHV,
                bVerticalSym, out dSymW, out dSymH);
            double dSymOffX = bVerticalSym ? dOffXV : dOffXH;
            double dSymOffY = bVerticalSym ? dOffYV : dOffYH;
            int nSymbolVariant = SymbolPlacementMath.VariantForOrientation(
                _oSettings.VariantH, _oSettings.VariantV, bVerticalSym);
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
                // rev.12.2 (H-3): ориентация — РАЗРЕШЁННОЕ значение (override или авто-детекция).
                bool bVerticalK4 = eOrientation == ReportOrientation.Vertical;
                double dExtreme = bVerticalK4 ? double.PositiveInfinity : double.NegativeInfinity;
                foreach (double dCol in oAnalysis.K4.Columns)
                {
                    if (bVerticalK4) { if (dCol < dExtreme) dExtreme = dCol; }
                    else if (dCol > dExtreme) dExtreme = dCol;
                }
                if (!double.IsInfinity(dExtreme)) dStripEndAxis = dExtreme;
            }
            CableGeometryResult oGeom = CableGeometryBuilder.Build(
                oLayout, eOrientation, oGeomCfg, dStripEndAxis);

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
            // rev.13.1 (H-4, R8): UI-вставка — тройка из настроек, вариант фактической
            // ориентации + его компенсация центра (dx,dy) из замера [SYMSIZE-OFF].
            int nSymbols = CableSymbolCreator.CreateSymbols(oPage, oGeom, _logger,
                _oSettings.SymbolLibrary, _oSettings.SymbolName, nSymbolVariant,
                dSymOffX, dSymOffY);
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
            return true;
        }

        private static short SafeLayerId(GraphicalPlacement oPlacement)
        {
            try { return oPlacement.LayerId; }
            catch { return (short)-1; }
        }

        /// <summary>H-3 (rev.12.2): суммарный габарит дерева отчёта для bbox-fallback
        /// авто-ориентации — min/max по GetBoundingBox() ВСЕХ объектов lstAll. KB 2.9
        /// (www.eplan.help | Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.Placement~
        /// GetBoundingBox.html): метод virtual у базового Placement, возвращает ровно
        /// 2 точки — [0] левый-нижний, [1] правый-верхний; бросает при Page==null, у
        /// PlaceHolder/DimensionItem/DimensionCircle и Text нулевой длины — каждое
        /// чтение в try/catch, сбойный объект пропускается. Возврат true — хотя бы
        /// один box прочитан; dWidth/dHeight — габарит дерева (может быть 0), при
        /// false — NaN.</summary>
        private static bool TryTreeBoundingBoxExtent(List<Placement> lstAll,
            out double dWidth, out double dHeight)
        {
            double dMinX = double.PositiveInfinity;
            double dMinY = double.PositiveInfinity;
            double dMaxX = double.NegativeInfinity;
            double dMaxY = double.NegativeInfinity;
            bool bAny = false;
            foreach (Placement oPlacement in lstAll)
            {
                if (oPlacement == null) continue;
                PointD[] arrBox;
                try { arrBox = oPlacement.GetBoundingBox(); }
                catch { continue; }
                if (arrBox == null || arrBox.Length < 2) continue;
                double dLoX = arrBox[0].X, dLoY = arrBox[0].Y;
                double dHiX = arrBox[1].X, dHiY = arrBox[1].Y;
                if (double.IsNaN(dLoX) || double.IsNaN(dLoY) ||
                    double.IsNaN(dHiX) || double.IsNaN(dHiY)) continue;
                bAny = true;
                if (dLoX < dMinX) dMinX = dLoX;
                if (dLoY < dMinY) dMinY = dLoY;
                if (dHiX > dMaxX) dMaxX = dHiX;
                if (dHiY > dMaxY) dMaxY = dHiY;
            }
            dWidth = bAny ? dMaxX - dMinX : double.NaN;
            dHeight = bAny ? dMaxY - dMinY : double.NaN;
            return bAny;
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

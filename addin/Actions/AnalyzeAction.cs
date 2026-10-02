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
        // имени — SpikePickVariant (1=XEGedIaInsertSymRef, 2=XEGActionInsertSymRef; в rev.13.4
        // классы TERMINAL_STRIP_PICK_SPIKE/…2 были зарегистрированы одновременно — с rev.13.7
        // класс-2 удалён, атрибут класса-1 — override-паттерн); legacy cli.Execute —
        // fallback сравнения. Факт rev.13.3: cli.Execute(<имя>) вернул False без диалога.
        // rev.13.5: SpikePickVariant=2 — тоже False (прогон 26.09). rev.13.6 (SPIKE-4):
        // имя класса → False, системное XEGedIaInsertSymRef → True (диалог, механизм жив).
        // rev.13.7 (SPIKE-5): override-паттерн 50/20 — запуск системного имени True,
        // но OnSuccess НЕ вызван (маршрутизации в наш класс нет).
        // rev.13.8 (SPIKE-6, прогон): сканирование при старте есть (.cctor/.ctor);
        // ручная вставка → OnStart/OnSuccess; InsertedPlacements Public 1 эл.;
        // тройка напрямую SymbolLibraryName/SymbolName/VariantNr; placed = EObjects.Cable;
        // XGedStartInteractionAction bypass-ит override.
        // rev.13.9 (SPIKE-7): GUI-экшен XEGActionInsertSymRef — маршрутизация через override?
        // rev.13.9 (SPIKE-7, прогон): GUI-экшен МАРШРУТИЗИРУЕТСЯ через override (OnStart
        // в пробах) — серия SPIKE-1..7 закрыта; П.82: Execute async (размещение переживает
        // отмену MainDialog). rev.13.10 (SPIKE-8): ожидание OnStop + OnCancel-пробы +
        // autorestart off.
        // rev.13.10 (SPIKE-8, прогоны п.84): модель О confirm; найден диалог свойств после
        // размещения (base.OnSuccess) — лишний UX. rev.13.11 (SPIKE-9): skip-base в
        // CaptureActive + PromptForStatusLine.
        private const string BUILD_STAMP = "2026-10-01 Этап 8 rev.16.2 прод-волна (BP точка разрыва «8/BP» SPECIAL: вставка SymbolReference.Create→InterruptionPoint (Function.Create=S063085 навсегда, спайк S1); наборы .emc point/ — скан + парс A2453(вариант)/A2454(имя), выбор по kind-константе И варианту, Import из чужого варианта — тихий no-op (S2), Import идемпотентен на каждый BP + Selected по A2454 (S1); ОУ полное — путь B Sepla (S4): LockObject + NameParts (структура 1100/1400/1200/1600 + код 20013 + счётчик 20014) + AdjustVisibleName; папка point/ — bin → CodeBase (shadow-copy, S1) → каталоги лога; multi — ДВА BP (G/F на дальнем конце шины + A/H на острие линии, стрелка у BP-линий не строится — стенд 01.10); Формат блока 20202[x] УДАЛЁН целиком (реш. 01.10: запись BlockFormat*, слот, сверка 1429, тесты) — чтение 20376/20377 ОСТАЛОСЬ (BP-классификация); rev.16.1: чтение 20376/20377 только с главного определения (#20122 TRUE; 20064-двойники меняли значения — стенд p1/p2); rev.16.0–15.x: см. логи/коммиты) ";

        // rev.14.14: однократная установка хуков исключений + статическая ссылка
        // на логгер текущего прогона (хуки статические — экземпляра в них нет).
        private static bool _bCrashHandlersInstalled = false;
        private static DiagnosticLogger _oLoggerStatic = null;

        // Заголовок MessageBox'ов UI-ветки — как Text диалога (MainDialog).
        private const string UI_CAPTION = "Генерация схемы подключений клеммника";

        private readonly DiagnosticLogger _logger = new DiagnosticLogger();

        // Фаза H (rev.12.0, H-1): настройки, загруженные при старте действия.
        // Пайплайном пока НЕ потребляются (поведение headless без изменений);
        // читаются сводкой [SETTINGS] после загрузки; потребители
        // появятся в H-2 (диалог/режим UI).
        private AddInSettings _oSettings;

        // rev.16.2 (решение пользователя 01.10): точка вставки отчёта (клик
        // пользователя, TryPickInsertPoint) — ось этой точки задаёт хвост
        // шинного BP («точка вставки + 20мм» по оси шины). Дефолт — конфиг
        // (headless-путь не кликает).
        private double _dInsertPointX = AddInConfiguration.InsertX;
        private double _dInsertPointY = AddInConfiguration.InsertY;

        // H-2: кандидаты каталога настроек (те же, что у AddInSettings.Load в
        // Execute) — нужны UI-ветке для сохранения после успешной генерации.
        private string[] _arrSettingsDirs;

        public bool Execute(ActionCallingContext oActionCallingContext)
        {
            // rev.13.10: SPIKE-8 — модель событий: хук RunSymbolPickSpike ЗАПУСКАЕТ
            // XEGActionInsertSymRef (spike-1 паттерн: FindAction + Execute(пустой
            // ActionCallingContext) = полный диалог выбора символа), Execute возвращает
            // ДО завершения размещения (факт п.82 — async), затем цикл ожидания до
            // OnStop (docs 2.9: терминатор обеих веток — после OnSuccess ИЛИ OnCancel;
            // StopSignaled). IsAutorestartEnabled=false — размещение завершает
            // интеракцию без Esc. Читалки [IA-PROBE] + [PICK-DUMP]/[PICK-ADAPT],
            // уборка [PICK-CLEAN] (при отмене PlacedObjects пуст — останца нет).
            // rev.13.11: SPIKE-9 — режим захвата CaptureActive: base.OnSuccess в
            // OnSuccess ПРОПУСКАЕТСЯ (нет диалога свойств после размещения — факт
            // п.84: диалог внутри base, пауза 2.56 с), подсказка о пробном
            // размещении — PromptForStatusLine в OnStart; флаг ставит хук перед
            // запуском, снимает сразу после цикла ожидания. Вне флага — обычная
            // вставка штатно (диалог на месте).
            _logger.BeginRun("TERMINAL_STRIP_ANALYZE — Этап 8 rev.16.2 прод-волна (см. BUILD_STAMP)", BUILD_STAMP);

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
            // rev.16.3 (Task 3): BP-решения — ОСНОВНОЙ источник концов = свойства
            // СОЕДИНЕНИЙ ЖИЛ №31019/№31020 (oDm.CableCoreEnds, задача 2) →
            // BreakPointResolver.Decide по списку DT; жил нет → fallback на 20376/20377
            // (DecideLegacy, ленивое перечисление ResolveCableEnds). Своя сторона =
            // полное ОУ клеммника = AddInConfiguration.TargetStripName (№20006).
            // out-параметр с концами кабеля УДАЛЁН: DT точки разрыва
            // теперь берётся из решения (BreakPointDecision.OppositeDt).
            List<bool> lstBpFlags;
            List<bool> lstBpMulti;
            List<BreakPointDecision> lstDecisions;
            string strStripOwnDt = AddInConfiguration.TargetStripName;
            PrepareBreakPointDecisions(oLayout, oProject, strStripOwnDt, oDm, _logger,
                out lstBpFlags, out lstBpMulti, out lstDecisions);

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
            // rev.16.2: ось ТОЧКИ ВСТАВКИ ОТЧЁТА (headless — конфиг InsertX/Y) —
            // хвост шинного BP тянется до «ось вставки + 20мм» в сторону отчёта.
            oGeomCfg.InsertOriginAxisMm = AddInConfiguration.Orientation == ReportOrientation.Vertical
                ? AddInConfiguration.InsertY
                : AddInConfiguration.InsertX;
            CableGeometryResult oGeom = CableGeometryBuilder.Build(
                oLayout, AddInConfiguration.Orientation, oGeomCfg, dStripEndAxis,
                lstBpFlags, lstBpMulti);   // rev.16.2: BP-списки (фича в двух режимах)

            // rev.16.3 (Task 3): начинка BreakPointPlacement (OppositeDt/Kind) —
            // ИСТОЧНИК DT — САМО РЕШЕНИЕ (OppositeDt): его заполняет Decide по
            // списку DT жил 31019/31020 либо DecideLegacy + вызывающий (fallback
            // 20376/20377). Потребляет BreakPointSymbolCreator.
            for (int i = 0; i < oGeom.BreakPoints.Count; i++)
            {
                if (oGeom.BreakPoints[i] == null) continue;
                int nIdx = oGeom.BreakPoints[i].CableIndex;
                if (nIdx < 0 || nIdx >= lstDecisions.Count) continue;
                BreakPointDecision oDec = lstDecisions[nIdx];
                if (oDec == null) continue;
                oGeom.BreakPoints[i].OppositeDt = oDec.OppositeDt;
                oGeom.BreakPoints[i].Kind = oDec.Kind;
            }

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
            // rev.16.2: вставка точек разрыва BP (фича в обоих режимах; straight —
            // стрелку у линий не рисовать уже учтено в ReferenceArrowCreator).
            int nBp = BreakPointSymbolCreator.CreateBreakPoints(oPage, oGeom,
                AddInConfiguration.Orientation == ReportOrientation.Vertical,
                BreakPointSymbolCreator.ResolvePointFolder(), _logger);
            _logger.Summarize("Фаза G: линий " + nLines + "/" + oGeom.Segments.Count +
                ", символов " + nSymbols + "/" + oGeom.Symbols.Count +
                ", ссылок " + nRefs + "/" + oGeom.References.Count +
                ", BP " + nBp + "/" + oGeom.BreakPoints.Count +
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

            // rev.14.14 (диагностика крэшей): глобальные хуки исключений UI-потока
            // и домена — в лог попадает managed-стек при падении диалогов.
            // Нативный AV (0xC0000005) сюда НЕ доходит — его ловит автосброс
            // лога (FlushPartial): хвост показывает последнюю пробу перед смертью.
            // Подписка ОДИН раз за процесс; логгер обновляется КАЖДЫЙ прогон
            // (rev.14.14 ревью I-1: иначе падение второго прогона уехало бы в
            // лог первого). Флаг — только после успешных подписок (ревью I-2:
            // отказ += не блокирует повторную установку).
            _oLoggerStatic = _logger;
            if (!_bCrashHandlersInstalled)
            {
                try
                {
                    System.Windows.Forms.Application.ThreadException += OnUiThreadException;
                    AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
                    _bCrashHandlersInstalled = true;
                    _logger.Log("[INFO] [BR] хуки исключений установлены");
                }
                catch (Exception oHookEx)
                {
                    _logger.Warn("[UIERR] хуки исключений не установлены: " + oHookEx.GetType().Name);
                }
            }

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
                // rev.15.13: список форм = скан каталога из настроек (MD_FORMS
                // активной схемы); пулов больше нет — текст честно про каталог.
                _logger.Fail("Формы *.f11 в каталоге настроек (MD_FORMS) не найдены — диалог невозможен.");
                MessageBox.Show("Не найдено ни одной формы (*.f11) в каталоге форм, " +
                    "заданном в настройках EPLAN (Опции > Настройки > Пользователь > " +
                    "Управление > Каталоги).\n\nПроверьте активную схему настроек и " +
                    "наличие форм в её каталоге форм.",
                    UI_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _logger.Log("[INFO] Данные для диалога: клеммников " + lstStripNames.Count +
                ", форм " + lstFormNames.Count + ".");

            // --- 2b. rev.17 (Task 7, план 2026-10-01-ui-emc-profiles): каталог
            //        профилей .emc (point/*.emc) по варианту символа BPIN (A=0,
            //        D=3, E=4, B=1) для ComboBox'ов диалога. Папка/каталог не
            //        найдены — ForVariant(null) даёт пустые списки (диалог всё
            //        равно показывается; BP без набора — штатная деградация).
            //        Скан тот же, что у BreakPointSymbolCreator (ResolvePointFolder). ---
            string strPointFolder = BreakPointSymbolCreator.ResolvePointFolder();
            List<EmcSchemeInfo> lstEmcProfiles = EmcSchemeCatalog.ParseDirectory(strPointFolder, _logger);
            Dictionary<int, List<EmcSchemeInfo>> dicProfilesByVariant =
                new Dictionary<int, List<EmcSchemeInfo>>();
            dicProfilesByVariant[0] = EmcProfileCatalog.ForVariant(lstEmcProfiles, 0);
            dicProfilesByVariant[3] = EmcProfileCatalog.ForVariant(lstEmcProfiles, 3);
            dicProfilesByVariant[4] = EmcProfileCatalog.ForVariant(lstEmcProfiles, 4);
            dicProfilesByVariant[1] = EmcProfileCatalog.ForVariant(lstEmcProfiles, 1);
            _logger.Log("[INFO] Профили .emc: всего " +
                (lstEmcProfiles == null ? 0 : lstEmcProfiles.Count) +
                ", по вариантам A=" + dicProfilesByVariant[0].Count +
                " D=" + dicProfilesByVariant[3].Count +
                " E=" + dicProfilesByVariant[4].Count +
                " B=" + dicProfilesByVariant[1].Count + ".");

            // --- 3. Цикл диалога: Отмена/закрытие — выход; «Создать» — пайплайн;
            //        провал создания отчёта — сообщение и заново диалог ---
            EmbeddedReportReader oReader = new EmbeddedReportReader(_logger);
            // rev.13.1 (H-4, R10): проект — браузеру символа (кнопка активна);
            // null — кнопка осталась бы выключенной (не наш проект — не наш случай).
            // rev.17 (Task 7): первый аргумент — ДЕРЕВО клеммников (структура ОУ
            // в промежуточных узлах, полные ОУ в листьях); его сборка ведётся по
            // тому же обходу, что lstStripNames (гейт пустоты — по lstStripNames).
            MainDialog oDialog = new MainDialog(
                StripStructureTree.Build(CollectStripInputs(oProject)),
                lstFormNames, _oSettings, oProject, _logger, dicProfilesByVariant);
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

                    // --- 3c. rev.15.0 (H-5/H-6): интерактивная точка вставки —
                    //        после «Создать» пользователь кликает точку на странице
                    //        (рамка-призрак под курсором; Esc/таймаут → заново диалог,
                    //        НИЧЕГО не создаётся). Константная точка конфигу
                    //        (InsertX/InsertY) — только при выключенном гейте;
                    //        headless-путь не затронут. ---
                    double dPointX = 0.0;
                    double dPointY = 0.0;
                    bool bPointPicked = TryPickInsertPoint(oProject, oPage, oTargetStrip,
                        strForm, eMode, oReader, strStrip, out dPointX, out dPointY);
                    if (bPointPicked)
                    {
                        // rev.16.2: точка вставки отчёта — для хвоста шинного BP.
                        _dInsertPointX = dPointX;
                        _dInsertPointY = dPointY;
                    }
                    if (!bPointPicked)
                    {
                        if (InsertPointInteraction.Cancelled || InsertPointInteraction.EscPressed)
                        {
                            // Отмена (Esc) — без MessageBox: пользователь сам решил.
                            // Итерация 2 (01.10.2026): фильтр ставит Cancelled мгновенно
                            // (TryMarkCancelled); EscPressed — страховка на путь, где
                            // Invoke вернулся раньше движкового OnCancel.
                            _logger.Log("[MODE] выбор точки отменён (Esc)");
                        }
                        else
                        {
                            // Таймаут / провал запуска / выключенный гейт — сообщение.
                            MessageBox.Show(oDialog, "Не удалось получить точку вставки " +
                                "(таймаут или запуск интеракции не удался — см. [IPING] в логе).\n\n" +
                                "Задайте точку ещё раз либо отмените генерацию.",
                                UI_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        continue;   // заново показать диалог (ничего не создано)
                    }

                    try
                    {
                        // --- 4. Отчёт на ВЫБРАННОЙ форме (override; «чужая форма»
                        //        в UI-режиме не проверяется); rev.12.5 (H-3b): цель —
                        //        выбранный TerminalStrip (4-арг. CreateEmbeddedReport);
                        //        rev.15.0 (H-5/H-6): точка — клик пользователя ---
                        ReportBlockReference oReportRef =
                            oReader.TryCreateEmbeddedReport(oProject, oPage, null, strForm, false,
                                new StorableObject[] { oTargetStrip },
                                new PointD(dPointX, dPointY));
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
                            // rev.17 (Task 7): выбор 6 профилей .emc слотами BP
                            // (строка 'файл|имя схемы' либо null — нет выбора).
                            _oSettings.EmcStripH = oDialog.SelectedEmcProfile(BpProfileSlot.StripH);
                            _oSettings.EmcStripV = oDialog.SelectedEmcProfile(BpProfileSlot.StripV);
                            _oSettings.EmcDeviceH = oDialog.SelectedEmcProfile(BpProfileSlot.DeviceH);
                            _oSettings.EmcDeviceV = oDialog.SelectedEmcProfile(BpProfileSlot.DeviceV);
                            _oSettings.EmcLinkH = oDialog.SelectedEmcProfile(BpProfileSlot.LinkH);
                            _oSettings.EmcLinkV = oDialog.SelectedEmcProfile(BpProfileSlot.LinkV);
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

        /// <summary>Фаза выбора точки вставки (Этап 8, H-5/H-6, rev.15.0; spec §6):
        /// создаёт рамку-призрак (GhostFrameCreator), запускает интеракцию
        /// TSA_INSERT_POINT (XGedStartInteractionAction + ctx.AddParameter("Name",...)
        /// — паттерн docs-примера KB ...Ged.Interaction.html; reflection — как в
        /// RunSymbolPickSpike [PICK-EXEC8]) и ждёт клик/отмену в цикле DoEvents+
        /// Sleep(50) до капа AddInConfiguration.SelectPointTimeoutSec (модель SPIKE-8).
        /// Призрак удаляется В КАЖДОМ исходе (и не запущен, и успех, и отмена,
        /// и таймаут — [GHOST-CLEAN]; урок п.48/паттерн [PICK-CLEAN]). Отклонение от
        /// брифа: параметр MainDialog oDialog исключён — MessageBox'ы решает
        /// вызывающая сторона RunUi (отмена — молча, таймаут — с окном).
        /// rev.15.5 (решение пользователя 30.09): параметр EmbeddedReportReader
        /// oReader (создаётся в RunUi) — ДО создания призрака читаются метрики
        /// шаблона формы TryGetFormTemplateMetrics(oProject, strForm) — ПАРСИНГ
        /// ФАЙЛА ФОРМЫ .f11 ([GHOST-SIZE]; PlotFrame.Size опровергнут: форма —
        /// мастер-данные, страницы формы в проекте может не быть). Успех —
        /// ALONG = шапка + N_строк×колонка_данных + футер +
        /// GhostCableGapMm (зазор для символов кабелей), ACROSS = высота
        /// шаблона; N строк = Ext+Int клеммника = ConnCount из DmReport
        /// (решение пользователя 30.09; факт X3: 20 строк × 7 мм = 140 мм
        /// данных) — DmReport читается ТОЛЬКО при успехе парсинга .f11,
        /// отказ Read/клеммника нет в PerStrip → фоллбэк nRows=nTerminals.
        /// Маппинг dLong=ALONG, dShort=ACROSS —
        /// CreateGhostFrame даёт итог dX=ALONG для Horizontal и dX=ACROSS для
        /// Vertical (призрак = след формы без поворота); расхождение направления
        /// данных шаблона с ориентацией — WARN «шаблон/имя расходятся»,
        /// значения КАК ЕСТЬ. Отказ парсинга — фоллбэк-эвристика
        /// nTerminals×pitch (деградация штатная). oProject потребляется
        /// разбором шаблона.
        /// true — точка получена (dPointX/dPointY; гейт OFF — константная точка
        /// InsertX/InsertY, легаси-поведение UI); false — отмена (Esc — теперь
        /// реальна: EscCancelFilter через DoEvents-насос; движковый OnCancel по
        /// Esc не приходит — факт стенда 01.10.2026; интеракция остаётся активной
        /// в движке до следующего клика/«Создать» — как при таймауте, M2.4),
        /// таймаут, провал запуска, завершение без точки — вызывающая сторона
        /// решает (MessageBox для таймаута/провала, заново диалог; ничего НЕ
        /// создаётся).
        /// Исключения наружу не выпускаются — false.</summary>
        private bool TryPickInsertPoint(Project oProject, Page oPage,
            TerminalStrip oTargetStrip, string strForm, SettingsOrientation eMode,
            EmbeddedReportReader oReader, string strStrip,
            out double dPointX, out double dPointY)
        {
            dPointX = 0.0;
            dPointY = 0.0;
            if (!AddInConfiguration.UseInsertPointPick)
            {
                // m1-фикс (ревью rev.15.0): гейт OFF — легаси-поведение фиксированной
                // точки в UI (как было до H-5), а НЕ блокировка генерации из UI.
                dPointX = AddInConfiguration.InsertX;
                dPointY = AddInConfiguration.InsertY;
                _logger.Log(string.Format(CultureInfo.InvariantCulture,
                    "[INFO] [IPING] выключено (UseInsertPointPick=false) — константная точка ({0:F1}; {1:F1})",
                    dPointX, dPointY));
                return true;
            }

            // --- 1. Число клемм (идиома EplanTerminalStripReader.cs:86-90). ---
            int nTerminals = 0;
            try
            {
                Terminal[] arrTerminals = oTargetStrip.Terminals;
                if (arrTerminals == null) arrTerminals = new Terminal[0];
                nTerminals = arrTerminals.Length;
            }
            catch (Exception oTermEx)
            {
                _logger.Log("[INFO] [IPING] Terminals бросил " + oTermEx.GetType().Name +
                    ": " + oTermEx.Message + " — клемм 0 (призрак минимальной длины)");
                nTerminals = 0;
            }

            // --- 2. Pitch: настройки → фоллбэк конфигу. ---
            double dPitch;
            bool bPitGot = _oSettings.TryGetGridPitch(strForm, out dPitch);
            if (!bPitGot) dPitch = AddInConfiguration.GhostPitchFallbackMm;
            _logger.Log(string.Format(CultureInfo.InvariantCulture,
                "[INFO] [IPING] pitch={0:F2} мм ({1}), клемм {2}", dPitch,
                (bPitGot ? "настройки GridPitch" : "фоллбэк GhostPitchFallbackMm"),
                nTerminals));

            // --- 3. Ориентация призрака: явный выбор пользователя > формы ---
            ReportOrientation eOrient = GhostFrameMath.ResolveOrientation(eMode, strForm);
            _logger.Log("[INFO] [IPING] ориентация=" +
                (eOrient == ReportOrientation.Vertical ? "Vertical" : "Horizontal") +
                " (режим " + eMode + ", форма '" + strForm + "')");
            // --- 3b. rev.15.5: размер призрака из ПАРСИНГА ФАЙЛА ФОРМЫ .f11
            //        ([GHOST-SIZE]; решение пользователя 30.09 — PlotFrame.Size
            //        опровергнут: форма — мастер-данные, страницы формы в проекте
            //        может не быть). Метрики шаблона: шапка + N×колонка_данных +
            //        футер + зазор GhostCableGapMm — вдоль направления данных
            //        (ALONG); высота шаблонных блоков — поперёк (ACROSS).
            //        N строк = Ext+Int клеммника = ConnCount из DmReport
            //        (решение пользователя 30.09) — читается ниже ТОЛЬКО при
            //        успехе парсинга .f11; отказ/нет в PerStrip → nTerminals.
            //        Маппинг (CreateGhostFrame: dX = Vertical?dShort:dLong):
            //        dLong=ALONG, dShort=ACROSS — итог dX=ALONG для Horizontal
            //        и dX=ACROSS для Vertical (призрак = след формы без поворота).
            //        Направление данных шаблона должно совпадать с ориентацией
            //        призрака (Horizontal → вдоль X) — расхождение — WARN
            //        «шаблон/имя расходятся», значения КАК ЕСТЬ (по шаблону,
            //        не по имени). Отказ парсинга — прежняя эвристика
            //        nTerminals×pitch (там маппинг CreateGhostFrame осмыслен:
            //        длинная/короткая). Ориентация призрака — из выравнивания
            //        формы (P13008) при успехе парсинга; ResolveOrientation
            //        (имя) — фоллбэк. ---
            double dLong;
            double dShort;
            // N строк данных шаблона (решение пользователя 30.09): Ext+Int
            // клеммника = ConnCount из DmReport (факт X3: 20 строк × 7 мм =
            // 140 мм данных). Свойства ConnCount у TerminalStrip в API 2.9
            // НЕТ — «ConnCount» в аналайзе — поле локального DmStripStats.
            // Читается НИЖЕ один Read на вызов, ТОЛЬКО при успехе парсинга
            // .f11 (не тратить Read при фоллбэке); отказ Read/клеммника нет
            // в PerStrip/исключение → фоллбэк nRows=nTerminals + INFO-лог.
            int nRows = nTerminals;
            double dHeaderMm;
            double dDataColMm;
            double dFooterMm;
            double dAcrossMm;
            bool bDataAlongX;
            bool bFormByColumns;
            if (oReader != null &&
                oReader.TryGetFormTemplateMetrics(oProject, strForm, out dHeaderMm,
                    out dDataColMm, out dFooterMm, out dAcrossMm, out bDataAlongX,
                    out bFormByColumns))
            {
                // rev.15.7: ориентация призрака — из ВЫРАВНИВАНИЯ ФОРМЫ
                // (P13008) при успехе парсинга (решение пользователя 30.09:
                // форма авторитетнее имени); ResolveOrientation (имя/диалог) —
                // фоллбэк (эвристика/сбой парсинга). eOrient объявлена выше
                // (шаг 3) и используется ниже (маппинг ALONG/ACROSS, WARN,
                // CreateGhostFrame) — присваиваем заново, НЕ переобъявляем.
                ReportOrientation eFormOrient = bFormByColumns
                    ? ReportOrientation.Horizontal
                    : ReportOrientation.Vertical;
                _logger.Log("[INFO] [GHOST-SIZE] выравнивание формы: " +
                    (bFormByColumns ? "по столбцам → Horizontal" : "по строкам → Vertical") +
                    " — заменяет ориентацию по имени (" +
                    (eOrient == ReportOrientation.Vertical ? "Vertical" : "Horizontal") +
                    ")");
                eOrient = eFormOrient;
                // DmReport — ОДИН Read на вызов TryPickInsertPoint, только при
                // успехе парсинга .f11 (эталон-идиома аналайзера:1485-1492:
                // PerStrip.TryGetValue(полное имя) → ConnCount = Ext+Int).
                try
                {
                    EplanTerminalStripReader oDmReader =
                        new EplanTerminalStripReader(_logger, strStrip);
                    DmReport oDm = oDmReader.Read(oProject);
                    DmStripStats oStats = null;
                    if (oDm != null && oDm.PerStrip != null &&
                        oDm.PerStrip.TryGetValue(strStrip, out oStats) &&
                        oStats != null)
                    {
                        nRows = oStats.ConnCount;
                        _logger.Log(string.Format(CultureInfo.InvariantCulture,
                            "[INFO] [GHOST-SIZE] N строк данных шаблона = Ext+Int = ConnCount: {0} (Ext={1}, Int={2})",
                            oStats.ConnCount, oStats.ExtCount, oStats.IntCount));
                    }
                    else
                    {
                        nRows = nTerminals;
                        _logger.Log("[INFO] [GHOST-SIZE] клеммник '" + strStrip +
                            "' не найден в PerStrip DmReport — фоллбэк nRows=nTerminals: " +
                            nRows.ToString(CultureInfo.InvariantCulture));
                    }
                }
                catch (Exception oDmEx)
                {
                    nRows = nTerminals;
                    _logger.Log("[INFO] [GHOST-SIZE] DmReport бросил " +
                        oDmEx.GetType().Name + ": " + oDmEx.Message +
                        " — фоллбэк nRows=nTerminals");
                }
                double dAlongMm;
                GhostFrameMath.ComputeFromTemplateMetrics(dHeaderMm, dDataColMm,
                    dFooterMm, dAcrossMm, nRows, AddInConfiguration.GhostCableGapMm,
                    out dAlongMm, out dShort);
                dLong = dAlongMm;
                bool bExpectAlongX = (eOrient == ReportOrientation.Horizontal);
                if (bDataAlongX != bExpectAlongX)
                    _logger.Warn("[GHOST-SIZE] шаблон/имя расходятся: данные шаблона вдоль " +
                        (bDataAlongX ? "X" : "Y") + ", ориентация призрака " +
                        (eOrient == ReportOrientation.Vertical ? "Vertical" : "Horizontal") +
                        " — используем размеры КАК ЕСТЬ (по шаблону)");
                _logger.Log(string.Format(CultureInfo.InvariantCulture,
                    "[INFO] [GHOST-SIZE] шаблон формы: шапка={0:F1} данные=колонка {1:F1} ×{2} строк + футер {3:F1} + зазор {4:F1} → ALONG={5:F1} ACROSS={6:F1} ({7})",
                    dHeaderMm, dDataColMm, nRows, dFooterMm,
                    AddInConfiguration.GhostCableGapMm, dAlongMm, dShort,
                    (eOrient == ReportOrientation.Vertical ? "Vertical" : "Horizontal")));
            }
            else
            {
                dLong = GhostFrameMath.ComputeWidthMm(nTerminals, dPitch);
                dShort = GhostFrameMath.ComputeHeightMm();
                _logger.Log(string.Format(CultureInfo.InvariantCulture,
                    "[INFO] [GHOST-SIZE] фоллбэк эвристики: призрак {0:F1}×{1:F1} мм ({2} клемм × {3:F2} мм)",
                    dLong, dShort, nTerminals, dPitch));
            }

            // --- 4. Сброс статики, призрак, запуск интеракции ---
            InsertPointInteraction.Reset();
            PolyLine oGhost = GhostFrameCreator.CreateGhostFrame(oPage, eOrient, dLong, dShort, _logger);
            InsertPointInteraction.PendingGhost = oGhost;

            // Итерация 2b (01.10.2026): фильтр ставится ДО запуска экшена. ФАКТ
            // стенда: Esc ловился фильтром ВО ВРЕМЯ блокирующего Invoke
            // ([IPING-ESC] через ~2 с после запуска, возврат Invoke — через ~90 с),
            // поэтому окно жизни фильтра = запуск экшена + цикл ожидания.
            EscCancelFilter oEscFilter = new EscCancelFilter();
            System.Windows.Forms.Application.AddMessageFilter(oEscFilter);

            bool bLaunched = false;
            try
            {
                // Launch — reflection-паттерн docs-примера KB
                // (...Ged.Interaction.html): FindAction + reflection
                // Execute(ActionCallingContext) + ctx.AddParameter("Name", ...) —
                // ИМЕННО так запускается интеракция по имени (XGedStartInteractionAction).
                // Запасной путь (не используется): CLI-строка
                // new CommandLineInterpreter().Execute("XGedStartInteractionAction /Name:TSA_INSERT_POINT").
                ActionManager oManager = new ActionManager();
                var oLaunchAction = oManager.FindAction("XGedStartInteractionAction");
                if (oLaunchAction == null)
                {
                    _logger.Warn("[IPING] экшен XGedStartInteractionAction не найден");
                }
                else
                {
                    MethodInfo oExecMethod = oLaunchAction.GetType().GetMethod("Execute",
                        new Type[] { typeof(ActionCallingContext) });
                    if (oExecMethod == null)
                    {
                        _logger.Warn("[IPING] Execute(ActionCallingContext) не найден (reflection) — запуска не было");
                    }
                    else
                    {
                        // ctx.AddParameter("Name", ...) — docs-пример KB (...Ged.Interaction.html):
                        // oContext.AddParameter("Name","MyInteraction") + Execute. Факт run:
                        // запуск экшена из-под нашего действия доказан (p.82: Execute async).
                        // Факт стенда 01.10.2026: фактический возврат Invoke — ПОСЛЕ
                        // завершения цепочки интеракции (может блокировать минуты).
                        ActionCallingContext oCtx = new ActionCallingContext();
                        oCtx.AddParameter("Name", "TSA_INSERT_POINT");
                        object oRes = oExecMethod.Invoke(oLaunchAction, new object[] { oCtx });
                        _logger.Log("[INFO] [IPING-LAUNCH] возврат=" + DescribeCliReturn(oRes));
                        bLaunched = (oRes is bool && (bool)oRes);
                    }
                }
            }
            catch (Exception oLaunchEx)
            {
                // Invoke оборачивает исключение callee в TargetInvocationException —
                // разворачиваем вручную (нет exception-filters C#6 на легаси-csc).
                Exception oReal = oLaunchEx;
                TargetInvocationException oTie = oLaunchEx as TargetInvocationException;
                if (oTie != null && oTie.InnerException != null) oReal = oTie.InnerException;
                _logger.Log("[INFO] [IPING-LAUNCH] исключение: " + oReal.GetType().Name + ": " + oReal.Message);
            }

            if (!bLaunched)
            {
                // Фильтр снять ДО выхода (окно = запуск+цикл; finally цикла ниже
                // не выполнится на этом пути).
                System.Windows.Forms.Application.RemoveMessageFilter(oEscFilter);
                _logger.Warn("[IPING] запуск интеракции не удался (возврат/исключение — см. [IPING-LAUNCH])");
                GhostFrameCreator.RemoveGhost(oGhost, _logger);
                return false;
            }

            // --- 5. Цикл ожидания (модель SPIKE-8): DoEvents качает UI-очередь,
            //        события интеракции приходят в этом потоке; Sleep(50) — опрос;
            //        кап 120 с. CalledOnPoint из OnPoint; Done/Cancelled — сигнал
            //        OnStop/OnCancel. B1 (Track B 01.10.2026): Esc ловится фильтром
            //        EscCancelFilter — AddMessageFilter видит ВСЕ сообщения очереди
            //        ДО диспетчеризации, не зависит от фокуса GED (KB: WinForms
            //        IMessageFilter.PreFilterMessage); окно фильтра — запуск экшена
            //        + этот цикл (ставится до Invoke, снимается в finally цикла —
            //        и на раннем выходе !bLaunched выше).
            //        Итерация 2: ФАКТ стенда — Execute БЛОКИРУЕТ до конца цепочки
            //        (цикл стартует уже после возврата; мгновенную отмену делает
            //        фильтр через TryMarkCancelled прямо в насосе Execute,
            //        цикл — страховка). ---
            DateTime oDeadline = DateTime.Now.AddSeconds(AddInConfiguration.SelectPointTimeoutSec);
            DateTime oWaitStart = DateTime.Now;
            try
            {
                while (!InsertPointInteraction.Captured && !InsertPointInteraction.Cancelled &&
                    !InsertPointInteraction.Done && !InsertPointInteraction.EscPressed &&
                    DateTime.Now < oDeadline)
                {
                    System.Windows.Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(50);
                }
            }
            catch (Exception oWaitEx)
            {
                // DoEvents диспатчит чужие обработчики — их исключение НЕ должно
                // ронять пайплайн (паттерн Ревью rev.13.10, [PICK-EXEC8]).
                _logger.Log("[INFO] [IPING-WAIT] цикл ожидания бросил: " +
                    oWaitEx.GetType().Name + ": " + oWaitEx.Message);
            }
            finally
            {
                // Снятие фильтра — при ЛЮБОМ выходе из цикла (точка/отмена/стоп/
                // Esc/таймаут/исключение): иначе Esc перехватывался бы и в других
                // фазах (диалоги, следующая генерация).
                System.Windows.Forms.Application.RemoveMessageFilter(oEscFilter);
            }
            double dWaitSec = Math.Round((DateTime.Now - oWaitStart).TotalSeconds, 1);

            // --- 6. Диагностика: файл пробы + буфер ([IPING-PROBE]/[IPING-DUMP]) ---
            try
            {
                string[] arrProbeDirs = new string[DiagnosticLogger.LOG_DIR_CANDIDATES.Length + 1];
                DiagnosticLogger.LOG_DIR_CANDIDATES.CopyTo(arrProbeDirs, 0);
                arrProbeDirs[arrProbeDirs.Length - 1] = System.IO.Path.GetTempPath();
                string strProbePath = null;
                foreach (string strDir in arrProbeDirs)
                {
                    string strCandidate = System.IO.Path.Combine(strDir, "insert_point_probe.log");
                    if (System.IO.File.Exists(strCandidate)) { strProbePath = strCandidate; break; }
                }
                if (strProbePath != null)
                {
                    string[] arrProbeLines = System.IO.File.ReadAllLines(strProbePath);
                    int nProbeFirst = 0;
                    if (arrProbeLines.Length > 120)
                    {
                        _logger.Log("[INFO] [IPING-PROBE] (всего строк " +
                            arrProbeLines.Length.ToString(CultureInfo.InvariantCulture) +
                            ", показаны последние 120)");
                        nProbeFirst = arrProbeLines.Length - 120;
                    }
                    for (int i = nProbeFirst; i < arrProbeLines.Length; i++)
                        _logger.Log("[INFO] [IPING-PROBE] " + arrProbeLines[i]);
                }
                else
                {
                    _logger.Log("[INFO] [IPING-PROBE] файл пробы не найден — записей нет");
                }
                int nDumpTotal = InsertPointInteraction.IPingDump.Count;
                for (int i = 0; i < nDumpTotal; i++)
                    _logger.Log("[INFO] [IPING-DUMP] " + InsertPointInteraction.IPingDump[i]);
            }
            catch (Exception oDumpEx)
            {
                // Сбор проб не влияет на успех фазы (ruling: Warn не фейлит).
                _logger.Log("[INFO] [IPING] исключение чтения проб: " +
                    oDumpEx.GetType().Name + ": " + oDumpEx.Message);
            }

            // --- 7. Истинный таймаут/Esc (Done=false по дедлайну либо EscPressed
            //        от фильтра): интеракция МОЖЕТ оставаться активной с
            //        рамкой-призраком под курсором — курсорную отрисовку снимаем
            //        ДО удаления объекта (KB 2.9 ClearCursor — «Remove
            //        Cursor-Representation»). Esc (B1, Track B 01.10.2026) —
            //        интеракция НЕ завершилась сама (движковый OnCancel по Esc не
            //        приходит, факт стенда 01.10.2026), курсорную копию снимаем
            //        мы; при B2-1 курсорная копия переживает удаление объекта —
            //        снять её ОБЯЗАТЕЛЬНО. Успех/отмена/стоп — отрисовку снимает
            //        сама интеракция; добавка || EscPressed покрывает и угол
            //        Esc+успех — повторный ClearCursor безопасен (try/catch,
            //        штатная деградация). ---
            if ((!InsertPointInteraction.Captured && !InsertPointInteraction.Cancelled &&
                !InsertPointInteraction.Done) || InsertPointInteraction.EscPressed)
            {
                InsertPointInteraction.TryClearCursor();
            }

            // --- 8. Уборка призрака — ВСЕГДА (и успех, и отмена, и таймаут): ---
            GhostFrameCreator.RemoveGhost(oGhost, _logger);

            // --- 9. Исходы ---
            if (InsertPointInteraction.Captured)
            {
                dPointX = InsertPointInteraction.CapturedX;
                dPointY = InsertPointInteraction.CapturedY;
                _logger.Log("[INFO] [IPING-WAIT] исход: point (" +
                    dPointX.ToString("F3", CultureInfo.InvariantCulture) + "; " +
                    dPointY.ToString("F3", CultureInfo.InvariantCulture) + "), ожидание=" +
                    dWaitSec.ToString(CultureInfo.InvariantCulture) + " сек");
                return true;
            }
            if (InsertPointInteraction.Cancelled)
            {
                _logger.Log("[INFO] [IPING-WAIT] исход: cancel, ожидание=" +
                    dWaitSec.ToString(CultureInfo.InvariantCulture) + " сек");
                return false;
            }
            // B1 (Track B 01.10.2026): Esc поймал фильтр (движковый OnCancel по
            // Esc не приходит — факт стенда 01.10.2026); в углу гонки
            // Esc+завершение исход тот же (false), курсорную копию снял
            // раздел 7 / движок.
            if (InsertPointInteraction.EscPressed)
            {
                _logger.Log("[INFO] [IPING-WAIT] исход: Esc (фильтр), ожидание=" +
                    dWaitSec.ToString(CultureInfo.InvariantCulture) + " сек");
                return false;
            }
            // m2-фикс (ревью rev.15.0): Done==true БЕЗ точки — интеракция завершилась
            // сама (OnStop), но координат дал, например, исключение внутри OnPoint
            // (детали — в буфере [IPING-DUMP]/файле [IA-POINT]); это НЕ таймаут.
            if (InsertPointInteraction.Done)
            {
                _logger.Log("[INFO] [IPING-WAIT] исход: интеракция завершилась без точки " +
                    "(исключение OnPoint?), ожидание=" +
                    dWaitSec.ToString(CultureInfo.InvariantCulture) + " сек");
                return false;
            }
            // Истинный таймаут: дедлайн пройден, Done тоже false (M2.3, ревью).
            _logger.Warn("[IPING] ТАЙМАУТ " + AddInConfiguration.SelectPointTimeoutSec.ToString(CultureInfo.InvariantCulture) +
                " с (Done=" + (InsertPointInteraction.Done ? "True" : "False") +
                "), ожидание=" + dWaitSec.ToString(CultureInfo.InvariantCulture) +
                " сек — интеракция может остаться активной — закрывается следующим кликом/Esc");
            return false;
        }

        /// <summary>rev.14.14: необработанное исключение UI-потока WinForms —
        /// полный стек в лог + автосброс (e.Exception может быть null).</summary>
        private static void OnUiThreadException(object oSender,
            System.Threading.ThreadExceptionEventArgs oArgs)
        {
            DiagnosticLogger oLogger = _oLoggerStatic;
            if (oLogger == null) return;
            Exception oEx = oArgs == null ? null : oArgs.Exception;
            oLogger.Error("[BR] UI-исключение: " +
                (oEx == null ? "<null>" : oEx.GetType().FullName + ": " + oEx.Message +
                    "\n" + oEx.StackTrace));
        }

        /// <summary>rev.14.14: терминальное исключение домена — факт + тип в лог
        /// (процесс всё равно падает; автосброс Warn-канала уже сработал).</summary>
        private static void OnDomainUnhandledException(object oSender,
            UnhandledExceptionEventArgs oArgs)
        {
            DiagnosticLogger oLogger = _oLoggerStatic;
            if (oLogger == null) return;
            Exception oEx = oArgs == null ? null : oArgs.ExceptionObject as Exception;
            oLogger.Error("[BR] домен-исключение (isTerminating=" +
                (oArgs == null ? "?" : oArgs.IsTerminating.ToString()) + "): " +
                (oEx == null
                    ? (oArgs == null || oArgs.ExceptionObject == null
                        ? "<null>" : oArgs.ExceptionObject.ToString())
                    : oEx.GetType().FullName + ": " + oEx.Message + "\n" + oEx.StackTrace));
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

        /// <summary>rev.17 (Task 7, план 2026-10-01-ui-emc-profiles): вход
        /// построения дерева клеммников — для каждого TerminalStrip полное ОУ
        /// (тот же источник, что CollectStripNames: oStrip.Name) и четыре
        /// свойства структуры 1100 (Plant «=»)/1400 (MountingSite «++»)/
        /// 1200 (PlaceOfInstallation «+»)/1600 (UserStruct «#»), каждый — с
        /// подчинёнными сегментами (базовый + 1..9, склейка через '.';
        /// образец — CableSymbolCreator.WriteStructureSegments/SplitSegments,
        /// чтение — SafePropText-паттерн EplanTerminalStripReader 475–497).
        /// ГЕЙТ (контроллер): если собранный префикс НЕ согласуется с полным ОУ
        /// (частичное/нечитаемое чтение), все четыре части отдаются ПУСТЫМИ —
        /// StripStructureTree уйдёт в фоллбэк разбора FullName и НЕ построит
        /// частичную структуру. Дубликаты полного имени — как в
        /// CollectStripNames: первый побеждает + WARN [STRIPDUP].</summary>
        private List<StripNodeInput> CollectStripInputs(Project oProject)
        {
            List<StripNodeInput> lstResult = new List<StripNodeInput>();
            Page[] arrPages;
            try { arrPages = oProject.Pages; }
            catch (Exception oException)
            {
                _logger.Log("[UIERR] Project.Pages (дерево клеммников): " +
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
                    _logger.Log("[UIERR] Page.TerminalStrips (дерево, '" + strPageName + "'): " +
                        oException.GetType().Name + ": " + oException.Message);
                    continue;
                }
                if (arrStrips == null) continue;
                foreach (TerminalStrip oStrip in arrStrips)
                {
                    string strStripName = SafeText("<n/a>", () => oStrip.Name);
                    if (strStripName == null || strStripName.Length == 0 || strStripName == "<n/a>")
                    {
                        _logger.Log("[UIERR] TerminalStrip.Name (дерево, страница " + strPageName +
                            ") не читается — клеммник пропущен");
                        continue;
                    }
                    if (ContainsStripInput(lstResult, strStripName))
                    {
                        _logger.Warn("[STRIPDUP] дубликат полного имени клеммника '" + strStripName +
                            "' (дерево, страница " + strPageName + ") — пропущен");
                        continue;
                    }

                    // Структура: главный сегмент + подчинённые 1..9 (i<10 — как
                    // WriteStructureSegments). Пустое чтение — "".
                    string strPlant = ReadStructureValue(oStrip, 1100);
                    string strMount = ReadStructureValue(oStrip, 1400);
                    string strPlace = ReadStructureValue(oStrip, 1200);
                    string strUser = ReadStructureValue(oStrip, 1600);

                    // Гейт: префиксная склейка ('=' plant '++' mount '+' place
                    // '#' user, пустые уровни пропущены) обязана быть префиксом
                    // полного ОУ, а остаток ОУ — быть ПУСТЫМ (имя без устройства)
                    // либо начинаться с '-' (лист устройства всегда '-XT…').
                    // Любой иной остаток ('.' — оборванный подчинённый сегмент,
                    // ещё структурный уровень) = неполное/несогласованное чтение.
                    // Не сошлось — все четыре части пусты → фоллбэк дерева.
                    string strPrefix = StructurePrefix(strPlant, strMount, strPlace, strUser);
                    if (strPrefix.Length == 0 ||
                        !strStripName.StartsWith(strPrefix, StringComparison.Ordinal) ||
                        !IsDeviceLeafRemainder(strStripName.Substring(strPrefix.Length)))
                    {
                        if (strPrefix.Length > 0)
                            _logger.Warn("[STRIPTREE] структура клеммника '" + strStripName +
                                "' не согласуется с ОУ ('" + strPrefix + "') — фоллбэк разбора ОУ");
                        strPlant = "";
                        strMount = "";
                        strPlace = "";
                        strUser = "";
                    }

                    StripNodeInput oInput = new StripNodeInput();
                    oInput.FullName = strStripName;
                    oInput.Plant = strPlant;
                    oInput.MountingSite = strMount;
                    oInput.PlaceOfInstallation = strPlace;
                    oInput.UserStruct = strUser;
                    lstResult.Add(oInput);
                }
            }
            _logger.Log("[INFO] клеммников для дерева: " + lstResult.Count +
                " (обход " + arrPages.Length + " стр.)");
            return lstResult;
        }

        /// <summary>rev.17: есть ли уже клеммник с таким полным ОУ во входе
        /// дерева (локальный перебор — List.Contains по объекту не годится).</summary>
        private static bool ContainsStripInput(List<StripNodeInput> lstInputs, string strFullName)
        {
            foreach (StripNodeInput oInput in lstInputs)
                if (oInput != null && oInput.FullName == strFullName) return true;
            return false;
        }

        /// <summary>rev.17: чтение одного структурного блока (главный + до 9
        /// подчинённых, напр. 1100/1101..) и склейка НЕпустых сегментов через
        /// '.' — обратная к SplitSegments ('HII-1' + '1' → 'HII-1.1'). Первый
        /// пустой сегмент обрывает цепочку (дальше — иной уровень). Любой отказ
        /// чтения — пустая строка (гейт вызывающей стороны уведёт в фоллбэк).</summary>
        private static string ReadStructureValue(TerminalStrip oStrip, int nBaseId)
        {
            string strJoined = "";
            for (int i = 0; i < 10; i++)
            {
                string strSeg = SafeStripPropText(oStrip, nBaseId + i);
                if (strSeg.Length == 0) break;
                if (strJoined.Length > 0) strJoined += ".";
                strJoined += strSeg;
            }
            return strJoined;
        }

        /// <summary>rev.17: одно свойство TerminalStrip по номеру (образец
        /// EplanTerminalStripReader.SafePropText: id — CreateAnyPropertyIdFromNumber,
        /// чтение Properties[AnyPropertyId]; null id / пусто / исключение — "").
        /// TerminalStrip : Function, Properties — TerminalStripPropertyListComplete
        /// (UniversalPropertyList: индексатор по AnyPropertyId).</summary>
        private static string SafeStripPropText(TerminalStrip oStrip, int nNumber)
        {
            try
            {
                AnyPropertyId oId = CableSymbolCreator.CreateAnyPropertyIdFromNumber(nNumber);
                if (oId == null) return "";
                PropertyValue oValue = oStrip.Properties[oId];
                if (oValue == null || oValue.IsEmpty) return "";
                string strValue = oValue.ToString();
                return strValue == null ? "" : strValue;
            }
            catch { return ""; }
        }

        /// <summary>rev.17: префиксная склейка четырёх структурных частей
        /// (порядок '=' → '++' → '+' → '#'; пустые уровни пропущены) — для
        /// гейта согласованности структуры с полным ОУ.</summary>
        private static string StructurePrefix(string strPlant, string strMount,
            string strPlace, string strUser)
        {
            string strPrefix = "";
            if (!string.IsNullOrEmpty(strPlant)) strPrefix += "=" + strPlant;
            if (!string.IsNullOrEmpty(strMount)) strPrefix += "++" + strMount;
            if (!string.IsNullOrEmpty(strPlace)) strPrefix += "+" + strPlace;
            if (!string.IsNullOrEmpty(strUser)) strPrefix += "#" + strUser;
            return strPrefix;
        }

        /// <summary>rev.17 (fix R1): остаток ОУ после структурного префикса
        /// допустим, только если он ПУСТ (имя без устройства) или начинается с
        /// '-' (лист устройства всегда '-XT…'). Иной остаток — хвост
        /// оборванного подчинённого сегмента (напр. '.1' при нечитаемой 1101),
        /// ещё один структурный префикс или мусор — означает неполное/
        /// несогласованное чтение структуры (гейт уводит в фоллбэк FullName).</summary>
        private static bool IsDeviceLeafRemainder(string strRemainder)
        {
            if (strRemainder.Length == 0) return true;
            return strRemainder.StartsWith("-", StringComparison.Ordinal);
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

            // --- 11.5. rev.16.2 (01.10): BlockFormatResolver.Resolve (запись
            // 20202[x]) УДАЛЕНА — «Формат блока» не используется; чтение
            // 20376/20377 остаётся для BP ниже.

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
            // rev.16.3 (Task 3): BP-решения — ОСНОВНОЙ источник концов = свойства
            // СОЕДИНЕНИЙ ЖИЛ №31019/№31020 (oDm.CableCoreEnds, задача 2) →
            // BreakPointResolver.Decide по списку DT; жил нет → fallback на 20376/20377
            // (DecideLegacy, ленивое перечисление ResolveCableEnds). Своя сторона =
            // полное ОУ клеммника = strTargetStripName (№20006).
            // out-параметр с концами кабеля УДАЛЁН: DT точки разрыва
            // теперь берётся из решения (BreakPointDecision.OppositeDt).
            List<bool> lstBpFlags;
            List<bool> lstBpMulti;
            List<BreakPointDecision> lstDecisions;
            string strStripOwnDt = strTargetStripName;
            PrepareBreakPointDecisions(oLayout, oProject, strStripOwnDt, oDm, _logger,
                out lstBpFlags, out lstBpMulti, out lstDecisions);

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
            // rev.16.2: ось ТОЧКИ ВСТАВКИ ОТЧЁТА (UI — клик пользователя,
            // _dInsertPoint*) — хвост шинного BP до «ось вставки + 20мм».
            oGeomCfg.InsertOriginAxisMm = eOrientation == ReportOrientation.Vertical
                ? _dInsertPointY
                : _dInsertPointX;
            CableGeometryResult oGeom = CableGeometryBuilder.Build(
                oLayout, eOrientation, oGeomCfg, dStripEndAxis,
                lstBpFlags, lstBpMulti);   // rev.16.2: BP-списки (фича в двух режимах)

            // rev.16.3 (Task 3): начинка BreakPointPlacement (OppositeDt/Kind) —
            // ИСТОЧНИК DT — САМО РЕШЕНИЕ (OppositeDt): его заполняет Decide по
            // списку DT жил 31019/31020 либо DecideLegacy + вызывающий (fallback
            // 20376/20377). Потребляет BreakPointSymbolCreator.
            for (int i = 0; i < oGeom.BreakPoints.Count; i++)
            {
                if (oGeom.BreakPoints[i] == null) continue;
                int nIdx = oGeom.BreakPoints[i].CableIndex;
                if (nIdx < 0 || nIdx >= lstDecisions.Count) continue;
                BreakPointDecision oDec = lstDecisions[nIdx];
                if (oDec == null) continue;
                oGeom.BreakPoints[i].OppositeDt = oDec.OppositeDt;
                oGeom.BreakPoints[i].Kind = oDec.Kind;
            }

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
            // rev.16.2: вставка точек разрыва BP (фича в обоих режимах; straight —
            // стрелку у линий не рисовать учтено в ReferenceArrowCreator).
            // rev.17 (Task 7): выбор профилей .emc пользователем — 6 слотов из
            // настроек (UI). Headless-путь (Run(), ~стр. 536) остаётся без
            // selections (дефолт null → legacy-константы Emc*, байт-идентично).
            BpProfileSelections oBpProfileSelections = BuildBpProfileSelections();
            int nBp = BreakPointSymbolCreator.CreateBreakPoints(oPage, oGeom,
                bVerticalSym, BreakPointSymbolCreator.ResolvePointFolder(), _logger,
                oBpProfileSelections);
            _logger.Summarize("Фаза G: линий " + nLines + "/" + oGeom.Segments.Count +
                ", символов " + nSymbols + "/" + oGeom.Symbols.Count +
                ", ссылок " + nRefs + "/" + oGeom.References.Count +
                ", BP " + nBp + "/" + oGeom.BreakPoints.Count +
                " (не идемпотентно: повторный прогон дублирует объекты).");

            // --- 14. Проба чтения реальных кабелей проекта (для реальной группировки) ---
            oDmReader.ReadCables(oProject);

            // --- 15. Проба [SYMBOX]: размеры символов кабеля (GetBoundingBox/GetLogicalArea).
            // Диагностика Фазы H: символы вставляются у пробной точки и сразу удаляются
            // (урок п.48 — не засорять страницу); на счётчики этапов 1–5 не влияет.
            SymbolBoxProbe.Probe(oPage, oProject, _logger);
            return true;
        }

        /// <summary>rev.17 (Task 7): раскладка выбранных пользователем профилей
        /// .emc из _oSettings по 6 слотам BP (BpProfileSelections). Каждое поле
        /// настроек — строка EncodeSelection 'файл|имя схемы'; пусто/невалидно
        /// (TryDecodeSelection false) → слот null (BP с дефолтным отображением,
        /// штатно). Потребитель — RunPipeline (UI-ветка); headless Run() не
        /// вызывает (selections = null).</summary>
        private BpProfileSelections BuildBpProfileSelections()
        {
            BpProfileSelections oSelections = new BpProfileSelections();
            oSelections.StripH = DecodeEmcProfile(_oSettings.EmcStripH);
            oSelections.StripV = DecodeEmcProfile(_oSettings.EmcStripV);
            oSelections.DeviceH = DecodeEmcProfile(_oSettings.EmcDeviceH);
            oSelections.DeviceV = DecodeEmcProfile(_oSettings.EmcDeviceV);
            oSelections.LinkH = DecodeEmcProfile(_oSettings.EmcLinkH);
            oSelections.LinkV = DecodeEmcProfile(_oSettings.EmcLinkV);
            return oSelections;
        }

        /// <summary>rev.17: строка настройки 'файл|имя схемы' → EmcSchemeInfo
        /// (File/SchemeName), невалидно/пусто → null. Вариант (Variant) не
        /// восстанавливается: потребителю (BpProfileSelections.For → ApplyEmcScheme)
        /// он не нужен.</summary>
        private static EmcSchemeInfo DecodeEmcProfile(string strValue)
        {
            string strFile;
            string strScheme;
            if (!EmcProfileCatalog.TryDecodeSelection(strValue, out strFile, out strScheme))
                return null;
            EmcSchemeInfo oInfo = new EmcSchemeInfo();
            oInfo.File = strFile;
            oInfo.SchemeName = strScheme;
            return oInfo;
        }

        private static short SafeLayerId(GraphicalPlacement oPlacement)
        {
            try { return oPlacement.LayerId; }
            catch { return (short)-1; }
        }

        /// <summary>rev.16.2 (Task 4), пересборка rev.16.3 (Task 3): BP-решения по
        /// уникальным кабелям oLayout.
        /// ОСНОВНОЙ источник концов — СВОЙСТВА СОЕДИНЕНИЙ ЖИЛ №31019/№31020
        /// (oDmForBp.CableCoreEnds, задача 2; ключ = полное DT кабеля) →
        /// BreakPointResolver.Decide(строка DT жил) — признак multi переносится
        /// с кабельных свойств на соединения жил (план 2026-10-02, Task 1).
        /// Жил нет (отчёт не передан / ключа нет / все значения пустые) → FALLBACK
        /// на свойства КАБЕЛЯ 20376/20377: BlockFormatResolver.ResolveCableEnds
        /// (главное определение rev.16.1) + BreakPointResolver.DecideLegacy. Вызов
        /// ResolveCableEnds ЛЕНИВЫЙ и ОДИН: он перечисляет функции по ВСЕМУ проекту
        /// (дорого + ~21 строка [CABENDS] в логе), поэтому при живых 31019/31020
        /// не выполняется вовсе, а при fallback — один раз на весь прогон.
        /// Своя сторона в обоих путях = полное ОУ клеммника (strStripOwnDt, равно
        /// oStrip.Name = №20006, rev.12.2); сравнение — только Ordinal (в Decide).
        /// Выходы: per-кабельные списки (BP-ставить, multi) для
        /// CableGeometryBuilder.Build + parallel-список решений. DT точки разрыва
        /// берётся ИЗ РЕШЕНИЯ (BreakPointDecision.OppositeDt) — отдельного
        /// out-параметра с концами кабеля больше нет; OppositeDt заполняют оба
        /// пути (Decide — сам; DecideLegacy — вызывающий, по старому правилу
        /// «противоположный = конец, не равный нашему»).
        /// Без EPLAN-объектов устойчиво: перечисление не удалось → все false
        /// (BP нет, поведение прежнее) + WARN.</summary>
        private static void PrepareBreakPointDecisions(CableLayoutModel oLayout,
            Project oProjectForBp, string strStripOwnDt, DmReport oDmForBp,
            DiagnosticLogger oLogger,
            out List<bool> lstBpFlags, out List<bool> lstBpMulti,
            out List<BreakPointDecision> lstDecisions)
        {
            lstBpFlags = new List<bool>();
            lstBpMulti = new List<bool>();
            lstDecisions = new List<BreakPointDecision>();
            // rev.16.3: legacy-концы 20376/20377 — по требованию, только если
            // встретился кабель без жил. null = ещё НЕ запрашивались (важно:
            // ResolveCableEnds может вернуть ПУСТЫЙ словарь — это не «не
            // запрашивались», повторный вызов был бы лишним перечислением).
            Dictionary<string, string[]> dicLegacyEnds = null;
            if (oLayout == null || oLayout.Cables == null) return;
            for (int i = 0; i < oLayout.Cables.Count; i++)
            {
                CableModel oCable = oLayout.Cables[i];
                string strName = oCable != null ? oCable.Name : null;
                // Шаг 2.1: концы жил по кабелю. oDmForBp == null (отчёт в ветке
                // вызова недоступен) = всегда fallback, НЕ заглушка.
                List<string> lstCore = null;
                if (oDmForBp != null && !string.IsNullOrEmpty(strName))
                    oDmForBp.CableCoreEnds.TryGetValue(strName, out lstCore);
                string[] arrCore = (lstCore != null && lstCore.Count > 0)
                    ? lstCore.ToArray()
                    : null;
                BreakPointDecision oDec;
                if (arrCore != null)
                {
                    // Шаг 2.2: новая ветка — признак multi по списку DT жил.
                    oDec = BreakPointResolver.Decide(strStripOwnDt, arrCore);
                    if (oDec.Kind == BpEndKind.Unreadable)
                        oLogger.Warn("[BP] кабель '" + strName +
                            "': обратный конец не определён (" + oDec.Reason +
                            ") — BP не ставится");
                    else
                        oLogger.Log("[INFO] [BP-DECIDE] '" + strName + "': " +
                            oDec.Reason + (oDec.MultiStrip ? " (multi)" : ""));
                }
                else
                {
                    // Шаг 2.3: жил нет → решение по 20376/20377 (старая логика).
                    oLogger.Log("[BP-FALLBACK] '" + (strName ?? "<без имени>") +
                        "': 31019/31020 пусты — решение по 20376/20377");
                    if (dicLegacyEnds == null)
                    {
                        // WARN здесь, а не на входе: без проекта fallback не
                        // читаем, но при живых 31019/31020 он и не нужен — входной
                        // WARN кричал бы «BP не ставится» там, где BP ставится.
                        if (oProjectForBp == null)
                            oLogger.Warn("[BP] проект недоступен — 20376/20377 " +
                                "не читаются, fallback по жилым недоступен");
                        dicLegacyEnds = oProjectForBp != null
                            ? BlockFormatResolver.ResolveCableEnds(oProjectForBp, oLogger, "[BP]")
                            : new Dictionary<string, string[]>();
                    }
                    string[] arrEnds;
                    if (string.IsNullOrEmpty(strName) ||
                        !dicLegacyEnds.TryGetValue(strName, out arrEnds) || arrEnds == null)
                    {
                        oDec = new BreakPointDecision();
                        oDec.Kind = BpEndKind.Unreadable;
                        oDec.Reason = "no-ends-read";
                        oLogger.Warn("[BP] кабель '" + (strName ?? "<без имени>") +
                            "': концы 20376/20377 не читаются — BP не ставится");
                    }
                    else
                    {
                        oDec = BreakPointResolver.DecideLegacy(strStripOwnDt, arrEnds[0], arrEnds[1]);
                        // Старое правило начинки BreakPointPlacement перенесено в
                        // решение: противоположный DT = конец, НЕ равный нашему.
                        oDec.OppositeDt = (strStripOwnDt == arrEnds[0]) ? arrEnds[1] : arrEnds[0];
                        if (oDec.Kind == BpEndKind.Unreadable)
                            oLogger.Warn("[BP] кабель '" + strName +
                                "': обратный конец не определён (" + oDec.Reason +
                                ") — BP не ставится");
                        else
                            oLogger.Log("[INFO] [BP-DECIDE] '" + strName + "': " +
                                oDec.Reason + (oDec.MultiStrip ? " (multi)" : ""));
                    }
                }
                lstDecisions.Add(oDec);
                lstBpFlags.Add(oDec != null && oDec.Kind != BpEndKind.Unreadable);
                lstBpMulti.Add(oDec != null && oDec.MultiStrip &&
                    oDec.Kind == BpEndKind.TerminalStrip);
            }
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

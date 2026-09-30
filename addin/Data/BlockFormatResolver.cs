using System;
using System.Collections.Generic;
using System.Globalization;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;

namespace MyEplanActions
{
    /// <summary>rev.16.x = fix round 4 (спека §4.2, РАБОЧАЯ МЕХАНИКА v3 —
    /// стенд 30.09, последний прогон): EPLAN-обвязка выбора строки формата
    /// блока символа кабеля со сверкой через свойства кабеля «Кабели:
    /// источник» (№20376, AddInConfiguration.BlockCabSourceProp) и «Кабели:
    /// цель» (№20377, BlockCabTargetProp) — строки с ПОЛНОЙ структурой
    /// концов — и поле «Место сборки» (№1429; BlockCompareProp) НАШЕГО
    /// клеммника. GetSourcesAndTargets больше НЕ используется (кабель может
    /// быть «развёрнут» — источник/цель женить ненадёжно).
    /// Чтение нашего 1429 — ОДНОКРАТНО до цикла кабелей: обход oProject.Pages
    /// (Page.TerminalStrips), поиск клеммника с Name == strTargetStripName
    /// (Ordinal), сбор значений по всем его Terminal («—» → ""); все
    /// непустые равны → единое значение; ≥2 разных непустых → конфликт
    /// (OurLoc = null, bUserError = true, [BLOCKFMT-ERR] по клеммам) — цикл
    /// кабелей не запускается (словарь пуст, SUM «conf=N = все»).
    /// PRE-CHECK: единое значение "" (поле не задано) → INFO + перебора
    /// кабелей нет (словарь пуст, SUM нулями).
    /// Цикл кабелей: на каждом кабеле читаются 20376/20377 (SafeAnyProp,
    /// «—» → "") и вызывается чистый BlockPropMath.Decide(strOurLoc,
    /// cabSrc, cabTgt, BlockFormat1, BlockFormat2):
    ///   our-source->fmt-target — 20376 содержит наш 1429 → наш конец =
    ///     источник → показываем ДРУГОЙ конец (цель) → 20211,2 (fmt2);
    ///   our-target->fmt-source — 20377 содержит наш → наш = цель →
    ///     показываем источник → 20211,1 (fmt1).
    ///   (В строке формата: 20211,1 = источник кабеля, 20211,2 = цель.)
    ///   not-related->skip — INFO «не связан» + nIdle;
    ///   cab-sides-unreadable / ambiguous-both-ends — WARN + nSkip;
    ///   our-loc-empty / our-loc-conflict / feature-off — до Decide не
    ///     доходят (pre-check/штатный выход), но в ветке держим FALLBACK.
    /// Ключ словаря = полный DT кабеля. Отсутствие ключа = для этого кабеля
    /// НЕ писать (потребитель — Task 4, CableSymbolCreator). Отказ имени
    /// кабеля = skip с WARN, обход продолжается.
    /// rev.16.1: 20376/20377 читаются ТОЛЬКО с ГЛАВНОГО определения функции
    /// кабеля (#20122 «Главная функция» = TRUE; сырая строка — через
    /// SafeAnyProp, разбор — BlockPropMath.IsMainFlag, выбор индекса —
    /// BlockPropMath.PickMainIndex). Причина: DefinitionsFilter
    /// Category=Cable возвращает КАЖДОЕ определение (двойники), а чекбокс
    /// «Кабель: заменить источник и цель» (#20064; в API 2.9 не
    /// индексирован) на НЕ-главных определениях МЕНЯЕТ их 20376/20377 →
    /// старый last-write-wins по определениям давал нестабильное решение
    /// (стенд p1/p2 30.09). Схема: фаза A — группировка определений по
    /// имени кабеля; фаза B — решение ОДИН раз на УНИКАЛЬНЫЙ кабель в
    /// порядке встречи (главное определение, иначе первое + WARN).
    /// SUM «кабелей N» теперь считает УНИКАЛЬНЫЕ кабели (не определения).
    /// Имя не читается → WARN «имя не читается — skip» — как раньше,
    /// с ключом в логе «<unparseable#i>».</summary>
    public static class BlockFormatResolver
    {
        /// <summary>Обход кабелей проекта (паттерн ReadCables) и сбор решений.
        /// bUserError = true при конфликте значений №1429 у клемм НАШЕГО
        /// клеммника (UserError-сценарий: MessageBox в UI-ветке AnalyzeAction;
        /// headless-Run() Resolve не вызывает).</summary>
        public static Dictionary<string, string> Resolve(Project oProject,
            DiagnosticLogger log, AddInSettings oSettings, string strTargetStripName,
            out bool bUserError)
        {
            Dictionary<string, string> dicResult = new Dictionary<string, string>();
            int nCables = 0;
            int nFmt1 = 0;
            int nFmt2 = 0;

            // 1) фича off: одна/обе строки формата пусты — не писать вовсе.
            if (oSettings == null ||
                string.IsNullOrEmpty(oSettings.BlockFormat1) ||
                string.IsNullOrEmpty(oSettings.BlockFormat2))
            {
                log.Log("[BLOCKFMT] load: disabled");
                bUserError = false;
                return dicResult;
            }

            // 2) параметры фичи в лог (строки форматов — до 60 символов).
            log.Log("[BLOCKFMT] load: индекс=" +
                oSettings.BlockFormatIndex.ToString(CultureInfo.InvariantCulture) +
                " свойство=" + oSettings.BlockCompareProp.ToString(CultureInfo.InvariantCulture) +
                " fmt1='" + TrimForLog(oSettings.BlockFormat1) +
                "' fmt2='" + TrimForLog(oSettings.BlockFormat2) + "'");

            // 3) наш 1429 — ПОИСК клеммника по имени (однократно, до цикла
            //    кабелей). Отказ чтения/конфликт значений → OurLoc = null.
            List<string> lstTermNames = new List<string>();
            List<string> lstTermValues = new List<string>();
            CollectOurLoc(oProject, strTargetStripName, oSettings.BlockCompareProp,
                lstTermNames, lstTermValues, log);
            string strOurLoc;
            if (lstTermNames.Count == 0)
            {
                // клеммник не найден на страницах проекта — как конфликт
                // чтения: значения у клемм не собрать, сверка невозможна.
                log.Warn("[BLOCKFMT] клеммник '" + strTargetStripName +
                    "' не найден в проекте — сверка невозможна");
                strOurLoc = null;
            }
            else
            {
                bool bStripConflict;
                strOurLoc = CollapseOurLoc(lstTermNames, lstTermValues, log, out bStripConflict);
                if (bStripConflict)
                {
                    strOurLoc = null;
                }
            }
            if (strOurLoc == null)
            {
                // 3а) конфликт клемм нашего клеммника (или клеммник не найден
                //     по имени) — MessageBox-сценарий; кабели НЕ перебираем
                //     вовсе (словарь пуст, SUM echo; conf >= 1).
                bUserError = true;
                log.Log("[BLOCKFMT-SUM] наш-конфликт: fmt1=0 fmt2=0 skip=0 conf=1 без-связи=0 — запись пропущена целиком");
                return dicResult;
            }
            bUserError = false;

            // 4) PRE-CHECK: у клеммника поле «Место сборки» пусто ("") —
            //    запись строки блока невозможна; перебора кабелей нет.
            if (strOurLoc.Length == 0)
            {
                log.Log("[BLOCKFMT] место сборки клеммника пусто — запись строки блока невозможна");
                log.Log("[BLOCKFMT-SUM] кабелей 0: fmt1=0 fmt2=0 skip=0 conf=0 без-связи=0");
                return dicResult;
            }

            // 5) перечисление кабелей — копия паттерна ReadCables
            //    (EplanTerminalStripReader): DMObjectsFinder + FunctionsFilter
            //    (Category = Cable) → GetFunctions → as Cable.
            Function[] arrFunctions;
            try
            {
                DMObjectsFinder oFinder = new DMObjectsFinder(oProject);
                FunctionsFilter oCableFilter = new FunctionsFilter();
                oCableFilter.Category = Function.Enums.Category.Cable;
                arrFunctions = oFinder.GetFunctions(oCableFilter);
            }
            catch (Exception oException)
            {
                log.Warn("[BLOCKFMT] перечисление кабелей не удалось (DMObjectsFinder/GetFunctions): " +
                    oException.GetType().Name + ": " + oException.Message);
                log.Log("[BLOCKFMT-SUM] кабелей 0: fmt1=0 fmt2=0 skip=0 conf=0 без-связи=0");
                return dicResult;
            }
            if (arrFunctions == null || arrFunctions.Length == 0)
            {
                log.Log("[BLOCKFMT-SUM] кабелей 0: fmt1=0 fmt2=0 skip=0 conf=0 без-связи=0");
                return dicResult;
            }

            int nSkip = 0;
            int nIdle = 0;
            // Фаза A (rev.16.1): группировка ОПРЕДЕЛЕНИЙ функций кабелей по
            // имени. DefinitionsFilter Category=Cable возвращает КАЖДОЕ
            // определение — у K140 их было ×3; решение должно быть ЛОЖНО
            // ОДИН раз на уникальный кабель.
            List<CableDefGroup> lstGroups = new List<CableDefGroup>();
            Dictionary<string, CableDefGroup> dicGroups =
                new Dictionary<string, CableDefGroup>();
            for (int i = 0; i < arrFunctions.Length; i++)
            {
                Cable oCable = arrFunctions[i] as Cable;
                if (oCable == null) continue;

                // Имя кабеля = ключ группировки (полный DT). Нечитаемо —
                // старый путь skip: WARN, nSkip++ (ключ в логе отдельный).
                string strName;
                try { strName = oCable.Name; }
                catch { strName = null; }
                if (string.IsNullOrEmpty(strName))
                {
                    log.Warn("[BLOCKFMT] кабель '<unparseable#" +
                        i.ToString(CultureInfo.InvariantCulture) + "'>: имя не читается — skip");
                    nSkip++;
                    continue;
                }

                CableDefGroup oGroup;
                if (!dicGroups.TryGetValue(strName, out oGroup))
                {
                    oGroup = new CableDefGroup();
                    oGroup.Name = strName;
                    oGroup.Definitions = new List<Function>();
                    lstGroups.Add(oGroup);          // порядок встречи уникальных кабелей
                    dicGroups.Add(strName, oGroup);
                }
                oGroup.Definitions.Add(arrFunctions[i]);
            }

            // Фаза B (rev.16.1): решение по УНИКАЛЬНЫМ кабелям в порядке
            // встречи. Чтение 20376/20377 — ТОЛЬКО у выбранного определения
            // (главное #20122=TRUE; иначе первое + WARN: чекбокс #20064
            // «Кабель: заменить источник и цель» на двойниках меняет их
            // 20376/20377 → last-write-wins был нестабилен, root cause
            // стенда p1/p2 30.09).
            nCables = lstGroups.Count;
            for (int g = 0; g < lstGroups.Count; g++)
            {
                CableDefGroup oGroup = lstGroups[g];
                string strName = oGroup.Name;

                // Выбор главного определения: FIRST TRUE из #20122
                // (IsMainFlag), иначе первое определение + WARN.
                List<bool> lstFlags = new List<bool>();
                for (int d = 0; d < oGroup.Definitions.Count; d++)
                {
                    // «—» = не читается (как у 20376/20377).
                    string strRaw = EplanTerminalStripReader.SafeAnyProp(oGroup.Definitions[d], 20122);
                    if (strRaw == "—") strRaw = "";
                    lstFlags.Add(BlockPropMath.IsMainFlag(strRaw));
                }
                int iMain = BlockPropMath.PickMainIndex(
                    oGroup.Definitions.Count, lstFlags);
                if (iMain >= 0)
                {
                    if (iMain != 0)
                    {
                        log.Log("[BLOCKFMT] кабель '" + strName +
                            "': чтение с главного определения (найдено среди " +
                            oGroup.Definitions.Count.ToString(CultureInfo.InvariantCulture) + ")");
                    }
                }
                else
                {
                    log.Warn("[BLOCKFMT] кабель '" + strName +
                        "': главное определение (#20122=TRUE) не найдено среди " +
                        oGroup.Definitions.Count.ToString(CultureInfo.InvariantCulture) +
                        " определений — чтение с первого найденного (порядок не гарантирован)");
                    iMain = 0;
                }
                Function oMainDefinition = oGroup.Definitions[iMain];

                // Полные структуры концов: 20376 «Кабели: источник» и
                // 20377 «Кабели: цель» (SafeAnyProp; «—» = «не читается»
                // → ""). Механика v3: сверка идёт ПО СТРОКАМ через Decide.
                string strCabSource = ReadSidesValue(oMainDefinition,
                    AddInConfiguration.BlockCabSourceProp);
                string strCabTarget = ReadSidesValue(oMainDefinition,
                    AddInConfiguration.BlockCabTargetProp);

                // Решение — чистый BlockPropMath.Decide.
                BlockFormatDecision oDec = BlockPropMath.Decide(strOurLoc,
                    strCabSource, strCabTarget,
                    oSettings.BlockFormat1, oSettings.BlockFormat2);

                // Лог решения — ОДИН раз на уникальный кабель (строки
                // 20376/20377 — до 60 символов).
                log.Log("[BLOCKFMT] кабель '" + strName + "': наш='" + strOurLoc +
                    "' 20376='" + TrimForLog(strCabSource) +
                    "' 20377='" + TrimForLog(strCabTarget) + "' → " + oDec.Reason);

                if (oDec.Reason == "our-source->fmt-target")
                {
                    // наш конец = источник → показываем ЦЕЛЬ → 20211,2.
                    nFmt2++;
                    dicResult[strName] = oDec.FormatToWrite;
                }
                else if (oDec.Reason == "our-target->fmt-source")
                {
                    // наш конец = цель → показываем ИСТОЧНИК → 20211,1.
                    nFmt1++;
                    dicResult[strName] = oDec.FormatToWrite;
                }
                else if (oDec.Reason == "not-related->skip")
                {
                    log.Log("[BLOCKFMT] кабель '" + strName +
                        "': не связан с клеммником — пропущен");
                    nIdle++;
                }
                else if (oDec.Reason == "cab-sides-unreadable->skip" ||
                         oDec.Reason == "ambiguous-both-ends->skip")
                {
                    // 20376/20377 пустые/не читаются, либо наш 1429 встретился
                    // в ОБОИХ структурах — однозначный конец определить нельзя.
                    log.Warn("[BLOCKFMT] кабель '" + strName +
                        "': свойства 20376/20377 не дали однозначный конец (20376='" +
                        TrimForLog(strCabSource) + "' 20377='" + TrimForLog(strCabTarget) + "')");
                    nSkip++;
                }
                else
                {
                    // FALLBACK: our-loc-empty->skip / our-loc-conflict->skip /
                    // feature-off до Decide не доходят (pre-check 4.3/выход
                    // 1/disabled) — если всё же пришли, это непредвиденный
                    // путь: WARN + skip, обход продолжается.
                    log.Warn("[BLOCKFMT] кабель '" + strName + "': решение " +
                        oDec.Reason + " (непредвидено — pre-check) — skip");
                    nSkip++;
                }
            }

            log.Log("[BLOCKFMT-SUM] кабелей " + nCables.ToString(CultureInfo.InvariantCulture) +
                ": fmt1=" + nFmt1.ToString(CultureInfo.InvariantCulture) +
                " fmt2=" + nFmt2.ToString(CultureInfo.InvariantCulture) +
                " skip=" + nSkip.ToString(CultureInfo.InvariantCulture) +
                " conf=0 без-связи=" + nIdle.ToString(CultureInfo.InvariantCulture));
            return dicResult;
        }

        /// <summary>rev.16.2: чтение полных структур концов (№20376/№20377)
        /// КАЖДОГО уникального кабеля проекта — БЕЗ фильтров №1429 (сверку и
        /// решение делает чистый BreakPointResolver.Decide у потребителя).
        /// Группировка и выбор главного определения — ТОЧНАЯ копия Фаз A/B
        /// rev.16.1 (FunctionsFilter Category=Cable возвращает КАЖДОЕ
        /// определение; чтение ТОЛЬКО с главного #20122=TRUE, иначе первое
        /// + WARN «порядок не гарантирован»; чекбокс #20064 на двойниках
        /// меняет 20376/20377 → last-write-wins нестабилен). Своя сторона
        /// определяется у потребителя сверкой с полным ОУ клеммника
        /// (strTargetStripName = oStrip.Name = №20006 — rev.12.2 «полное
        /// имя уникально»). Значение '—'/'-пусто' читается как "".
        /// Возвращает: ключ = полное DT кабеля; [0] = значение №20376
        /// (null = нечитаемо), [1] = №20377 (null = нечитаемо).</summary>
        public static Dictionary<string, string[]> ResolveCableEnds(Project oProject,
            DiagnosticLogger log, string strTagForLog)
        {
            Dictionary<string, string[]> dicEnds = new Dictionary<string, string[]>();

            // перечисление кабелей (копия шага 5 Resolve).
            Function[] arrFunctions;
            try
            {
                DMObjectsFinder oFinder = new DMObjectsFinder(oProject);
                FunctionsFilter oCableFilter = new FunctionsFilter();
                oCableFilter.Category = Function.Enums.Category.Cable;
                arrFunctions = oFinder.GetFunctions(oCableFilter);
            }
            catch (Exception oException)
            {
                log.Warn("[CABENDS] перечисление кабелей не удалось (DMObjectsFinder/GetFunctions): " +
                    oException.GetType().Name + ": " + oException.Message);
                log.Log("[INFO] [CABENDS-SUM] уникальных кабелей 0 (ошибка перечисления)");
                return dicEnds;
            }
            if (arrFunctions == null || arrFunctions.Length == 0)
            {
                log.Log("[INFO] [CABENDS-SUM] уникальных кабелей 0");
                return dicEnds;
            }

            // Фаза A (копия rev.16.1).
            List<CableDefGroup> lstGroups = new List<CableDefGroup>();
            Dictionary<string, CableDefGroup> dicGroups = new Dictionary<string, CableDefGroup>();
            for (int i = 0; i < arrFunctions.Length; i++)
            {
                Cable oCable = arrFunctions[i] as Cable;
                if (oCable == null) continue;
                string strName;
                try { strName = oCable.Name; }
                catch { strName = null; }
                if (string.IsNullOrEmpty(strName))
                {
                    log.Warn("[CABENDS] кабель '<unparseable#" +
                        i.ToString(CultureInfo.InvariantCulture) + "'>: имя не читается — skip");
                    continue;
                }
                CableDefGroup oGroup;
                if (!dicGroups.TryGetValue(strName, out oGroup))
                {
                    oGroup = new CableDefGroup();
                    oGroup.Name = strName;
                    oGroup.Definitions = new List<Function>();
                    lstGroups.Add(oGroup);
                    dicGroups.Add(strName, oGroup);
                }
                oGroup.Definitions.Add(arrFunctions[i]);
            }

            // Фаза B (копия rev.16.1): главное определение → 20376/20377.
            for (int g = 0; g < lstGroups.Count; g++)
            {
                CableDefGroup oGroup = lstGroups[g];
                string strName = oGroup.Name;

                List<bool> lstFlags = new List<bool>();
                for (int d = 0; d < oGroup.Definitions.Count; d++)
                {
                    string strRaw = EplanTerminalStripReader.SafeAnyProp(oGroup.Definitions[d], 20122);
                    if (strRaw == "—") strRaw = "";
                    lstFlags.Add(BlockPropMath.IsMainFlag(strRaw));
                }
                int iMain = BlockPropMath.PickMainIndex(oGroup.Definitions.Count, lstFlags);
                if (iMain < 0)
                {
                    if (oGroup.Definitions.Count > 1)
                        log.Warn("[CABENDS] кабель '" + TrimForLog(strName) +
                            "': главное определение (#20122=TRUE) не найдено — чтение с первого (порядок не гарантирован)");
                    iMain = 0;
                }
                else if (iMain != 0 && oGroup.Definitions.Count > 1)
                {
                    log.Log("[CABENDS] кабель '" + TrimForLog(strName) +
                        "': чтение с главного определения (" +
                        iMain.ToString(CultureInfo.InvariantCulture) + " из " +
                        oGroup.Definitions.Count.ToString(CultureInfo.InvariantCulture) + ")");
                }
                Function oMainDefinition = oGroup.Definitions[iMain];

                string strCabSource = ReadSidesValue(oMainDefinition,
                    AddInConfiguration.BlockCabSourceProp);
                string strCabTarget = ReadSidesValue(oMainDefinition,
                    AddInConfiguration.BlockCabTargetProp);
                string[] arrEnds = new string[2];
                arrEnds[0] = strCabSource.Length == 0 ? null : strCabSource;
                arrEnds[1] = strCabTarget.Length == 0 ? null : strCabTarget;
                dicEnds[strName] = arrEnds;
                log.Log("[INFO] [CABENDS] '" + TrimForLog(strName) + "': 20376='" +
                    TrimForLog(strCabSource) + "' 20377='" + TrimForLog(strCabTarget) + "'");
            }
            log.Log("[INFO] [CABENDS-SUM] уникальных кабелей " +
                lstGroups.Count.ToString(CultureInfo.InvariantCulture));
            return dicEnds;
        }

        /// <summary>Поиск клеммника strTargetStripName по страницам проекта и
        /// сбор сырых значений свойства nPropNumber (BlockCompareProp)
        /// по всем его Terminal. Паттерн Read (EplanTerminalStripReader):
        /// Project.Pages → Page.TerminalStrips (try/catch — страница не как
        /// источник ошибок) → TerminalStrip.Terminals (try/catch). Сравнение
        /// имени клеммника — Ordinal. Не найден → списки пусты.</summary>
        private static void CollectOurLoc(Project oProject, string strTargetStripName,
            int nPropNumber, List<string> lstTermNames, List<string> lstTermValues,
            DiagnosticLogger log)
        {
            if (string.IsNullOrEmpty(strTargetStripName)) return;
            if (oProject == null || oProject.Pages == null) return;
            foreach (Page oPage in oProject.Pages)
            {
                if (oPage == null) continue;
                TerminalStrip[] arrStrips;
                try { arrStrips = oPage.TerminalStrips; }
                catch (Exception oException)
                {
                    log.Warn("[BLOCKFMT] Page.TerminalStrips ('" +
                        SafePageName(oPage) + "') не читаются: " +
                        oException.GetType().Name + ": " + oException.Message);
                    continue;
                }
                if (arrStrips == null || arrStrips.Length == 0) continue;
                for (int i = 0; i < arrStrips.Length; i++)
                {
                    TerminalStrip oStrip = arrStrips[i];
                    if (oStrip == null) continue;
                    string strName;
                    try { strName = oStrip.Name; }
                    catch { strName = null; }
                    if (string.IsNullOrEmpty(strName)) continue;
                    if (!string.Equals(strName, strTargetStripName, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    // Клеммник найден — собираем значения по всем его клеммам.
                    Terminal[] arrTerminals;
                    try
                    {
                        arrTerminals = oStrip.Terminals;
                        if (arrTerminals == null) arrTerminals = new Terminal[0];
                    }
                    catch (Exception oException)
                    {
                        log.Warn("[BLOCKFMT] TerminalStrip.Terminals ('" + strName +
                            "') не читаются: " + oException.GetType().Name + ": " +
                            oException.Message);
                        arrTerminals = new Terminal[0];
                    }
                    for (int j = 0; j < arrTerminals.Length; j++)
                    {
                        Terminal oTerminal = arrTerminals[j];
                        if (oTerminal == null) continue;
                        string strTermName;
                        try { strTermName = oTerminal.Name; }
                        catch { strTermName = null; }
                        if (string.IsNullOrEmpty(strTermName)) strTermName = "<n/a>";
                        lstTermNames.Add(strTermName);
                        lstTermValues.Add(EplanTerminalStripReader.SafeAnyProp(
                            oTerminal, nPropNumber));
                    }
                    return; // полное имя клеммника уникально (assumption ридера)
                }
            }
        }

        /// <summary>СВЁРКА значений нашего клеммника (ruling «игнор пустых»,
        /// как в старой CollectSide): пустые ("") участвуют только если
        /// непустых нет вовсе; все непустые равны → это значение; ≥2 разных
        /// непустых → конфликт: null + [BLOCKFMT-ERR] по ВСЕМ клеммам
        /// (значение как прочитано, «—» как есть).</summary>
        private static string CollapseOurLoc(List<string> lstTermNames,
            List<string> lstTermValues, DiagnosticLogger log, out bool bConflict)
        {
            bConflict = false;
            string strFirst = null;
            bool bDiffer = false;
            for (int i = 0; i < lstTermValues.Count; i++)
            {
                string strValue = lstTermValues[i] == "—" ? "" : lstTermValues[i];
                if (strValue.Length == 0) continue;
                if (strFirst == null)
                {
                    strFirst = strValue;
                }
                else if (!string.Equals(strFirst, strValue, StringComparison.Ordinal))
                {
                    bDiffer = true;
                }
            }
            if (strFirst != null && bDiffer)
            {
                for (int i = 0; i < lstTermValues.Count; i++)
                {
                    log.Log("[BLOCKFMT-ERR] клемма='" + lstTermNames[i] +
                        "' значение='" + lstTermValues[i] + "'");
                }
                bConflict = true;
                return null;
            }
            if (strFirst == null) return ""; // клеммник есть, непустых нет → единое пустое
            return strFirst;
        }

        /// <summary>Чтение структуры конца кабеля (20376/20377) по НОМЕРУ
        /// свойства (SafeAnyProp; «—» → "").</summary>
        private static string ReadSidesValue(Function oCable, int nPropNumber)
        {
            string strRaw = EplanTerminalStripReader.SafeAnyProp(oCable, nPropNumber);
            return strRaw == "—" ? "" : strRaw;
        }

        /// <summary>Имя страницы для [BLOCKFMT]-лога (безопасное чтение).</summary>
        private static string SafePageName(Page oPage)
        {
            try
            {
                string str = oPage.IdentifyingName;
                return str == null ? "<n/a>" : str;
            }
            catch { return "<n/a>"; }
        }

        /// <summary>Строки форматов/структур в лог — не длиннее 60 символов
        /// (спека §4.2).</summary>
        private static string TrimForLog(string strValue)
        {
            if (strValue == null) return "";
            if (strValue.Length <= 60) return strValue;
            return strValue.Substring(0, 60);
        }

        /// <summary>rev.16.1: группа ОПРЕДЕЛЕНИЙ функции одного уникального
        /// кабеля (полный DT = ключ). Решение — по главному определению
        /// (#20122=TRUE), иначе по первому + WARN.</summary>
        private sealed class CableDefGroup
        {
            public string Name;
            public List<Function> Definitions;
        }
    }
}

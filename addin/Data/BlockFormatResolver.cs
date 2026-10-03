using System;
using System.Collections.Generic;
using System.Globalization;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;

namespace MyEplanActions
{
    /// <summary>rev.16.1: чтение полных структур концов кабелей
    /// (№20376 «Кабели: источник» / №20377 «Кабели: цель») С ГЛАВНОГО
    /// определения функции (#20122 TRUE; чекбокс #20064 на не-главных
    /// двойниках меняет их — last-write-wins нестабилен, стенд p1/p2 30.09).
    /// rev.16.2 (решение 01.10): Resolve-запись 20202[x] УДАЛЕНА (фича
    /// «Формат блока» снята целиком); остался ResolveCableEnds —
    /// потребитель BreakPointResolver.DecideLegacy (BP-классификация; rev16.3 —
    /// fallback, новая ветка multi читает 31019/31020).
    ///</summary>
    public static class BlockFormatResolver
    {

        /// <summary>rev.16.2: чтение полных структур концов (№20376/№20377)
        /// КАЖДОГО уникального кабеля проекта — БЕЗ фильтров №1429 (сверку и
        /// решение делает чистый BreakPointResolver.DecideLegacy у потребителя).
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

            // Перечисление функций кабельных ОПРЕДЕЛЕНИЙ.
            // rev16.5-бис-д (03.10.2026, стенд «БТЭЦ-2 замена Т-1», клеммник
            // =1Т++ШЗВ+#-XT1): кабель задаётся НЕ только символом 4/CABDL
            // (Category.Cable). Определение, сделанное символом 5/SH —
            // ЭКРАНИРОВАНИЕ, это отдельная категория Function.Enums.Category.Shielding
            // (KB: EObjects.Shield~Category — «only 'Shielding' is allowed»;
            // KB: CableService.DoReassignWires — Cable и Function этой категории
            // обрабатываются как равноправные кабели). Прежний код брал ТОЛЬКО
            // Category.Cable ⇒ 5 кабелей клеммника (=1Т++М+#3-K140/K144/K145/
            // K146/K156) не дали ни 20376, ни 20377 ⇒ имя точки разрыва не
            // строилось, BP не ставился, а стрелка оставалась.
            // FunctionsFilter.Category — ОДНО значение ({get;set;} типа Category,
            // KB: FunctionsFilter~Category) ⇒ ДВА прохода в один список. Второй
            // не должен ронять первый: ошибка Shielding логируется и идёт дальше.
            List<FunctionHit> lstAll = new List<FunctionHit>();
            DMObjectsFinder oFinder;
            try
            {
                // Конструктор бросает ArgumentNullException на null-проекте —
                // защиту из rev.16.1 НЕ теряем (ревью 03.10, Important 1): при
                // вынесении из try исключение ушло бы наружу из public static.
                oFinder = new DMObjectsFinder(oProject);
            }
            catch (Exception oException)
            {
                log.Warn("[CABENDS] перечисление кабелей не удалось (DMObjectsFinder): " +
                    oException.GetType().Name + ": " + oException.Message);
                log.Log("[INFO] [CABENDS-SUM] уникальных кабелей 0 (ошибка перечисления)");
                return dicEnds;
            }
            for (int c = 0; c < 2; c++)
            {
                Function.Enums.Category oCat = c == 0
                    ? Function.Enums.Category.Cable
                    : Function.Enums.Category.Shielding;
                Function[] arrFunctions;
                try
                {
                    FunctionsFilter oFilter = new FunctionsFilter();
                    oFilter.Category = oCat;
                    arrFunctions = oFinder.GetFunctions(oFilter);
                }
                catch (Exception oException)
                {
                    log.Warn("[CABENDS] перечисление категории " + oCat.ToString() +
                        " не удалось (DMObjectsFinder/GetFunctions): " +
                        oException.GetType().Name + ": " + oException.Message);
                    continue;
                }
                int nAdded = 0;
                if (arrFunctions != null)
                    for (int i = 0; i < arrFunctions.Length; i++)
                        if (arrFunctions[i] != null)
                        {
                            FunctionHit oHit = new FunctionHit();
                            oHit.Definition = arrFunctions[i];
                            oHit.Shielding = oCat == Function.Enums.Category.Shielding;
                            lstAll.Add(oHit);
                            nAdded++;
                        }
                log.Log("[INFO] [CABENDS] категория " + oCat.ToString() +
                    ": функций " + nAdded.ToString(CultureInfo.InvariantCulture));
            }
            if (lstAll.Count == 0)
            {
                log.Log("[INFO] [CABENDS-SUM] уникальных кабелей 0" +
                    " (категории Cable и Shielding не дали ни одной функции)");
                return dicEnds;
            }

            // Фаза A (копия rev.16.1). Тип объекта намеренно НЕ сужаем до Cable:
            // определение экранирования — объект Shield, и `as Cable` вернул бы
            // для него null ⇒ continue, кабель молча выпал бы даже при правильном
            // фильтре. Нужны только Function.Name и SafeAnyProp (оба — на
            // Function), поэтому работаем с Function целиком.
            List<CableDefGroup> lstGroups = new List<CableDefGroup>();
            Dictionary<string, CableDefGroup> dicGroups = new Dictionary<string, CableDefGroup>();
            for (int i = 0; i < lstAll.Count; i++)
            {
                Function oFunc = lstAll[i].Definition;
                string strName;
                try { strName = oFunc.Name; }
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
                // Признак Shielding — «хоть одно определение из экранирования»: проход Cable
                // идёт первым и задал бы false, а для диагностики нужен факт.
                // Смешанная группа (имя и в Cable, и в Shielding) ловится ТОЛЬКО
                // здесь — на щите, пришедшем ПОСЛЕ кабеля. Прежняя запись вида
                // `if (Shielding) ... else if (Shielding) warn` была недостижима:
                // кабель приходит первым и всегда снимал else-ветку, WARN не
                // срабатывал НИ РАЗУ — стенд 03.10 это и показал (K276 смешанная,
                // предупреждения нет). Поэтому «видел кабель» хранится явно.
                if (lstAll[i].Shielding)
                {
                    if (oGroup.SawCable)
                        // Имя встречается и как Shielding, и как Cable. Главное
                        // определение может оказаться щитом — тогда 20376/20377
                        // прочитаются с другого объекта, чем раньше. Это решение
                        // предметной области, не молчалив: помечаем WARN (ревью
                        // 03.10, Important 2), чтобы стенд показал случай явно.
                        log.Warn("[CABENDS] кабель '" + TrimForLog(strName) +
                            "': имя есть и как Shielding (5/SH), и как Cable (4/CABDL) — " +
                            "главное определение выберется по №20122, приоритет источника НЕ задан");
                    oGroup.Shielding = true;
                }
                else
                    oGroup.SawCable = true;
                oGroup.Definitions.Add(oFunc);
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
                    TrimForLog(strCabSource) + "' 20377='" + TrimForLog(strCabTarget) +
                    "'" + (oGroup.Shielding ? " [Shielding — определение символом 5/SH]" : ""));
            }
            int nShieldNames = 0;
            for (int g = 0; g < lstGroups.Count; g++)
                if (lstGroups[g].Shielding) nShieldNames++;
            log.Log("[INFO] [CABENDS-SUM] уникальных кабелей " +
                lstGroups.Count.ToString(CultureInfo.InvariantCulture) +
                " (Shielding/5-SH: " + nShieldNames.ToString(CultureInfo.InvariantCulture) +
                ", Cable/4-CABDL: " +
                (lstGroups.Count - nShieldNames).ToString(CultureInfo.InvariantCulture) + ")");
            return dicEnds;
        }



        /// <summary>Чтение структуры конца кабеля (20376/20377) по НОМЕРУ
        /// свойства (SafeAnyProp; «—» → "").</summary>
        private static string ReadSidesValue(Function oCable, int nPropNumber)
        {
            string strRaw = EplanTerminalStripReader.SafeAnyProp(oCable, nPropNumber);
            return strRaw == "—" ? "" : strRaw;
        }


        /// <summary>Строки форматов/структур в лог — не длиннее 60 символов
        /// (спека §4.2).</summary>
        private static string TrimForLog(string strValue)
        {
            if (strValue == null) return "";
            if (strValue.Length <= 60) return strValue;
            return strValue.Substring(0, 60);
        }

        /// <summary>rev16.5-бис-д: функция из перечисления + признак, что она
        /// пришла из категории Shielding (символ 5/SH), а не Cable (4/CABDL).
        /// Нужен для диагностики [CABENDS]: у клиента определение кабеля может
        /// быть сделано экранированием, и такие кабели раньше не попадали в
        /// перечисление вообще.</summary>
        private sealed class FunctionHit
        {
            public Function Definition;
            public bool Shielding;
        }

        /// <summary>rev.16.1: группа ОПРЕДЕЛЕНИЙ функции одного уникального
        /// кабеля (полный DT = ключ). Решение — по главному определению
        /// (#20122=TRUE), иначе по первому + WARN.</summary>
        private sealed class CableDefGroup
        {
            public string Name;
            public List<Function> Definitions;
            public bool Shielding;
            public bool SawCable;
        }
    }
}

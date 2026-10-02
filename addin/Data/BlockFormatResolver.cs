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

// using System — чистый модуль без EPLAN-типов (компилируется и в test-раннер tests/).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MyEplanActions
{
    /// <summary>Группа категорий дерева символов (Этап 8, задача H-4b, rev.14.0):
    /// имя категории, признак fallback (не FD) и символы группы В ИСХОДНОМ перечислении
    /// библиотеки (план_stage8.md § H-4b). Чистый модуль БЕЗ EPLAN-типов —
    /// компилируется и в консольный тест-раннер tests/ (add UI/SymbolCatalog.cs).</summary>
    public class SymbolCatalogGroup
    {
        /// <summary>Имя категории: FD-имя, «Прочее (FD N)», fallback-префикс или «Прочие».</summary>
        public string Name { get; set; }

        /// <summary>true — fallback-бакет (FD недоступен: по префиксу имени или «Прочие»);
        /// false — категория по определению функции (FD).</summary>
        public bool IsFallback { get; set; }

        /// <summary>Имена символов категории (номера вариантов диалог добирает сам) —
        /// в исходном порядке перечисления; дубли НЕ схлопываются.</summary>
        public List<string> SymbolNames { get { return _lstNames; } }
        private readonly List<string> _lstNames = new List<string>();

        public override string ToString()
        {
            return Name + " (" + _lstNames.Count.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>Чистая категоризация символов для дерева браузера (Этап 8, H-4b,
    /// rev.14.0; plan_stage8.md § Task H-4b). Вход: список имён символов, FD-ID по
    /// символу (long? или null — для цепочки A DataModel FD недоступен) и словарь
    /// FD-ID → имя категории (может быть пустым/частичным). Выход: упорядоченный
    /// список категорий («SymbolCatalogGroup») со списками имён в исходном порядке.
    /// Правила: (а) FD-ID есть и в словаре — имя FD; (б) FD-ID есть, в словаре нет —
    /// «Прочее (FD N)» (InvariantCulture); (в) FD-ID нет — fallback-бакет по ПРЕФИКСУ
    /// имени (ведущая нецифровая часть до первой цифры, по образцу
    /// SplitDeviceTagLetterCounter, CableSymbolCreator.cs; пустой префикс — «Прочие»);
    /// (г) полностью пустой вход — одна категория «Прочие». Сортировка: FD-категории
    /// по имени (OrdinalIgnoreCase), затем fallback-бакеты, «Прочие» последней;
    /// внутри категории — исходный порядок символов (дубли сохраняются).</summary>
    public static class SymbolCatalog
    {
        /// <summary>Имя бакета-«отстойника»: пустой префикс / пустой вход.</summary>
        public const string STR_MISC = "Прочие";

        /// <summary>Группировка (см. класс-комментарий). Списки lstFdIds — параллельны
        /// lstNames (короче/отсутствует — FD как null → fallback). dctFdNames
        /// отсутствует/пуст — FD-символы уходят в «Прочее (FD N)».</summary>
        public static List<SymbolCatalogGroup> Build(List<string> lstNames,
            List<long?> lstFdIds, Dictionary<long, string> dctFdNames)
        {
            // (г) пустой вход — одна категория «Прочие» (включая null-аргументы).
            if (lstNames == null || lstNames.Count == 0)
            {
                List<SymbolCatalogGroup> lstEmpty = new List<SymbolCatalogGroup>();
                lstEmpty.Add(MakeGroup(STR_MISC, true));
                return lstEmpty;
            }

            // Группы копятся в словарь (уникальность — по tier+имени: fallback-бакет
            // «K» и FD-категория «K» — разные сущности) и в список сохранения порядка.
            Dictionary<string, SymbolCatalogGroup> dctGroups =
                new Dictionary<string, SymbolCatalogGroup>();
            for (int i = 0; i < lstNames.Count; i++)
            {
                string strName = lstNames[i];
                long? nFd = null;
                if (lstFdIds != null && i < lstFdIds.Count) nFd = lstFdIds[i];

                string strCategory;
                bool bFallback;
                if (nFd.HasValue)
                {
                    string strFdName;
                    if (dctFdNames != null && dctFdNames.TryGetValue(nFd.Value, out strFdName) &&
                        !string.IsNullOrEmpty(strFdName))
                    {
                        strCategory = strFdName;        // (а) имя FD из словаря
                    }
                    else
                    {
                        // (б) FD-ID вне словаря — «Прочее (FD N)», InvariantCulture.
                        strCategory = "Прочее (FD " +
                            nFd.Value.ToString(CultureInfo.InvariantCulture) + ")";
                    }
                    bFallback = false;
                }
                else
                {
                    string strPrefix = ExtractNonDigitPrefix(strName);
                    if (strPrefix.Length == 0)
                    {
                        strCategory = STR_MISC;         // (в) пустой префикс → «Прочие»
                        bFallback = true;
                    }
                    else
                    {
                        strCategory = strPrefix;        // (в) fallback-бакет по префиксу
                        bFallback = true;
                    }
                }

                string strKey = (bFallback ? "F:" : "D:") + strCategory;
                SymbolCatalogGroup oGroup;
                if (!dctGroups.TryGetValue(strKey, out oGroup))
                {
                    oGroup = MakeGroup(strCategory, bFallback);
                    dctGroups[strKey] = oGroup;
                }
                oGroup.SymbolNames.Add(strName);
            }

            List<SymbolCatalogGroup> lstResult = new List<SymbolCatalogGroup>();
            foreach (SymbolCatalogGroup oGroup in dctGroups.Values) lstResult.Add(oGroup);
            // Сортировка категорий (символы внутри групп не пересобираются — порядок
            // вставки в списке групп не влияет на участников; List.Sort нестабилен,
            // но ключи уникальны — эквивалентно).
            lstResult.Sort(GroupCompare);
            return lstResult;
        }

        private static SymbolCatalogGroup MakeGroup(string strName, bool bFallback)
        {
            SymbolCatalogGroup oGroup = new SymbolCatalogGroup();
            oGroup.Name = strName;
            oGroup.IsFallback = bFallback;
            return oGroup;
        }

        /// <summary>Сортировка: FD-категории раньше fallback; «Прочие» последняя;
        /// внутри уровня — по имени OrdinalIgnoreCase (регистронезависимо).</summary>
        private static int GroupCompare(SymbolCatalogGroup oA, SymbolCatalogGroup oB)
        {
            int nA = TierOf(oA);
            int nB = TierOf(oB);
            if (nA != nB) return nA - nB;
            return string.Compare(oA.Name, oB.Name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Уровень: 0 — FD (IsFallback=false), 2 — «Прочие», 1 — fallback-префикс.</summary>
        private static int TierOf(SymbolCatalogGroup oGroup)
        {
            if (!oGroup.IsFallback) return 0;
            return string.Compare(oGroup.Name, STR_MISC, StringComparison.OrdinalIgnoreCase) == 0
                ? 2 : 1;
        }

        /// <summary>Ведущая нецифровая часть имени (до первой цифры) — по образцу
        /// SplitDeviceTagLetterCounter (CableSymbolCreator.cs: ведущие нецифровые —
        /// «буква»); пустая/цифровая с первой позиции → «» (→ «Прочие»). Null/пустое
        /// имя — «».</summary>
        private static string ExtractNonDigitPrefix(string strName)
        {
            if (string.IsNullOrEmpty(strName)) return string.Empty;
            int i = 0;
            while (i < strName.Length && !char.IsDigit(strName[i])) i++;
            return strName.Substring(0, i);
        }
    }
}

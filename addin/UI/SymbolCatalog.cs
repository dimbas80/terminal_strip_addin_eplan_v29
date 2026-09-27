// using System — чистый модуль без EPLAN-типов (компилируется и в test-раннер tests/).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MyEplanActions
{
    /// <summary>Определение функции для категоризации (rev.14.1, замечание R2):
    /// имя категории и имя группы из FunctionDefinition (KB: CategoryName/GroupName).
    /// Чистый класс без EPLAN-типов.</summary>
    public class FdInfo
    {
        /// <summary>Имя категории (FunctionDefinition.CategoryName).</summary>
        public string Category;

        /// <summary>Имя группы (FunctionDefinition.GroupName).</summary>
        public string Group;
    }

    /// <summary>Категория дерева символов (Этап 8, H-4b v3, rev.14.1): имя категории
    /// и упорядоченный список групп. Чистый модуль БЕЗ EPLAN-типов — компилируется
    /// и в консольный тест-раннер tests/ (add UI/SymbolCatalog.cs).</summary>
    public class SymbolCatalogCategory
    {
        /// <summary>Имя категории: FD CategoryName, «Без категории», fallback-префикс, «Прочие».</summary>
        public string Name { get; set; }

        /// <summary>true — fallback-бакет (FD недоступен: по префиксу имени, «Прочие»
        /// или символ без FD при рабочем словаре); false — категория по FD.</summary>
        public bool IsFallback { get; set; }

        /// <summary>Группы категории (имя + символы в исходном порядке перечисления).</summary>
        public List<SymbolCatalogGroup> Groups { get { return _lstGroups; } }
        private readonly List<SymbolCatalogGroup> _lstGroups = new List<SymbolCatalogGroup>();

        public override string ToString()
        {
            return Name + " (" + _lstGroups.Count.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>Группа внутри категории дерева символов (rev.14.1, замечание R2):
    /// имя группы (FD GroupName или «—») и символы В ИСХОДНОМ перечислении библиотеки.</summary>
    public class SymbolCatalogGroup
    {
        /// <summary>Имя группы: FD-GroupName, «—» (без группы), fallback «—» или «Прочие».</summary>
        public string Name { get; set; }

        /// <summary>Имена символов группы (номера вариантов диалог добирает сам) —
        /// в исходном порядке перечисления; дубли НЕ схлопываются.</summary>
        public List<string> SymbolNames { get { return _lstNames; } }
        private readonly List<string> _lstNames = new List<string>();

        public override string ToString()
        {
            return Name + " (" + _lstNames.Count.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>Чистая категоризация символов для дерева браузера (Этап 8, H-4b v3,
    /// rev.14.1; замечание R2: нативное дерево EPLAN = Категория → Группа). Вход:
    /// список имён символов, FD-ID по символу (long? или null) и словарь FD-ID →
    /// FdInfo {Category, Group} (из Project.FunctionDefinitionLibrary.FunctionDefinitions).
    /// Выход: упорядоченный список категорий со списками групп; символы — в исходном
    /// порядке. Правила: (а) словарь FD НЕ пуст: FD-ID есть и в словаре — категория =
    /// FdInfo.Category (пустая → «Без категории»), группа = FdInfo.Group (пустая → «—»);
    /// (б) FD-ID нет или записи в словаре нет — категория «Без категории», группа «—»
    /// (по решению пользователя — НЕ префикс имени); (в) словарь пуст (цепочка A,
    /// FD недоступен) — fallback-бакеты по ПРЕФИКСУ имени (ведущая нецифровая часть,
    /// по образцу SplitDeviceTagLetterCounter; пустой префикс — «Прочие»), группа «—»;
    /// (г) пустой вход — одна категория «Прочие» с группой «—». Сортировка: категории
    /// resolved (FD) раньше fallback; внутри уровня по имени OrdinalIgnoreCase;
    /// группы в категории — resolved по имени, «—» последней; символы — исходный
    /// порядок (дубли сохраняются).</summary>
    public static class SymbolCatalog
    {
        /// <summary>Имя бакета-«отстойника»: пустой префикс / пустой вход.</summary>
        public const string STR_MISC = "Прочие";

        /// <summary>Категория символа без FD (словарь рабочий) — решение R2.</summary>
        public const string STR_NO_CATEGORY = "Без категории";

        /// <summary>Имя группы, когда группа не определена.</summary>
        public const string STR_NO_GROUP = "—";

        /// <summary>Группировка (см. класс-комментарий). Списки lstFdIds — параллельны
        /// lstNames (короче/отсутствует — FD как null). dctFd отсутствует/пуст —
        /// fallback-префиксы (цепочка A).</summary>
        public static List<SymbolCatalogCategory> Build(List<string> lstNames,
            List<long?> lstFdIds, Dictionary<long, FdInfo> dctFd)
        {
            // (г) пустой вход — одна категория «Прочие» с группой «—».
            if (lstNames == null || lstNames.Count == 0)
            {
                List<SymbolCatalogCategory> lstEmpty = new List<SymbolCatalogCategory>();
                lstEmpty.Add(MakeCategory(STR_MISC, true, STR_NO_GROUP));
                return lstEmpty;
            }

            bool bDictEmpty = dctFd == null || dctFd.Count == 0;

            // Категории копятся в словарь (уникальность — по tier+имени) и в список
            // сохранения порядка; группы — внутри категории.
            Dictionary<string, SymbolCatalogCategory> dctCategories =
                new Dictionary<string, SymbolCatalogCategory>();
            for (int i = 0; i < lstNames.Count; i++)
            {
                string strName = lstNames[i];
                long? nFd = null;
                if (lstFdIds != null && i < lstFdIds.Count) nFd = lstFdIds[i];

                string strCategory;
                string strGroup;
                bool bFallback;
                if (!bDictEmpty)
                {
                    // Основной путь (R2): рабочее дерево по FD. Без FD/без записи —
                    // «Без категории»/«—» (решение пользователя — НЕ префикс).
                    // Fix (rev.14.1 ревью): категория и группа считаются НЕЗАВИСИМО —
                    // запись FD с пустой CategoryName теряет только категорию,
                    // GroupName сохраняется.
                    FdInfo oInfo = null;
                    if (nFd.HasValue) dctFd.TryGetValue(nFd.Value, out oInfo);
                    if (oInfo != null)
                    {
                        strCategory = string.IsNullOrEmpty(oInfo.Category)
                            ? STR_NO_CATEGORY : oInfo.Category;
                        strGroup = string.IsNullOrEmpty(oInfo.Group)
                            ? STR_NO_GROUP : oInfo.Group;
                        bFallback = string.IsNullOrEmpty(oInfo.Category);
                    }
                    else
                    {
                        strCategory = STR_NO_CATEGORY;
                        strGroup = STR_NO_GROUP;
                        bFallback = true;
                    }
                }
                else
                {
                    // Fallback (цепочка A): FD недоступен — прежние префиксные бакеты.
                    string strPrefix = ExtractNonDigitPrefix(strName);
                    strCategory = strPrefix.Length == 0 ? STR_MISC : strPrefix;
                    strGroup = STR_NO_GROUP;
                    bFallback = true;
                }

                string strKey = (bFallback ? "F:" : "D:") + strCategory;
                SymbolCatalogCategory oCategory;
                if (!dctCategories.TryGetValue(strKey, out oCategory))
                {
                    oCategory = MakeCategory(strCategory, bFallback, strGroup);
                    dctCategories[strKey] = oCategory;
                }
                SymbolCatalogGroup oGroup = FindOrMakeGroup(oCategory, strGroup);
                oGroup.SymbolNames.Add(strName);
            }

            List<SymbolCatalogCategory> lstResult = new List<SymbolCatalogCategory>();
            foreach (SymbolCatalogCategory oCategory in dctCategories.Values)
            {
                lstResult.Add(oCategory);
                // Группы внутри категории: «—» последней, остальные по имени.
                oCategory.Groups.Sort(GroupCompare);
            }
            // Сортировка категорий (символы внутри групп не пересобираются; List.Sort
            // нестабилен, но ключи словаря уникальны — эквивалентно).
            lstResult.Sort(CategoryCompare);
            return lstResult;
        }

        private static SymbolCatalogCategory MakeCategory(string strName, bool bFallback,
            string strFirstGroup)
        {
            SymbolCatalogCategory oCategory = new SymbolCatalogCategory();
            oCategory.Name = strName;
            oCategory.IsFallback = bFallback;
            oCategory.Groups.Add(MakeGroup(strFirstGroup));
            return oCategory;
        }

        private static SymbolCatalogGroup FindOrMakeGroup(SymbolCatalogCategory oCategory,
            string strGroup)
        {
            foreach (SymbolCatalogGroup oExisting in oCategory.Groups)
            {
                if (string.Compare(oExisting.Name, strGroup, StringComparison.OrdinalIgnoreCase) == 0)
                    return oExisting;
            }
            SymbolCatalogGroup oMade = MakeGroup(strGroup);
            oCategory.Groups.Add(oMade);
            return oMade;
        }

        private static SymbolCatalogGroup MakeGroup(string strName)
        {
            SymbolCatalogGroup oGroup = new SymbolCatalogGroup();
            oGroup.Name = strName;
            return oGroup;
        }

        /// <summary>Сортировка категорий: FD-resolved раньше fallback; «Прочие»
        /// последняя; внутри уровня — по имени OrdinalIgnoreCase.</summary>
        private static int CategoryCompare(SymbolCatalogCategory oA, SymbolCatalogCategory oB)
        {
            int nA = CategoryTierOf(oA);
            int nB = CategoryTierOf(oB);
            if (nA != nB) return nA - nB;
            return string.Compare(oA.Name, oB.Name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Уровень категории: 0 — FD (IsFallback=false), 1 — fallback
        /// (префикс/«Без категории»), 2 — «Прочие».</summary>
        private static int CategoryTierOf(SymbolCatalogCategory oCategory)
        {
            if (!oCategory.IsFallback) return 0;
            return string.Compare(oCategory.Name, STR_MISC, StringComparison.OrdinalIgnoreCase) == 0
                ? 2 : 1;
        }

        /// <summary>Сортировка групп: «—» последней; остальные по имени OrdinalIgnoreCase.</summary>
        private static int GroupCompare(SymbolCatalogGroup oA, SymbolCatalogGroup oB)
        {
            bool bMiscA = string.Compare(oA.Name, STR_NO_GROUP, StringComparison.OrdinalIgnoreCase) == 0;
            bool bMiscB = string.Compare(oB.Name, STR_NO_GROUP, StringComparison.OrdinalIgnoreCase) == 0;
            if (bMiscA != bMiscB) return bMiscA ? 1 : -1;
            return string.Compare(oA.Name, oB.Name, StringComparison.OrdinalIgnoreCase);
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

// using System — чистый модуль без EPLAN-типов (компилируется и в test-раннер tests/).
using System;
using System.Collections.Generic;

namespace MyEplanActions
{
    /// <summary>Определение функции для категоризации (Этап 8, H-4b v4, rev.14.2;
    /// спека category_and_prewiev.md §4/§20). ВСЕ поля — уже ЛОКАЛИЗОВАННЫЕ строки:
    /// MultiLangString-блобы формата «de_DE@…;ru_RU@…» парсятся
    /// SymbolCatalog.LocalizeMultiLang на стороне извлечения (SymbolBrowserDialog,
    /// формат блоба доказан дампом [FD] rev.14.1). Чистый класс без EPLAN-типов
    /// (компилируется и в tests/).</summary>
    public class FdInfo
    {
        /// <summary>Trade — вершина дерева (FunctionDefinition.MainGroup).</summary>
        public string MainGroup;

        /// <summary>Area — уровень «Area» дерева (FunctionDefinition.CategoryRegion,
        /// KB: «'Area' level in function definitions tree»).</summary>
        public string Area;

        /// <summary>Категория (FunctionDefinition.CategoryName).</summary>
        public string Category;

        /// <summary>Группа (FunctionDefinition.GroupName).</summary>
        public string Group;

        /// <summary>Имя FD (FunctionDefinition.Name) — карточке (rev.14.2: категория
        /// карточки = FD.Name выбранной записи).</summary>
        public string Name;

        /// <summary>Описание FD (FunctionDefinition.Description, KB members:
        /// «Get function definition description»; рантайм НЕ подтверждён — может
        /// не извлечься, тогда карточка «Описание: —»).</summary>
        public string Description;
    }

    /// <summary>Входная запись каталога (rev.14.2): имя символа + УЖЕ резолвленный
    /// FD (Fd=null — не сопоставился: обратный словарь BaseSymbol не дал, fallback
    /// #16018 не дал, либо цепочка names-only). Сопоставление делает диалог
    /// (SymbolBrowserDialog), у SymbolCatalog только разметка дерева.</summary>
    public class SymbolCatalogEntry
    {
        /// <summary>Имя символа (как перечислен библиотекой; дубли сохраняются).</summary>
        public string Name;

        /// <summary>Определение функции (null — не сопоставился → «Без классификации»/
        /// fallback-бакеты).</summary>
        public FdInfo Fd;

        /// <summary>Символьное описание (SYMB_DESC; rev.14.3): входит в поисковый
        /// индекс Symbol-листа (SymbolCatalogNode.SearchIndex); null допустим —
        /// SYMB_DESC может не читаться (заполняет диалог в RebuildTree).</summary>
        public string Description;
    }

    /// <summary>Узел дерева каталога (rev.14.2, спека §19–§21): 5 уровней
    /// Trade → Area → Category → Group → Fd + лист Symbol (Kind=Symbol, детей нет);
    /// unmapped-символы — бакет «Без классификации» (IsFallback=true, Kind=Group),
    /// при полном отсутствии FD-пути — префиксные бакеты + «Прочие». Рендер —
    /// рекурсия в SymbolBrowserDialog.RebuildTree.</summary>
    public class SymbolCatalogNode
    {
        /// <summary>Имя узла (локализованный текст уровня; для Symbol — имя символа).</summary>
        public string Name;

        /// <summary>Тип узла — константы SymbolCatalog.KIND_*.</summary>
        public string Kind;

        /// <summary>Описание (содержательно только для Kind=Fd — FD.Description;
        /// для карточки rev.14.2 branch-карточки читает Fd-узел напрямую из записи).</summary>
        public string Description;

        /// <summary>Поисковый индекс узла (rev.14.3, фильтр поиска каталога):
        /// заполняется в Build — Symbol-лист: имя + разделитель + FD-поля×6
        /// (MainGroup, Area, Category, Group, Fd.Name, Fd.Description) +
        /// символьный Description; Fd-ветка: Name + разделитель + Description;
        /// прочие ветки и бакеты: просто Name. Разделитель — «\u0001» (как
        /// MakeFdSymbolKey SymbolBrowserDialog); сегменты null-safe; после Build
        /// не null.</summary>
        public string SearchIndex;

        /// <summary>true — fallback-узел («Прочие», префиксные бакеты,
        /// «Без классификации»); false — узел FD-пути.</summary>
        public bool IsFallback;

        /// <summary>Дочерние узлы (Symbol — всегда лист: детей не добавляется).</summary>
        public List<SymbolCatalogNode> Children
        {
            get { return _lstChildren; }
        }
        private readonly List<SymbolCatalogNode> _lstChildren = new List<SymbolCatalogNode>();

        public override string ToString()
        {
            return "[" + (Kind ?? "?") + "] " + (Name ?? "<null>");
        }
    }

    /// <summary>Чистое построение дерева каталога символов (Этап 8, H-4b v4,
    /// rev.14.2; спека category_and_prewiev.md). Вход: список SymbolCatalogEntry
    /// {Name, Fd} (Fd=null — не сопоставился) и словарь FD-ID → FdInfo (fallback-
    /// остаточный параметр для будущих UseCase'ов; резолв уже сделан на стороне
    /// диалога, здесь НЕ используется). Правила:
    /// (а) вход пуст → один fallback-узел «Прочие» без детей;
    /// (б) НИ ОДНОГО сопоставления (все Fd==null) → прежнее поведение цепочки C —
    ///     fallback-бакеты по ПРЕФИКСУ имени (ExtractNonDigitPrefix; пустой
    ///     префикс — «Прочие»), символы — исходный порядок;
    /// (в) есть хотя бы одно сопоставление → mapped-записи идут по пути
    ///     Trade (MainGroup) → Area (CategoryRegion) → Category (CategoryName) →
    ///     Group (GroupName) → Fd (Name) → лист Symbol; уникальность узлов уровня —
    ///     по имени (OrdinalIgnoreCase), Fd-узел — по (путь групп + FD.Name);
    ///     пустые уровни ПРОПУСКАЮТСЯ (коллапс, безликих узлов нет); unmapped —
    ///     бакет «Без классификации» в корне (Kind=Group, IsFallback=true),
    ///     символы — листьями напрямую, в исходном порядке;
    /// (г) сортировка: узлы FD-пути по имени OrdinalIgnoreCase; «Прочие»/«Без
    ///     классификации» — последними; списки Symbol-листьев НЕ сортируются
    ///     (исходный порядок библиотеки, дубли сохраняются).
    /// LocalizeMultiLang — парсер MultiLangString-блоба (ru_RU → en_US → de_DE →
    /// первый непустой). rev.14.3 (аддитивно): SymbolCatalogEntry.Description
    /// (SYMB_DESC), SymbolCatalogNode.SearchIndex (заполняется в Build, после
    /// Build не null), IsCellEnabled — строгий три-состояние клеток превью
    /// (решение пользователя, summary п.93).</summary>
    public static class SymbolCatalog
    {
        /// <summary>Имя бакета-«отстойника»: пустой префикс / пустой вход.</summary>
        public const string STR_MISC = "Прочие";

        /// <summary>Корневой бакет символов без FD при частичном сопоставлении.</summary>
        public const string STR_UNCLASSIFIED = "Без классификации";

        /// <summary>Прочерк: FD-имя пустое / пустые уровни куда раньше писали «—».</summary>
        internal const string STR_DASH = "—";

        /// <summary>Kind узлов: Trade (FD MainGroup).</summary>
        public const string KIND_TRADE = "Trade";

        /// <summary>Kind узлов: Area (FD CategoryRegion).</summary>
        public const string KIND_AREA = "Area";

        /// <summary>Kind узлов: Category (FD CategoryName).</summary>
        public const string KIND_CATEGORY = "Category";

        /// <summary>Kind узлов: Group (FD GroupName) — так же помечены fallback-бакеты.</summary>
        public const string KIND_GROUP = "Group";

        /// <summary>Kind узлов: Fd (FD.Name; Description — содержательно).</summary>
        public const string KIND_FD = "Fd";

        /// <summary>Kind листьев: Symbol (имя символа; детей нет).</summary>
        public const string KIND_SYMBOL = "Symbol";

        /// <summary>Построение дерева (см. класс-комментарий; dctFdById — параметр
        /// контракта rev.14.2, оставлен для будущих UseCase'ов и единообразия
        /// вызова; классификация опирается ТОЛЬКО на entry.Fd).</summary>
        public static List<SymbolCatalogNode> Build(List<SymbolCatalogEntry> lstEntries,
            Dictionary<long, FdInfo> dctFdById)
        {
            // (а) пустой/null вход — один fallback-узел «Прочие» без детей.
            if (lstEntries == null || lstEntries.Count == 0)
            {
                List<SymbolCatalogNode> lstEmpty = new List<SymbolCatalogNode>();
                lstEmpty.Add(MakeBucket(STR_MISC));
                return lstEmpty;
            }

            List<SymbolCatalogNode> lstMappedTop = new List<SymbolCatalogNode>();
            SymbolCatalogNode oUnclassified = null;

            foreach (SymbolCatalogEntry oEntry in lstEntries)
            {
                if (oEntry == null) continue;
                if (oEntry.Fd != null)
                {
                    AddToFdTree(lstMappedTop, oEntry.Name, oEntry.Fd,
                        oEntry.Description);
                }
                else
                {
                    if (oUnclassified == null)
                    {
                        oUnclassified = MakeBucket(STR_UNCLASSIFIED);
                    }
                    SymbolCatalogNode oLeaf = MakeLeaf(oEntry.Name);
                    oLeaf.SearchIndex = MakeLeafSearchIndex(oEntry.Name, oEntry.Fd,
                        oEntry.Description);
                    oUnclassified.Children.Add(oLeaf);
                }
            }

            List<SymbolCatalogNode> lstResult;
            if (lstMappedTop.Count > 0)
            {
                // (в) основной путь; unmapped — бакет «Без классификации» последним.
                if (oUnclassified != null) lstMappedTop.Add(oUnclassified);
                lstResult = lstMappedTop;
                SortBranches(lstResult);
                return lstResult;
            }

            // (б) ни одного сопоставления — префиксные fallback-бакеты (цепочка C).
            return BuildPrefixBuckets(lstEntries);
        }

        /// <summary>Клетка превью A–H активна? Строгий три-состояние «известен /
        /// есть / неизвестен» (решение пользователя, summary п.93): клетка
        /// nVariantNr (A=0 … H=7) включена ТОЛЬКО если варианты ИЗВЕСТНЫ
        /// (bVariantsKnown=true — Symbol.Variants прочитан успешно, пусть даже
        /// при 0 вариантах) И список реальных VariantNr не null И содержит
        /// nVariantNr. «Неизвестно → есть только клетка A» ЗАПРЕЩЕНО:
        /// bVariantsKnown=false → false (все клетки выключены); известный пустой
        /// список (0 реальных вариантов) → false (все клетки выключены);
        /// null-список → false.</summary>
        public static bool IsCellEnabled(bool bVariantsKnown,
            List<int> lstVariantNrs, int nVariantNr)
        {
            if (!bVariantsKnown) return false;
            if (lstVariantNrs == null) return false;
            return lstVariantNrs.Contains(nVariantNr);
        }

        /// <summary>Mapped-запись: путь Trade→Area→Category→Group (пустые уровни
        /// пропускаются) → Fd-узел (имя FD.Name, пустое — «—») → лист символа
        /// (дубли сохраняются). strEntryDescription — символьный SYMB_DESC входной
        /// записи: входит в SearchIndex листа (rev.14.3).</summary>
        private static void AddToFdTree(List<SymbolCatalogNode> lstMappedTop,
            string strName, FdInfo oFd, string strEntryDescription)
        {
            List<SymbolCatalogNode> lstLevel = lstMappedTop;

            if (!string.IsNullOrEmpty(oFd.MainGroup))
            {
                SymbolCatalogNode oTrade = FindOrMake(lstLevel, oFd.MainGroup,
                    KIND_TRADE, null);
                lstLevel = oTrade.Children;
            }
            if (!string.IsNullOrEmpty(oFd.Area))
            {
                SymbolCatalogNode oArea = FindOrMake(lstLevel, oFd.Area, KIND_AREA, null);
                lstLevel = oArea.Children;
            }
            if (!string.IsNullOrEmpty(oFd.Category))
            {
                SymbolCatalogNode oCategory = FindOrMake(lstLevel, oFd.Category,
                    KIND_CATEGORY, null);
                lstLevel = oCategory.Children;
            }
            if (!string.IsNullOrEmpty(oFd.Group))
            {
                SymbolCatalogNode oGroup = FindOrMake(lstLevel, oFd.Group, KIND_GROUP, null);
                lstLevel = oGroup.Children;
            }
            string strFdName = string.IsNullOrEmpty(oFd.Name) ? STR_DASH : oFd.Name;
            SymbolCatalogNode oFdNode = FindOrMake(lstLevel, strFdName, KIND_FD,
                oFd.Description);
            SymbolCatalogNode oLeaf = MakeLeaf(strName);
            oLeaf.SearchIndex = MakeLeafSearchIndex(strName, oFd,
                strEntryDescription);
            oFdNode.Children.Add(oLeaf);
        }

        /// <summary>Поиск/создание дочернего узла: уникальность — по (Kind, имя)
        /// OrdinalIgnoreCase (спека: уникальность узлов по имени уровня; Fd-узел —
        /// по (путь групп + FD.Name), что даёт поиск внутри нужной группы).
        /// rev.14.3: SearchIndex ветки заполняется сразу (Fd-узел — Name +
        /// «\u0001» + Description; прочие ветки — Name).</summary>
        private static SymbolCatalogNode FindOrMake(List<SymbolCatalogNode> lstSiblings,
            string strName, string strKind, string strDescription)
        {
            foreach (SymbolCatalogNode oChild in lstSiblings)
            {
                if (oChild.Kind == strKind &&
                    string.Compare(oChild.Name, strName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return oChild;
                }
            }
            SymbolCatalogNode oMade = new SymbolCatalogNode();
            oMade.Name = strName;
            oMade.Kind = strKind;
            oMade.Description = strDescription;
            oMade.SearchIndex = MakeFdNodeSearchIndex(strName, strDescription,
                strKind == KIND_FD);
            lstSiblings.Add(oMade);
            return oMade;
        }

        /// <summary>SearchIndex Symbol-листа (rev.14.3): имя + «\u0001» + FD-блок
        /// (MainGroup, Area, Category, Group, Fd.Name, Fd.Description — null-safe)
        /// + «\u0001» + символьный Description. FD-блок при Fd==null опускается;
        /// поиск по описанию работает и вне FD (Name + «\u0001» + Description);
        /// Fd==null и Description==null → ровно Name (CaseSearchIndexLeafNoFd,
        /// без хвостового разделителя); поиск «выключатель» → OLS.</summary>
        private static string MakeLeafSearchIndex(string strName, FdInfo oFd,
            string strEntryDescription)
        {
            if (oFd == null)
            {
                return strEntryDescription == null
                    ? strName
                    : strName + "\u0001" + strEntryDescription;
            }
            return strName
                + "\u0001"
                + (oFd.MainGroup ?? string.Empty)
                + "\u0001" + (oFd.Area ?? string.Empty)
                + "\u0001" + (oFd.Category ?? string.Empty)
                + "\u0001" + (oFd.Group ?? string.Empty)
                + "\u0001" + (oFd.Name ?? string.Empty)
                + "\u0001" + (oFd.Description ?? string.Empty)
                + "\u0001" + (strEntryDescription ?? string.Empty);
        }

        /// <summary>SearchIndex ветки/бакета (rev.14.3): Fd-узел — Name +
        /// «\u0001» + Description; прочие ветки и бакеты — просто Name.</summary>
        private static string MakeFdNodeSearchIndex(string strName,
            string strDescription, bool bIsFd)
        {
            if (bIsFd) return strName + "\u0001" + (strDescription ?? string.Empty);
            return strName;
        }

        /// <summary>(б) префиксные fallback-бакеты (цепочка C): ведущая нецифровая
        /// часть имени; пустой/цифровой с первой позиции — «Прочие». Символы — в
        /// исходном порядке; бакеты — по имени, «Прочие» последним.</summary>
        private static List<SymbolCatalogNode> BuildPrefixBuckets(
            List<SymbolCatalogEntry> lstEntries)
        {
            List<SymbolCatalogNode> lstTop = new List<SymbolCatalogNode>();
            Dictionary<string, SymbolCatalogNode> dctBuckets =
                new Dictionary<string, SymbolCatalogNode>(StringComparer.OrdinalIgnoreCase);
            foreach (SymbolCatalogEntry oEntry in lstEntries)
            {
                if (oEntry == null) continue;
                string strName = oEntry.Name;
                string strPrefix = ExtractNonDigitPrefix(strName);
                string strKey = strPrefix.Length == 0 ? STR_MISC : strPrefix;
                SymbolCatalogNode oBucket;
                if (!dctBuckets.TryGetValue(strKey, out oBucket))
                {
                    oBucket = MakeBucket(strKey);
                    dctBuckets[strKey] = oBucket;
                    lstTop.Add(oBucket);
                }
                SymbolCatalogNode oLeaf = MakeLeaf(strName);
                oLeaf.SearchIndex = MakeLeafSearchIndex(strName, oEntry.Fd,
                    oEntry.Description);
                oBucket.Children.Add(oLeaf);
            }
            SortBranches(lstTop);
            return lstTop;
        }

        /// <summary>Fallback-бакет («Прочие», префикс, «Без классификации»):
        /// Kind=Group, IsFallback=true. rev.14.3: SearchIndex = Name.</summary>
        private static SymbolCatalogNode MakeBucket(string strName)
        {
            SymbolCatalogNode oBucket = new SymbolCatalogNode();
            oBucket.Name = strName;
            oBucket.Kind = KIND_GROUP;
            oBucket.IsFallback = true;
            oBucket.SearchIndex = strName;
            return oBucket;
        }

        /// <summary>Лист-символ: Kind=Symbol, без детей; SearchIndex-дефолт =
        /// имя (контракт «после Build не null» не зависит от call-site — минимор
        /// ревью rev.14.3 Task 1; call-sites перезаписывают поверх).</summary>
        private static SymbolCatalogNode MakeLeaf(string strName)
        {
            SymbolCatalogNode oLeaf = new SymbolCatalogNode();
            oLeaf.Name = strName;
            oLeaf.Kind = KIND_SYMBOL;
            oLeaf.SearchIndex = strName;
            return oLeaf;
        }

        /// <summary>Сортировка НЕ-листовых уровней после постройки (SortBranches):
        /// по имени OrdinalIgnoreCase; fallback-бакеты и «Прочие» — последними; списки
        /// Symbol-листьев НЕ трогаются (исходный порядок библиотеки).</summary>
        private static void SortBranches(List<SymbolCatalogNode> lstNodes)
        {
            bool bHasBranch = false;
            foreach (SymbolCatalogNode oNode in lstNodes)
            {
                if (oNode.Kind != KIND_SYMBOL) { bHasBranch = true; break; }
            }
            if (!bHasBranch) return;
            lstNodes.Sort(NodeCompare);
            foreach (SymbolCatalogNode oNode in lstNodes)
            {
                if (oNode.Kind != KIND_SYMBOL && oNode.Children.Count > 0)
                    SortBranches(oNode.Children);
            }
        }

        /// <summary>Сортировка узлов уровня: «Прочие»/fallback-бакеты — последними,
        /// внутри уровня по имени OrdinalIgnoreCase.</summary>
        private static int NodeCompare(SymbolCatalogNode oA, SymbolCatalogNode oB)
        {
            int nA = TierOf(oA);
            int nB = TierOf(oB);
            if (nA != nB) return nA - nB;
            return string.Compare(oA.Name ?? string.Empty, oB.Name ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Уровень: 0 — FD-путь; 1 — fallback-бакет; 2 — «Прочие».</summary>
        private static int TierOf(SymbolCatalogNode oNode)
        {
            if (oNode == null) return 0;
            if (string.Compare(oNode.Name ?? string.Empty, STR_MISC,
                StringComparison.OrdinalIgnoreCase) == 0) return 2;
            if (oNode.IsFallback) return 1;
            return 0;
        }

        /// <summary>Локализация MultiLangString-блоба (формат доказан дампом [FD]
        /// rev.14.1 и.ToString() API): «de_DE@Текст;en_US@Text;ru_RU@Текст;…».
        /// Приоритет ru_RU → en_US → de_DE → ПЕРВЫЙ непустой элемент; элемент без
        /// '@' — кандидат на «первый непустой» (текст целиком); строка вовсе без
        /// '@' возвращается как есть (обрезанная); null/пусто — string.Empty.
        /// Чистый статический метод (НЕ угадываем поверхность API — парсим строку).</summary>
        public static string LocalizeMultiLang(string strRaw)
        {
            if (strRaw == null) return string.Empty;
            string strTrimmed = strRaw.Trim();
            if (strTrimmed.Length == 0) return string.Empty;
            if (strTrimmed.IndexOf('@') < 0) return strTrimmed;
            string[] arrParts = strTrimmed.Split(';');
            foreach (string strLang in new string[]
                { "ru_RU", "en_US", "de_DE" })   // приоритет по решению задачи
            {
                foreach (string strPart in arrParts)
                {
                    string strText = ExtractLangText(strPart, strLang);
                    if (!string.IsNullOrEmpty(strText)) return strText;
                }
            }
            foreach (string strPart in arrParts)
            {
                string strText = ExtractLangText(strPart, null);
                if (!string.IsNullOrEmpty(strText)) return strText;
            }
            return string.Empty;
        }

        /// <summary>Имя FD из FUNC_CATEGORY_GROUP_ID (#20188, rev.14.8): KB-формат
        /// «Category / Group / Function definition» — имя определения функции это
        /// ПОСЛЕДНИЙ сегмент по разделителю " / " (пробел-слэш-пробел). Правила
        /// (решение по плану rev.14.8): null/пусто → null; разделителя нет → null;
        /// последний сегмент после Trim пустой → null — строго ПОСЛЕДНИЙ сегмент,
        /// «последний непустой» НЕ ищем (хвост " / " и пустой сегмент — null);
        /// сегмент Trim'ится. Чистая статика (компилируется и в tests/).</summary>
        public static string ExtractFdNameFromCategoryGroup(string strCatGroup)
        {
            if (string.IsNullOrEmpty(strCatGroup)) return null;
            string strSep = " / ";
            int nPos = strCatGroup.LastIndexOf(strSep, StringComparison.Ordinal);
            if (nPos < 0) return null;
            string strName = strCatGroup.Substring(nPos + strSep.Length).Trim();
            return strName.Length == 0 ? null : strName;
        }

        /// <summary>Текст одного элемента блоба: strLang==null — «любой язык»
        /// (элемент без '@' тоже кандидат); иначе текст элемента, чей язык совпал
        /// (OrdinalIgnoreCase); не совпал/пусто — string.Empty.</summary>
        private static string ExtractLangText(string strPart, string strLang)
        {
            if (strPart == null) return string.Empty;
            string strPartTrim = strPart.Trim();
            if (strPartTrim.Length == 0) return string.Empty;
            int nAt = strPartTrim.IndexOf('@');
            if (nAt < 0)
            {
                // Элемент без языка — кандидат только при запросе «любого» языка.
                return strLang == null ? strPartTrim : string.Empty;
            }
            string strPartLang = strPartTrim.Substring(0, nAt).Trim();
            string strPartText = strPartTrim.Substring(nAt + 1).Trim();
            if (strLang == null) return strPartText;
            return string.Compare(strPartLang, strLang, StringComparison.OrdinalIgnoreCase) == 0
                ? strPartText : string.Empty;
        }

        /// <summary>Ведущая нецифровая часть имени (до первой цифры) — по образцу
        /// SplitDeviceTagLetterCounter (CableSymbolCreator.cs); пустая/цифровая
        /// с первой позиции → «» (→ «Прочие»). Null/пустое имя — «».</summary>
        private static string ExtractNonDigitPrefix(string strName)
        {
            if (string.IsNullOrEmpty(strName)) return string.Empty;
            int i = 0;
            while (i < strName.Length && !char.IsDigit(strName[i])) i++;
            return strName.Substring(0, i);
        }
    }
}

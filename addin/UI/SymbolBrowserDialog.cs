using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;

namespace MyEplanActions
{
    /// <summary>Браузер символов кабеля v2 (Этап 8, задача H-4b, ruling R11+см. план
    /// § Task H-4b): выбор библиотеки/символа + два слота варианта (H/V, 0-based) +
    /// ДЕРЕВО КАТЕГОРИЙ + ГРАФИЧЕСКОЕ ПРЕВЬЮ. Программный layout без designer
    /// (паттерн MainDialog: TableLayoutPanel, хелперы статическими методами —
    /// локальных функций в C#5 нет). Диалог ТОЛЬКО ЧИТАЕТ — никаких EPLAN-мутаций
    /// (display list рендера — внутренний объект DrawingService, проект не трогает).
    /// Выход: свойства Library/SymbolName/VariantH/VariantV (rev.14.0 — НЕ менялись);
    /// «ОК» валиден только при непустых Library/SymbolName (значения НЕ триммингуются —
    /// урок хвостового пробела п.33). «Отмена»/крестик — ничего не возвращается.
    /// СЛЕВА: библиотеки (как rev.13.1) + поиск + TreeView «Библиотека → Категория →
    /// Символ» (листья Tag=имя; двойной клик = ОК после валидации; поиск фильтрует
    /// ЛИСТЬЯ, пустые категории скрываются; предвыбор — разворот пути + подсветка).
    /// СПРАВА: превью-Panel + карточка (имя/категория/«вариантов: N»/NumericUpDown
    /// «Вариант превью» 0..count−1, по умолчанию 0, НЕ связан со слотами H/V) +
    /// статусная строка внизу на всю ширину.
    /// Категории дерева — чистый SymbolCatalog (addin/UI/SymbolCatalog.cs): ПЕРВИЧНО
    /// по FD-ID (SYMB_MAINFUNCTION #16018 из MDSymbol, цепочка B); в цепочке A FD
    /// недоступен — fallback-бакеты по префиксу имени + «Прочие».
    /// KB-факты API 2.9 (www.eplan.help; грабля базы: блок [Code] — артефакт
    /// скрейпера, авторитетен Remarks):
    /// - Project.SymbolLibraries — «Gets the symbol libraries used by the project»,
    ///   «public SymbolLibrary[] SymbolLibraries { get; }» — KB-доказан (rev.13.1,
    ///   цепочка A листинга) —
    ///   https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.Project~SymbolLibraries.html
    /// - MDSymbolLibrary.Symbols — «Gets a read only list of all Symbols in the
    ///   library», «public MDSymbol[] Symbols { get; }» (rev.13.1, цепочка B) —
    ///   https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.MasterDatau~Eplan.EplApi.MasterData.MDSymbolLibrary~Symbols.html
    /// - MDSymbol.Variants — «a read only list of all MDSymbolVariants in the
    ///   symbol», «public MDSymbolVariant[] Variants { get; }» —
    ///   https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.MasterDatau~Eplan.EplApi.MasterData.MDSymbol~Variants.html
    /// - Project.FunctionDefinitionLibrary (класс
    ///   Eplan.EplApi.DataModel.MasterData.FunctionDefinitionLibrary) и его
    ///   FunctionDefinitions : FunctionDefinition[] («public FunctionDefinition[]
    ///   FunctionDefinitions { get; }») — KB-доказано; члены FunctionDefinition
    ///   («Id», «Name», …) НЕ доказаны — ТОЛЬКО reflection-пробы (урок rev.7).
    ///   https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.Project~FunctionDefinitionLibrary.html
    ///   https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.MasterData.FunctionDefinitionLibrary~FunctionDefinitions.html
    /// - MDSymbolPropertyList ctor (MDSymbol) + SYMB_MAINFUNCTION (Main function
    ///   # 16018, «Returns property value of type System.Int64» → MDPropertyValue) —
    ///   https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.MasterDatau~Eplan.EplApi.MasterData.MDSymbolPropertyList~SYMB_MAINFUNCTION().html
    /// - MasterData.Symbol(SymbolLibrary, String) и SymbolVariant(Symbol, Int32) —
    ///   конструкторы KB-доказаны; символ из библиотеки + вариант — вход display list:
    ///   https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.MasterData.Symbol~_ctor.html
    ///   https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.MasterData.SymbolVariant~_ctor.html
    ///   (паттерн «new SymbolLibrary(page.Project, name); new Symbol(symbolLibrary,
    ///   symbolName)» — Suplanus.Sepla SymbolUtility.cs, база eplan_api).
    /// - HEServices.DrawingService: ctor() без параметров; CreateDisplayList
    ///   (SymbolVariant[, Boolean bReturnSymbolConnectionPointsData]) — «Creates a
    ///   display list for a symbol variant», перегрузки (String,String,Int32,Project),
    ///   (Placement), (Placement[]), (Page[]), (WindowMacro), (StorableObject[]);
    ///   DrawDisplayList(PaintEventArgs, Rectangle) — «Draws a display list on a
    ///   window. The preview is fit to the window, while keeping its aspect ratio.
    ///   Only the rectangle specified by the parameter rcClientRect will be used»;
    ///   SetDefaultViewport — «Adjusts viewport to the bounding box of the objects
    ///   from drawing list» (включён после CreateDisplayList — безвредно); свойства
    ///   DrawConnections/DrawBackGround/MacroPreview и др.; Reset()/Dispose().
    ///   https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.HEServicesu~Eplan.EplApi.HEServices.DrawingService.html
    ///   https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.HEServicesu~Eplan.EplApi.HEServices.DrawingService~DrawDisplayList.html
    ///   https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.HEServicesu~Eplan.EplApi.HEServices.DrawingService~SetDefaultViewport.html
    ///   Примеры: DrawingService.html (Panel.Paint → DrawDisplayList) и HE_Display.html
    ///   (CreateDisplayList(strObj,"",0,gProject) → Picture1.Invalidate()).
    /// НЕ доказано KB: члены FunctionDefinition, поверхность MDPropertyValue
    /// (значение числа — reflection-проба), перегрузка CreateDisplayList
    /// (String,String,RepresentationType,Int32,Project) — сознательно НЕ пробуется
    /// (одна гипотеза за прогон; RepresentationType не доказан). Отказы проб —
    /// Console-дамп + статусная строка, ручной ввод имени не запрещаем.</summary>
    public class SymbolBrowserDialog : Form
    {
        private readonly Project _oProject;
        private readonly TextBox _txtLibrary = new TextBox();
        private readonly ListBox _lstLibraries = new ListBox();
        private readonly TextBox _txtSearch = new TextBox();
        private readonly TreeView _treeSymbols = new TreeView();
        private readonly TextBox _txtSymbol = new TextBox();
        private readonly NumericUpDown _numVariantH = new NumericUpDown();
        private readonly NumericUpDown _numVariantV = new NumericUpDown();
        private readonly Label _lblCount = new Label();
        private readonly Label _lblStatus = new Label();

        // H-4b: карточка превью (справа) — имя/категория/число вариантов + слайдер варианта.
        private readonly Panel _pnlPreview = new Panel();
        private readonly Label _lblPreviewName = new Label();
        private readonly Label _lblPreviewCategory = new Label();
        private readonly Label _lblPreviewVariants = new Label();
        private readonly NumericUpDown _numPreviewVariant = new NumericUpDown();

        // Объекты библиотек под строками списка (параллельны _lstLibraries.Items).
        private readonly List<SymbolLibrary> _lstLibraryObjects = new List<SymbolLibrary>();
        // Все символы выбранной библиотеки (поиск фильтрует дерево отсюда);
        // параллельные списки — число вариантов (-1 — неизвестно; R11: тогда
        // свободный int-ввод ≥ 0) и FD-ID по символу (null — FD не извлёкся или
        // цепочка A; недостающие в конце списка — тоже null).
        private List<string> _lstAllSymbols = new List<string>();
        private List<int> _lstAllVariantCounts = new List<int>();
        private List<long?> _lstAllFdIds = new List<long?>();

        // H-4b: словарь FD-ID → имя категории (проект, строится один раз) и
        // [SYMFDMAP]-счётчик сопоставления текущего перечисления.
        private Dictionary<long, string> _dctFdNames;
        private int _nFdMapped;

        // Fix-минор r14.1: чистые имена категорий по узлам дерева (текст узла
        // содержит счётчик «(N)» — карточке он не нужен; Tag листьев занят именем
        // символа, категориям Tag сознательно не назначаем — см. FindLeaf).
        private readonly Dictionary<TreeNode, string> _dctCatNodeNames =
            new Dictionary<TreeNode, string>();

        // H-4b: DrawingService — один экземпляр на жизнь диалога, Dispose при закрытии.
        private DrawingService _oDrawingService;
        private bool _bDrawingServiceReady;
        private bool _bDisplayListReady;
        private bool _bFdDumped;

        /// <summary>Вход: открытый проект (null — списки пусты, только ручной
        /// ввод) и текущий выбор: строки как есть (предвыбор точным совпадением
        /// БЕЗ trim — урок п.33), индексы вариантов прижимаются к 0..</summary>
        public SymbolBrowserDialog(Project oProject, string strLibrary,
            string strName, int nVariantH, int nVariantV)
        {
            _oProject = oProject;

            Text = "Выбор символа кабеля";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(840, 620);

            // Диапазон слотов: 0-based (эмпирика rev.10.0), сначала свободный
            // int-ввод (count неизвестен), сузится при выборе символа с известным
            // числом вариантов (MDSymbol.Variants, KB).
            _numVariantH.Minimum = 0;
            _numVariantV.Minimum = 0;
            _numVariantH.Maximum = int.MaxValue;
            _numVariantV.Maximum = int.MaxValue;
            SetVariantValue(_numVariantH, nVariantH);
            SetVariantValue(_numVariantV, nVariantV);

            // Слот «Вариант превью» — НЕ связан со слотами H/V (спека H-4b):
            // 0..count−1 при известном count, default 0.
            _numPreviewVariant.Minimum = 0;
            _numPreviewVariant.Maximum = int.MaxValue;
            SetVariantValue(_numPreviewVariant, 0);

            // Карточка превью — прочерк до первого выбора.
            _lblPreviewName.Text = "Имя: —";
            _lblPreviewCategory.Text = "Категория: —";
            _lblPreviewVariants.Text = "вариантов: —";

            _txtLibrary.Text = strLibrary ?? string.Empty;
            _txtSymbol.Text = strName ?? string.Empty;

            // (б) DrawingService — H-4b: один экземпляр на жизнь диалога
            // (Dispose при закрытии, OnFormClosing); базовые настройки вида —
            // до первого CreateDisplayList (факт вида зафиксирует прогон).
            try
            {
                _oDrawingService = new DrawingService();
                _bDrawingServiceReady = true;
            }
            catch (Exception oEx)
            {
                Console.WriteLine("[DSPROBE] конструктор DrawingService — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            if (_bDrawingServiceReady) ApplyPreviewSettings();

            // (1) Библиотеки: Project.SymbolLibraries (KB-цитата в шапке).
            LoadLibraries(strLibrary);

            // (2) Реакции (диалог только читает; программы мутаций нет).
            _lstLibraries.SelectedIndexChanged += LstLibrariesOnSelectedIndexChanged;
            _treeSymbols.AfterSelect += TreeSymbolsOnAfterSelect;
            _treeSymbols.NodeMouseDoubleClick += TreeSymbolsOnNodeMouseDoubleClick;
            _txtSearch.TextChanged += delegate { RebuildTree(); };
            // Ручной ввод имени символа: точное совпадение в дереве — подсветка +
            // сужение диапазона вариантов.
            _txtSymbol.TextChanged += TxtSymbolOnTextChanged;
            _numPreviewVariant.ValueChanged += delegate { RebuildPreview(); };
            _pnlPreview.Paint += delegate(object oSender, PaintEventArgs oArgs)
            {
                DrawingService oDs = _oDrawingService;
                if (oDs == null || !_bDisplayListReady) return;
                try
                {
                    oDs.DrawDisplayList(oArgs, _pnlPreview.ClientRectangle);
                }
                catch (Exception oEx)
                {
                    // Отказ рендера — Console-дамп, выбор символа не ломаем (H-4b).
                    Console.WriteLine("[DSPROBE] DrawDisplayList — " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
            };

            // (3) Символы: цепочки перечисления для предвыбранной библиотеки
            // (обработчики уже навешаны — предвыбор имени применится внутри).
            if (_lstLibraries.SelectedIndex >= 0)
                RefreshSymbols();
            SelectSymbolPrechoice(strName);

            // (4) Кнопки: «ОК» — валидация в Click (DialogResult=OK ставится
            // ТОЛЬКО при непустых Library/Name — паттерн MainDialog «Создать»);
            // «Отмена» — DialogResult.Cancel (CancelButton).
            Button btnOk = new Button();
            btnOk.Text = "ОК";
            btnOk.Size = new Size(110, 27);
            btnOk.Click += BtnOkOnClick;
            Button btnCancel = new Button();
            btnCancel.Text = "Отмена";
            btnCancel.Size = new Size(110, 27);
            btnCancel.DialogResult = DialogResult.Cancel;
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            BuildLayout(btnOk, btnCancel);
        }

        /// <summary>Имя библиотеки (как выбрано/введено, БЕЗ trim).</summary>
        public string Library
        {
            get { return _txtLibrary.Text; }
        }

        /// <summary>Имя символа (как выбрано/введено, БЕЗ trim). Fix-3 (R13):
        /// переименовано с Name — то имя скрывало наследуемый Control.Name
        /// (CS0108); new-тенью Form-член не перекрываем.</summary>
        public string SymbolName
        {
            get { return _txtSymbol.Text; }
        }

        /// <summary>Индекс варианта слота H (0-based).</summary>
        public int VariantH
        {
            get { return (int)_numVariantH.Value; }
        }

        /// <summary>Индекс варианта слота V (0-based).</summary>
        public int VariantV
        {
            get { return (int)_numVariantV.Value; }
        }

        // --- обработчики ---

        private void LstLibrariesOnSelectedIndexChanged(object oSender, EventArgs oArgs)
        {
            int iIndex = _lstLibraries.SelectedIndex;
            if (iIndex >= 0 && iIndex < _lstLibraryObjects.Count)
                _txtLibrary.Text = _lstLibraryObjects[iIndex] == null
                    ? string.Empty
                    : ResolveDisplayPath(_lstLibraryObjects[iIndex]);
            RefreshSymbols();
        }

        /// <summary>Выбор ЛИСТА дерева (символа) — ручной ввод уходит в TextBox,
        /// диапазоны сужаются, карточка/превью обновляются. Корень/категория —
        /// ничего (Tag только у листьев).</summary>
        private void TreeSymbolsOnAfterSelect(object oSender, TreeViewEventArgs oArgs)
        {
            TreeNode oNode = oArgs == null ? null : oArgs.Node;
            if (oNode == null || oNode.Tag == null) return;
            string strShown = (string)oNode.Tag;
            _txtSymbol.Text = strShown;   // TextChanged ничего не делает — лист уже выделен
            UpdateVariantRange(strShown);
            UpdateCard(strShown, CategoryOfNode(oNode.Parent));
            RebuildPreview();
        }

        /// <summary>Чистое имя категории узла-родителя (фикс r14.1: минорный
        /// пункт 4 ревью — текст узла содержит счётчик «(N)», карточке нужен
        /// только символ): карта из RebuildTree; узел не из дерева текущей
        /// сборки — fallback на текст узла.</summary>
        private string CategoryOfNode(TreeNode oCat)
        {
            if (oCat == null) return string.Empty;
            string strName;
            if (_dctCatNodeNames.TryGetValue(oCat, out strName) &&
                !string.IsNullOrEmpty(strName))
            {
                return strName;
            }
            return oCat.Text;
        }

        /// <summary>Двойной клик по листу = ОК (наше «создать» — после валидации,
        /// как кнопка). Клик по корню/категории — игнор.</summary>
        private void TreeSymbolsOnNodeMouseDoubleClick(object oSender,
            TreeNodeMouseClickEventArgs oArgs)
        {
            if (oArgs == null || oArgs.Node == null || oArgs.Node.Tag == null) return;
            BtnOkOnClick(oArgs.Node, oArgs);
        }

        /// <summary>Ручной ввод имени (фикс r14.1): очистка имени — сброс превью
        /// пречерками; точное совпадение в дереве — подсветка + сужение диапазона;
        /// имя НЕ в дереве — карточка по имени и полный каскад превью
        /// (RebuildPreview: типизированный + строковый CreateDisplayList(strName,
        /// strLib, n, project), отказ — честный статус).</summary>
        private void TxtSymbolOnTextChanged(object oSender, EventArgs oArgs)
        {
            string strName = _txtSymbol.Text;
            if (string.IsNullOrEmpty(strName))
            {
                // Очистка имени — старый display list гасим (картинка старого
                // символа при пустой строке недопустима); карточка — прочерки.
                RebuildPreview();   // пустое имя → сброс display list + ClearCard
                return;
            }
            TreeNode oLeaf = FindLeaf(_treeSymbols.Nodes, strName);   // точное, без trim (п.33)
            if (oLeaf != null)
            {
                if (_treeSymbols.SelectedNode != oLeaf)
                {
                    // Разворот пути до листа + подсветка (AfterSelect обновит
                    // диапазон, карточку и превью — повторный вызов не нужен).
                    TreeNode oWalk = oLeaf;
                    while (oWalk != null)
                    {
                        oWalk.Expand();
                        oWalk = oWalk.Parent;
                    }
                    _treeSymbols.SelectedNode = oLeaf;
                }
                else
                {
                    RebuildPreview();   // лист уже был выделен — превью по текущему полю
                }
            }
            else
            {
                // Имя НЕ в дереве (ручной ввод / лист скрыт фильтром / сменилась
                // библиотека): ранее ветка молчала — старая картинка оставалась при
                // новом имени. Диапазон вариантов — по перечислению (если имя там);
                // карточка и превью — всегда по имени из поля.
                if (_lstAllSymbols.IndexOf(strName) >= 0)
                    UpdateVariantRange(strName);
                UpdateCard(strName, string.Empty);
                RebuildPreview();
            }
        }

        private void BtnOkOnClick(object oSender, EventArgs oArgs)
        {
            if (string.IsNullOrEmpty(Library) || string.IsNullOrEmpty(SymbolName))
            {
                MessageBox.Show(this, "Укажите библиотеку и имя символа.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }

        /// <summary>Крестик/Alt+F4 (CloseReason.UserClosing) — это «отмена»: гасим
        /// залипший DialogResult.OK (паттерн MainDialog). Валидации здесь нет.
        /// H-4b: здесь же — Dispose DrawingService (владение диалогово: один
        /// экземпляр на жизнь диалога).</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel && e.CloseReason == CloseReason.UserClosing &&
                DialogResult == DialogResult.OK)
            {
                DialogResult = DialogResult.Cancel;
            }
            if (_oDrawingService != null)
            {
                try { _oDrawingService.Dispose(); }
                catch (Exception oEx)
                {
                    Console.WriteLine("[DSPROBE] Dispose DrawingService — " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
                _oDrawingService = null;
                _bDrawingServiceReady = false;
                _bDisplayListReady = false;
            }
        }

        // --- перечисление библиотек/символов (только чтение; цепочки проб) ---

        /// <summary>Project.SymbolLibraries — KB-доказанный SymbolLibrary[]
        /// (цитата в шапке). Отображаемое имя члена НЕ доказано KB —
        /// reflection-проба кандидатов (ResolveDisplayPath), отказ — ToString().
        /// Сбой перечисления — статусная строка, пустой список (ручной ввод
        /// имени библиотеки не запрещаем).</summary>
        private void LoadLibraries(string strPreselect)
        {
            if (_oProject == null)
            {
                SetStatus("Проект недоступен — только ручной ввод.");
                return;
            }
            try
            {
                SymbolLibrary[] arrLibs = _oProject.SymbolLibraries;
                if (arrLibs == null)
                {
                    SetStatus("Project.SymbolLibraries вернул null — только ручной ввод.");
                    return;
                }
                foreach (SymbolLibrary oLib in arrLibs)
                {
                    if (oLib == null) continue;
                    _lstLibraryObjects.Add(oLib);
                    _lstLibraries.Items.Add(ResolveDisplayPath(oLib));
                }
                SelectExact(_lstLibraries, strPreselect);
                if (_lstLibraries.Items.Count == 0)
                    SetStatus("Проектных библиотек символов не найдено — только ручной ввод.");
            }
            catch (Exception oEx)
            {
                SetStatus("Project.SymbolLibraries недоступен: " +
                    oEx.GetType().Name + ": " + oEx.Message + " — только ручной ввод.");
            }
        }

        /// <summary>Цепочка перечисления символов выбранной библиотеки (R11:
        /// пробовать по очереди, первая успешная): A) перечислители DataModel
        /// SymbolLibrary — НЕ доказаны, reflection-проба (FD для дерева тут
        /// недоступен — SymbolCatalog уйдёт в fallback-префиксы); B) MDSymbolLibrary
        /// (path).Symbols — свойство KB-доказано (цитата в шапке), путь и конструктор
        /// НЕ доказаны — Activator-проба; FD-ID — ПЕРВИЧНО из MDSymbol
        /// (SYMB_MAINFUNCTION #16018, [SYMFDMAP]). Полный отказ — INFO-статус,
        /// ручной ввод имени символа не запрещаем. Никаких мутаций.</summary>
        private void RefreshSymbols()
        {
            _lstAllSymbols = new List<string>();
            _lstAllVariantCounts = new List<int>();
            _lstAllFdIds = new List<long?>();
            _nFdMapped = 0;
            _bDisplayListReady = false;
            _pnlPreview.Invalidate();
            BuildFdDictionary();   // [FD] — один раз за жизнь диалога; до [SYMFDMAP]
            _treeSymbols.Nodes.Clear();
            _lblCount.Text = string.Empty;

            int iIndex = _lstLibraries.SelectedIndex;
            if (iIndex < 0 || iIndex >= _lstLibraryObjects.Count)
            {
                SetStatus("Библиотека не выбрана из списка — список символов пуст, " +
                    "имя можно ввести вручную.");
                return;
            }
            SymbolLibrary oLib = _lstLibraryObjects[iIndex];

            // Цепочка A: DataModel-перечислитель (проба).
            if (TryEnumerateViaDataModel(oLib))
            {
                // Fix r14.1 (Important-2 ревью): ранний выход с явным [SYMFDMAP] —
                // отсутствие источника FD должно быть различимо в диагностике.
                Console.WriteLine("[SYMFDMAP] FD недоступен: перечисление через " +
                    "DataModel (fallback-префиксы)");
                SetStatus("Символы перечислены через DataModel SymbolLibrary (проба), " +
                    _lstAllSymbols.Count.ToString(CultureInfo.InvariantCulture) + " шт.; " +
                    "FD недоступен — категории по префиксу имён.");
                RebuildTree();
                return;
            }

            // Цепочка B: MDSymbolLibrary.Symbols. FIX-1 (review 09fb80d Important):
            // путь библиотеки — СВОИ кандидаты (EnumeratePathCandidates внутри
            // TryCreateMdLibrary), НЕ одно и то же значение, что отображаемое имя:
            // ResolveDisplayPath отдаёт Name первым и остаётся только отображением/
            // возвращаемым Library (бриф разделяет: «MDSymbolLibrary(path)» — про путь,
            // «резолв имён библиотек — свойство Name» — про имя).
            if (TryEnumerateViaMasterData(oLib))
            {
                Console.WriteLine("[SYMFDMAP] сопоставлено " +
                    _nFdMapped.ToString(CultureInfo.InvariantCulture) + " из " +
                    _lstAllSymbols.Count.ToString(CultureInfo.InvariantCulture) +
                    " символов, словарь FD: " +
                    _dctFdNames.Count.ToString(CultureInfo.InvariantCulture) + " записей");
                SetStatus("Символы перечислены через MDSymbolLibrary.Symbols (KB), " +
                    _lstAllSymbols.Count.ToString(CultureInfo.InvariantCulture) + " шт.; " +
                    "FD сопоставлено " + _nFdMapped.ToString(CultureInfo.InvariantCulture) +
                    " из " + _lstAllSymbols.Count.ToString(CultureInfo.InvariantCulture) + " — " +
                    "остальные категории по префиксам.");
                RebuildTree();
                return;
            }

            SetStatus("Перечисление символов недоступно (обе цепочки пробили в отказ) — " +
                "имя символа можно ввести вручную.");
        }

        // CS0618 (ruling контроллера, fix-2): PropertyInfo.GetValue(obj, args[]) —
        // устаревший, но единственный гарантированно доступный в референсе .NET 4
        // способ чтения свойства; newer overloads (GetValue(obj)/GetPropertyValue)
        // на стенде не доказаны — прямое обращение = риск CS1061. Заглушение плотной
        // парой pragma вокруг метода, вызывающего GetValue; поведение не меняется.
#pragma warning disable 618
        /// <summary>Цепочка A: reflection-перебор кандидатов перечисления у
        /// DataModel SymbolLibrary (имена членов НЕ доказаны KB — только проба).
        /// Значение — string[] или Array элементов (имена — ResolveDisplayPath);
        /// счётчик вариантов при этом -1 (неизвестен), FD — null (в цепочке A
        /// FD недоступен — план H-4b). Пустой/отказной результат кандидата —
        /// следующий; всё не удалось — false.</summary>
        private bool TryEnumerateViaDataModel(SymbolLibrary oLib)
        {
            string[] arrCandidateProps = new string[] { "Symbols", "Names" };
            foreach (string strProp in arrCandidateProps)
            {
                try
                {
                    PropertyInfo oProp = oLib.GetType().GetProperty(strProp);
                    if (oProp == null) continue;
                    object oValue = oProp.GetValue(oLib, null);
                    string[] arrStrings = oValue as string[];
                    if (arrStrings != null)
                    {
                        foreach (string strName in arrStrings)
                        {
                            if (string.IsNullOrEmpty(strName)) continue;
                            AddSymbolEntry(strName, -1, null);
                        }
                        if (_lstAllSymbols.Count > 0) return true;
                        continue;
                    }
                    System.Array arrItems = oValue as System.Array;
                    if (arrItems != null)
                    {
                        foreach (object oItem in arrItems)
                        {
                            if (oItem == null) continue;
                            string strName = ResolveDisplayPath(oItem);
                            if (string.IsNullOrEmpty(strName)) continue;
                            AddSymbolEntry(strName, -1, null);
                        }
                        if (_lstAllSymbols.Count > 0) return true;
                    }
                }
                catch (Exception oEx)
                {
                    // Проба — не мутация: отказ кандидата — в Console-дамп,
                    // переходим к следующему (урок rev.7).
                    Console.WriteLine("SymbolBrowserDialog: проба «" + strProp + "» — " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
            }
            return false;
        }
#pragma warning restore 618

        /// <summary>Цепочка B: MDSymbolLibrary по кандидатам ПУТИ библиотеки
        /// (конструктор НЕ доказан KB — Activator-проба форм (string) и
        /// (Project, string) для каждого кандидата, fix-1), затем Symbols —
        /// KB-доказанное свойство «public MDSymbol[] Symbols { get; }» (прямое
        /// типизированное обращение; сборка Eplan.EplApi.MasterDatau — добавлена
        /// в build_addin.bat). Имена: MDSymbol.Name НЕ доказан — reflection-проба.
        /// Число вариантов: MDSymbol.Variants KB-доказано (TryGetVariantCount).
        /// FD-ID (#16018): чтение с MDSymbol — TryGetFdId ([SYMFDMAP] внутри).</summary>
        private bool TryEnumerateViaMasterData(SymbolLibrary oLib)
        {
            object oMdLib = TryCreateMdLibrary(oLib);
            if (oMdLib == null) return false;
            try
            {
                Eplan.EplApi.MasterData.MDSymbol[] arrSymbols =
                    ((Eplan.EplApi.MasterData.MDSymbolLibrary)oMdLib).Symbols;
                if (arrSymbols == null || arrSymbols.Length == 0) return false;
                int iOrdinal = 0;
                foreach (Eplan.EplApi.MasterData.MDSymbol oMdSym in arrSymbols)
                {
                    if (oMdSym == null) continue;
                    string strName = ResolveNameViaReflection(oMdSym);
                    if (string.IsNullOrEmpty(strName)) continue;
                    long? nFdId = TryGetFdId(oMdSym, iOrdinal, strName);
                    AddSymbolEntry(strName, TryGetVariantCount(oMdSym), nFdId);
                    // [SYMFDMAP]-итог: сопоставлено = FD извлечён И в словаре FD.
                    if (nFdId.HasValue && _dctFdNames != null &&
                        _dctFdNames.ContainsKey(nFdId.Value))
                        _nFdMapped++;
                    iOrdinal++;
                }
                return _lstAllSymbols.Count > 0;
            }
            catch (Exception oEx)
            {
                Console.WriteLine("SymbolBrowserDialog: MDSymbolLibrary.Symbols — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return false;
            }
        }

        /// <summary>Параллельное добавление символа (имя, число вариантов, FD-ID).</summary>
        private void AddSymbolEntry(string strName, int nVariantCount, long? nFdId)
        {
            _lstAllSymbols.Add(strName);
            _lstAllVariantCounts.Add(nVariantCount);
            _lstAllFdIds.Add(nFdId);
        }

        /// <summary>FD-ID символа — ПЕРВИЧНО из MDSymbol (план H-4b): KB
        /// MDSymbolPropertyList ctor(MDSymbol) + SYMB_MAINFUNCTION (Main function
        /// # 16018; «Returns property value of type System.Int64» — поэтому
        /// MDPropertyValue, извлечение числа — reflection-проба ExtractLongVia
        /// Reflection; НЕ угадывать API — урок rev.7). Метка [SYMFDMAP]: raw
        /// + сопоставление со словарём FD для первых 10 символов (возможно,
        /// уровень MDSymbolVariant также пройдёт — отдельная проба, НЕ в rev.14.0).
        /// Отказ (нет/пусто/проба не удалась) — null: SymbolCatalog уйдёт в
        /// fallback-префиксы по этому символу.</summary>
        private static long? TryGetFdId(Eplan.EplApi.MasterData.MDSymbol oMdSym,
            int iOrdinal, string strName)
        {
            if (oMdSym == null) return null;
            long? nId = null;
            string strRaw = null;
            try
            {
                Eplan.EplApi.MasterData.MDSymbolPropertyList oProps =
                    new Eplan.EplApi.MasterData.MDSymbolPropertyList(oMdSym);
                Eplan.EplApi.MasterData.MDPropertyValue oVal = oProps.SYMB_MAINFUNCTION;
                try { strRaw = oVal == null ? null : oVal.ToString(); }
                catch (Exception oEx)
                {
                    strRaw = null;
                    Console.WriteLine("[SYMFDMAP] raw ToString «" + strName + "» — " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
                string strVia;
                nId = ExtractLongViaReflection(oVal, "SYMFD:" + strName, out strVia);
                if (iOrdinal < 10)
                {
                    Console.WriteLine("[SYMFDMAP] «" + strName + "» raw=«" +
                        (strRaw ?? "<null>") + "» id=" +
                        (nId.HasValue ? nId.Value.ToString(CultureInfo.InvariantCulture) +
                            " via " + strVia : "НЕТ"));
                }
            }
            catch (Exception oEx)
            {
                if (iOrdinal < 10)
                    Console.WriteLine("[SYMFDMAP] «" + strName + "» #16018 — " +
                        oEx.GetType().Name + ": " + oEx.Message);
            }
            return nId;
        }

        // CS0618 (ruling контроллера, fix-2): GetValue(args[]-перегрузка) — см. выше.
#pragma warning disable 618
        /// <summary>Извлечение числа из объекта-провайдера (MDPropertyValue и прочих):
        /// reflection-проба кандидатов — НИЧЕГО не угадываем (урок rev.7: CS1061 на
        /// первой стендовой сборке): (1) уже готовые long/int; (2) методы
        /// ToInt64()/ToInt(); (3) свойство Value; (4) fallback — ToString()+long.
        /// TryParse. strVia — каким путём получилось (печатается в [SYMFDMAP]).
        /// Отказ — null; ошибки каждой пробы — Console-дамп.</summary>
        private static long? ExtractLongViaReflection(object oValue, string strContext,
            out string strVia)
        {
            strVia = "—";
            if (oValue == null) return null;
            if (oValue is long) { strVia = "direct long"; return (long)oValue; }
            if (oValue is int) { strVia = "direct int"; return (long)(int)oValue; }
            Type oType = oValue.GetType();
            // (2) методы ToInt64()/ToInt() — кандидаты; отказ/отсутствие — далее.
            string[] arrMethods = new string[] { "ToInt64", "ToInt" };
            foreach (string strMethod in arrMethods)
            {
                try
                {
                    MethodInfo oMethodInfo = oType.GetMethod(strMethod, Type.EmptyTypes);
                    if (oMethodInfo == null) continue;
                    object oRes = oMethodInfo.Invoke(oValue, null);
                    if (oRes is long)
                    {
                        strVia = strMethod + "()";
                        return (long)oRes;
                    }
                    if (oRes is int)
                    {
                        strVia = strMethod + "()";
                        return (long)(int)oRes;
                    }
                }
                catch (Exception oEx)
                {
                    Console.WriteLine("[SYMFDMAP] " + strContext + " " + strMethod +
                        "() — " + oEx.GetType().Name + ": " + oEx.Message);
                }
            }
            // (3) свойство Value — может вернуть готовое число.
            try
            {
                PropertyInfo oProp = oType.GetProperty("Value");
                if (oProp != null)
                {
                    object oVal = oProp.GetValue(oValue, null);
                    if (oVal is long) { strVia = "Value(long)"; return (long)oVal; }
                    if (oVal is int)
                    {
                        strVia = "Value(int)";
                        return (long)(int)oVal;
                    }
                }
            }
            catch (Exception oEx)
            {
                Console.WriteLine("[SYMFDMAP] " + strContext + " Value — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            // (4) fallback — ToString + TryParse (урок: НЕ угадывать API).
            try
            {
                string strText = oValue.ToString();
                if (!string.IsNullOrEmpty(strText))
                {
                    long nParsed;
                    if (long.TryParse(strText.Trim(), out nParsed))
                    {
                        strVia = "ToString+TryParse";
                        return nParsed;
                    }
                }
            }
            catch (Exception oEx)
            {
                Console.WriteLine("[SYMFDMAP] " + strContext + " ToString — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            return null;
        }
#pragma warning restore 618

        /// <summary>Словарь FD-ID → имя категории (H-4b): Project.FunctionDefinition
        /// Library.FunctionDefinitions — KB-доказан (цитата в шапке); имена FD и Id —
        /// reflection-пробы (урок rev.7). Первый вызов строит дамп [FD]: счётчик,
        /// поверхность членов FunctionDefinition (GetProperty — существование, БЕЗ
        /// вызовов), ToString() первого элемента. Сбой/пусто — словарь пуст, дерево
        /// уйдёт в fallback/«Прочее (FD N)». Строится ОДИН раз за жизнь диалога
        /// (_bFdDumped): список FD — статическое свойство проекта.</summary>
        private void BuildFdDictionary()
        {
            if (_bFdDumped) return;   // дамп/словарь строим один раз за жизнь диалога
            _bFdDumped = true;
            _dctFdNames = new Dictionary<long, string>();
            if (_oProject == null)
            {
                // Fix r14.1 (Important-2 ревью): ранний выход с явным [FD].
                Console.WriteLine("[FD] недоступен: проект null — дерево по префиксам имён");
                return;
            }
            try
            {
                // KB: Project.FunctionDefinitionLibrary : FunctionDefinitionLibrary —
                // цитата в шапке. Тип пишем под var: свойство KB-доказано, полное
                // имя класса в коде не повторяем (меньше риск CS0234 по namespaces).
                var oFdLibrary = _oProject.FunctionDefinitionLibrary;
                var arrFdDefs = oFdLibrary.FunctionDefinitions;
                int nCount = arrFdDefs == null ? 0 : arrFdDefs.Length;
                Console.WriteLine("[FD] FunctionDefinitions: " +
                    nCount.ToString(CultureInfo.InvariantCulture) + " шт.");
                if (nCount == 0) return;

                // Поверхность первого FD: существование свойств (GetProperty — НЕ вызов!).
                object oFirst = arrFdDefs.GetValue(0);
                if (oFirst == null)
                {
                    Console.WriteLine("[FD] первый FD — null");
                    return;
                }
                Type oType = oFirst.GetType();
                string[] arrCandidates = new string[] { "Id", "IdentifyingName", "Name",
                    "Number", "SuperFunctionDefinition" };
                foreach (string strProp in arrCandidates)
                {
                    Console.WriteLine("[FD]   член «" + strProp + "»: " +
                        (oType.GetProperty(strProp) != null ? "есть" : "нет"));
                }
                try
                {
                    Console.WriteLine("[FD]   ToString(fd[0]): «" + oFirst.ToString() + "»");
                }
                catch (Exception oEx)
                {
                    Console.WriteLine("[FD]   ToString(fd[0]) — " + oEx.GetType().Name +
                        ": " + oEx.Message);
                }

                foreach (object oFd in arrFdDefs)
                {
                    if (oFd == null) continue;
                    long? nId = TryGetLongProperty(oFd, "Id");
                    if (!nId.HasValue) nId = TryGetLongProperty(oFd, "Number");
                    string strFdName = TryGetStringProperty(oFd, "Name");
                    if (string.IsNullOrEmpty(strFdName))
                        strFdName = TryGetStringProperty(oFd, "IdentifyingName");
                    if (string.IsNullOrEmpty(strFdName))
                    {
                        try { strFdName = oFd.ToString(); }
                        catch { strFdName = null; }
                    }
                    if (nId.HasValue && !string.IsNullOrEmpty(strFdName) &&
                        !_dctFdNames.ContainsKey(nId.Value))
                    {
                        _dctFdNames[nId.Value] = strFdName;
                    }
                }
                Console.WriteLine("[FD] словарь FD: " +
                    _dctFdNames.Count.ToString(CultureInfo.InvariantCulture) + " записей");
            }
            catch (Exception oEx)
            {
                Console.WriteLine("[FD] недоступен: " + oEx.GetType().Name + ": " +
                    oEx.Message + " — дерево по префиксам имён");
            }
        }

        // CS0618 (ruling контроллера, fix-2): GetValue(args[]-перегрузка) — см. выше.
#pragma warning disable 618
        /// <summary>Reflection-проба long-свойства («Id»/«Number» у FunctionDefinition —
        /// НЕ доказаны KB): GetValue с заглушением CS0618; значение — готовое
        /// int/long (строки НЕ парсим: «Id» — числовое свойство; ToString/TryParse
        /// отдельная проба не нужна). Отказ — null.</summary>
        private static long? TryGetLongProperty(object oTarget, string strProp)
        {
            try
            {
                PropertyInfo oPropInfo = oTarget.GetType().GetProperty(strProp);
                if (oPropInfo == null) return null;
                object oValue = oPropInfo.GetValue(oTarget, null);
                if (oValue is long) return (long)oValue;
                if (oValue is int) return (long)(int)oValue;
                return null;
            }
            catch (Exception oEx)
            {
                Console.WriteLine("[FD] «" + strProp + "» бросил — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return null;
            }
        }
#pragma warning restore 618

        // CS0618 (ruling контроллера, fix-2): GetValue(args[]-перегрузка) — см. выше.
#pragma warning disable 618
        private static string TryGetStringProperty(object oTarget, string strProp)
        {
            try
            {
                PropertyInfo oProp = oTarget.GetType().GetProperty(strProp);
                if (oProp == null) return null;
                object oValue = oProp.GetValue(oTarget, null);
                return oValue == null ? null : oValue.ToString();
            }
            catch (Exception oEx)
            {
                Console.WriteLine("SymbolBrowserDialog: «" + strProp + "» бросил — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return null;
            }
        }
#pragma warning restore 618

        /// <summary>Activator-проба конструкторов MDSymbolLibrary (сигнатуры кторов
        /// KB НЕ доказал; класс — да): для каждого кандидата пути (порядок ниже)
        /// формы (string path), затем (Project, string path); первая вернувшая
        /// экземпляр — успех. MissingMethodException формы запоминается ( перегрузки
        /// нет — повтор с другим путём бессмысленен, лог не шумим), прочие отказы
        /// формы — Console-дамп и следующий кандидат. Ни одна комбинация — null:
        /// цепочка B деградирует в INFO-статус диалога, ручной ввод имени не
        /// запрещаем (первый прогон на стенде уточнит поверхность по Console).</summary>
        private object TryCreateMdLibrary(SymbolLibrary oLib)
        {
            Type oType = typeof(Eplan.EplApi.MasterData.MDSymbolLibrary);
            bool bStringFormMissing = false;
            bool bProjectFormMissing = false;
            foreach (string strPath in EnumeratePathCandidates(oLib))
            {
                if (!bStringFormMissing)
                {
                    try
                    {
                        object oCandidate = Activator.CreateInstance(oType, new object[] { strPath });
                        if (oCandidate != null) return oCandidate;
                    }
                    catch (MissingMethodException oEx)
                    {
                        bStringFormMissing = true;
                        Console.WriteLine("SymbolBrowserDialog: ctor(string) не найден — " + oEx.Message);
                    }
                    catch (Exception oEx)
                    {
                        Console.WriteLine("SymbolBrowserDialog: ctor(string) «" + strPath +
                            "» бросил — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                }
                if (!bProjectFormMissing)
                {
                    try
                    {
                        object oCandidate = Activator.CreateInstance(oType,
                            new object[] { _oProject, strPath });
                        if (oCandidate != null) return oCandidate;
                    }
                    catch (MissingMethodException oEx)
                    {
                        bProjectFormMissing = true;
                        Console.WriteLine("SymbolBrowserDialog: ctor(Project,string) не найден — " + oEx.Message);
                    }
                    catch (Exception oEx)
                    {
                        Console.WriteLine("SymbolBrowserDialog: ctor(Project,string) «" + strPath +
                            "» бросил — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                }
            }
            return null;
        }

        /// <summary>Кандидаты ПУТИ к библиотеке (fix-1: ОТДЕЛЬНО от отображаемого
        /// имени ResolveDisplayPath). Порядок path-first: LocationInfo (полный путь —
        /// основной кандидат для MDSymbolLibrary(path)), затем Name, IdentifyingName
        /// (ctor мог принимать и имя). Значения свойств НЕ доказаны KB — reflection-
        /// проба TryGetStringProperty (graceful), пустые пропуск, дубли не зондируем.</summary>
        private static List<string> EnumeratePathCandidates(SymbolLibrary oLib)
        {
            string[] arrPathProps = new string[] { "LocationInfo", "Name", "IdentifyingName" };
            List<string> lstPaths = new List<string>();
            foreach (string strProp in arrPathProps)
            {
                string strValue = TryGetStringProperty(oLib, strProp);
                if (string.IsNullOrEmpty(strValue)) continue;
                if (lstPaths.IndexOf(strValue) >= 0) continue;
                lstPaths.Add(strValue);
            }
            return lstPaths;
        }

        /// <summary>Число вариантов MDSymbol: KB (цитата в шапке) «public
        /// MDSymbolVariant[] Variants { get; }» — прямое обращение; бросил/null →
        /// -1 (диапазон слотов тогда свободный int ≥ 0 — R11).</summary>
        private static int TryGetVariantCount(Eplan.EplApi.MasterData.MDSymbol oMdSym)
        {
            try
            {
                Eplan.EplApi.MasterData.MDSymbolVariant[] arrVariants = oMdSym.Variants;
                if (arrVariants != null) return arrVariants.Length;
            }
            catch (Exception oEx)
            {
                Console.WriteLine("SymbolBrowserDialog: MDSymbol.Variants — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            return -1;
        }

        /// <summary>Reflection-проба имени (MDSymbol.Name KB не доказан):
        /// Name → IdentifyingName → ToString(). Только чтение.</summary>
        private static string ResolveNameViaReflection(object oTarget)
        {
            string strName = TryGetStringProperty(oTarget, "Name");
            if (!string.IsNullOrEmpty(strName)) return strName;
            strName = TryGetStringProperty(oTarget, "IdentifyingName");
            if (!string.IsNullOrEmpty(strName)) return strName;
            try { return oTarget.ToString(); }
            catch { return null; }
        }

        /// <summary>Отображаемое ИМЯ (он же возвращаемый Library): кандидаты Name →
        /// IdentifyingName → LocationInfo (свойства НЕ доказаны KB — reflection-проба;
        /// фикс-ревью 1: путь для цепочки B здесь НЕ берётся — EnumeratePathCandidates).
        /// Отказ всех — ToString().</summary>
        private static string ResolveDisplayPath(object oTarget)
        {
            string[] arrCandidates = new string[] { "Name", "IdentifyingName", "LocationInfo" };
            foreach (string strProp in arrCandidates)
            {
                string strValue = TryGetStringProperty(oTarget, strProp);
                if (!string.IsNullOrEmpty(strValue)) return strValue;
            }
            try { return oTarget.ToString(); }
            catch { return null; }
        }

        // --- дерево категорий (SymbolCatalog) + поиск + предвыбор ---

        /// <summary>Пересборка дерева из чистого SymbolCatalog: корень —
        /// библиотека (как в TextBox, вручную тоже), уровни — категории
        /// (FD-имена / «Прочее (FD N)» / fallback-префикс / «Прочие»), листья —
        /// символы (Tag = имя; дубли сохранены). Поиск фильтрует ЛИСТЬЯ
        /// (substring, OrdinalIgnoreCase — R11); категории без совпадений
        /// скрываются; пустой поиск — полное дерево. В конце — подсветка
        /// текущего символа (если он в дереве).</summary>
        private void RebuildTree()
        {
            string strFilter = _txtSearch.Text ?? string.Empty;
            List<SymbolCatalogGroup> lstGroups =
                SymbolCatalog.Build(_lstAllSymbols, _lstAllFdIds, _dctFdNames);
            string strRootText = string.IsNullOrEmpty(_txtLibrary.Text)
                ? "Библиотека"
                : _txtLibrary.Text;

            _treeSymbols.BeginUpdate();
            _treeSymbols.Nodes.Clear();
            _dctCatNodeNames.Clear();   // узлы пересозданы — карта чистых имён тоже
            TreeNode oRoot = new TreeNode(strRootText);
            int nShownTotal = 0;
            foreach (SymbolCatalogGroup oGroup in lstGroups)
            {
                List<string> lstShown = new List<string>();
                foreach (string strName in oGroup.SymbolNames)
                {
                    if (strFilter.Length == 0 ||
                        strName.IndexOf(strFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        lstShown.Add(strName);
                    }
                }
                if (lstShown.Count == 0) continue;   // категория без совпадений — скрыта
                TreeNode oCat = new TreeNode(oGroup.Name + " (" +
                    lstShown.Count.ToString(CultureInfo.InvariantCulture) + ")");
                _dctCatNodeNames[oCat] = oGroup.Name;   // чистое имя — карточке (фикс r14.1)
                foreach (string strName in lstShown)
                {
                    TreeNode oLeaf = new TreeNode(strName);
                    oLeaf.Tag = strName;
                    oCat.Nodes.Add(oLeaf);
                }
                oRoot.Nodes.Add(oCat);
                nShownTotal += lstShown.Count;
            }
            if (oRoot.Nodes.Count > 0) _treeSymbols.Nodes.Add(oRoot);
            oRoot.Expand();   // категории видны сразу (символы — по желанию клика)
            _treeSymbols.EndUpdate();

            _lblCount.Text = "показано " + nShownTotal.ToString(CultureInfo.InvariantCulture) +
                " из " + _lstAllSymbols.Count.ToString(CultureInfo.InvariantCulture);
            HighlightCurrentSymbol();
        }

        /// <summary>Подсветка текущего символа (_txtSymbol.Text) в дереве:
        /// точное совпадение листа (БЕЗ trim — урок п.33) — разворот пути +
        /// выделение (вызовет AfterSelect). Нет листа — дерево остаётся без
        /// подсветки (ручной ввод сохраняется).</summary>
        private void HighlightCurrentSymbol()
        {
            string strName = _txtSymbol.Text;
            if (string.IsNullOrEmpty(strName)) return;
            TreeNode oLeaf = FindLeaf(_treeSymbols.Nodes, strName);
            if (oLeaf == null || _treeSymbols.SelectedNode == oLeaf) return;
            TreeNode oWalk = oLeaf;
            while (oWalk != null)
            {
                oWalk.Expand();
                oWalk = oWalk.Parent;
            }
            _treeSymbols.SelectedNode = oLeaf;
        }

        /// <summary>Поиск листа по Tag (точное string-совпадение, Ordinal).
        /// Tag ставится ТОЛЬКО листьям (см. RebuildTree) — категориям/корню
        /// Tag не назначаем.</summary>
        private static TreeNode FindLeaf(TreeNodeCollection oNodes, string strName)
        {
            foreach (TreeNode oNode in oNodes)
            {
                if (oNode.Tag != null &&
                    string.Equals((string)oNode.Tag, strName, StringComparison.Ordinal))
                {
                    return oNode;
                }
                TreeNode oFound = FindLeaf(oNode.Nodes, strName);
                if (oFound != null) return oFound;
            }
            return null;
        }

        /// <summary>Предвыбор текущего имени символа точным совпадением (без trim —
        /// урок п.33): TextBox получает имя (TextChanged → подсветка листа +
        /// карточка/превью через AfterSelect); имени нет в дереве — текст остаётся
        /// ручным вводом, карточка прочерками.</summary>
        private void SelectSymbolPrechoice(string strName)
        {
            if (string.IsNullOrEmpty(strName)) return;
            _txtSymbol.Text = strName;
            UpdateVariantRange(strName);
        }

        // --- превью (DrawingService, каскад проб — план H-4b) ---

        /// <summary>Базовые настройки вида ДО первого CreateDisplayList
        /// (H-4b; фактический вид зафиксирует стендовый прогон): связи и
        /// мета-логика макроса не в тему диалога символа; фон — панель сама.
        /// Каждое свойство — отдельная проба try/catch (чтение/запись свойства
        /// может не сработать — отказ монитора Console, без остановки).</summary>
        private void ApplyPreviewSettings()
        {
            DrawingService oDs = _oDrawingService;
            if (oDs == null) return;
            try { oDs.DrawConnections = false; }
            catch (Exception oEx)
            {
                Console.WriteLine("[DSPROBE] DrawConnections=false — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            try { oDs.MacroPreview = false; }
            catch (Exception oEx)
            {
                Console.WriteLine("[DSPROBE] MacroPreview=false — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            try { oDs.DrawBackGround = false; }
            catch (Exception oEx)
            {
                Console.WriteLine("[DSPROBE] DrawBackGround=false — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        /// <summary>Пересоздание display list (ПЕРВИЧНО — типизированный KB-путь
        /// Symbol+SymbolVariant; затем — по строкам String,String,Int32,Project;
        /// RepresentationType-перегрузка сознательно НЕ пробуется) + Invalidate.
        /// Мутации display list при каждом клике безопасны (KB: «Removes the
        /// representation of previously displayed objects when creating a new
        /// list»), но пересоздание в try/catch — отказ рендера НЕ должен ломать
        /// выбор символа (H-4b). Пустые Library/Name — превью сброс.</summary>
        private void RebuildPreview()
        {
            string strLib = _txtLibrary.Text;
            string strName = _txtSymbol.Text;
            if (string.IsNullOrEmpty(strLib) || string.IsNullOrEmpty(strName))
            {
                // Fix-минор r14.1: при сбросе превью карточка тоже очищается
                // (ранее оставалось имя/категория старого символа).
                _bDisplayListReady = false;
                ClearCard();
                _pnlPreview.Invalidate();
                return;
            }
            if (!_bDrawingServiceReady)
            {
                _bDisplayListReady = false;
                ClearCard();
                SetStatus("Превью недоступно: DrawingService не создан (см. Console [DSPROBE]).");
                return;
            }
            int nVariant = (int)_numPreviewVariant.Value;

            // Каскад: (1) типизированный KB-путь (объект библиотеки из списка);
            // (2) по строкам String,String,Int32,Project — имя библиотеки → "" (HE_Display).
            Exception oPrimaryErr = TryDisplayListTyped(strName, nVariant);
            Exception oStringsErr = null;
            if (oPrimaryErr != null)
                oStringsErr = TryDisplayListByStrings(strName, strLib, nVariant);

            if (oPrimaryErr == null || oStringsErr == null)
            {
                _bDisplayListReady = true;
                TrySetDefaultViewport();   // KB: подгон viewport по bbox — безвредно
                SetStatus("Превью: «" + strName + "», вариант " +
                    nVariant.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                // Деградация (план H-4b): панель пустая, карточка и статус честно.
                _bDisplayListReady = false;
                SetStatus("Превью недоступно: " + oStringsErr.GetType().Name + ": " +
                    oStringsErr.Message);
            }
            _pnlPreview.Invalidate();
        }

        /// <summary>Путь 1 (первичный): DataModel MasterData Symbol(oSymbolLibrary,
        /// strName) → SymbolVariant(oSymbol, nVariant) → CreateDisplayList(SymbolVariant)
        /// — все три шага KB-доказаны (цитаты в шапке). Объект библиотеки — из
        /// _lstLibraryObjects (тот же, что использует цепочка A); не выбран из
        /// списка — путь недоступен (исключение-заглушка, [DSPROBE]).</summary>
        private Exception TryDisplayListTyped(string strName, int nVariant)
        {
            int iIndex = _lstLibraries.SelectedIndex;
            SymbolLibrary oLib = (iIndex >= 0 && iIndex < _lstLibraryObjects.Count)
                ? _lstLibraryObjects[iIndex]
                : null;
            if (oLib == null)
            {
                Exception oStub = new InvalidOperationException(
                    "объект библиотеки не выбран из списка — типизированный путь недоступен");
                Console.WriteLine("[DSPROBE] CreateDisplayList(SymbolVariant) — " + oStub.Message);
                return oStub;
            }
            try
            {
                Symbol oSymbol = new Symbol(oLib, strName);
                SymbolVariant oSymbolVariant = new SymbolVariant(oSymbol, nVariant);
                _oDrawingService.CreateDisplayList(oSymbolVariant);
                Console.WriteLine("[DSPROBE] CreateDisplayList(SymbolVariant) — ок " +
                    "(типизированный путь: Symbol(SymbolLibrary) + SymbolVariant(Symbol,Int32))");
                return null;
            }
            catch (Exception oEx)
            {
                Console.WriteLine("[DSPROBE] CreateDisplayList(SymbolVariant) — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return oEx;
            }
        }

        /// <summary>Путь 2 (запасной): CreateDisplayList(String,String,Int32,Project)
        /// — KB-доказана (пример HE_Display: CreateDisplayList(strObj,"",0,gProject));
        /// первый пробой — с именем библиотеки, второй — с "" вместо библиотеки.
        /// Перегрузка с RepresentationType сознательно НЕ пробуется (план H-4b: не
        /// доказана — одна гипотеза за прогон). Оба отказа — последнее исключение.</summary>
        private Exception TryDisplayListByStrings(string strName, string strLib, int nVariant)
        {
            try
            {
                _oDrawingService.CreateDisplayList(strName, strLib, nVariant, _oProject);
                Console.WriteLine("[DSPROBE] CreateDisplayList(String,String,Int32,Project) — ок" +
                    " (имя библ: «" + strLib + "»)");
                return null;
            }
            catch (Exception oEx)
            {
                Console.WriteLine("[DSPROBE] CreateDisplayList(...," + strLib + ",...) — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            try
            {
                _oDrawingService.CreateDisplayList(strName, "", nVariant, _oProject);
                Console.WriteLine("[DSPROBE] CreateDisplayList(String,String,Int32,Project) — ок" +
                    " (пустое имя библ, паттерн HE_Display)");
                return null;
            }
            catch (Exception oEx)
            {
                Console.WriteLine("[DSPROBE] CreateDisplayList(...,\"\") — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return oEx;
            }
        }

        /// <summary>Проба SetDefaultViewport ПОСЛЕ создания списка (KB: «Adjusts
        /// viewport to the bounding box of the objects from drawing list») —
        /// безвредна; факт — в [DSPROBE]. Отказ — Console-дамп (не поле отказа
        /// рендера: список уже создан).</summary>
        private void TrySetDefaultViewport()
        {
            try
            {
                _oDrawingService.SetDefaultViewport();
                Console.WriteLine("[DSPROBE] SetDefaultViewport — ок");
            }
            catch (Exception oEx)
            {
                Console.WriteLine("[DSPROBE] SetDefaultViewport — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        // --- слоты вариантов + карточка ---

        /// <summary>Диапазон слотов варианта 0..count-1 из числа вариантов
        /// выбранного символа (KB MDSymbol.Variants); count неизвестен (-1) —
        /// свободный int-ввод ≥ 0 (Maximum = int.MaxValue). Текущие значения
        /// прижимаются к новому диапазону. H-4b: слот «Вариант превью» —
        /// тот же диапазон (НЕ связан со слотами H/V по семантике, но
        /// ограничен тем же count — варианты символа одни и те же).</summary>
        private void UpdateVariantRange(string strSymbolName)
        {
            int nCount = -1;
            int iIndex = _lstAllSymbols.IndexOf(strSymbolName);
            if (iIndex >= 0) nCount = _lstAllVariantCounts[iIndex];
            decimal dMax = nCount >= 1 ? (decimal)(nCount - 1) : (decimal)nCount;
            if (dMax < 0) dMax = int.MaxValue;   // count неизвестен/пуст — свободный ввод
            _numVariantH.Maximum = dMax;
            _numVariantV.Maximum = dMax;
            SetVariantValue(_numVariantH, (int)_numVariantH.Value);
            SetVariantValue(_numVariantV, (int)_numVariantV.Value);
            _numPreviewVariant.Maximum = dMax;
            SetVariantValue(_numPreviewVariant, (int)_numPreviewVariant.Value);
        }

        private static void SetVariantValue(NumericUpDown oUpDown, int nValue)
        {
            if (nValue < 0) nValue = 0;
            decimal dValue = (decimal)nValue;
            if (dValue > oUpDown.Maximum) dValue = oUpDown.Maximum;
            oUpDown.Value = dValue;
        }

        /// <summary>Карточка превью: имя (как есть), категория (текст узла-родителя
        /// или «—» при ручном вводе), «вариантов: N» / «неизвестно» (из параллельного
        /// списка, -1 — не извлёкся).</summary>
        private void UpdateCard(string strName, string strCategory)
        {
            _lblPreviewName.Text = "Имя: " +
                (string.IsNullOrEmpty(strName) ? "—" : strName);
            _lblPreviewCategory.Text = "Категория: " +
                (string.IsNullOrEmpty(strCategory) ? "—" : strCategory);
            int nCount = -1;
            int iIndex = _lstAllSymbols.IndexOf(strName);
            if (iIndex >= 0) nCount = _lstAllVariantCounts[iIndex];
            _lblPreviewVariants.Text = nCount >= 0
                ? "вариантов: " + nCount.ToString(CultureInfo.InvariantCulture)
                : "вариантов: неизвестно";
        }

        /// <summary>Сброс карточки в прочерки (фикс r14.1: минорный пункт 5 ревью
        /// — вместе со сбросом display list). Вызывается из RebuildPreview при
        /// пустых Library/Name или не созданном DrawingService.</summary>
        private void ClearCard()
        {
            _lblPreviewName.Text = "Имя: —";
            _lblPreviewCategory.Text = "Категория: —";
            _lblPreviewVariants.Text = "вариантов: —";
        }

        private void SetStatus(string strText)
        {
            _lblStatus.Text = strText;
        }

        // --- Layout (H-4b: 2 колонки ~55%/45%; паттерн MainDialog) ---

        private static Label LabelOf(string strText)
        {
            Label oLabel = new Label();
            oLabel.Text = strText;
            oLabel.AutoSize = true;
            return oLabel;
        }

        /// <summary>Добавить контрол строкой в одноколоночный TableLayoutPanel
        /// (bStretch — растянуть по ширине панели).</summary>
        private static void AddRow(TableLayoutPanel oPanel, Control oControl, bool bStretch)
        {
            oPanel.RowCount++;
            oPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            oControl.Anchor = bStretch
                ? AnchorStyles.Left | AnchorStyles.Right
                : AnchorStyles.Left;
            oPanel.Controls.Add(oControl, 0, oPanel.RowCount - 1);
        }

        /// <summary>Строка с КОНКРЕТНОЙ высотой (превью-панель фиксированной
        /// высоты ~260 по плану H-4b).</summary>
        private static void AddRowFixed(TableLayoutPanel oPanel, Control oControl, int nHeight)
        {
            oPanel.RowCount++;
            oPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, nHeight));
            oControl.Dock = DockStyle.Fill;
            oPanel.Controls.Add(oControl, 0, oPanel.RowCount - 1);
        }

        /// <summary>Layout (rev.14.0): контент — TableLayoutPanel 2 колонки
        /// (~55%/45%), слева — библиотеки/поиск/дерево/имя, справа — превью и
        /// карточка; ниже — слоты H/V, статусная строка (на всю ширину),
        /// кнопки (как rev.13.1).</summary>
        private void BuildLayout(Button btnOk, Button btnCancel)
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12, 8, 12, 8);
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // Контент: 2 колонки 55/45.
            TableLayoutPanel oContent = new TableLayoutPanel();
            oContent.ColumnCount = 2;
            oContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            oContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            oContent.Dock = DockStyle.Fill;
            oContent.AutoSize = true;

            // ЛЕВО: библиотека/список/поиск/дерево/счётчик/имя (как rev.13.1 + дерево).
            TableLayoutPanel oLeft = new TableLayoutPanel();
            oLeft.Dock = DockStyle.Fill;
            oLeft.ColumnCount = 1;
            oLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            AddRow(oLeft, LabelOf("Библиотека (проекта; имя можно ввести вручную):"), false);
            AddRow(oLeft, _txtLibrary, true);
            _lstLibraries.Height = 88;
            _lstLibraries.HorizontalScrollbar = true;
            AddRow(oLeft, _lstLibraries, true);
            AddRow(oLeft, LabelOf("Поиск символа (подстрока):"), false);
            AddRow(oLeft, _txtSearch, true);
            AddRow(oLeft, LabelOf("Символы (дерево: библиотека → категория → символ; " +
                "двойной клик — выбрать):"), false);
            _treeSymbols.Height = 180;
            _treeSymbols.HorizontalScrollbar = true;
            AddRow(oLeft, _treeSymbols, true);
            AddRow(oLeft, _lblCount, false);
            AddRow(oLeft, LabelOf("Имя символа:"), false);
            AddRow(oLeft, _txtSymbol, true);

            // ПРАВО: превью-Panel (фикс. высота 260) + карточка.
            TableLayoutPanel oRight = new TableLayoutPanel();
            oRight.Dock = DockStyle.Fill;
            oRight.ColumnCount = 1;
            oRight.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            AddRow(oRight, LabelOf("Превью (рисуется вариантом ниже):"), false);
            _pnlPreview.BorderStyle = BorderStyle.FixedSingle;
            _pnlPreview.BackColor = Color.White;
            AddRowFixed(oRight, _pnlPreview, 260);
            _lblPreviewName.AutoSize = true;
            _lblPreviewCategory.AutoSize = true;
            _lblPreviewVariants.AutoSize = true;
            AddRow(oRight, _lblPreviewName, false);
            AddRow(oRight, _lblPreviewCategory, false);
            AddRow(oRight, _lblPreviewVariants, false);
            FlowLayoutPanel pnlPreviewVariant = new FlowLayoutPanel();
            pnlPreviewVariant.FlowDirection = FlowDirection.LeftToRight;
            pnlPreviewVariant.AutoSize = true;
            pnlPreviewVariant.Margin = new Padding(3, 0, 3, 0);
            pnlPreviewVariant.Controls.Add(LabelOf("Вариант превью:"));
            _numPreviewVariant.Width = 70;
            pnlPreviewVariant.Controls.Add(_numPreviewVariant);
            AddRow(oRight, pnlPreviewVariant, false);

            oContent.Controls.Add(oLeft, 0, 0);
            oContent.Controls.Add(oRight, 1, 0);

            // Низ (как rev.13.1): контент → слоты H/V → статус (шире) → кнопки.
            root.RowCount++;
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.Controls.Add(oContent, 0, 0);

            FlowLayoutPanel pnlVariants = new FlowLayoutPanel();
            pnlVariants.FlowDirection = FlowDirection.LeftToRight;
            pnlVariants.AutoSize = true;
            pnlVariants.Margin = new Padding(3, 0, 3, 0);
            pnlVariants.Controls.Add(LabelOf("Вариант H:"));
            _numVariantH.Width = 70;
            pnlVariants.Controls.Add(_numVariantH);
            pnlVariants.Controls.Add(LabelOf("  Вариант V:"));
            _numVariantV.Width = 70;
            pnlVariants.Controls.Add(_numVariantV);
            AddRow(root, pnlVariants, false);

            _lblStatus.AutoSize = true;
            _lblStatus.MaximumSize = new Size(780, 0);
            AddRow(root, _lblStatus, false);

            FlowLayoutPanel pnlButtons = new FlowLayoutPanel();
            pnlButtons.FlowDirection = FlowDirection.RightToLeft;
            pnlButtons.Dock = DockStyle.Fill;
            pnlButtons.AutoSize = true;
            pnlButtons.Controls.Add(btnOk);       // первый — у правого края
            pnlButtons.Controls.Add(btnCancel);
            AddRow(root, pnlButtons, true);

            Controls.Add(root);
        }

        /// <summary>Предвыбор точным совпадением (БЕЗ trim — урок п.33); значения
        /// нет в списке — список остаётся без предвыбора.</summary>
        private static void SelectExact(ListBox oList, string strValue)
        {
            if (string.IsNullOrEmpty(strValue)) return;
            int iIndex = oList.Items.IndexOf(strValue);
            if (iIndex >= 0) oList.SelectedIndex = iIndex;
        }
    }
}

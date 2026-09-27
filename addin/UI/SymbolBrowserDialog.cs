using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
// CS0104: HEServices.Label конфликтует с WinForms.Label — все Labels в этом файле контролы (уроки CS0108/CS0246 п.30/72).
using Label = System.Windows.Forms.Label;

namespace MyEplanActions
{
    /// <summary>Браузер символов кабеля v3 (Этап 8, H-4b, rev.14.1 — замечания R2-R9
    /// прогона rev.14.0): выбор библиотеки/символа + два слота варианта (H/V, буквы
    /// A-H) + ДЕРЕВО «Категория → Группа → Символ» (R2, как нативное EPLAN) +
    /// ПРЕВЬЮ-СЕТКА всех вариантов (R5: клетки A-H на чёрном фоне R3, центр R4).
    /// Программный layout без designer (паттерн MainDialog: TableLayoutPanel, хелперы
    /// статическими методами — локальных функций в C#5 нет). Диалог ТОЛЬКО ЧИТАЕТ —
    /// никаких EPLAN-мутаций (display list рендера — внутренний объект DrawingService,
    /// проект не трогает). Выход: свойства Library/SymbolName/VariantH/VariantV
    /// (Variant* — числа, как rev.14.0: пайплайн и настройки не тронуты; в UI — буквы);
    /// «ОК» валиден только при непустых Library/SymbolName (значения НЕ триммингуются —
    /// урок хвостового пробела п.33). «Отмена»/крестик — ничего не возвращается.
    /// СЛЕВА: библиотеки (как rev.13.1) + поиск + TreeView «Библиотека → Категория →
    /// Группа → Символ» (листья Tag=имя; двойной клик = ОК после валидации; поиск
    /// фильтрует ЛИСТЬЯ, пустые группы/категории скрываются; предвыбор — разворот
    /// пути + подсветка). СПРАВА: превью-сетка (до 8 клеток A-H, фон чёрный — R3;
    /// у каждой клетки СВОЙ DrawingService: Reset → CreateDisplayList(вариант) →
    /// SetDefaultViewport — гипотеза центра R4: display list накапливался между
    /// CreateDisplayList и уезжал в левый верхний угол) + карточка
    /// (имя/категория/ОПИСАНИЕ СИМВОЛА SYMB_DESC #16011 — R7; счётчик «вариантов»
    /// удалён R7) + статусная строка внизу на всю ширину (подпись «Превью: …»
    /// удалена R10).
    /// Категории/группы дерева — чистый SymbolCatalog (addin/UI/SymbolCatalog.cs):
    /// ПЕРВИЧНО по FD (SYMB_MAINFUNCTION #16018 из MDSymbol, цепочка B; словарь
    /// Project.FunctionDefinitionLibrary.FunctionDefinitions → CategoryName/GroupName,
    /// KB-доказано); символ без FD при рабочем словаре — «Без категории» (R2, НЕ
    /// префикс); в цепочке A FD недоступен — fallback-бакеты по префиксу имени +
    /// «Прочие». Пробы [FD]/[SYMFDMAP]/[DSPROBE] пишутся ДУБЛИРОВАННО Console +
    /// DiagnosticLogger (урок прогона rev.14.0: Console-канал не попадал в главный
    /// лог — провал FD-цепочки был невидим).
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
    /// Console-дамп + статусная строка; поле имени ReadOnly — выбор только из дерева.</summary>
    public class SymbolBrowserDialog : Form
    {
        private readonly Project _oProject;
        private readonly TextBox _txtLibrary = new TextBox();
        private readonly ListBox _lstLibraries = new ListBox();
        private readonly TextBox _txtSearch = new TextBox();
        private readonly TreeView _treeSymbols = new TreeView();
        private readonly TextBox _txtSymbol = new TextBox();

        // rev.14.1 (R6): слоты H/V — ComboBox букв A-H (всегда 8 пунктов); наружу —
        // числа (SelectedIndex = индекс варианта), пайплайн/настройки не тронуты.
        private readonly ComboBox _cbVariantH = new ComboBox();
        private readonly ComboBox _cbVariantV = new ComboBox();

        private readonly Label _lblCount = new Label();
        private readonly Label _lblStatus = new Label();

        // H-4b: карточка превью (справа) — имя/категория/описание символа (R7:
        // SYMB_DESC #16011; счётчик «вариантов» удалён по R7).
        private readonly Label _lblPreviewName = new Label();
        private readonly Label _lblPreviewCategory = new Label();
        private readonly Label _lblPreviewDescription = new Label();

        // Объекты библиотек под строками списка (параллельны _lstLibraries.Items).
        private readonly List<SymbolLibrary> _lstLibraryObjects = new List<SymbolLibrary>();
        // Все символы выбранной библиотеки (поиск фильтрует дерево отсюда);
        // параллельные списки — число вариантов (-1 — неизвестно), FD-ID по символу
        // (null — FD не извлёкся или цепочка A) и ОПИСАНИЕ символа SYMB_DESC #16011
        // (null — не извлёкся/цепочка A; rev.14.1 R7).
        private List<string> _lstAllSymbols = new List<string>();
        private List<int> _lstAllVariantCounts = new List<int>();
        private List<long?> _lstAllFdIds = new List<long?>();
        private List<string> _lstAllDescriptions = new List<string>();

        // H-4b v3 (rev.14.1): словарь FD-ID → FdInfo{Category,Group} (проект,
        // строится один раз) и [SYMFDMAP]-счётчик сопоставления текущего перечисления.
        private Dictionary<long, FdInfo> _dctFd;
        private int _nFdMapped;

        // Fix-минор r14.1: чистые имена узлов дерева (категория/группа) по ссылкам
        // (текст узла содержит счётчик «(N)» — карточке он не нужен; Tag листьев
        // занят именем символа, категориям/группам Tag сознательно не назначаем —
        // см. FindLeaf).
        private readonly Dictionary<TreeNode, string> _dctCatNodeNames =
            new Dictionary<TreeNode, string>();
        private readonly Dictionary<TreeNode, string> _dctGroupNodeNames =
            new Dictionary<TreeNode, string>();

        // H-4b v3 (rev.14.1, R3-R5): превью-сетка — до 8 клеток (варианты A..H),
        // у каждой клетки СВОЙ DrawingService (один сервис = один display list);
        // список готовых клеток параллелен; Dispose при закрытии (OnFormClosing).
        private readonly TableLayoutPanel _tlpPreviewGrid = new TableLayoutPanel();
        private readonly List<Panel> _lstPreviewCells = new List<Panel>();
        private readonly List<DrawingService> _lstPreviewServices = new List<DrawingService>();
        private readonly List<bool> _lstCellReady = new List<bool>();

        // rev.14.1: пробы [FD]/[SYMFDMAP]/[DSPROBE] — Console + DiagnosticLogger
        // (урок прогона rev.14.0: Console-канал не попадал в главный лог).
        private readonly DiagnosticLogger _oLogger;
        private bool _bFdDumped;

        /// <summary>Вход: открытый проект (null — списки пусты), текущий выбор:
        /// строки как есть (предвыбор точным совпадением БЕЗ trim — урок п.33),
        /// индексы вариантов прижимаются к 0..7; oLogger — канал проб (nullable).</summary>
        public SymbolBrowserDialog(Project oProject, string strLibrary,
            string strName, int nVariantH, int nVariantV, DiagnosticLogger oLogger)
        {
            _oProject = oProject;
            _oLogger = oLogger;

            Text = "Выбор символа кабеля";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(840, 620);

            // rev.14.1 (R6): слоты — ComboBox букв A-H (8 пунктов всегда); предвыбор
            // из настроек: число → SelectedIndex (вне 0..7 — прижать к 0 + INFO).
            FillVariantCombo(_cbVariantH);
            FillVariantCombo(_cbVariantV);
            SetVariantIndex(_cbVariantH, nVariantH);
            SetVariantIndex(_cbVariantV, nVariantV);

            // rev.14.1 (R8): имя символа пользователь не правит — только выбор
            // из дерева; программная предвыборка (ReadOnly не блокирует set).
            _txtSymbol.ReadOnly = true;

            // Карточка превью — прочерк до первого выбора.
            _lblPreviewName.Text = "Имя: —";
            _lblPreviewCategory.Text = "Категория: —";
            _lblPreviewDescription.Text = "Описание: —";

            _txtLibrary.Text = strLibrary ?? string.Empty;
            _txtSymbol.Text = strName ?? string.Empty;

            // (1) Библиотеки: Project.SymbolLibraries (KB-цитата в шапке).
            LoadLibraries(strLibrary);

            // (2) Реакции (диалог только читает; программы мутаций нет).
            _lstLibraries.SelectedIndexChanged += LstLibrariesOnSelectedIndexChanged;
            _treeSymbols.AfterSelect += TreeSymbolsOnAfterSelect;
            _treeSymbols.NodeMouseDoubleClick += TreeSymbolsOnNodeMouseDoubleClick;
            _txtSearch.TextChanged += delegate { RebuildTree(); };
            // Программная установка имени (ReadOnly): подсветка листа + карточка/превью.
            _txtSymbol.TextChanged += TxtSymbolOnTextChanged;

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

        /// <summary>Проба в главный лог (дублированный канал: Console + DiagnosticLogger,
        /// урок прогона rev.14.0 — только-Console был невидим в terminal_strip_addin.log).</summary>
        private void ProbeInfo(string strText)
        {
            Console.WriteLine(strText);
            if (_oLogger != null) _oLogger.Log(strText);
        }

        /// <summary>Проба-предупреждение (как ProbeInfo, уровень WARN в логе).</summary>
        private void ProbeWarn(string strText)
        {
            Console.WriteLine(strText);
            if (_oLogger != null) _oLogger.Warn(strText);
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

        /// <summary>Индекс варианта слота H (0-based; UI — буква A-H, наружу число).</summary>
        public int VariantH
        {
            get { return _cbVariantH.SelectedIndex >= 0 ? _cbVariantH.SelectedIndex : 0; }
        }

        /// <summary>Индекс варианта слота V (0-based; UI — буква A-H, наружу число).</summary>
        public int VariantV
        {
            get { return _cbVariantV.SelectedIndex >= 0 ? _cbVariantV.SelectedIndex : 0; }
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

        /// <summary>Выбор ЛИСТА дерева (символа) — имя уходит в TextBox, карточка/
        /// превью обновляются. Корень/категория/группа — ничего (Tag только у листьев).</summary>
        private void TreeSymbolsOnAfterSelect(object oSender, TreeViewEventArgs oArgs)
        {
            TreeNode oNode = oArgs == null ? null : oArgs.Node;
            if (oNode == null || oNode.Tag == null) return;
            string strShown = (string)oNode.Tag;
            _txtSymbol.Text = strShown;   // TextChanged ничего не делает — лист уже выделен
            // Карточке нужна КАТЕГОРИЯ (rev.14.1: родитель листа — группа, категория —
            // родитель группы; null-safe — leaf может быть в корне при деградации).
            TreeNode oGroup = oNode.Parent;
            TreeNode oCat = oGroup == null ? null : oGroup.Parent;
            UpdateCard(strShown, CategoryOfNode(oCat));
            RefreshPreviewGrid();
        }

        /// <summary>Чистое имя категории по узлу (rev.14.1: два уровня карт —
        /// категория/группа; текст узла содержит счётчик «(N)», карточке нужен
        /// только текст). Узел не из дерева текущей сборки — fallback на текст.</summary>
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

        /// <summary>Программная установка имени (rev.14.1 R8: поле ReadOnly —
        /// пользовательский ввод отключён; ветка «имя НЕ в дереве» осталась для
        /// предвыборки сохранённого имени вне текущей библиотеки): очистка — сброс
        /// превью пречерками; точное совпадение — подсветка + карточка/превью;
        /// имени нет в дереве — карточка по имени и превью по каскаду.</summary>
        private void TxtSymbolOnTextChanged(object oSender, EventArgs oArgs)
        {
            string strName = _txtSymbol.Text;
            if (string.IsNullOrEmpty(strName))
            {
                // Очистка имени — старые display lists гасим (картинка старого
                // символа при пустой строке недопустима); карточка — прочерки.
                RefreshPreviewGrid();   // пустое имя → сброс клеток + ClearCard
                return;
            }
            TreeNode oLeaf = FindLeaf(_treeSymbols.Nodes, strName);   // точное, без trim (п.33)
            if (oLeaf != null)
            {
                if (_treeSymbols.SelectedNode != oLeaf)
                {
                    // Разворот пути до листа + подсветка (AfterSelect обновит
                    // карточку и превью — повторный вызов не нужен).
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
                    RefreshPreviewGrid();   // лист уже был выделен — превью по текущему полю
                }
            }
            else
            {
                // Имя НЕ в дереве (предвыборка вне текущей библиотеки): ранее ветка
                // молчала — старая картинка оставалась при новом имени; карточка
                // и превью — всегда по имени из поля.
                UpdateCard(strName, string.Empty);
                RefreshPreviewGrid();
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
            // Fix (rev.14.1 ревью, Important): поле имени ReadOnly — выбор только
            // из дерева; имя НЕ из текущего перечисления (сменилась библиотека /
            // перечисление отказало) — OK блокируем, иначе вернулась бы пара
            // библ/имя, которой нет.
            if (_lstAllSymbols.Count == 0)
            {
                MessageBox.Show(this,
                    "Перечисление символов недоступно для этой библиотеки — " +
                    "выбор невозможен (подробности в логе [DSPROBE]).",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_lstAllSymbols.IndexOf(SymbolName) < 0)
            {
                MessageBox.Show(this,
                    "Выбранный символ отсутствует в списке текущей библиотеки — " +
                    "выберите символ из дерева.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }

        /// <summary>Крестик/Alt+F4 (CloseReason.UserClosing) — это «отмена»: гасим
        /// залипший DialogResult.OK (паттерн MainDialog). Валидации здесь нет.
        /// H-4b v3: здесь же — Dispose ВСЕХ DrawingService превью-сетки (владение
        /// диалогово: один сервис на клетку варианта).</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel && e.CloseReason == CloseReason.UserClosing &&
                DialogResult == DialogResult.OK)
            {
                DialogResult = DialogResult.Cancel;
            }
            DisposePreviewGrid();
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
        /// отказ листинга — выбор символа блокируется OK-валидацией. Никаких мутаций.</summary>
        private void RefreshSymbols()
        {
            _lstAllSymbols = new List<string>();
            _lstAllVariantCounts = new List<int>();
            _lstAllFdIds = new List<long?>();
            _lstAllDescriptions = new List<string>();
            _nFdMapped = 0;
            DisposePreviewGrid();
            BuildFdDictionary();   // [FD] — один раз за жизнь диалога; до [SYMFDMAP]
            _treeSymbols.Nodes.Clear();
            _lblCount.Text = string.Empty;
            // Fix (rev.14.1 ревью, Important): смена библиотеки — старое имя нового
            // списка не соответствует; поле ReadOnly (выбор из дерева), залиставшее
            // имя гасим (иначе OK вернул бы пару библ/имя, которой нет). Пустое имя
            // через TxtSymbolOnTextChanged гасит карточку и превью.
            if (_txtSymbol.Text.Length > 0)
                _txtSymbol.Text = string.Empty;

            int iIndex = _lstLibraries.SelectedIndex;
            if (iIndex < 0 || iIndex >= _lstLibraryObjects.Count)
            {
                SetStatus("Библиотека не выбрана из списка — список символов пуст " +
                    "(выбор символа недоступен).");
                return;
            }
            SymbolLibrary oLib = _lstLibraryObjects[iIndex];

            // Цепочка A: DataModel-перечислитель (проба).
            if (TryEnumerateViaDataModel(oLib))
            {
                // Fix r14.1 (Important-2 ревью): ранний выход с явным [SYMFDMAP] —
                // отсутствие источника FD должно быть различимо в диагностике.
                ProbeInfo("[SYMFDMAP] FD недоступен: перечисление через " +
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
                ProbeInfo("[SYMFDMAP] сопоставлено " +
                    _nFdMapped.ToString(CultureInfo.InvariantCulture) + " из " +
                    _lstAllSymbols.Count.ToString(CultureInfo.InvariantCulture) +
                    " символов, словарь FD: " +
                    _dctFd.Count.ToString(CultureInfo.InvariantCulture) + " записей");
                SetStatus("Символы перечислены через MDSymbolLibrary.Symbols (KB), " +
                    _lstAllSymbols.Count.ToString(CultureInfo.InvariantCulture) + " шт.; " +
                    "FD сопоставлено " + _nFdMapped.ToString(CultureInfo.InvariantCulture) +
                    " из " + _lstAllSymbols.Count.ToString(CultureInfo.InvariantCulture) + ".");
                RebuildTree();
                return;
            }

            SetStatus("Перечисление символов недоступно (обе цепочки пробили в отказ) — " +
                "выбор символа невозможен (подробности в [DSPROBE] лога).");
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
                    ProbeInfo("SymbolBrowserDialog: проба «" + strProp + "» — " +
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
                    string strDesc = TryGetSymbDesc(oMdSym, iOrdinal, strName);
                    AddSymbolEntry(strName, TryGetVariantCount(oMdSym), nFdId, strDesc);
                    // [SYMFDMAP]-итог: сопоставлено = FD извлечён И в словаре FD.
                    if (nFdId.HasValue && _dctFd != null &&
                        _dctFd.ContainsKey(nFdId.Value))
                        _nFdMapped++;
                    iOrdinal++;
                }
                return _lstAllSymbols.Count > 0;
            }
            catch (Exception oEx)
            {
                ProbeInfo("SymbolBrowserDialog: MDSymbolLibrary.Symbols — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return false;
            }
        }

        /// <summary>Параллельное добавление символа (имя, число вариантов, FD-ID,
        /// описание SYMB_DESC — rev.14.1 R7).</summary>
        private void AddSymbolEntry(string strName, int nVariantCount, long? nFdId,
            string strDescription)
        {
            _lstAllSymbols.Add(strName);
            _lstAllVariantCounts.Add(nVariantCount);
            _lstAllFdIds.Add(nFdId);
            _lstAllDescriptions.Add(strDescription);
        }

        /// <summary>Цепочка A: описание недоступно — null (карточка «—»).</summary>
        private void AddSymbolEntry(string strName, int nVariantCount, long? nFdId)
        {
            AddSymbolEntry(strName, nVariantCount, nFdId, null);
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
        private long? TryGetFdId(Eplan.EplApi.MasterData.MDSymbol oMdSym,
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
                    Probe("[SYMFDMAP] raw ToString «" + strName + "» — " +
                        oEx.GetType().Name + ": " + oEx.Message, iOrdinal);
                }
                string strVia;
                nId = ExtractLongViaReflection(oVal, "SYMFD:" + strName, out strVia);
                if (iOrdinal < 10)
                {
                    Probe("[SYMFDMAP] «" + strName + "» raw=«" +
                        (strRaw ?? "<null>") + "» id=" +
                        (nId.HasValue ? nId.Value.ToString(CultureInfo.InvariantCulture) +
                            " via " + strVia : "НЕТ"), iOrdinal);
                }
            }
            catch (Exception oEx)
            {
                if (iOrdinal < 10)
                    Probe("[SYMFDMAP] «" + strName + "» #16018 — " +
                        oEx.GetType().Name + ": " + oEx.Message, iOrdinal);
            }
            return nId;
        }

        /// <summary>Описание символа (rev.14.1 R7): KB MDSymbolPropertyList.SYMB_DESC —
        /// «Symbol description # 16011» (source: ...MDSymbolPropertyList~SYMB_DESC.html);
        ///PropertyValue → ToString(). Метка [SYMFDMAP-DESC] первых 10 символов.
        /// Пусто/отказ — null (карточка «—»).</summary>
        private string TryGetSymbDesc(Eplan.EplApi.MasterData.MDSymbol oMdSym,
            int iOrdinal, string strName)
        {
            if (oMdSym == null) return null;
            try
            {
                Eplan.EplApi.MasterData.MDSymbolPropertyList oProps =
                    new Eplan.EplApi.MasterData.MDSymbolPropertyList(oMdSym);
                Eplan.EplApi.MasterData.MDPropertyValue oVal = oProps.SYMB_DESC;
                string strText = oVal == null ? null : oVal.ToString();
                if (iOrdinal < 10)
                    ProbeInfo("[SYMFDMAP-DESC] «" + strName + "» desc=«" +
                        (string.IsNullOrEmpty(strText) ? "<пусто>" : strText) + "»");
                return string.IsNullOrEmpty(strText) ? null : strText;
            }
            catch (Exception oEx)
            {
                if (iOrdinal < 10)
                    ProbeWarn("[SYMFDMAP-DESC] «" + strName + "» #16011 — " +
                        oEx.GetType().Name + ": " + oEx.Message);
                return null;
            }
        }

        /// <summary>Проба [SYMFDMAP] — дублированный канал (ProbeInfo) c капом шума:
        /// после первых 10 символов строки НЕ эмитятся вовсе (шум ~800 символов;
        /// итог — сводка [SYMFDMAP] в RefreshSymbols; фикс ревью rev.14.1:
        /// Console-only после капа — bypass логгера).</summary>
        private void Probe(string strText, int iOrdinal)
        {
            if (iOrdinal < 10) ProbeInfo(strText);
        }

        // CS0618 (ruling контроллера, fix-2): GetValue(args[]-перегрузка) — см. выше.
#pragma warning disable 618
        /// <summary>Извлечение числа из объекта-провайдера (MDPropertyValue и прочих):
        /// reflection-проба кандидатов — НИЧЕГО не угадываем (урок rev.7: CS1061 на
        /// первой стендовой сборке): (1) уже готовые long/int; (2) методы
        /// ToInt64()/ToInt(); (3) свойство Value; (4) fallback — ToString()+long.
        /// TryParse. strVia — каким путём получилось (печатается в [SYMFDMAP]).
        /// Отказ — null; ошибки каждой пробы — Console-дамп. Инстансный (Probe-канал).
        /// </summary>
        private long? ExtractLongViaReflection(object oValue, string strContext,
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
                    ProbeWarn("[SYMFDMAP] " + strContext + " " + strMethod +
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
                ProbeWarn("[SYMFDMAP] " + strContext + " Value — " +
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
                ProbeWarn("[SYMFDMAP] " + strContext + " ToString — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            return null;
        }
#pragma warning restore 618

        /// <summary>Словарь FD-ID → FdInfo{Category,Group} (H-4b v3, rev.14.1 R2):
        /// Project.FunctionDefinitionLibrary.FunctionDefinitions — KB-доказан; члены
        /// FunctionDefinition.CategoryName/GroupName/MainGroup/CategoryRegion/Name/Id —
        /// KB-доказаны (MultiLangString → ToString(), паттерн rev.5.5). Типизированный
        /// доступ в try/catch; дамп [FD] первых 10 FD — сверка с нативным деревом
        /// EPLAN. Сбой/пусто — словарь пуст, дерево уйдёт в fallback-префиксы.
        /// Строится ОДИН раз за жизнь диалога (_bFdDumped): список FD — статическое
        /// свойство проекта.</summary>
        private void BuildFdDictionary()
        {
            if (_bFdDumped) return;   // дамп/словарь строим один раз за жизнь диалога
            _bFdDumped = true;
            _dctFd = new Dictionary<long, FdInfo>();
            if (_oProject == null)
            {
                // Fix r14.1 (Important-2 ревью): ранний выход с явным [FD].
                ProbeInfo("[FD] недоступен: проект null — дерево по префиксам имён");
                return;
            }
            try
            {
                // KB: Project.FunctionDefinitionLibrary : FunctionDefinitionLibrary
                // (в .MasterData), FunctionDefinitions : FunctionDefinition[] —
                // САМ FunctionDefinition в Eplan.EplApi.DataModel (страница класса
                // «Eplan.EplApi.DataModel Namespace / FunctionDefinition Class»;
                // урок CS0246 п.30/63 — пространство имён сверять с компилирующимся
                // кодом, не по «логичности»).
                Eplan.EplApi.DataModel.FunctionDefinition[] arrFdDefs =
                    _oProject.FunctionDefinitionLibrary.FunctionDefinitions;
                int nCount = arrFdDefs == null ? 0 : arrFdDefs.Length;
                ProbeInfo("[FD] FunctionDefinitions: " +
                    nCount.ToString(CultureInfo.InvariantCulture) + " шт.");
                if (nCount == 0) return;

                int iOrdinal = 0;
                foreach (Eplan.EplApi.DataModel.FunctionDefinition oFd in arrFdDefs)
                {
                    if (oFd == null) continue;
                    long nId = -1;
                    string strCat = null;
                    string strGroup = null;
                    string strMain = null;
                    string strRegion = null;
                    string strName = null;
                    try { nId = oFd.Id; }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] Id — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try { strCat = oFd.CategoryName == null ? null : oFd.CategoryName.ToString(); }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] CategoryName — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try { strGroup = oFd.GroupName == null ? null : oFd.GroupName.ToString(); }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] GroupName — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try { strMain = oFd.MainGroup == null ? null : oFd.MainGroup.ToString(); }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] MainGroup — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try { strRegion = oFd.CategoryRegion == null ? null : oFd.CategoryRegion.ToString(); }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] CategoryRegion — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try { strName = oFd.Name == null ? null : oFd.Name.ToString(); }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] Name — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    if (iOrdinal < 10)
                    {
                        ProbeInfo("[FD] id=" + nId.ToString(CultureInfo.InvariantCulture) +
                            " cat='" + (strCat ?? "<null>") + "' main='" + (strMain ?? "<null>") +
                            "' region='" + (strRegion ?? "<null>") + "' group='" +
                            (strGroup ?? "<null>") + "' name='" + (strName ?? "<null>") + "'");
                    }
                    if (nId >= 0 && (!_dctFd.ContainsKey(nId)))
                    {
                        FdInfo oInfo = new FdInfo();
                        oInfo.Category = strCat;
                        oInfo.Group = strGroup;
                        _dctFd[nId] = oInfo;
                    }
                    iOrdinal++;
                }
                ProbeInfo("[FD] словарь FD: " +
                    _dctFd.Count.ToString(CultureInfo.InvariantCulture) + " записей");
            }
            catch (Exception oEx)
            {
                ProbeWarn("[FD] недоступен: " + oEx.GetType().Name + ": " +
                    oEx.Message + " — дерево по префиксам имён");
            }
        }

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
        /// цепочка B деградирует в INFO-статус диалога; OK-валидация блокирует выбор
        /// при пустом перечислении.</summary>
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
                        ProbeInfo("SymbolBrowserDialog: ctor(string) не найден — " + oEx.Message);
                    }
                    catch (Exception oEx)
                    {
                        ProbeInfo("SymbolBrowserDialog: ctor(string) «" + strPath +
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
                        ProbeInfo("SymbolBrowserDialog: ctor(Project,string) не найден — " + oEx.Message);
                    }
                    catch (Exception oEx)
                    {
                        ProbeInfo("SymbolBrowserDialog: ctor(Project,string) «" + strPath +
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
        /// -1 (превью-сетка тогда — одна клетка «A»). Инстансный (Probe-канал,
        /// фикс ревью rev.14.1: static + ProbeWarn = CS0120).</summary>
        private int TryGetVariantCount(Eplan.EplApi.MasterData.MDSymbol oMdSym)
        {
            try
            {
                Eplan.EplApi.MasterData.MDSymbolVariant[] arrVariants = oMdSym.Variants;
                if (arrVariants != null) return arrVariants.Length;
            }
            catch (Exception oEx)
            {
                ProbeWarn("SymbolBrowserDialog: MDSymbol.Variants — " +
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

        /// <summary>Пересборка дерева из чистого SymbolCatalog (rev.14.1 R2: 3 уровня
        /// как нативное EPLAN): корень — библиотека (как в TextBox), уровни —
        /// категории (FD CategoryName / «Без категории» / fallback-префикс / «Прочие»)
        /// и группы (FD GroupName / «—»), листья — символы (Tag = имя; дубли
        /// сохранены). Поиск фильтрует ЛИСТЬЯ (substring, OrdinalIgnoreCase — R11);
        /// группы/категории без совпадений скрываются; пустой поиск — полное дерево.
        /// В конце — подсветка текущего символа (если он в дереве).</summary>
        private void RebuildTree()
        {
            string strFilter = _txtSearch.Text ?? string.Empty;
            List<SymbolCatalogCategory> lstCategories =
                SymbolCatalog.Build(_lstAllSymbols, _lstAllFdIds, _dctFd);
            string strRootText = string.IsNullOrEmpty(_txtLibrary.Text)
                ? "Библиотека"
                : _txtLibrary.Text;

            _treeSymbols.BeginUpdate();
            _treeSymbols.Nodes.Clear();
            _dctCatNodeNames.Clear();     // узлы пересозданы — карты чистых имён тоже
            _dctGroupNodeNames.Clear();
            TreeNode oRoot = new TreeNode(strRootText);
            int nShownTotal = 0;
            foreach (SymbolCatalogCategory oCategory in lstCategories)
            {
                int nShownInCategory = 0;
                TreeNode oCat = new TreeNode(oCategory.Name);
                foreach (SymbolCatalogGroup oGroup in oCategory.Groups)
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
                    if (lstShown.Count == 0) continue;   // группа без совпадений — скрыта
                    TreeNode oGrp = new TreeNode(oGroup.Name + " (" +
                        lstShown.Count.ToString(CultureInfo.InvariantCulture) + ")");
                    _dctGroupNodeNames[oGrp] = oGroup.Name;   // чистое имя группы
                    foreach (string strName in lstShown)
                    {
                        TreeNode oLeaf = new TreeNode(strName);
                        oLeaf.Tag = strName;
                        oGrp.Nodes.Add(oLeaf);
                    }
                    oCat.Nodes.Add(oGrp);
                    nShownInCategory += lstShown.Count;
                }
                if (nShownInCategory == 0) continue;   // категория без совпадений — скрыта
                oCat.Text = oCategory.Name + " (" +
                    nShownInCategory.ToString(CultureInfo.InvariantCulture) + ")";
                _dctCatNodeNames[oCat] = oCategory.Name;   // чистое имя категории — карточке
                oRoot.Nodes.Add(oCat);
                nShownTotal += nShownInCategory;
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
        /// подсветки (сохранённое имя вне дерева — без подсветки).</summary>
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
        /// как есть, карточка прочерками (OK-валидация проверит принадлежность).</summary>
        private void SelectSymbolPrechoice(string strName)
        {
            if (string.IsNullOrEmpty(strName)) return;
            _txtSymbol.Text = strName;
        }

        // --- превью-сетка (DrawingService, rev.14.1 R3/R4/R5) ---

        /// <summary>Настройки вида ОДНОГО сервиса превью (связи и мета-логика
        /// макроса не в тему диалога символа; фон — клетка сама, чёрная по R3).
        /// Каждое свойство — отдельная проба try/catch (отказ — ProbeWarn,
        /// без остановки).</summary>
        private void ApplyPreviewSettings(DrawingService oDs)
        {
            if (oDs == null) return;
            try { oDs.DrawConnections = false; }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] DrawConnections=false — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            try { oDs.MacroPreview = false; }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] MacroPreview=false — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            try { oDs.DrawBackGround = false; }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] DrawBackGround=false — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        /// <summary>Пересборка превью-СЕТКИ (rev.14.1 R5): по числу вариантов
        /// символа (параллельный список; -1/неизвестно — одна клетка «A»; &gt;8 —
        /// кап 8) создаются клетки с буквами A..H. Пустые Library/Name — сброс.
        /// Отказ рендера клетки не ломает остальные и выбор символа.</summary>
        private void RefreshPreviewGrid()
        {
            string strLib = _txtLibrary.Text;
            string strName = _txtSymbol.Text;
            if (string.IsNullOrEmpty(strLib) || string.IsNullOrEmpty(strName))
            {
                // Fix-минор r14.1: при сбросе превью карточка тоже очищается.
                DisposePreviewGrid();
                ClearCard();
                return;
            }
            int nCount = -1;
            int iIndex = _lstAllSymbols.IndexOf(strName);
            if (iIndex >= 0) nCount = _lstAllVariantCounts[iIndex];
            int nCells = nCount < 1 ? 1 : Math.Min(nCount, 8);
            if (nCount > 8)
                ProbeWarn("[DSPROBE] вариантов " +
                    nCount.ToString(CultureInfo.InvariantCulture) +
                    " > 8 — показаны первые 8");
            BuildPreviewGrid(strLib, strName, nCells);
        }

        /// <summary>Построение клеток: панель (чёрная, R3) + подпись буквы под
        /// вариантом (R5), СВОЙ DrawingService на клетку (один сервис = один
        /// display list); перед CreateDisplayList — Reset() (гипотеза центра R4:
        /// display list накапливался между вызовами и уезжал в угол) + SetDefault
        /// Viewport после (KB: «Adjusts viewport to the bounding box»).</summary>
        private void BuildPreviewGrid(string strLib, string strName, int nCells)
        {
            DisposePreviewGrid();
            _tlpPreviewGrid.SuspendLayout();
            int nFailed = 0;
            for (int i = 0; i < nCells; i++)
            {
                string strLetter = ((char)('A' + i)).ToString(CultureInfo.InvariantCulture);
                Panel oCell = new Panel();
                oCell.BackColor = Color.Black;          // R3: чёрный фон
                oCell.BorderStyle = BorderStyle.FixedSingle;
                oCell.Dock = DockStyle.Fill;
                oCell.Margin = new Padding(1);
                Label oLetter = new Label();
                oLetter.Text = strLetter;
                oLetter.BackColor = Color.Black;
                oLetter.ForeColor = Color.White;
                oLetter.TextAlign = ContentAlignment.MiddleCenter;
                oLetter.Dock = DockStyle.Bottom;
                oLetter.Height = 18;
                oCell.Controls.Add(oLetter);

                DrawingService oDs = null;
                bool bReady = false;
                try
                {
                    oDs = new DrawingService();
                    ApplyPreviewSettings(oDs);
                    bReady = BuildCellList(oDs, strLib, strName, i, strLetter);
                }
                catch (Exception oEx)
                {
                    ProbeWarn("[DSPROBE] DrawingService клетки " + strLetter + " — " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
                if (!bReady)
                {
                    nFailed++;
                    if (oDs != null)
                    {
                        try { oDs.Dispose(); }
                        catch (Exception oEx)
                        {
                            ProbeWarn("[DSPROBE] Dispose клетки " + strLetter + " — " +
                                oEx.GetType().Name + ": " + oEx.Message);
                        }
                        oDs = null;
                    }
                }
                _lstPreviewCells.Add(oCell);
                _lstPreviewServices.Add(oDs);
                _lstCellReady.Add(bReady);

                // Paint клетки — захват индекса (анонимный метод, C#5).
                int iCell = i;
                oCell.Paint += delegate(object oSender, PaintEventArgs oArgs)
                {
                    if (iCell >= _lstPreviewServices.Count) return;
                    DrawingService oCellDs = _lstPreviewServices[iCell];
                    if (oCellDs == null || !_lstCellReady[iCell]) return;
                    try
                    {
                        oCellDs.DrawDisplayList(oArgs, oCell.ClientRectangle);
                    }
                    catch (Exception oEx)
                    {
                        // Отказ рендера — ProbeWarn, выбор символа не ломаем.
                        ProbeWarn("[DSPROBE] DrawDisplayList клетка " +
                            ((char)('A' + iCell)).ToString(CultureInfo.InvariantCulture) +
                            " — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                };
                _tlpPreviewGrid.Controls.Add(oCell, i % 4, i / 4);
            }
            _tlpPreviewGrid.ResumeLayout();
            if (nFailed == nCells)
                SetStatus("Превью недоступно: все клетки не отрисованы (см. [DSPROBE] в логе).");
        }

        /// <summary>Display list ОДНОЙ клетки: Reset() → CreateDisplayList(вариант
        /// i каскадом) → SetDefaultViewport(). true — клетка готова к отрисовке.</summary>
        private bool BuildCellList(DrawingService oDs, string strLib, string strName,
            int iVariant, string strLetter)
        {
            try
            {
                oDs.Reset();   // KB: «Resets all settings to standard values» (R4)
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] Reset клетки " + strLetter + " — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            Exception oErr = TryDisplayListTyped(oDs, strName, iVariant);
            if (oErr != null)
                oErr = TryDisplayListByStrings(oDs, strName, strLib, iVariant);
            if (oErr != null)
            {
                ProbeWarn("[DSPROBE] вариант " + strLetter + " не отрисован: " +
                    oErr.GetType().Name + ": " + oErr.Message);
                return false;
            }
            TrySetDefaultViewport(oDs);
            return true;
        }

        /// <summary>Сброс сетки: Dispose ВСЕХ сервисов + удаление клеток
        /// (OnFormClosing и каждая пересборка).</summary>
        private void DisposePreviewGrid()
        {
            foreach (DrawingService oDs in _lstPreviewServices)
            {
                if (oDs == null) continue;
                try { oDs.Dispose(); }
                catch (Exception oEx)
                {
                    ProbeWarn("[DSPROBE] Dispose DrawingService — " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
            }
            _lstPreviewServices.Clear();
            foreach (Panel oCell in _lstPreviewCells)
            {
                if (oCell == null) continue;
                try { oCell.Dispose(); }
                catch { /* контрол уже отсоединён — не мешает */ }
            }
            _lstPreviewCells.Clear();
            _lstCellReady.Clear();
            _tlpPreviewGrid.Controls.Clear();
            _tlpPreviewGrid.Invalidate();
        }

        /// <summary>Путь 1 (первичный): DataModel MasterData Symbol(oSymbolLibrary,
        /// strName) → SymbolVariant(oSymbol, nVariant) → CreateDisplayList(SymbolVariant)
        /// — все три шага KB-доказаны (цитаты в шапке). Объект библиотеки — из
        /// _lstLibraryObjects (тот же, что использует цепочка A); не выбран из
        /// списка — путь недоступен (исключение-заглушка, [DSPROBE]).</summary>
        private Exception TryDisplayListTyped(DrawingService oDs, string strName, int nVariant)
        {
            int iIndex = _lstLibraries.SelectedIndex;
            SymbolLibrary oLib = (iIndex >= 0 && iIndex < _lstLibraryObjects.Count)
                ? _lstLibraryObjects[iIndex]
                : null;
            if (oLib == null)
            {
                Exception oStub = new InvalidOperationException(
                    "объект библиотеки не выбран из списка — типизированный путь недоступен");
                ProbeInfo("[DSPROBE] CreateDisplayList(SymbolVariant) — " + oStub.Message);
                return oStub;
            }
            try
            {
                Symbol oSymbol = new Symbol(oLib, strName);
                SymbolVariant oSymbolVariant = new SymbolVariant(oSymbol, nVariant);
                oDs.CreateDisplayList(oSymbolVariant);
                ProbeInfo("[DSPROBE] CreateDisplayList(SymbolVariant) — ок " +
                    "(типизированный путь: Symbol(SymbolLibrary) + SymbolVariant(Symbol,Int32))");
                return null;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] CreateDisplayList(SymbolVariant) — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return oEx;
            }
        }

        /// <summary>Путь 2 (запасной): CreateDisplayList(String,String,Int32,Project)
        /// — KB-доказана (пример HE_Display: CreateDisplayList(strObj,"",0,gProject));
        /// первый пробой — с именем библиотеки, второй — с "" вместо библиотеки.
        /// Перегрузка с RepresentationType сознательно НЕ пробуется (план H-4b: не
        /// доказана — одна гипотеза за прогон). Оба отказа — последнее исключение.</summary>
        private Exception TryDisplayListByStrings(DrawingService oDs, string strName,
            string strLib, int nVariant)
        {
            try
            {
                oDs.CreateDisplayList(strName, strLib, nVariant, _oProject);
                ProbeInfo("[DSPROBE] CreateDisplayList(String,String,Int32,Project) — ок" +
                    " (имя библ: «" + strLib + "»)");
                return null;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] CreateDisplayList(...," + strLib + ",...) — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            try
            {
                oDs.CreateDisplayList(strName, "", nVariant, _oProject);
                ProbeInfo("[DSPROBE] CreateDisplayList(String,String,Int32,Project) — ок" +
                    " (пустое имя библ, паттерн HE_Display)");
                return null;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] CreateDisplayList(...,\"\") — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return oEx;
            }
        }

        /// <summary>Проба SetDefaultViewport ПОСЛЕ создания списка (KB: «Adjusts
        /// viewport to the bounding box of the objects from drawing list») —
        /// безвредна; факт — в [DSPROBE]. Отказ — Console-дамп (не поле отказа
        /// рендера: список уже создан).</summary>
        private void TrySetDefaultViewport(DrawingService oDs)
        {
            try
            {
                oDs.SetDefaultViewport();
                ProbeInfo("[DSPROBE] SetDefaultViewport — ок");
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] SetDefaultViewport — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        // --- слоты вариантов + карточка ---

        /// <summary>rev.14.1 (R6): слоты — ComboBox букв A-H (всегда 8; решение
        /// пользователя: «Вариантов может быть всего 8 у любого символа»). Буква =
        /// (char)('A'+индекс); наружу Variant*/настройки — числа (индексы).</summary>
        private static void FillVariantCombo(ComboBox oComboBox)
        {
            oComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            oComboBox.Items.Clear();
            for (int i = 0; i < 8; i++)
                oComboBox.Items.Add(((char)('A' + i)).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Предвыбор слота из числа: 0..7 → SelectedIndex; вне диапазона —
        /// прижать к 0 + INFO (сохранённое имя/вариант другой библиотеки).</summary>
        private void SetVariantIndex(ComboBox oComboBox, int nValue)
        {
            if (nValue < 0 || nValue > 7)
            {
                if (nValue > 7)
                    ProbeInfo("[DSPROBE] вариант " +
                        nValue.ToString(CultureInfo.InvariantCulture) +
                        " вне A-H — слот прижат к A");
                nValue = 0;
            }
            oComboBox.SelectedIndex = nValue;
        }

        /// <summary>Карточка превью: имя (как есть), категория (чистое имя узла-
        /// категории или «—»), описание символа (SYMB_DESC #16011, R7; счётчик
        /// «вариантов» удалён по R7).</summary>
        private void UpdateCard(string strName, string strCategory)
        {
            _lblPreviewName.Text = "Имя: " +
                (string.IsNullOrEmpty(strName) ? "—" : strName);
            _lblPreviewCategory.Text = "Категория: " +
                (string.IsNullOrEmpty(strCategory) ? "—" : strCategory);
            string strDesc = null;
            int iIndex = _lstAllSymbols.IndexOf(strName);
            if (iIndex >= 0 && iIndex < _lstAllDescriptions.Count)
                strDesc = _lstAllDescriptions[iIndex];
            _lblPreviewDescription.Text = "Описание: " +
                (string.IsNullOrEmpty(strDesc) ? "—" : strDesc);
        }

        /// <summary>Сброс карточки в прочерки (фикс r14.1: минорный пункт 5 ревью
        /// — вместе со сбросом превью). Вызывается из RefreshPreviewGrid при
        /// пустых Library/Name.</summary>
        private void ClearCard()
        {
            _lblPreviewName.Text = "Имя: —";
            _lblPreviewCategory.Text = "Категория: —";
            _lblPreviewDescription.Text = "Описание: —";
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
            // rev.14.1 R8: подпись прямо над полем (верхний отступ 0).
            oLabel.Margin = new Padding(3, 0, 3, 0);
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

        /// <summary>Layout (rev.14.1): контент — TableLayoutPanel 2 колонки
        /// (~55%/45%), слева — библиотеки/поиск/дерево/имя (read-only), справа —
        /// превью-сетка A-H и карточка; ниже — слоты H/V (комбо букв), статусная
        /// строка (на всю ширину), кнопки (как rev.13.1).</summary>
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
            AddRow(oLeft, LabelOf("Символы (дерево: библиотека → категория → группа → " +
                "символ; двойной клик — выбрать):"), false);
            _treeSymbols.Height = 180;
            // HSCROLL у TreeView автоматический (свойства HorizontalScrollbar нет — CS1061).
            AddRow(oLeft, _treeSymbols, true);
            AddRow(oLeft, _lblCount, false);
            AddRow(oLeft, LabelOf("Имя выбранного символа:"), false);
            AddRow(oLeft, _txtSymbol, true);

            // ПРАВО: превью-сетка (rev.14.1 R3-R5: до 8 клеток A-H, фон чёрный) +
            // карточка (имя/категория/описание).
            TableLayoutPanel oRight = new TableLayoutPanel();
            oRight.Dock = DockStyle.Fill;
            oRight.ColumnCount = 1;
            oRight.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            AddRow(oRight, LabelOf("Превью"), false);
            _tlpPreviewGrid.ColumnCount = 4;
            for (int iCol = 0; iCol < 4; iCol++)
                _tlpPreviewGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            _tlpPreviewGrid.RowCount = 2;
            for (int iRow = 0; iRow < 2; iRow++)
                _tlpPreviewGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 110f));
            _tlpPreviewGrid.Dock = DockStyle.Fill;
            _tlpPreviewGrid.BackColor = Color.Black;
            AddRowFixed(oRight, _tlpPreviewGrid, 228);
            _lblPreviewName.AutoSize = true;
            _lblPreviewCategory.AutoSize = true;
            _lblPreviewDescription.AutoSize = true;
            AddRow(oRight, _lblPreviewName, false);
            AddRow(oRight, _lblPreviewCategory, false);
            AddRow(oRight, _lblPreviewDescription, false);

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
            pnlVariants.Controls.Add(LabelOf("Вариант для горизонтальной формы:"));
            _cbVariantH.Width = 90;
            pnlVariants.Controls.Add(_cbVariantH);
            pnlVariants.Controls.Add(LabelOf("  Вариант для вертикальной формы:"));
            _cbVariantV.Width = 90;
            pnlVariants.Controls.Add(_cbVariantV);
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

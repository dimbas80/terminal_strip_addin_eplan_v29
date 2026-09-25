using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;

namespace MyEplanActions
{
    /// <summary>Браузер символов кабеля (Этап 8, задача H-4; ruling R11): выбор
    /// библиотеки/символа + два слота варианта (H/V, 0-based). Программный layout
    /// без designer (паттерн MainDialog: TableLayoutPanel, хелперы статическими
    /// методами — локальных функций в C#5 нет). Диалог ТОЛЬКО ЧИТАЕТ — никаких
    /// EPLAN-мутаций. Выход: свойства Library/SymbolName/VariantH/VariantV
    /// (fix-3, R13: Name -> SymbolName — свойство «Name» скрывало
    /// Control.Name, CS0108; new-тень не используется сознательно);
    /// «ОК» валиден
    /// только при непустых Library/SymbolName (значения НЕ триммингуются — урок хвостового
    /// пробела п.33). «Отмена»/крестик — ничего не возвращается.
    /// KB-факты API 2.9 (www.eplan.help; грабля базы: блок [Code] — артефакт
    /// скрейпера, авторитетен Remarks):
    /// - Project.SymbolLibraries — «Gets the symbol libraries used by the project»,
    ///   «public SymbolLibrary[] SymbolLibraries { get; }», Property Value: «Table
    ///   of Eplan.EplApi.DataModel.MasterData.SymbolLibrary objects» —
    ///   https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.Project~SymbolLibraries.html
    ///   (KB-запрос 25.09 этого прогона: элементный тип ДОКАЗАН — перебор
    ///   типизирован,SymbolLibrary[]). Только ПРОЕКТНЫЕ библиотеки
    ///   (системные — «Отложено» спеки).
    /// - MDSymbolLibrary.Symbols — «Gets a read only list of all Symbols in the
    ///   library», «public MDSymbol[] Symbols { get; }» —
    ///   https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.MasterDatau~Eplan.EplApi.MasterData.MDSymbolLibrary~Symbols.html
    /// - MDSymbol.Variants — «a read only list of all MDSymbolVariants in the
    ///   symbol», «public MDSymbolVariant[] Variants { get; }» —
    ///   https://www.eplan.help/en-us/infoportal/content/api/2.9/Eplan.EplApi.MasterDatau~Eplan.EplApi.MasterData.MDSymbol~Variants.html
    /// НЕ доказано KB: конструктор MDSymbolLibrary, MDSymbol.Name, свойства имени/
    /// пути у DataModel SymbolLibrary, его перечислители — ТОЛЬКО reflection-пробы
    /// с graceful-деградацией (урок rev.7: имена членов не угадываем — CS1061 на
    /// первой стендовой сборке; отказы проб — Console-дамп + статусная строка
    /// диалога, ручной ввод имени не запрещаем). Fix-1 (ревью 09fb80d): ПУТЬ для
    /// цепочки B — свои кандидаты LocationInfo→Name→IdentifyingName
    /// (EnumeratePathCandidates), отдельно от отображаемого имени/возвращаемого
    /// Library (ResolveDisplayPath, Name-first).</summary>
    public class SymbolBrowserDialog : Form
    {
        private readonly Project _oProject;
        private readonly TextBox _txtLibrary = new TextBox();
        private readonly ListBox _lstLibraries = new ListBox();
        private readonly TextBox _txtSearch = new TextBox();
        private readonly ListBox _lstSymbols = new ListBox();
        private readonly TextBox _txtSymbol = new TextBox();
        private readonly NumericUpDown _numVariantH = new NumericUpDown();
        private readonly NumericUpDown _numVariantV = new NumericUpDown();
        private readonly Label _lblCount = new Label();
        private readonly Label _lblStatus = new Label();

        // Объекты библиотек под строками списка (параллельны _lstLibraries.Items).
        private readonly List<SymbolLibrary> _lstLibraryObjects = new List<SymbolLibrary>();
        // Все символы выбранной библиотеки (поиск фильтрует _lstSymbols отсюда);
        // параллельный список — число вариантов, -1 — неизвестно (R11: тогда
        // свободный int-ввод ≥ 0).
        private List<string> _lstAllSymbols = new List<string>();
        private List<int> _lstAllVariantCounts = new List<int>();

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
            ClientSize = new Size(520, 560);

            // Диапазон слотов: 0-based (эмпирика rev.10.0), сначала свободный
            // int-ввод (count неизвестен), сузится при выборе символа с известным
            // числом вариантов (MDSymbol.Variants, KB).
            _numVariantH.Minimum = 0;
            _numVariantV.Minimum = 0;
            _numVariantH.Maximum = int.MaxValue;
            _numVariantV.Maximum = int.MaxValue;
            SetVariantValue(_numVariantH, nVariantH);
            SetVariantValue(_numVariantV, nVariantV);

            _txtLibrary.Text = strLibrary ?? string.Empty;
            _txtSymbol.Text = strName ?? string.Empty;

            // (1) Библиотеки: Project.SymbolLibraries (KB-цитата в шапке).
            LoadLibraries(strLibrary);

            // (2) Реакции (диалог только читает; программы мутаций нет).
            _lstLibraries.SelectedIndexChanged += LstLibrariesOnSelectedIndexChanged;
            _lstSymbols.SelectedIndexChanged += LstSymbolsOnSelectedIndexChanged;
            _txtSearch.TextChanged += delegate { RefillSymbolList(); };
            // Ручной ввод имени символа: точное совпадение в списке — подсветка +
            // сужение диапазона вариантов.
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

            // --- Layout (TableLayoutPanel, без designer — паттерн MainDialog) ---
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12, 8, 12, 8);
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            AddRow(root, LabelOf("Библиотека (проекта; имя можно ввести вручную):"), false);
            AddRow(root, _txtLibrary, true);
            _lstLibraries.Height = 96;
            _lstLibraries.HorizontalScrollbar = true;
            AddRow(root, _lstLibraries, true);
            AddRow(root, LabelOf("Поиск символа (подстрока):"), false);
            AddRow(root, _txtSearch, true);
            AddRow(root, LabelOf("Символы:"), false);
            _lstSymbols.Height = 120;
            _lstSymbols.HorizontalScrollbar = true;
            AddRow(root, _lstSymbols, true);
            AddRow(root, _lblCount, false);
            AddRow(root, LabelOf("Имя символа:"), false);
            AddRow(root, _txtSymbol, true);

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
            _lblStatus.MaximumSize = new Size(480, 0);
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

        private void LstSymbolsOnSelectedIndexChanged(object oSender, EventArgs oArgs)
        {
            int iIndex = _lstSymbols.SelectedIndex;
            if (iIndex < 0) return;
            string strShown = _lstSymbols.Items[iIndex].ToString();
            _txtSymbol.Text = strShown;
            UpdateVariantRange(strShown);
        }

        private void TxtSymbolOnTextChanged(object oSender, EventArgs oArgs)
        {
            string strName = _txtSymbol.Text;
            if (string.IsNullOrEmpty(strName)) return;
            int iShown = _lstSymbols.Items.IndexOf(strName);   // точное, без trim (п.33)
            if (iShown >= 0 && _lstSymbols.SelectedIndex != iShown)
                _lstSymbols.SelectedIndex = iShown;            // вызовет UpdateVariantRange
            else if (iShown < 0 && _lstAllSymbols.IndexOf(strName) >= 0)
                UpdateVariantRange(strName);
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
        /// залипший DialogResult.OK (паттерн MainDialog). Валидации здесь нет.</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel && e.CloseReason == CloseReason.UserClosing &&
                DialogResult == DialogResult.OK)
            {
                DialogResult = DialogResult.Cancel;
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
        /// SymbolLibrary — НЕ доказаны, reflection-проба; B) MDSymbolLibrary(path)
        /// .Symbols — свойство KB-доказано (цитата в шапке), путь и конструктор
        /// НЕ доказаны — Activator-проба. Полный отказ — INFO-статус, ручной ввод
        /// имени символа не запрещаем. Никаких мутаций.</summary>
        private void RefreshSymbols()
        {
            _lstAllSymbols = new List<string>();
            _lstAllVariantCounts = new List<int>();
            _lstSymbols.Items.Clear();
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
                SetStatus("Символы перечислены через DataModel SymbolLibrary (проба), " +
                    _lstAllSymbols.Count.ToString(CultureInfo.InvariantCulture) + " шт.");
                RefillSymbolList();
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
                SetStatus("Символы перечислены через MDSymbolLibrary.Symbols (KB), " +
                    _lstAllSymbols.Count.ToString(CultureInfo.InvariantCulture) + " шт.");
                RefillSymbolList();
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
        /// счётчик вариантов при этом -1 (неизвестен). Пустой/отказной результат
        /// кандидата — следующий; всё не удалось — false.</summary>
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
                            _lstAllSymbols.Add(strName);
                            _lstAllVariantCounts.Add(-1);
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
                            _lstAllSymbols.Add(strName);
                            _lstAllVariantCounts.Add(-1);
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
        /// Число вариантов: MDSymbol.Variants KB-доказано (TryGetVariantCount).</summary>
        private bool TryEnumerateViaMasterData(SymbolLibrary oLib)
        {
            object oMdLib = TryCreateMdLibrary(oLib);
            if (oMdLib == null) return false;
            try
            {
                Eplan.EplApi.MasterData.MDSymbol[] arrSymbols =
                    ((Eplan.EplApi.MasterData.MDSymbolLibrary)oMdLib).Symbols;
                if (arrSymbols == null || arrSymbols.Length == 0) return false;
                foreach (Eplan.EplApi.MasterData.MDSymbol oMdSym in arrSymbols)
                {
                    if (oMdSym == null) continue;
                    string strName = ResolveNameViaReflection(oMdSym);
                    if (string.IsNullOrEmpty(strName)) continue;
                    _lstAllSymbols.Add(strName);
                    _lstAllVariantCounts.Add(TryGetVariantCount(oMdSym));
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

        // CS0618 (ruling контроллера, fix-2): тот же устаревший, но единственный
        // гарантированно доступный в референсе .NET 4 способ чтения свойства
        // (args[]-перегрузка); newer overloads на стенде не доказаны (CS1061-риск).
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

        // --- список символов: фильтр + предвыбор + диапазон вариантов ---

        /// <summary>Заполнение _lstSymbols из _lstAllSymbols фильтром подстрокой
        /// (Contains через IndexOf, OrdinalIgnoreCase — R11) по тексту поиска.</summary>
        private void RefillSymbolList()
        {
            string strFilter = _txtSearch.Text ?? string.Empty;
            _lstSymbols.BeginUpdate();
            _lstSymbols.Items.Clear();
            int nShown = 0;
            for (int i = 0; i < _lstAllSymbols.Count; i++)
            {
                if (strFilter.Length == 0 ||
                    _lstAllSymbols[i].IndexOf(strFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lstSymbols.Items.Add(_lstAllSymbols[i]);
                    nShown++;
                }
            }
            _lstSymbols.EndUpdate();
            _lblCount.Text = "показано " + nShown.ToString(CultureInfo.InvariantCulture) +
                " из " + _lstAllSymbols.Count.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Предвыбор текущего имени символа точным совпадением (без trim —
        /// урок п.33): подсветка в списке + сужение диапазона вариантов; имени нет
        /// в списке — текст остаётся ручным вводом.</summary>
        private void SelectSymbolPrechoice(string strName)
        {
            if (string.IsNullOrEmpty(strName)) return;
            _txtSymbol.Text = strName;
            int iAll = _lstAllSymbols.IndexOf(strName);
            if (iAll >= 0)
            {
                RefillSymbolList();
                int iShown = _lstSymbols.Items.IndexOf(strName);
                if (iShown >= 0) _lstSymbols.SelectedIndex = iShown;
                UpdateVariantRange(strName);
            }
        }

        /// <summary>Диапазон слотов варианта 0..count-1 из числа вариантов
        /// выбранного символа (KB MDSymbol.Variants); count неизвестен (-1) —
        /// свободный int-ввод ≥ 0 (Maximum = int.MaxValue). Текущие значения
        /// прижимаются к новому диапазону.</summary>
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
        }

        private static void SetVariantValue(NumericUpDown oUpDown, int nValue)
        {
            if (nValue < 0) nValue = 0;
            decimal dValue = (decimal)nValue;
            if (dValue > oUpDown.Maximum) dValue = oUpDown.Maximum;
            oUpDown.Value = dValue;
        }

        private void SetStatus(string strText)
        {
            _lblStatus.Text = strText;
        }

        // --- Хелперы построения (паттерн MainDialog) ---

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

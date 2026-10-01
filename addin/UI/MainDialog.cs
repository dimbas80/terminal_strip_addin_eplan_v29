using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
// rev.13.1 (H-4, ruling R10): Project — только для браузера символов (чтение
// списков); диалог по-прежнему не мутирует EPLAN-объекты.
using Eplan.EplApi.DataModel;

namespace MyEplanActions
{
    /// <summary>
    /// Диалог генерации схемы подключений клеммника (Фаза H, задача H-2; спека
    /// 2026-09-25-fase-h-ui-design.md §2 п.1–3, 9, §4). Программный layout без
    /// designer (TableLayoutPanel). EPLAN-тип держит только Project (rev.13.1,
    /// H-4: браузер символа; null — кнопка браузера остаётся выключенной, штатно).
    /// Предвыбор — точным сравнением БЕЗ trim (хвостовой пробел имени формы
    /// значим, урок п.33). Результаты после DialogResult.OK — свойства
    /// SelectedStripName / SelectedFormName / SelectedOrientation +
    /// SelectedSymbolLibrary / SelectedSymbolName / SelectedVariantH /
    /// SelectedVariantV (H-4: выбор браузера применён к внутреннему
    /// settings-объекту; «Отмена» браузера — ничего не меняет; значения
    /// НЕ триммингуются).
    /// rev.17 (Task 5, план 2026-10-01-ui-emc-profiles): клеммник — TreeView
    /// структуры ОУ (листья = полные ОУ → SelectedStripName); групбокс
    /// «Профили отображения» — 6 списков .emc по слотам BP
    /// (SelectedEmcProfile); подписи вариантов — буквой (VariantText.Letter).
    /// </summary>
    public class MainDialog : Form
    {
        private readonly TreeView _treeStrip = new TreeView();
        private readonly ComboBox _cboForm = new ComboBox();
        private readonly ComboBox _cboOrientation = new ComboBox();
        // rev.17 (Task 5): 6 списков профилей .emc по слотам BP.
        private readonly ComboBox _cboEmcStripH = new ComboBox();
        private readonly ComboBox _cboEmcStripV = new ComboBox();
        private readonly ComboBox _cboEmcDeviceH = new ComboBox();
        private readonly ComboBox _cboEmcDeviceV = new ComboBox();
        private readonly ComboBox _cboEmcLinkH = new ComboBox();
        private readonly ComboBox _cboEmcLinkV = new ComboBox();
        private readonly Label _lblSymbol = new Label();
        private readonly Label _lblVariantH = new Label();
        private readonly Label _lblVariantV = new Label();
        private readonly Button _btnBrowseSymbol = new Button();

        // H-4 (R10): проект для браузера (null — кнопка выключена) и объект
        // настроек диалога — выбор браузера применяется к его 4 полям.
        private readonly Project _oProject;
        private readonly AddInSettings _oEffective;
        // rev.14.1: канал проб браузера символов (nullable; пробы [FD]/[DSPROBE]
        // дублируются в главный лог — урок прогона rev.14.0).
        private readonly DiagnosticLogger _oLogger;

        /// <summary>Основной конструктор (rev.17, Task 5). lstStripTree — дерево
        /// клеммников (промежуточные узлы — структура ОУ, листья — полные ОУ);
        /// dicProfilesByVariant — профили .emc по варианту символа (Task 7
        /// собирает EmcSchemeCatalog + ForVariant); null/нет ключа →
        /// соответствующий список пуст и выключен (деградация: BP без набора).
        /// oProject (rev.13.1, H-4) — текущий проект для браузера символов;
        /// null — кнопка выключена.</summary>
        public MainDialog(List<StripTreeNode> lstStripTree,
            List<string> lstFormNames, AddInSettings oSettings, Project oProject,
            DiagnosticLogger oLogger,
            Dictionary<int, List<EmcSchemeInfo>> dicProfilesByVariant)
        {
            AddInSettings oEffective = oSettings ?? new AddInSettings();
            _oEffective = oEffective;
            _oProject = oProject;
            _oLogger = oLogger;

            Text = "Генерация схемы подключений клеммника";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 660);

            // (1) Клеммник: дерево структуры ОУ. Выбираемы ТОЛЬКО листья
            // (BeforeSelect отменяет выбор промежуточных узлов — FullName
            // непусто лишь у листа).
            _treeStrip.HideSelection = false;
            _treeStrip.Height = 140;
            _treeStrip.BeforeSelect += TreeStripBeforeSelect;
            FillTree(_treeStrip, lstStripTree);

            // (2) Форма отчёта: все *.f11 без расширения, DropDownList.
            _cboForm.DropDownStyle = ComboBoxStyle.DropDownList;
            FillCombo(_cboForm, lstFormNames);
            SelectExact(_cboForm, oEffective.Form);

            // (3) Ориентация: индексы 0/1/2 ↔ SettingsOrientation Auto/H/V.
            _cboOrientation.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboOrientation.Items.Add("Авто (из формы)");
            _cboOrientation.Items.Add("Горизонтальная");
            _cboOrientation.Items.Add("Вертикальная");
            _cboOrientation.SelectedIndex = OrientationIndexOf(oEffective.OrientationMode);

            // (3b) rev.17: 6 списков профилей .emc по слотам BP + предвыбор
            // из настроек (пустой список → ComboBox выключен).
            FillEmcCombos(dicProfilesByVariant);

            // (4) Символ кабеля — H-4 (ruling R10): браузер активен при доступном
            // проекте; выбор применяется к 4 полям _oEffective + переподписи.
            _lblSymbol.AutoSize = true;
            _lblVariantH.AutoSize = true;
            _lblVariantV.AutoSize = true;
            _btnBrowseSymbol.Text = "Выбрать символ…";
            _btnBrowseSymbol.AutoSize = true;
            _btnBrowseSymbol.Enabled = _oProject != null;
            _btnBrowseSymbol.Click += BtnBrowseSymbolOnClick;
            UpdateSymbolLabels();

            // (5) Кнопки: «Создать» — валидация в Click (DialogResult=OK ставится
            // ТОЛЬКО при валидном выборе — закрыться с пустым выбором нельзя);
            // «Отмена» — DialogResult.Cancel (CancelButton).
            Button btnCreate = new Button();
            btnCreate.Text = "Создать";
            btnCreate.Size = new Size(110, 27);
            btnCreate.Click += BtnCreateOnClick;
            Button btnCancel = new Button();
            btnCancel.Text = "Отмена";
            btnCancel.Size = new Size(110, 27);
            btnCancel.DialogResult = DialogResult.Cancel;
            AcceptButton = btnCreate;
            CancelButton = btnCancel;

            // --- Layout (TableLayoutPanel, без designer) ---
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12, 10, 12, 10);
            root.AutoScroll = true;
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            AddRow(root, LabelOf("Клеммник:"), false);
            AddRow(root, _treeStrip, true);
            AddRow(root, LabelOf("Форма отчёта:"), false);
            AddRow(root, _cboForm, true);
            AddRow(root, LabelOf("Ориентация:"), false);
            AddRow(root, _cboOrientation, true);

            // Профили отображения: 2 колонки (подпись слева, список справа),
            // одна строка на слот — компактнее одноколоночного варианта.
            GroupBox grpEmc = new GroupBox();
            grpEmc.Text = "Профили отображения";
            grpEmc.AutoSize = true;
            grpEmc.Padding = new Padding(8, 2, 8, 4);
            TableLayoutPanel grpEmcLayout = new TableLayoutPanel();
            grpEmcLayout.Dock = DockStyle.Fill;
            grpEmcLayout.AutoSize = true;
            grpEmcLayout.ColumnCount = 2;
            grpEmcLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grpEmcLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            AddLabeledRow(grpEmcLayout, "Клеммники, гориз.:", _cboEmcStripH);
            AddLabeledRow(grpEmcLayout, "Клеммники, верт.:", _cboEmcStripV);
            AddLabeledRow(grpEmcLayout, "Устройства, гориз.:", _cboEmcDeviceH);
            AddLabeledRow(grpEmcLayout, "Устройства, верт.:", _cboEmcDeviceV);
            AddLabeledRow(grpEmcLayout, "Ссылка кабеля, гориз.:", _cboEmcLinkH);
            AddLabeledRow(grpEmcLayout, "Ссылка кабеля, верт.:", _cboEmcLinkV);
            grpEmc.Controls.Add(grpEmcLayout);
            AddRow(root, grpEmc, true);

            GroupBox grpSymbol = new GroupBox();
            grpSymbol.Text = "Символ кабеля";
            grpSymbol.AutoSize = true;
            grpSymbol.Padding = new Padding(8, 2, 8, 4);
            TableLayoutPanel grpLayout = new TableLayoutPanel();
            grpLayout.Dock = DockStyle.Fill;
            grpLayout.AutoSize = true;
            grpLayout.ColumnCount = 1;
            grpLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            AddRow(grpLayout, _lblSymbol, false);
            AddRow(grpLayout, _btnBrowseSymbol, false);
            FlowLayoutPanel pnlVariants = new FlowLayoutPanel();
            pnlVariants.FlowDirection = FlowDirection.LeftToRight;
            pnlVariants.AutoSize = true;
            pnlVariants.Margin = new Padding(3, 0, 3, 0);
            pnlVariants.Controls.Add(_lblVariantH);
            pnlVariants.Controls.Add(_lblVariantV);
            AddRow(grpLayout, pnlVariants, false);
            grpSymbol.Controls.Add(grpLayout);
            AddRow(root, grpSymbol, true);

            FlowLayoutPanel pnlButtons = new FlowLayoutPanel();
            pnlButtons.FlowDirection = FlowDirection.RightToLeft;
            pnlButtons.Dock = DockStyle.Fill;
            pnlButtons.AutoSize = true;
            pnlButtons.Controls.Add(btnCreate);   // первый — у правого края
            pnlButtons.Controls.Add(btnCancel);
            AddRow(root, pnlButtons, true);

            Controls.Add(root);
        }

        /// <summary>Совместимый конструктор (rev.17, Task 5) — до перевода
        /// AnalyzeAction на дерево (Task 7): строит дерево из полных имён
        /// фоллбэком StripStructureTree (четыре свойства структуры не заданы
        /// → разбор FullName) и делегирует основному конструктору с пустой
        /// картой профилей (все 6 списков пусты и выключены).</summary>
        public MainDialog(List<string> lstStripNames, List<string> lstFormNames,
            AddInSettings oSettings, Project oProject = null,
            DiagnosticLogger oLogger = null)
            : this(BuildTreeFromNames(lstStripNames), lstFormNames, oSettings,
                oProject, oLogger,
                new Dictionary<int, List<EmcSchemeInfo>>())
        {
        }

        /// <summary>«Создать»: валидация — выбраны непустые клеммник и форма.
        /// Невалидно → MessageBox (владелец — диалог), DialogResult НЕ меняем
        /// (форма остаётся открытой); валидно → DialogResult=OK — модальная форма
        /// закрывается с результатом. Валидация именно здесь: клик по кнопке —
        /// единственный путь закрыть диалог с OK (крестик/ESC = отмена).</summary>
        private void BtnCreateOnClick(object oSender, EventArgs oArgs)
        {
            if (SelectedStripName == null || SelectedFormName == null)
            {
                MessageBox.Show(this, "Выберите клеммник и форму отчёта.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }

        /// <summary>Крестик/Alt+F4 (CloseReason.UserClosing) — это «отмена»: гасим
        /// залипший DialogResult.OK, если он остался от предыдущего показа (повтор
        /// после провала создания отчёта). Валидации здесь НЕТ — она в
        /// btnCreate.Click; закрытие крестом = отмена при любом выборе.</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel && e.CloseReason == CloseReason.UserClosing &&
                DialogResult == DialogResult.OK)
            {
                DialogResult = DialogResult.Cancel;
            }
        }

        /// <summary>H-4 (R10): «Выбрать символ…» — браузер с текущим выбором;
        /// OK — применить 4 значения к _oEffective (значения НЕ триммить — урок
        /// хвостового пробела) и переподписать слоты; «Отмена» — ничего не меняет.
        /// _oProject == null — выход (кнопка в этом случае выключена, штатно).</summary>
        private void BtnBrowseSymbolOnClick(object oSender, EventArgs oArgs)
        {
            if (_oProject == null) return;
            using (SymbolBrowserDialog oBrowser = new SymbolBrowserDialog(_oProject,
                _oEffective.SymbolLibrary, _oEffective.SymbolName,
                _oEffective.VariantH, _oEffective.VariantV, _oLogger))
            {
                if (oBrowser.ShowDialog(this) != DialogResult.OK) return;
                _oEffective.SymbolLibrary = oBrowser.Library;
                _oEffective.SymbolName = oBrowser.SymbolName;
                _oEffective.VariantH = oBrowser.VariantH;
                _oEffective.VariantV = oBrowser.VariantV;
                UpdateSymbolLabels();
            }
        }

        /// <summary>Подписи блока «Символ кабеля» из _oEffective (rev.17, Task 3/5):
        /// «Вариант горизонтальный:» / «Вариант вертикальный:» — вариант БУКВОЙ
        /// (VariantText.Letter: 0→A … 7→H); _lblSymbol — «биб/имя (H/V б/б)».</summary>
        private void UpdateSymbolLabels()
        {
            _lblSymbol.Text = _oEffective.SymbolLibrary + "/" + _oEffective.SymbolName +
                " (H/V " + VariantText.Letter(_oEffective.VariantH) + "/" +
                VariantText.Letter(_oEffective.VariantV) + ")";
            _lblVariantH.Text = "Вариант горизонтальный: " +
                VariantText.Letter(_oEffective.VariantH);
            _lblVariantV.Text = "Вариант вертикальный: " +
                VariantText.Letter(_oEffective.VariantV);
        }

        /// <summary>Полное ОУ выбранного клеммника (null — лист не выбран).
        /// Промежуточные узлы дерева невыбираемы (BeforeSelect), поэтому null
        /// означает «ничего не выбрано».</summary>
        public string SelectedStripName
        {
            get
            {
                TreeNode oSelected = _treeStrip.SelectedNode;
                if (oSelected == null) return null;
                StripTreeNode oData = oSelected.Tag as StripTreeNode;
                if (oData == null || string.IsNullOrEmpty(oData.FullName)) return null;
                return oData.FullName;
            }
        }

        /// <summary>Имя выбранной формы без расширения (null — не выбрано);
        /// хвостовой пробел сохраняется как есть (урок п.33).</summary>
        public string SelectedFormName
        {
            get { return TextOf(_cboForm); }
        }

        /// <summary>Режим ориентации из выпадающего списка (нулевой индекс или
        /// «не выбран» — Auto).</summary>
        public SettingsOrientation SelectedOrientation
        {
            get
            {
                int iIndex = _cboOrientation.SelectedIndex;
                if (iIndex == 1) return SettingsOrientation.Horizontal;
                if (iIndex == 2) return SettingsOrientation.Vertical;
                return SettingsOrientation.Auto;
            }
        }

        /// <summary>Библиотека символа (H-4): выбор браузера (без trim) либо
        /// предвыбор из настроек, если браузер не открывался/отменён.</summary>
        public string SelectedSymbolLibrary
        {
            get { return _oEffective.SymbolLibrary; }
        }

        /// <summary>Имя символа (H-4, без trim) — см. SelectedSymbolLibrary.</summary>
        public string SelectedSymbolName
        {
            get { return _oEffective.SymbolName; }
        }

        /// <summary>Индекс варианта слота H (0-based; H-4).</summary>
        public int SelectedVariantH
        {
            get { return _oEffective.VariantH; }
        }

        /// <summary>Индекс варианта слота V (0-based; H-4).</summary>
        public int SelectedVariantV
        {
            get { return _oEffective.VariantV; }
        }

        /// <summary>Выбор профиля .emc для слота BP (rev.17, Task 5): строка
        /// EmcProfileCatalog.EncodeSelection(file, SchemeName) выбранного
        /// элемента соответствующего ComboBox; пусто/нет выбора/неизвестный
        /// слот → null (пайплайн: BP без набора).</summary>
        public string SelectedEmcProfile(BpProfileSlot eSlot)
        {
            ComboBox oCombo = ComboFor(eSlot);
            if (oCombo == null) return null;
            EmcSchemeInfo oInfo = oCombo.SelectedItem as EmcSchemeInfo;
            if (oInfo == null) return null;
            return EmcProfileCatalog.EncodeSelection(oInfo.File, oInfo.SchemeName);
        }

        // --- Хелперы построения/выбора (локальных функций в C#5 нет) ---

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

        private static void FillCombo(ComboBox oCombo, List<string> lstItems)
        {
            if (lstItems == null) return;
            foreach (string strItem in lstItems)
                oCombo.Items.Add(strItem);
        }

        /// <summary>Предвыбор точным совпадением (БЕЗ trim — урок п.33); значения
        /// нет в списке — список остаётся без предвыбора.</summary>
        private static void SelectExact(ComboBox oCombo, string strValue)
        {
            if (string.IsNullOrEmpty(strValue)) return;
            int iIndex = oCombo.Items.IndexOf(strValue);
            if (iIndex >= 0) oCombo.SelectedIndex = iIndex;
        }

        private static int OrientationIndexOf(SettingsOrientation eMode)
        {
            if (eMode == SettingsOrientation.Horizontal) return 1;
            if (eMode == SettingsOrientation.Vertical) return 2;
            return 0;
        }

        private static string TextOf(ComboBox oCombo)
        {
            object oItem = oCombo.SelectedItem;
            return oItem == null ? null : oItem.ToString();
        }

        /// <summary>rev.17 (Task 5): подпись + контрол одной строкой в
        /// 2-колоночном TableLayoutPanel (групбокс профилей).</summary>
        private static void AddLabeledRow(TableLayoutPanel oPanel,
            string strLabel, Control oControl)
        {
            oPanel.RowCount++;
            oPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label oLabel = LabelOf(strLabel);
            oLabel.Anchor = AnchorStyles.Left;
            oLabel.Margin = new Padding(3, 6, 3, 0);
            oControl.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            oPanel.Controls.Add(oLabel, 0, oPanel.RowCount - 1);
            oPanel.Controls.Add(oControl, 1, oPanel.RowCount - 1);
        }

        /// <summary>rev.17: наполнить TreeView узлами (рекурсивно; Text —
        /// отображение, Tag — StripTreeNode) и раскрыть все уровни.</summary>
        private static void FillTree(TreeView oTree, List<StripTreeNode> lstNodes)
        {
            oTree.BeginUpdate();
            oTree.Nodes.Clear();
            if (lstNodes != null)
            {
                foreach (StripTreeNode oNode in lstNodes)
                    AddTreeNode(oTree.Nodes, oNode);
            }
            oTree.EndUpdate();
            oTree.ExpandAll();
        }

        private static void AddTreeNode(TreeNodeCollection oParent,
            StripTreeNode oData)
        {
            if (oData == null) return;
            TreeNode oNode = new TreeNode(oData.Text == null ? "" : oData.Text);
            oNode.Tag = oData;
            oParent.Add(oNode);
            if (oData.Children == null) return;
            foreach (StripTreeNode oChild in oData.Children)
                AddTreeNode(oNode.Nodes, oChild);
        }

        /// <summary>rev.17: выбираемы только листья (FullName непусто); выбор
        /// промежуточного узла отменяется.</summary>
        private void TreeStripBeforeSelect(object oSender,
            TreeViewCancelEventArgs oArgs)
        {
            if (oArgs == null) return;
            StripTreeNode oData = oArgs.Node == null
                ? null : oArgs.Node.Tag as StripTreeNode;
            if (oData == null || string.IsNullOrEmpty(oData.FullName))
                oArgs.Cancel = true;
        }

        /// <summary>rev.17: заполнить 6 ComboBox профилей. Для слота — вариант
        /// (VariantFor) и список профилей этого варианта из карты (нет
        /// ключа/null → пусто и выключено). Отображение — SchemeName (A2454),
        /// элемент — сам EmcSchemeInfo (у ComboBox нет per-item Tag; SelectedItem
        /// и есть объект); предвыбор — из _oEffective.</summary>
        private void FillEmcCombos(
            Dictionary<int, List<EmcSchemeInfo>> dicProfilesByVariant)
        {
            for (int nSlot = 0; nSlot < 6; nSlot++)
            {
                BpProfileSlot eSlot = (BpProfileSlot)nSlot;
                ComboBox oCombo = ComboFor(eSlot);
                if (oCombo == null) continue;
                oCombo.DropDownStyle = ComboBoxStyle.DropDownList;
                oCombo.DisplayMember = "SchemeName";
                int nVariant = EmcProfileCatalog.VariantFor(eSlot);
                List<EmcSchemeInfo> lstProfiles = null;
                if (dicProfilesByVariant != null &&
                    dicProfilesByVariant.ContainsKey(nVariant))
                    lstProfiles = dicProfilesByVariant[nVariant];
                if (lstProfiles != null)
                {
                    foreach (EmcSchemeInfo oInfo in lstProfiles)
                    {
                        if (oInfo != null) oCombo.Items.Add(oInfo);
                    }
                }
                if (oCombo.Items.Count == 0)
                {
                    oCombo.Enabled = false;
                    continue;
                }
                PreselectEmc(oCombo, SettingsValueOf(eSlot));
            }
        }

        /// <summary>ComboBox слота (null — неизвестный слот).</summary>
        private ComboBox ComboFor(BpProfileSlot eSlot)
        {
            switch (eSlot)
            {
                case BpProfileSlot.StripH: return _cboEmcStripH;
                case BpProfileSlot.StripV: return _cboEmcStripV;
                case BpProfileSlot.DeviceH: return _cboEmcDeviceH;
                case BpProfileSlot.DeviceV: return _cboEmcDeviceV;
                case BpProfileSlot.LinkH: return _cboEmcLinkH;
                case BpProfileSlot.LinkV: return _cboEmcLinkV;
                default: return null;
            }
        }

        /// <summary>Строка настройки слота («file|scheme» или null).</summary>
        private string SettingsValueOf(BpProfileSlot eSlot)
        {
            switch (eSlot)
            {
                case BpProfileSlot.StripH: return _oEffective.EmcStripH;
                case BpProfileSlot.StripV: return _oEffective.EmcStripV;
                case BpProfileSlot.DeviceH: return _oEffective.EmcDeviceH;
                case BpProfileSlot.DeviceV: return _oEffective.EmcDeviceV;
                case BpProfileSlot.LinkH: return _oEffective.EmcLinkH;
                case BpProfileSlot.LinkV: return _oEffective.EmcLinkV;
                default: return null;
            }
        }

        /// <summary>rev.17: предвыбор из сохранённого «file|scheme» — точным
        /// совпадением File и SchemeName (значение писано из того же каталога,
        /// поэтому регистр совпадает); не найдено/не декодировано — список
        /// остаётся без предвыбора.</summary>
        private static void PreselectEmc(ComboBox oCombo, string strSetting)
        {
            string strFile;
            string strScheme;
            if (!EmcProfileCatalog.TryDecodeSelection(strSetting,
                out strFile, out strScheme)) return;
            for (int i = 0; i < oCombo.Items.Count; i++)
            {
                EmcSchemeInfo oInfo = oCombo.Items[i] as EmcSchemeInfo;
                if (oInfo == null) continue;
                if (oInfo.File == strFile && oInfo.SchemeName == strScheme)
                {
                    oCombo.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>rev.17: дерево из полных имён (совместимый конструктор):
        /// StripNodeInput только с FullName — четыре свойства структуры пусты,
        /// поэтому StripStructureTree уходит в разбор FullName (ParseLevels).</summary>
        private static List<StripTreeNode> BuildTreeFromNames(
            List<string> lstStripNames)
        {
            List<StripNodeInput> lstInputs = new List<StripNodeInput>();
            if (lstStripNames != null)
            {
                foreach (string strName in lstStripNames)
                {
                    StripNodeInput oIn = new StripNodeInput();
                    oIn.FullName = strName;
                    lstInputs.Add(oIn);
                }
            }
            return StripStructureTree.Build(lstInputs);
        }
    }
}

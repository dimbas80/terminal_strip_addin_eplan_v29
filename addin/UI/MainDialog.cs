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
    /// </summary>
    public class MainDialog : Form
    {
        private readonly ComboBox _cboStrip = new ComboBox();
        private readonly ComboBox _cboForm = new ComboBox();
        private readonly ComboBox _cboOrientation = new ComboBox();
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

        /// <summary>Диалог: списки (null — пустые), предвыбор из настроек
        /// (null — дефолты из AddInConfiguration). oProject (rev.13.1, H-4) —
        /// текущий проект для браузера символов; null — кнопка выключена.</summary>
        public MainDialog(List<string> lstStripNames, List<string> lstFormNames,
            AddInSettings oSettings, Project oProject = null,
            DiagnosticLogger oLogger = null)
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
            ClientSize = new Size(480, 352);

            // (1) Клеммник: полные имена, DropDownList.
            _cboStrip.DropDownStyle = ComboBoxStyle.DropDownList;
            FillCombo(_cboStrip, lstStripNames);
            SelectExact(_cboStrip, oEffective.TargetStrip);

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
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            AddRow(root, LabelOf("Клеммник:"), false);
            AddRow(root, _cboStrip, true);
            AddRow(root, LabelOf("Форма отчёта:"), false);
            AddRow(root, _cboForm, true);
            AddRow(root, LabelOf("Ориентация:"), false);
            AddRow(root, _cboOrientation, true);

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

        /// <summary>Подписи блока «Символ кабеля» из _oEffective — формат прежний
        /// (контракт H-2: «биб/имя (вариант H/V h/v)» + отдельные подписи слотов).</summary>
        private void UpdateSymbolLabels()
        {
            _lblSymbol.Text = _oEffective.SymbolLibrary + "/" + _oEffective.SymbolName +
                " (вариант H/V " + _oEffective.VariantH.ToString() + "/" +
                _oEffective.VariantV.ToString() + ")";
            _lblVariantH.Text = "вариант H: " + _oEffective.VariantH.ToString();
            _lblVariantV.Text = "вариант V: " + _oEffective.VariantV.ToString();
        }

        /// <summary>Полное имя выбранного клеммника (null — не выбран).</summary>
        public string SelectedStripName
        {
            get { return TextOf(_cboStrip); }
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
    }
}

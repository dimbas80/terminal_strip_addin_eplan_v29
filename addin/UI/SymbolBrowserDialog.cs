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
    /// <summary>Браузер символов кабеля v4 (Этап 8, H-4b v4, rev.14.2 — замечания
    /// 1–7 прогона rev.14.1; спека category_and_prewiev.md; rev.14.3 — строгий
    /// три-состояний клеток превью (bVariantsKnown + SymbolCatalog.IsCellEnabled),
    /// OK-гейты слотов H/V, пробы [SYMFDMAP-KEY]/[FD-BASE-SUM], дедуп двойного
    /// перечисления библиотеки в ctor и сдвоенной сетки предвыборки):
    /// выбор библиотеки +
    /// ДЕРЕВО «Trade → Area → Категория → Группа → Определение функции → символ»
    /// (5 уровней по FunctionDefinition) + ПРЕВЬЮ-СЕТКА ФИКСИРОВАННО 8 клеток A–H
    /// (VariantNr 0..7; отсутствующие — disabled, не кликабельны; клик = визуальное
    /// выделение клетки, слоты H/V комбо — отдельно). Программный layout без
    /// designer (паттерн MainDialog: TableLayoutPanel, хелперы статическими
    /// методами — локальных функций в C#5 нет). Диалог ТОЛЬКО ЧИТАЕТ.
    /// Выход: свойства Library/SymbolName/VariantH/VariantV (контракт с MainDialog
    /// НЕ менялся: ctor(Project, library, name, variantH, variantV, logger), значения
    /// НЕ триммингуются — урок хвостового пробела п.33; Variant* — числа, как
    /// rev.14.0). «ОК» валиден только при непустых Library/SymbolName И принадлежности
    /// перечислению; «Отмена»/крестик — ничего не возвращается.
    /// === Перечисление (RefreshSymbols, rev.14.2; RC-1: замечания 1+2+4 = один
    /// корень — ранняя цепочка A пишла ТОЛЬКО имена) ===
    /// Новый порядок цепочек ПО ИНФОРМАТИВНОСТИ:
    /// - A2 (первичная): reflection-проба «Symbols» у DataModel SymbolLibrary
    ///   (рантайм-доказано rev.14.1) → элементы as MasterData.Symbol; для каждого:
    ///   варианты — ТИПИРОВАННО Symbol.Variants : SymbolVariant[] (KB страницы
    ///   Symbol~Variants; VariantNr + живые объекты для CreateDisplayList), FD —
    ///   обратный словарь _dctFdBySymbol (ключ = вариант.SymbolLibraryName +
    ///   "\u0001" + вариант.SymbolName; fallback: SYMB_MAINFUNCTION #16018 через
    ///   reflection-пробу Properties на Symbol), описание — SYMB_DESC #16011 той же
    ///   пробой; НИ ОДНОГО Symbol-объекта — отказ A2;
    /// - B (fallback): MDSymbolLibrary.Symbols (KB rev.13.1) — FD #16018 →
    ///   _dctFdById, число вариантов MDSymbol.Variants, SYMB_DESC;
    /// - C (последняя): names-only («Symbols» → строки / «Names») — деградация:
    ///   клетки отключены (варианты неизвестны), категории по префиксу, FunInfo
    ///   без описаний.
    /// Отказ всех — прежний статус-текст ошибки. R6: на УСПЕХЕ статус НЕ печатается
    /// (пустой); статусы ошибок/деградации сохранены.
    /// === FD-словари (BuildFdDictionary) ===
    /// Один раз за жизнь диалога: Project.FunctionDefinitionLibrary.FunctionDefinitions,
    /// (а) _dctFdById (Id → FdInfo; первый побеждает — Id НЕ уникален глобально:
    /// 1305 FD → 40 Id, дамп [FD] rev.14.1); (б) НОВЫЙ _dctFdBySymbol
    /// (ключ = lib + '\u0001' + sym → FdInfo) из FD.BaseSymbol → SymbolLibraryName/
    /// SymbolName (KB members; рантайм НЕ подтверждён — try/catch + дамп [FD-BASE]
    /// первых 10). Поля уровней и Description локализуются SymbolCatalog.
    /// LocalizeMultiLang (формат блоба «de_DE@…;en_US@…;ru_RU@…» доказан дампом [FD];
    /// дамп [FD] первых 10 сохранён + desc).
    /// === Состояние выбора (R5, замечание 3) ===
    /// Поле «Имя выбранного символа» (_txtSymbol) УДАЛЕНО из layout: имя — private
    /// _strSelectedSymbol (SymbolName → оно же); AfterSelect листа применяет новый
    /// выбор (карточка по SymbolEntry из FD.Name/Desc, превью по записи); предвыборка
    /// — программная (FindLeaf → разворот+выделение; не найден — карточка по имени +
    /// превью по каскаду); OK-валидация — принадлежность перечислению;
    /// TxtSymbolOnTextChanged удалён совсем.
    /// === Дерево (RebuildTree, RC-3) ===
    /// Рендер SymbolCatalogNode рекурсией: Kind=Symbol — лист (Tag=имя), остальные
    /// ветки БЕЗ Tag с текстом «Name (N)» (число показанных листьев; карты чистых
    /// имён больше НЕ нужны — карточка берёт FD.Name из SymbolEntry по имени);
    /// поиск фильтрует листья (substring OrdinalIgnoreCase), пустые ветки скрыты;
    /// корень — библиотека; 5 уровней FD-пути (Trade → Area → Категория → Группа →
    /// Определение функции), unmapped — бакет «Без классификации» последним
    /// (SymbolCatalog, addin/UI/SymbolCatalog.cs).
    /// === Превью-сетка (R3+спека, RC-2: замечание 2) ===
    /// ВСЕГДА 8 клеток (4×2): внешняя Panel + ВНУТРЕННЯЯ панель рисования (Dock=Fill,
    /// добавлена ПЕРВОЙ) + Label буквы (Dock=Bottom 18, добавлена ВТОРОЙ — docking
    /// обрабатывается в обратном порядке, метка НЕ накрывает область рисования;
    /// Paint внутренней панели → DrawDisplayList(oArgs, inner.ClientRectangle);
    /// ResizeRedraw=true). Enabled — фон чёрный (R3); disabled — серый фон, серая
    /// буква, клик игнор. Клик по enabled = выделение (золотая рамка + буква золотая),
    /// слоты H/V НЕ трогаются; перерисовка выделения — Invalidate клеток. Display
    /// list клетки — каскад: живой SymbolVariant (A2) → new Symbol+new SymbolVariant
    /// (типизированная KB-связка) → by-strings (CreateDisplayList(String,String,
    /// Int32,Project), строка с именем библ и «»; рев.14.0, HE_Display), у каждой
    /// клетки СВОЙ DrawingService (один сервис = один display list); Reset перед
    /// каждым созданием + SetDefaultViewport (гипотеза центра R4 rev.14.1);
    /// деградация chain C → все клетки disabled (варианты неизвестны); Dispose всех сервисов — OnFormClosing
    /// и каждая пересборка.
    /// === Строки статуса и пробы ===
    /// Пробы [FD]/[FD-BASE]/[FD-BASE-SUM]/[SYMFDMAP]/[SYMFDMAP-KEY]/
    /// [SYMFDMAP-DESC]/[DSPROBE] — дублированный канал Console + DiagnosticLogger
    /// (урок прогона rev.14.0: только-Console был невидим в terminal_strip_addin.log!).
    /// R6: статус на успехе пустой.
    /// KB-факты API 2.9 (www.eplan.help):
    /// - Project.SymbolLibraries — «public SymbolLibrary[] SymbolLibraries { get; }»
    ///   — KB-доказан (rev.13.1);
    /// - MasterData.Symbol.Variants : SymbolVariant[]; SymbolVariant.SymbolLibraryName /
    ///   SymbolName («unique for library») / VariantNr — KB + рантайм (rev.13.8);
    /// - MDSymbolLibrary.Symbols — «public MDSymbol[] Symbols { get; }»; MD.Symbol.ctor
    ///   (path) и (Project, path) — конструктор НЕ доказан — Activator-проба (fix-1);
    /// - MDSymbolPropertyList ctor (MDSymbol) + SYMB_MAINFUNCTION (#16018, Int64),
    ///   SYMB_DESC (#16011) — KB;
    /// - DataModel.MasterData.SymbolPropertyList (SYMB_MAINFUNCTION/SYMB_DESC) —
    ///   KB-доказаны, НО как получить SymbolPropertyList от Symbol НЕ доказано →
    ///   ТОЛЬКО reflection-проба (без компиляционных рисков);
    /// - Project.FunctionDefinitionLibrary.FunctionDefinitions : FunctionDefinition[]
    ///   — KB; члены FD Id/Name/CategoryName/CategoryRegion/GroupName/MainGroup/
    ///   Description/BaseSymbol — KB members page; Id/кат/группа/имя рантайм-
    ///   подтверждены дампом [FD] rev.14.1; Description/BaseSymbol — рантайм ещё НЕ
    ///   подтвержден (try/catch + [FD-BASE] первых 10);
    /// - MasterData.Symbol(SymbolLibrary, String) + SymbolVariant(Symbol, Int32) —
    ///   конструкторы KB (вход display list клетек B/C-цепочек);
    /// - HEServices.DrawingService: ctor(); CreateDisplayList (SymbolVariant[, bool]);
    ///   CreateDisplayList (String,String,Int32,Project) — KB (пример HE_Display);
    ///   DrawDisplayList (PaintEventArgs, Rectangle) — «fit keeping aspect ratio»;
    ///   SetDefaultViewport — «Adjusts viewport to the bounding box»; Reset/Dispose.
    /// НЕ доказано KB (reflection/Activator-пробы; «одна гипотеза за прогон»):
    /// перечислитель «Symbols» у DataModel SymbolLibrary (элементы — рантайм-факт
    /// rev.14.1), Properties/Activator SymbolPropertyList(SА Symbol), поверхность
    /// PropertyValue (ExtractLongViaReflection), перегрузка CreateDisplayList
    /// (String,String,RepresentationType,Int32,Project) — сознательно НЕ пробуется.
    /// Отказы проб — Console-дамп + честные статусы старых ревизий.</summary>
    public class SymbolBrowserDialog : Form
    {
        private readonly Project _oProject;
        private readonly TextBox _txtLibrary = new TextBox();
        private readonly ListBox _lstLibraries = new ListBox();
        private readonly TextBox _txtSearch = new TextBox();
        private readonly TreeView _treeSymbols = new TreeView();

        // rev.14.1 (R6): слоты H/V — ComboBox букв A-H (всегда 8 пунктов); наружу —
        // числа (SelectedIndex = индекс варианта), пайплайн/настройки не тронуты.
        // rev.14.2: выделение клетки превью слоты сознательно НЕ трогает (спека §5).
        private readonly ComboBox _cbVariantH = new ComboBox();
        private readonly ComboBox _cbVariantV = new ComboBox();

        private readonly Label _lblCount = new Label();
        private readonly Label _lblStatus = new Label();

        // H-4b: карточка превью (справа) — имя/категория/описание символа (R7).
        private readonly Label _lblPreviewName = new Label();
        private readonly Label _lblPreviewCategory = new Label();
        private readonly Label _lblPreviewDescription = new Label();

        // Объекты библиотек под строками списка (параллельны _lstLibraries.Items).
        private readonly List<SymbolLibrary> _lstLibraryObjects = new List<SymbolLibrary>();

        // rev.14.2: записи символов (RC-1): имя + варианты (номера и ЖИВЫЕ объекты
        // SymbolVariant из A2) + резолв FD (BaseSymbol-обратный словарь → fallback
        // #16018 → MDSymbol #16018 → цепочка C без FD) + описание SYMB_DESC.
        // rev.14.3: флаг bVariantsKnown — строгий три-состояний клеток (п.93).
        private sealed class SymbolEntry
        {
            public string Name;
            public int nVariantCount;                          // -1 — неизвестно (деградация)
            /// <summary>true — Symbol.Variants прочитан успешно (даже при 0
            /// вариантов); false — сбой чтения / chain C / запись вне списка
            /// (строгий три-состояний клеток: false ⇒ все клетки disabled).</summary>
            public bool bVariantsKnown;
            public readonly List<int> lstVariantNrs = new List<int>();
            public readonly List<object> lstVariantObjects = new List<object>();
            public FdInfo Fd;                                  // null — не сопоставился
            public string strFdPath;                           // «BaseSymbol»|«16018»|«MDSymbol»|null
            public string Description;
        }
        private List<SymbolEntry> _lstEntries = new List<SymbolEntry>();

        // rev.14.2 (R5): выбор символа — состояние БЕЗ текстового поля («Имя
        // выбранного символа» удалено из layout; имя задаётся ТОЛЬКО выбором
        // листа дерева/программной предвыборкой).
        private string _strSelectedSymbol = string.Empty;

        // rev.14.3 Task 5 (дедуп, фикс ревью C1): факт прогона rev.14.2 — двойной
        // обход библиотеки (2×796). Рантайм-тайминг подписки: SelectExact в
        // LoadLibraries стреляет событием ДО подписки (подписчик не видит), а
        // реальный вторый обход — отложенная доставка события на ShowDialog.
        // Дедуп по ИДЕНТИЧНОСТИ перечисленной библиотеки: обработчик пропускает
        // повтор, если перечислена та же SymbolLibrary; ctor после явного
        // RefreshSymbols выставляет идентичность — отложенный обработчик увидит
        // ту же библиотеку и пропустит. Смена на ДРУГУЮ библиотеку (другой
        // объект) — перечисляет; null при начале. Ровно одно перечисление.
        private SymbolLibrary _oEnumeratedLib;

        // rev.14.2: словари FD (проект; строятся ОДИН раз за жизнь диалога):
        // (а) Id → FdInfo — fallback-бакетизация (Id НЕ уникален глобально: 1305 FD
        //     → 40 Id, дамп [FD] rev.14.1; first wins);
        // (б) «lib\u0001sym» → FdInfo — ПЕРВИЧНАЯ связь символ → FD через FD.BaseSymbol.
        private Dictionary<long, FdInfo> _dctFdById;
        private Dictionary<string, FdInfo> _dctFdBySymbol;
        private int _nFdMapped;
        private int _nFdViaBase;
        private int _nFdVia16018;
        private string _strFdPath = "нет";   // путь сопоставления [SYMFDMAP] текущего перечисления

        // Превью-сетка (RC-2 fix): ВСЕГДА 8 фиксированных клеток (4×2); параллельные
        // списки: внешние панели, ВНУТРЕННИЕ панели рисования, буквы, готовность
        // display list, доступность варианта (disabled — серые, не кликаются).
        private readonly TableLayoutPanel _tlpPreviewGrid = new TableLayoutPanel();
        private readonly List<Panel> _lstPreviewCells = new List<Panel>();
        private readonly List<Panel> _lstCellDrawPanels = new List<Panel>();
        private readonly List<Label> _lstCellLetters = new List<Label>();
        private readonly List<DrawingService> _lstPreviewServices = new List<DrawingService>();
        private readonly List<bool> _lstCellReady = new List<bool>();
        // Ревью rev.14.2 (Minor-шум): успех-строка [DSPROBE] пути рендера — ОДИН раз
        // на пересборку сетки (8 клеток × 3 пути = до 24 строк на выделение).
        private bool _bPathLogged;
        private readonly List<bool> _lstCellEnabled = new List<bool>();
        private int _nSelectedCell = -1;   // выделенная клетка (клик = выбор варианта)
        // rev.14.3 Task 5 (гейт ApplySelection): имя, для которого превью-сетка
        // УЖЕ построена (ставится в RefreshPreviewGridByEntry после
        // BuildPreviewGrid — в т.ч. при полном отказе рендера: клетки уже стоят);
        // сброс — при разборке сетки пустыми строками и при смене библиотеки
        // (RefreshSymbols). Повторный AfterSelect того же имени сетку не пересобирает.
        private string _strPreviewGridKey = string.Empty;

        // Пробы [FD]/[FD-BASE]/[SYMFDMAP]/[SYMFDMAP-DESC]/[DSPROBE] — Console +
        // DiagnosticLogger (урок прогона rev.14.0: только-Console был невидим).
        private readonly DiagnosticLogger _oLogger;
        private bool _bFdDumped;           // FD-словари — один раз за жизнь диалога
        private int _nPropsFormLogged;     // [DSPROBE] «какая форма SymbolPropertyList сработала» — кап 5

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
            // R3 (замечание 2): окно 840×620 — 8 клеток не помещались достойно.
            ClientSize = new Size(1120, 800);
            // Страховка маленьких мониторов (ревью rev.14.2): фиксированный диалог
            // не должен вылезать за рабочий стол — прижимаем к WorkingArea.
            Rectangle oWork = Screen.PrimaryScreen.WorkingArea;
            if (ClientSize.Height > oWork.Height - 40)
                ClientSize = new Size(ClientSize.Width, oWork.Height - 40);
            if (ClientSize.Width > oWork.Width - 40)
                ClientSize = new Size(oWork.Width - 40, ClientSize.Height);

            // Слоты H/V — ComboBox букв A-H (8 пунктов всегда); предвыбор из
            // настроек: число → SelectedIndex (вне 0..7 — прижать к 0 + INFO).
            FillVariantCombo(_cbVariantH);
            FillVariantCombo(_cbVariantV);
            SetVariantIndex(_cbVariantH, nVariantH);
            SetVariantIndex(_cbVariantV, nVariantV);

            // Карточка превью — прочерк до первого выбора.
            _lblPreviewName.Text = "Имя: —";
            _lblPreviewCategory.Text = "Категория: —";
            _lblPreviewDescription.Text = "Описание: —";

            _txtLibrary.Text = strLibrary ?? string.Empty;

            // (1) Библиотеки: Project.SymbolLibraries (KB-цитата в шапке).
            LoadLibraries(strLibrary);

            // (2) Реакции (диалог только читает; программных мутаций нет).
            // rev.14.2: поле «Имя выбранного символа» удалено (R5) — обработчик
            // TxtSymbolOnTextChanged и его подписка больше НЕ существуют.
            _lstLibraries.SelectedIndexChanged += LstLibrariesOnSelectedIndexChanged;
            _treeSymbols.AfterSelect += TreeSymbolsOnAfterSelect;
            _treeSymbols.NodeMouseDoubleClick += TreeSymbolsOnNodeMouseDoubleClick;
            _txtSearch.TextChanged += delegate { RebuildTree(); };

            // (3) Символы: цепочки перечисления для предвыбранной библиотеки,
            // затем программная предвыборка имени (FindLeaf/карточка/превью).
            // rev.14.3 Task 5 (дедуп, фикс ревью C1): явный вызов при предвыборе —
            // первичный перечислитель (событие на SelectExact теряется до подписки);
            // после него фиксируем идентичность — отложенный обработчик (Handle
            // создался к ShowDialog) увидит ту же библиотеку и пропустит.
            // Нет предвыбора (SelectedIndex<0) — не перечисляем (список пуст,
            // прежняя семантика).
            if (_lstLibraries.SelectedIndex >= 0)
            {
                RefreshSymbols();
                int iSel = _lstLibraries.SelectedIndex;
                _oEnumeratedLib = (iSel >= 0 && iSel < _lstLibraryObjects.Count)
                    ? _lstLibraryObjects[iSel] : null;
            }
            SelectSymbolPrechoice(strName);

            // (4) Кнопки: «ОК» — валидация в Click (DialogResult=OK ставится ТОЛЬКО
            // при непустых Library/Name, принадлежащих перечислению); «Отмена» —
            // DialogResult.Cancel (CancelButton).
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

        /// <summary>Проба [SYMFDMAP/[SYMFDMAP-DESC]/[FD…]] с капом шума: после первых
        /// 10 символов строки НЕ эмитятся вовсе (шум ~800 символов; итог — сводка
        /// [SYMFDMAP] в RefreshSymbols). Комментарий выправлен ревью rev.14.2:
        /// после капа каналов НЕТ (ни Console, ни логгер) — сводка единственный итог.</summary>
        private void Probe(string strText, int iOrdinal)
        {
            if (iOrdinal < 10) ProbeInfo(strText);
        }

        /// <summary>Имя библиотеки (как выбрано/введено, БЕЗ trim).</summary>
        public string Library
        {
            get { return _txtLibrary.Text; }
        }

        /// <summary>Имя символа (rev.14.2 R5: состояние выбора — поле «Имя выбранного
        /// символа» удалено, имя задаётся ТОЛЬКО выбором листа дерева/предвыборкой).
        /// Имя свойства SymbolName (не Name): CS0108-тень Form-члена — урок п.30.</summary>
        public string SymbolName
        {
            get { return _strSelectedSymbol; }
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

        /// <summary>Смена библиотеки (пользователь или отложенная доставка события
        /// после ctor): путь в поле → RefreshSymbols. rev.14.3 Task 5 (дедуп,
        /// фикс ревью C1): та же SymbolLibrary, что уже перечислена (ReferenceEquals),
        /// — пропуск (перечисление актуально); другая/null — перечисляет.</summary>
        private void LstLibrariesOnSelectedIndexChanged(object oSender, EventArgs oArgs)
        {
            int iIndex = _lstLibraries.SelectedIndex;
            SymbolLibrary oLib = (iIndex >= 0 && iIndex < _lstLibraryObjects.Count)
                ? _lstLibraryObjects[iIndex] : null;
            if (ReferenceEquals(oLib, _oEnumeratedLib)) return;
            _oEnumeratedLib = oLib;
            if (iIndex >= 0 && iIndex < _lstLibraryObjects.Count)
                _txtLibrary.Text = _lstLibraryObjects[iIndex] == null
                    ? string.Empty
                    : ResolveDisplayPath(_lstLibraryObjects[iIndex]);
            RefreshSymbols();   // stale-выбор/карточка гасятся ВНУТРИ RefreshSymbols
        }

        /// <summary>Выбор ЛИСТА дерева (символа) — состояние R5 + карточка/превью.
        /// Ветки (Trade/Area/Category/Group/Fd/бакеты) — Tag нет, игнор.</summary>
        private void TreeSymbolsOnAfterSelect(object oSender, TreeViewEventArgs oArgs)
        {
            TreeNode oNode = oArgs == null ? null : oArgs.Node;
            if (oNode == null || oNode.Tag == null) return;
            ApplySelection((string)oNode.Tag);
        }

        /// <summary>Применение выбранного имени (rev.14.2 R5): состояние + карточка
        /// (из SymbolEntry по имени) + превью-сетка по записи (каскад). rev.14.3
        /// Task 5: гейт «то же имя И сетка уже построена для него» — повторный
        /// AfterSelect (HighlightCurrentSymbol/предвыбор уже ставили SelectedNode)
        /// сетку заново не строит; при смене символа ключ обновится в
        /// RefreshPreviewGridByEntry.</summary>
        private void ApplySelection(string strName)
        {
            if (string.IsNullOrEmpty(strName)) return;
            if (_strSelectedSymbol == strName && _strPreviewGridKey == strName)
                return;   // гейт: карточка+сетка уже применены к этому имени
            _strSelectedSymbol = strName;
            UpdateCardByEntry(strName);
            RefreshPreviewGridByEntry(strName);
        }

        /// <summary>Двойной клик по листу = ОК (наше «создать» — после валидации,
        /// как кнопка). Клик по ветке — игнор.</summary>
        private void TreeSymbolsOnNodeMouseDoubleClick(object oSender,
            TreeNodeMouseClickEventArgs oArgs)
        {
            if (oArgs == null || oArgs.Node == null || oArgs.Node.Tag == null) return;
            BtnOkOnClick(oArgs.Node, oArgs);
        }

        private void BtnOkOnClick(object oSender, EventArgs oArgs)
        {
            if (string.IsNullOrEmpty(Library) || string.IsNullOrEmpty(SymbolName))
            {
                MessageBox.Show(this, "Укажите библиотеку и выберите символ в дереве.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // Перечисление недоступно — выбор невозможен (пустое перечисление при
            // старой предвыборке не должно пропустить пару «библ/имя, которой нет» —
            // Important ревью rev.14.1; проверка сохранена, только источник —
            // _lstEntries).
            if (_lstEntries.Count == 0)
            {
                MessageBox.Show(this,
                    "Перечисление символов недоступно для этой библиотеки — " +
                    "выбор невозможен (подробности в логе [DSPROBE]).",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (FindEntryIndex(SymbolName) < 0)
            {
                MessageBox.Show(this,
                    "Выбранный символ отсутствует в списке текущей библиотеки — " +
                    "выберите символ из дерева.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // rev.14.3 (п.93): варианты неизвестны (сбой чтения Symbol.Variants /
            // chain C / names-only) — OK блокирован: выбор слота по клетке не даст
            // реального варианта, падение на уровне пайплайна недопустимо.
            SymbolEntry oEntry = FindEntryObj(SymbolName);
            if (oEntry != null && !oEntry.bVariantsKnown)
            {
                MessageBox.Show(this,
                    "Варианты выбранного символа неизвестны (перечислены только " +
                    "имена) — выбор варианта невозможен.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // rev.14.3 (п.93): слоты должны указывать на РЕАЛЬНЫЙ вариант записи
            // (клетка i ↔ VariantNr == i, тот же индекс 0..7) — иначе пайплайн
            // получит слот без варианта (гейт строится на ==).
            if (oEntry != null && (!oEntry.lstVariantNrs.Contains(VariantH) ||
                !oEntry.lstVariantNrs.Contains(VariantV)))
            {
                MessageBox.Show(this,
                    "У символа нет варианта слота H/V (" +
                    VariantH.ToString(CultureInfo.InvariantCulture) +
                    "=" + ((char)('A' + VariantH)).ToString(CultureInfo.InvariantCulture) +
                    " / " +
                    VariantV.ToString(CultureInfo.InvariantCulture) +
                    "=" + ((char)('A' + VariantV)).ToString(CultureInfo.InvariantCulture) +
                    ") — выберите слот реального варианта.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }

        /// <summary>Крестик/Alt+F4 (CloseReason.UserClosing) — это «отмена»: гасим
        /// залипший DialogResult.OK (паттерн MainDialog). Валидации здесь нет.
        /// rev.14.2: здесь же — Dispose ВСЕХ DrawingService превью-сетки (владение
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
        /// Сбой перечисления — статусная строка, пустой список.</summary>
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

        /// <summary>Цепочки перечисления символов выбранной библиотеки — В ПОРЯДКЕ
        /// ИНФОРМАТИВНОСТИ (rev.14.2, RC-1: цепочка A rev.14.1 резко выигрывала
        /// и рано выходила, оставляя fdId=null/варианты -1/описание null):
        /// A2) DataModel SymbolLibrary «Symbols» → MasterData.Symbol (INTERVAL:
        ///     варианты + FD + SYMB_DESC — ПОЛНАЯ запись);
        /// B)  MDSymbolLibrary.Symbols (KB; FD #16018 → dctFdById, варианты числом,
        ///     SYMB_DESC);
        /// C)  names-only («Symbols» → строки / «Names») — деградация: клетки
        ///    отключены (варианты неизвестны), категории-префиксы.
        /// R6: на успехе статус НЕ печатается (пустой); деградация C и отказ всех —
        /// честные статусы. [SYMFDMAP]-сводка после каждой успешной цепочки.
        /// Никаких мутаций (диалог только читает).</summary>
        private void RefreshSymbols()
        {
            _lstEntries = new List<SymbolEntry>();
            ResetFdMatchCounters();
            DisposePreviewGrid();
            BuildFdDictionary();   // [FD]/[FD-BASE] — один раз за жизнь диалога
            _treeSymbols.Nodes.Clear();
            _lblCount.Text = string.Empty;
            // Fix (rev.14.1 ревью, Important) — сохранено: смена библиотеки — старое
            // имя не соответствует новому списку; stale-выбор гасим (OK не вернул бы
            // пару библ/имя, которой нет); карточка — прочерки.
            _strSelectedSymbol = string.Empty;
            _strPreviewGridKey = string.Empty;   // rev.14.3: ключ сетки — за выбором
            ClearCard();

            int iIndex = _lstLibraries.SelectedIndex;
            if (iIndex < 0 || iIndex >= _lstLibraryObjects.Count)
            {
                SetStatus("Библиотека не выбрана из списка — список символов пуст " +
                    "(выбор символа недоступен).");
                return;
            }
            SymbolLibrary oLib = _lstLibraryObjects[iIndex];

            // A2 (первичная): ПОЛНЫЕ записи Symbol (варианты/FD/описание).
            if (TryEnumerateViaDataModelTyped(oLib))
            {
                _strFdPath = _nFdViaBase > 0
                    ? "BaseSymbol"
                    : (_nFdVia16018 > 0 ? "16018" : "нет");
                ProbeFdSummary();
                RebuildTree();
                SetStatus(string.Empty);   // R6: успех — статус пустой
                return;
            }

            // Цепочка B: MDSymbolLibrary.Symbols. FIX-1 (review 09fb80d Important):
            // путь библиотеки — СВОИ кандидаты (EnumeratePathCandidates), НЕ
            // отображаемое имя ResolveDisplayPath (то остаётся возвращаемым Library).
            if (TryEnumerateViaMasterData(oLib))
            {
                _strFdPath = "MDSymbol";
                ProbeFdSummary();
                RebuildTree();
                SetStatus(string.Empty);   // R6: успех — статус пустой
                return;
            }

            // Цепочка C: names-only — деградация (клетки отключены: варианты
            // неизвестны, префиксные бакеты).
            if (TryEnumerateNamesOnly(oLib))
            {
                _strFdPath = "нет";
                ProbeFdSummary();
                RebuildTree();
                SetStatus("Деградация: получены только имена символов (без вариантов, " +
                    "FD и описаний) — категории по префиксу. Варианты неизвестны — " +
                    "клетки превью отключены (выбор невозможен; подробности [DSPROBE]).");
                return;
            }

            SetStatus("Перечисление символов недоступно (все цепочки пробили в отказ) — " +
                "выбор символа невозможен (подробности в [DSPROBE] лога).");
        }

        private void ResetFdMatchCounters()
        {
            _nFdMapped = 0;
            _nFdViaBase = 0;
            _nFdVia16018 = 0;
            _strFdPath = "нет";
        }

        /// <summary>[SYMFDMAP]-сводка текущего перечисления.</summary>
        private void ProbeFdSummary()
        {
            ProbeInfo("[SYMFDMAP] сопоставлено " +
                _nFdMapped.ToString(CultureInfo.InvariantCulture) + " из " +
                _lstEntries.Count.ToString(CultureInfo.InvariantCulture) +
                " символов (путь: " + _strFdPath + ")");
        }

        // CS0618 (ruling контроллера, fix-2): PropertyInfo.GetValue(obj, args[]) —
        // устаревший, но единственный гарантированно доступный в референсе .NET 4
        // способ чтения свойства; newer overloads (GetValue(obj)/GetPropertyValue)
        // на стенде не доказаны — прямое обращение = риск CS1061. Заглушение плотной
        // парой pragma вокруг каждого метода, вызывающего GetValue; поведение не меняется.
#pragma warning disable 618

        /// <summary>Цепочка A2 (первичная, rev.14.2): reflection-доступ к «Symbols»
        /// у DataModel SymbolLibrary (рантайм-доказано rev.14.1), элементы —
        /// MasterData.Symbol; НИ ОДНОГО Symbol-объекта — false (переход к B).
        /// Для каждого символа: имя (ResolveNameViaReflection), варианты —
        /// ТИПИРОВАННО Symbol.Variants : SymbolVariant[] (KB): номера VariantNr
        /// 0..7 (вне — не в сетку A–H) + ЖИВЫЕ объекты вариантов (для
        /// CreateDisplayList), FD — обратный словарь BaseSymbol, fallback #16018
        /// (Properties-проба), описание — SYMB_DESC (та же проба). [DSPROBE]:
        /// какая форма сработала («Symbols → N объектов Symbol»). Только чтение.</summary>
        private bool TryEnumerateViaDataModelTyped(SymbolLibrary oLib)
        {
            try
            {
                PropertyInfo oProp = oLib.GetType().GetProperty("Symbols");
                if (oProp == null)
                {
                    ProbeInfo("[DSPROBE] A2: свойства «Symbols» у DataModel SymbolLibrary нет");
                    return false;
                }
                object oValue = oProp.GetValue(oLib, null);
                System.Array arrItems = oValue as System.Array;
                if (arrItems == null)
                {
                    ProbeInfo("[DSPROBE] A2: «Symbols» не массив (" +
                        (oValue == null ? "null" : oValue.GetType().Name) + ") — переход к B");
                    return false;
                }
                int nSymbols = 0;
                int iOrdinal = 0;
                foreach (object oItem in arrItems)
                {
                    if (oItem == null) continue;
                    // ГЕЙТ A2: элемент должен быть MasterData.Symbol (иначе — не наш
                    // путь: string[] уйдёт в цепочку B/C). Смешанные массивы —
                    // Symbol'ы собираем, посторонние пропускаем.
                    // CS0246-фикс: «MasterData» без квалификатора не резолвится
                    // (using импортирует ТИПЫ, не сам namespace) — «Symbol» дальше
                    // по файлу резолвится в DataModel.MasterData.Symbol тем же using.
                    Symbol oSymbol = oItem as Symbol;
                    if (oSymbol == null) continue;
                    iOrdinal++;
                    string strName = ResolveNameViaReflection(oSymbol);
                    if (string.IsNullOrEmpty(strName)) continue;

                    SymbolEntry oEntry = new SymbolEntry();
                    oEntry.Name = strName;
                    oEntry.nVariantCount = -1;

                    // Варианты: KB Symbol~Variants — SymbolVariant[] (типизированно);
                    // вне 0..7 — в сетку A–H не попадают (16 = contact image).
                    try
                    {
                        SymbolVariant[] arrVars = oSymbol.Variants;
                        if (arrVars != null)
                        {
                            // rev.14.3: чтение Variants УСПЕШНО — варианты известны
                            // (даже при 0 вариантов); строгий три-состояний клеток.
                            oEntry.bVariantsKnown = true;
                            oEntry.nVariantCount = arrVars.Length;
                            foreach (SymbolVariant oVar in arrVars)
                            {
                                if (oVar == null) continue;
                                int nNr = -1;
                                try { nNr = oVar.VariantNr; }
                                catch (Exception oExVariant)
                                {
                                    Probe("[SYMFDMAP] VariantNr «" + strName + "» — " +
                                        oExVariant.GetType().Name + ": " +
                                        oExVariant.Message, iOrdinal - 1);
                                    continue;
                                }
                                if (nNr < 0 || nNr > 7) continue;
                                oEntry.lstVariantNrs.Add(nNr);
                                oEntry.lstVariantObjects.Add(oVar);
                            }
                        }
                    }
                    catch (Exception oEx)
                    {
                        // rev.14.3: сбой чтения Symbol.Variants — варианты
                        // НЕИЗВЕСТНЫ → все клетки disabled (не «только A»).
                        oEntry.bVariantsKnown = false;
                        Probe("[SYMFDMAP] Symbol.Variants «" + strName + "» — " +
                            oEx.GetType().Name + ": " + oEx.Message, iOrdinal - 1);
                    }

                    // FD записи: (1) ПЕРВИЧНО — обратный словарь по lib/sym первого
                    // варианта (KB SymbolVariant.SymbolLibraryName/SymbolName);
                    // (2) fallback — SYMB_MAINFUNCTION #16018 через Properties-пробу.
                    if (oEntry.lstVariantNrs.Count > 0 && oEntry.lstVariantObjects.Count > 0)
                    {
                        object oFirstVariant = oEntry.lstVariantObjects[0];
                        string strVarLib = TryGetStringProperty(oFirstVariant, "SymbolLibraryName");
                        string strVarSym = TryGetStringProperty(oFirstVariant, "SymbolName");
                        // rev.14.3: гипотеза прогона — различить «пустые lib/sym»
                        // и «ключ есть / промах словаря» (0 из 796 при 279 пар).
                        // Проба ДО IsNullOrEmpty-гейта: пустой случай обязан быть
                        // ВИДЕН в логе (иначе ячейка «молчит» четырёхзначно, п.94).
                        if (string.IsNullOrEmpty(strVarLib) ||
                            string.IsNullOrEmpty(strVarSym))
                        {
                            Probe("[SYMFDMAP-KEY] «" + strName + "» вар[0] тип=" +
                                oFirstVariant.GetType().Name + " lib='" +
                                (strVarLib ?? "<null>") + "' sym='" +
                                (strVarSym ?? "<null>") +
                                "' → lookup НЕ выполнялся (пустые lib/sym)",
                                iOrdinal - 1);
                        }
                        if (!string.IsNullOrEmpty(strVarLib) && !string.IsNullOrEmpty(strVarSym))
                        {
                            FdInfo oInfoFd = null;
                            bool bHit = _dctFdBySymbol != null &&
                                _dctFdBySymbol.TryGetValue(
                                    MakeFdSymbolKey(strVarLib, strVarSym), out oInfoFd);
                            Probe("[SYMFDMAP-KEY] «" + strName + "» вар[0] тип=" +
                                oFirstVariant.GetType().Name + " lib='" +
                                (strVarLib ?? "<null>") + "' sym='" +
                                (strVarSym ?? "<null>") + "' → ключ " +
                                (bHit
                                    ? "НАЙДЕН (FD «" + (oInfoFd == null ? "<null>" : oInfoFd.Name) + "»)"
                                    : "нет в словаре"),
                                iOrdinal - 1);
                            if (bHit)
                            {
                                oEntry.Fd = oInfoFd;
                                oEntry.strFdPath = "BaseSymbol";
                                _nFdViaBase++;
                            }
                        }
                    }
                    if (oEntry.Fd == null)
                    {
                        long? nFdId = TryGetFdIdFromSymbolProperties(
                            oSymbol, iOrdinal - 1, strName);
                        if (nFdId.HasValue && _dctFdById != null)
                        {
                            FdInfo oInfoById;
                            if (_dctFdById.TryGetValue(nFdId.Value, out oInfoById))
                            {
                                oEntry.Fd = oInfoById;
                                oEntry.strFdPath = "16018";
                                _nFdVia16018++;
                            }
                        }
                    }

                    // Описание: SYMB_DESC #16011 через Properties-пробу (rev.14.2).
                    oEntry.Description = TryGetSymbolSymbDesc(oSymbol, iOrdinal - 1, strName);

                    _lstEntries.Add(oEntry);
                    if (oEntry.Fd != null) _nFdMapped++;
                    nSymbols++;
                }
                if (nSymbols == 0)
                {
                    // Спека: «если ни одного Symbol-объекта → false» — B перечислит,
                    // C доберёт names-only.
                    ProbeInfo("[DSPROBE] A2: MasterData.Symbol-объектов нет — переход к B");
                    return false;
                }
                ProbeInfo("[DSPROBE] A2: «Symbols» → " +
                    nSymbols.ToString(CultureInfo.InvariantCulture) +
                    " объектов MasterData.Symbol");
                return true;
            }
            catch (Exception oEx)
            {
                ProbeInfo("[DSPROBE] A2 отказ: " + oEx.GetType().Name + ": " + oEx.Message);
                return false;
            }
        }
#pragma warning restore 618

        /// <summary>Цепочка B (fallback): MDSymbolLibrary по кандидатам ПУТИ
        /// библиотеки (конструктор НЕ доказан KB — Activator-проба форм (string) и
        /// (Project, string) для каждого кандидата, fix-1), затем Symbols —
        /// KB-доказанное свойство (прямое типизированное обращение). Имена —
        /// ResolveNameViaReflection; число вариантов — MDSymbol.Variants (KB);
        /// FD-ID (#16018) → _dctFdById (TryGetFdId + [SYMFDMAP]); SYMB_DESC.
        /// Записи: номера вариантов 0..min(count,8)-1 (клетки включены по списку).</summary>
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
                    int nVariantCount = TryGetVariantCount(oMdSym);
                    long? nFdId = TryGetFdId(oMdSym, iOrdinal, strName);
                    string strDesc = TryGetSymbDesc(oMdSym, iOrdinal, strName);

                    SymbolEntry oEntry = new SymbolEntry();
                    oEntry.Name = strName;
                    oEntry.nVariantCount = nVariantCount;
                    // rev.14.3: (п.93) Variants прочитан (=число) → известен;
                    // сбой/отказ чтения (-1) → неизвестен (все клетки disabled).
                    oEntry.bVariantsKnown = (nVariantCount >= 0);
                    int nCells = nVariantCount < 0 ? 0 : Math.Min(nVariantCount, 8);
                    for (int i = 0; i < nCells; i++) oEntry.lstVariantNrs.Add(i);
                    // FD: словарь по Id (16018 — FD-Id; первый FD с этим Id выигрывает:
                    // принцип dctFdById first-wins, рантайм-правило rev.14.1).
                    if (nFdId.HasValue && _dctFdById != null)
                    {
                        FdInfo oInfoFd;
                        if (_dctFdById.TryGetValue(nFdId.Value, out oInfoFd))
                        {
                            oEntry.Fd = oInfoFd;
                            oEntry.strFdPath = "MDSymbol";
                            _nFdMapped++;
                        }
                    }
                    oEntry.Description = strDesc;
                    _lstEntries.Add(oEntry);
                    iOrdinal++;
                }
                return _lstEntries.Count > 0;
            }
            catch (Exception oEx)
            {
                ProbeInfo("SymbolBrowserDialog: MDSymbolLibrary.Symbols — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return false;
            }
        }

        // CS0618 — см. блок выше.
#pragma warning disable 618
        /// <summary>Цепочка C (последняя, names-only): reflection-перебор кандидатов
        /// перечисления у DataModel SymbolLibrary (имена членов НЕ доказаны KB —
        /// только проба). Значение — string[] или Array элементов (имена —
        /// ResolveDisplayPath); записи БЕЗ вариантов/FD/описания (деградация:
        /// клетки отключены (варианты неизвестны), префиксные бакеты SymbolCatalog).
        /// Пустой/отказной результат кандидата — следующий; всё не удалось — false.</summary>
        private bool TryEnumerateNamesOnly(SymbolLibrary oLib)
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
                            AddNamesOnlyEntry(strName);
                        }
                        if (_lstEntries.Count > 0)
                        {
                            ProbeInfo("[DSPROBE] C: «" + strProp + "» → " +
                                _lstEntries.Count.ToString(CultureInfo.InvariantCulture) +
                                " имён (строковый массив) — деградация");
                            return true;
                        }
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
                            AddNamesOnlyEntry(strName);
                        }
                        if (_lstEntries.Count > 0)
                        {
                            ProbeInfo("[DSPROBE] C: «" + strProp + "» → " +
                                _lstEntries.Count.ToString(CultureInfo.InvariantCulture) +
                                " имён — деградация");
                            return true;
                        }
                    }
                }
                catch (Exception oEx)
                {
                    // Проба — не мутация: отказ кандидата — в дамп, далее (урок rev.7).
                    ProbeInfo("SymbolBrowserDialog: проба «" + strProp + "» — " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
            }
            return false;
        }
#pragma warning restore 618

        /// <summary>Деградационная запись (цепочка C): имя, варианты НЕИЗВЕСТНЫ
        /// (nVariantCount=-1, bVariantsKnown=false — все клетки disabled, rev.14.3),
        /// FD=null (префиксные бакеты), описание=null.</summary>
        private void AddNamesOnlyEntry(string strName)
        {
            SymbolEntry oEntry = new SymbolEntry();
            oEntry.Name = strName;
            oEntry.nVariantCount = -1;
            oEntry.bVariantsKnown = false;
            _lstEntries.Add(oEntry);
        }

        /// <summary>FD-ID символа в цепочке A2 — FALLBACK #16018 c DataModel Symbol:
        /// как получить SymbolPropertyList от Symbol НЕ доказано (страниц
        /// Symbol~Properties / SymbolPropertyList~_ctor в базе нет) — ТОЛЬКО
        /// reflection-проба (свойство «Properties» / Activator ctor(Symbol)); далее
        /// SYMB_MAINFUNCTION чтение той же пробой → ExtractLongViaReflection →
        /// _dctFdById. Метка [SYMFDMAP] первых 10. Отказ — null (→ «Без
        /// классификации»). Инстансный (Probe-канал).</summary>
        private long? TryGetFdIdFromSymbolProperties(object oSymbol, int iOrdinal,
            string strName)
        {
            if (oSymbol == null) return null;
            object oProps = GetSymbolPropertyListViaProbes(oSymbol, strName);
            if (oProps == null) return null;
            try
            {
                object oVal = TryGetMemberValue(oProps, "SYMB_MAINFUNCTION");
                if (oVal == null)
                {
                    Probe("[SYMFDMAP] «" + strName + "» Symbol.props #16018 = null",
                        iOrdinal);
                    return null;
                }
                string strVia;
                long? nId = ExtractLongViaReflection(oVal, "SYMFDMAP:" + strName,
                    out strVia);
                Probe("[SYMFDMAP] «" + strName + "» Symbol.props #16018 → " +
                    (nId.HasValue
                        ? nId.Value.ToString(CultureInfo.InvariantCulture) + " via " + strVia
                        : "НЕТ"), iOrdinal);
                return nId;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[SYMFDMAP] «" + strName + "» #16018 через Symbol.Properties — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return null;
            }
        }

        /// <summary>Описание SYMB_DESC (#16011) в цепочке A2 — через Properties-пробу
        /// (reflection — как получить SymbolPropertyList от Symbol НЕ доказано).
        /// Блоб может вернуться MultiLangString-форматом «de_DE@…;ru_RU@…» —
        /// локализуется SymbolCatalog.LocalizeMultiLang. Метка [SYMFDMAP-DESC]
        /// первых 10. Пусто/отказ — null (карточка «—», иначе FD.Description).</summary>
        private string TryGetSymbolSymbDesc(object oSymbol, int iOrdinal, string strName)
        {
            if (oSymbol == null) return null;
            object oProps = GetSymbolPropertyListViaProbes(oSymbol, strName);
            if (oProps == null) return null;
            try
            {
                object oVal = TryGetMemberValue(oProps, "SYMB_DESC");
                if (oVal == null) return null;
                string strRaw = oVal.ToString();
                string strLocalized = SymbolCatalog.LocalizeMultiLang(strRaw);
                Probe("[SYMFDMAP-DESC] «" + strName + "» raw=«" +
                    (string.IsNullOrEmpty(strRaw) ? "<пусто>" : strRaw) + "» → «" +
                    (string.IsNullOrEmpty(strLocalized) ? "<пусто>" : strLocalized) + "»",
                    iOrdinal);
                return string.IsNullOrEmpty(strLocalized) ? null : strLocalized;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[SYMFDMAP-DESC] «" + strName + "» SYMB_DESC — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return null;
            }
        }

        /// <summary>SymbolPropertyList для DataModel MasterData Symbol — НИКАК не
        /// доказано (Rev.14.2: страниц Symbol~Properties / SymbolPropertyList~
        /// _ctor(Symbol) в базе нет): проба (1) свойство «Properties» объекта;
        /// (2) Activator ctor MasterData.SymbolPropertyList(Symbol). Формы сработок
        /// — в [DSPROBE] (кап 5; [DSPROBE] обязан показать КАКАЯ форма).
        /// Null/отказ — null; каждый отказ — Console-дамп.</summary>
        private object GetSymbolPropertyListViaProbes(object oSymbol, string strName)
        {
            if (oSymbol == null) return null;
            object oProps = TryGetMemberValue(oSymbol, "Properties");
            if (oProps != null)
            {
                if (_nPropsFormLogged < 5)
                {
                    ProbeInfo("[DSPROBE] SymbolPropertyList: форма «Symbol.Properties» → " +
                        oProps.GetType().FullName);
                    _nPropsFormLogged++;
                }
                return oProps;
            }
            try
            {
                Type oPlType = oSymbol.GetType().Assembly.GetType(
                    "Eplan.EplApi.DataModel.MasterData.SymbolPropertyList");
                if (oPlType == null) return null;
                oProps = Activator.CreateInstance(oPlType, new object[] { oSymbol });
                if (oProps != null && _nPropsFormLogged < 5)
                {
                    ProbeInfo("[DSPROBE] SymbolPropertyList: форма «Activator ctor(Symbol)» → ок");
                    _nPropsFormLogged++;
                }
                return oProps;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] SymbolPropertyList (Activator) «" + strName +
                    "» — " + oEx.GetType().Name + ": " + oEx.Message);
                return null;
            }
        }

        /// <summary>FD-ID символа в цепочке B — ПЕРВИЧНО из MDSymbol (rev.14.0
        /// KB MDSymbolPropertyList ctor(MDSymbol) + SYMB_MAINFUNCTION «Returns
        /// System.Int64»): извлечение числа — ExtractLongViaReflection (НЕ угадываем
        /// поверхность MDPropertyValue). Метка [SYMFDMAP] первых 10. Отказ — null:
        /// символ → «Без классификации».</summary>
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
                Probe("[SYMFDMAP] «" + strName + "» raw=«" + (strRaw ?? "<null>") +
                    "» id=" + (nId.HasValue
                        ? nId.Value.ToString(CultureInfo.InvariantCulture) + " via " + strVia
                        : "НЕТ"), iOrdinal);
            }
            catch (Exception oEx)
            {
                ProbeWarn("[SYMFDMAP] «" + strName + "» #16018 — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            return nId;
        }

        /// <summary>Описание символа в цепочке B: KB MDSymbolPropertyList.SYMB_DESC —
        /// «Symbol description # 16011»; значение → ToString(). Метка [SYMFDMAP-DESC]
        /// первых 10. Пусто/отказ — null (карточка «—» или FD.Description).</summary>
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

        // CS0618 — см. блоки выше: ExtractLongViaReflection и TryGetMemberValue
        // используют устаревший GetValue(args[]).
#pragma warning disable 618
        /// <summary>Извлечение числа из объекта-провайдера (PropertyValue и прочих):
        /// reflection-проба кандидатов — НИЧЕГО не угадываем (урок rev.7):
        /// (1) уже готовые long/int; (2) методы ToInt64()/ToInt(); (3) свойство Value;
        /// (4) fallback — ToString()+long.TryParse. strVia — каким путём получилось
        /// (печатается в [SYMFDMAP]). Отказ — null.</summary>
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

        // CS0618 — GetValue(args[]) внутри TryGetMemberValue.
#pragma warning disable 618
        /// <summary>Чтение члена (свойство или поле) — reflection-проба БЕЗ
        /// компиляционных рисков; отказ/отсутствие — null + Console-дамп ошибки.
        /// Static: используется из статических хелперов (урок CS0120 rev.14.1).</summary>
        private static object TryGetMemberValue(object oTarget, string strMember)
        {
            if (oTarget == null) return null;
            try
            {
                PropertyInfo oProp = oTarget.GetType().GetProperty(strMember);
                if (oProp != null) return oProp.GetValue(oTarget, null);
                FieldInfo oField = oTarget.GetType().GetField(strMember);
                if (oField != null) return oField.GetValue(oTarget);
            }
            catch (Exception oEx)
            {
                Console.WriteLine("SymbolBrowserDialog: чтение «" + strMember +
                    "» бросило — " + oEx.GetType().Name + ": " + oEx.Message);
            }
            return null;
        }
#pragma warning restore 618

        /// <summary>Линейное reflection-чтение строкового свойства (TryGetMemberValue
        /// + ToString()); отказ — null (Console-дамп внутри). Static.</summary>
        private static string TryGetStringProperty(object oTarget, string strProp)
        {
            object oValue = TryGetMemberValue(oTarget, strProp);
            return oValue == null ? null : oValue.ToString();
        }

        /// <summary>Reflection-проба имени (MDSymbol.Name / Symbol.Name KB НЕ
        /// доказан): Name → IdentifyingName → ToString(). Только чтение.</summary>
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
        /// fix-ревью 1: путь для цепочки B здесь НЕ берётся — EnumeratePathCandidates).
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

        /// <summary>Activator-проба конструкторов MDSymbolLibrary (сигнатуры кторов
        /// KB НЕ доказал; класс — да): для каждого кандидата пути (ниже) формы
        /// (string path), затем (Project, string path); первая вернувшая экземпляр —
        /// успех. MissingMethodException формы запоминается; прочие отказы —
        /// Console-дамп и следующий кандидат. Ни одна комбинация — null.</summary>
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
                        object oCandidate = Activator.CreateInstance(oType,
                            new object[] { strPath });
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
        /// основной кандидат для MDSymbolLibrary(path)), затем Name, IdentifyingName.
        /// Свойства НЕ доказаны KB — reflection-проба, пустые пропуск, дубли —
        /// не зондируем.</summary>
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
        /// -1 (деградация: варианты неизвестны, клетки disabled — rev.14.3).
        /// Инстансный (Probe-канал; фикс ревью rev.14.1: static + ProbeWarn = CS0120).</summary>
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

        // --- FD-словари (Project.FunctionDefinitionLibrary) ---

        /// <summary>Словари FD (rev.14.2, ОДИН раз за жизнь диалога; смотрят
        /// _bFdDumped): (а) _dctFdById (Id → FdInfo, first wins — Id НЕ уникален
        /// глобально: 1305 FD → 40 Id, дамп [FD] rev.14.1) — fallback-бакетизация
        /// для #16018-путей; (б) НОВЫЙ _dctFdBySymbol («lib\u0001sym» → FdInfo)
        /// из FD.BaseSymbol → SymbolLibraryName/SymbolName — ПЕРВИЧНАЯ связь
        /// символ → FD. Поля уровней: MainGroup (Trade)/CategoryRegion (Area)/
        /// CategoryName/GroupName/Name/Description — каждый getter в try/catch
        /// (Description/BaseSymbol рантайм НЕ подтверждён); MultiLangString-блобы
        /// локализуются SymbolCatalog.LocalizeMultiLang (формат «de_DE@…;ru_RU@…»
        /// доказан дампом [FD] rev.14.1). Дамп [FD] первых 10 (формат сохранён)
        /// + [FD-BASE] первых 10 пар BaseSymbol. Сбой/пусто — словари пусты,
        /// A2 хмурится в fallback-пути.</summary>
        private void BuildFdDictionary()
        {
            if (_bFdDumped) return;   // дамп/словари строим ОДИН раз за жизнь диалога
            _bFdDumped = true;
            _dctFdById = new Dictionary<long, FdInfo>();
            _dctFdBySymbol = new Dictionary<string, FdInfo>();
            // rev.14.3: счётчик попыток записи пары по strBaseLib ([FD-BASE-SUM];
            // ключ null/пустой lib — тоже считаем, как попытку — категория «<пусто>»).
            Dictionary<string, int> dctBaseLibs = new Dictionary<string, int>();
            if (_oProject == null)
            {
                ProbeInfo("[FD] недоступен: проект null");
                return;
            }
            try
            {
                // KB: Project.FunctionDefinitionLibrary (в .MasterData),
                // FunctionDefinitions : FunctionDefinition[] — сам FunctionDefinition
                // в Eplan.EplApi.DataModel (урок CS0246 п.30/63 — пространство
                // имён сверять с компилирующимся кодом).
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
                    string strDesc = null;
                    try { nId = oFd.Id; }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] Id — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try
                    {
                        strCat = oFd.CategoryName == null
                            ? null : SymbolCatalog.LocalizeMultiLang(oFd.CategoryName.ToString());
                    }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] CategoryName — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try
                    {
                        strGroup = oFd.GroupName == null
                            ? null : SymbolCatalog.LocalizeMultiLang(oFd.GroupName.ToString());
                    }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] GroupName — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try
                    {
                        strMain = oFd.MainGroup == null
                            ? null : SymbolCatalog.LocalizeMultiLang(oFd.MainGroup.ToString());
                    }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] MainGroup — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try
                    {
                        strRegion = oFd.CategoryRegion == null
                            ? null : SymbolCatalog.LocalizeMultiLang(oFd.CategoryRegion.ToString());
                    }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] CategoryRegion — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try
                    {
                        strName = oFd.Name == null
                            ? null : SymbolCatalog.LocalizeMultiLang(oFd.Name.ToString());
                    }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] Name — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    // Description: KB members page есть, рантайм НЕ подтверждён —
                    // проба try/catch (отказ — только диагностика, поле описания
                    // карточки остаётся SYMB_DESC-первичным).
                    try
                    {
                        strDesc = oFd.Description == null
                            ? null : SymbolCatalog.LocalizeMultiLang(oFd.Description.ToString());
                    }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] Description — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    if (iOrdinal < 10)
                    {
                        ProbeInfo("[FD] id=" + nId.ToString(CultureInfo.InvariantCulture) +
                            " cat='" + (strCat ?? "<null>") + "' main='" + (strMain ?? "<null>") +
                            "' region='" + (strRegion ?? "<null>") + "' group='" +
                            (strGroup ?? "<null>") + "' name='" + (strName ?? "<null>") +
                            "' desc='" + (strDesc ?? "<null>") + "'");
                    }

                    // (а) dctFdById — first wins (Id НЕ уникален глобально; равный Id
                    // у разных FD — аспект fallback-бакетизации, рантайм-факт rev.14.1).
                    if (nId >= 0 && !_dctFdById.ContainsKey(nId))
                    {
                        FdInfo oInfo = new FdInfo();
                        oInfo.MainGroup = strMain;
                        oInfo.Area = strRegion;
                        oInfo.Category = strCat;
                        oInfo.Group = strGroup;
                        oInfo.Name = strName;
                        oInfo.Description = strDesc;
                        _dctFdById[nId] = oInfo;
                    }

                    // (б) BaseSymbol → (lib\sym) → dctFdBySymbol. BaseSymbol: KB
                    // members («Gets the best fitting SymbolVariant ...»), рантайм
                    // НЕ подтверждён — типизированная проба в try/catch + [FD-BASE].
                    object oBaseSymbol = null;
                    try { oBaseSymbol = oFd.BaseSymbol; }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD-BASE] BaseSymbol FD «" + (strName ?? "<null>") +
                                "» — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    string strBaseLib = null;
                    string strBaseSym = null;
                    if (oBaseSymbol != null)
                    {
                        // SymbolLibraryName/SymbolName у MasterData.Symbol — рантайм
                        // НЕ подтверждены; отражаем reflection-пробой (TryGetStringProperty)
                        // БЕЗ компиляционных рисков (урок некомпилируемого угадывания).
                        strBaseLib = TryGetStringProperty(oBaseSymbol, "SymbolLibraryName");
                        strBaseSym = TryGetStringProperty(oBaseSymbol, "SymbolName");
                    }
                    if (iOrdinal < 10)
                    {
                        ProbeInfo("[FD-BASE] FD id=" + nId.ToString(CultureInfo.InvariantCulture) +
                            " «" + (strName ?? "<null>") + "» → lib «" +
                            (strBaseLib ?? "<null>") + "» sym «" + (strBaseSym ?? "<null>") + "»");
                    }
                    if (!string.IsNullOrEmpty(strBaseSym))
                    {
                        // rev.14.3: инкремент при КАЖДОЙ попытке записи пары (до
                        // ContainsKey-гейта) — расклад ключей по библиотекам для
                        // сверки с [SYMFDMAP-KEY].
                        string strKey = MakeFdSymbolKey(strBaseLib, strBaseSym);
                        // нормализация null/пустого lib в категорию «<пусто>»
                        // (Dictionary<string,int> null-ключ не принимает)
                        string strLibBucket = string.IsNullOrEmpty(strBaseLib)
                            ? "<пусто>" : strBaseLib;
                        if (dctBaseLibs.ContainsKey(strLibBucket))
                            dctBaseLibs[strLibBucket]++;
                        else
                            dctBaseLibs[strLibBucket] = 1;
                        if (!_dctFdBySymbol.ContainsKey(strKey))
                        {
                            FdInfo oInfoBySymbol = new FdInfo();
                            oInfoBySymbol.MainGroup = strMain;
                            oInfoBySymbol.Area = strRegion;
                            oInfoBySymbol.Category = strCat;
                            oInfoBySymbol.Group = strGroup;
                            oInfoBySymbol.Name = strName;
                            oInfoBySymbol.Description = strDesc;
                            _dctFdBySymbol[strKey] = oInfoBySymbol;
                        }
                    }
                    iOrdinal++;
                }
                // rev.14.3: расклад попыток записи пар по базовым библиотекам
                // (каждая попытка, вкл. дубли BaseSymbol) — сортировка по убыванию,
                // кап 8, хвост «+X ещё».
                string strJoined = string.Empty;
                List<KeyValuePair<string, int>> lstLibs =
                    new List<KeyValuePair<string, int>>(dctBaseLibs);
                lstLibs.Sort(delegate(KeyValuePair<string, int> oA,
                    KeyValuePair<string, int> oB)
                {
                    return oB.Value - oA.Value;   // по убыванию count
                });
                for (int iLib = 0; iLib < lstLibs.Count; iLib++)
                {
                    if (iLib == 8)
                    {
                        strJoined += " (+" +
                            (lstLibs.Count - 8).ToString(CultureInfo.InvariantCulture) +
                            " ещё)";
                        break;
                    }
                    strJoined += (iLib == 0 ? string.Empty : ", ") + lstLibs[iLib].Key +
                        "=" + lstLibs[iLib].Value.ToString(CultureInfo.InvariantCulture);
                }
                ProbeInfo("[FD] словари FD: по Id " +
                    _dctFdById.Count.ToString(CultureInfo.InvariantCulture) +
                    ", по символу " + _dctFdBySymbol.Count.ToString(CultureInfo.InvariantCulture) +
                    " записей");
                ProbeInfo("[FD-BASE-SUM] BaseSymbol-словарь: " +
                    _dctFdBySymbol.Count.ToString(CultureInfo.InvariantCulture) +
                    " пар; библиотеки (кап 8 по убыванию; попытки записи вкл. дубли): " +
                    (strJoined.Length == 0 ? "<пусто>" : strJoined));
            }
            catch (Exception oEx)
            {
                ProbeWarn("[FD] недоступен: " + oEx.GetType().Name + ": " +
                    oEx.Message + " — FD-пути дерева отключены");
            }
        }

        /// <summary>Ключ обратного словаря FD: «libName\u0001symName» (разделитель
        /// — невидимый \u0001; коллизий «libA|symB» нет).</summary>
        private static string MakeFdSymbolKey(string strLib, string strSym)
        {
            return (strLib ?? string.Empty) + "\u0001" + (strSym ?? string.Empty);
        }

        // --- дерево категорий (SymbolCatalog 5 уровней) + поиск + предвыбор ---

        /// <summary>Пересборка дерева из чистого SymbolCatalog (rev.14.2, R2/RC-3;
        /// rev.14.3: SYMB_DESC записи — в SearchIndex листа): 5 уровней Trade →
        /// Area → Категория → Группа → Определение функции, листья — символы
        /// (Tag = имя); unmapped — «Без классификации» (или префиксные бакеты при
        /// полном отсутствии FD-пути). Поиск — индексно-рекурсивный: лист
        /// совпадает по SearchIndex (имя + FD-поля + SYMB_DESC), ветка — по
        /// своему SearchIndex; совпавшая ветка показывает ВСЕХ детей (рестарт
        /// фильтра), непустые без self-match ветки фильтруют листья рекурсивно;
        /// пустые ветки скрыты; корень — библиотека. В конце — подсветка
        /// текущего символа (если он в дереве).</summary>
        private void RebuildTree()
        {
            string strFilter = _txtSearch.Text ?? string.Empty;
            // Каталог — rev.14.2: SymbolCatalogEntry {Name, Fd} →
            // дерево SymbolCatalogNode (5 уровней).
            List<SymbolCatalogEntry> lstCatalogEntries = new List<SymbolCatalogEntry>();
            foreach (SymbolEntry oEntry in _lstEntries)
            {
                if (oEntry == null) continue;
                SymbolCatalogEntry oCatEntry = new SymbolCatalogEntry();
                oCatEntry.Name = oEntry.Name;
                oCatEntry.Fd = oEntry.Fd;
                oCatEntry.Description = oEntry.Description;   // SYMB_DESC — в SearchIndex листа (rev.14.3)
                lstCatalogEntries.Add(oCatEntry);
            }
            List<SymbolCatalogNode> lstTop =
                SymbolCatalog.Build(lstCatalogEntries, _dctFdById);
            string strRootText = string.IsNullOrEmpty(_txtLibrary.Text)
                ? "Библиотека"
                : _txtLibrary.Text;

            _treeSymbols.BeginUpdate();
            _treeSymbols.Nodes.Clear();
            TreeNode oRoot = new TreeNode(strRootText);
            int nShownTotal = 0;
            foreach (SymbolCatalogNode oNode in lstTop)
            {
                nShownTotal += RenderCatalogNode(oNode, strFilter, oRoot.Nodes);
            }
            if (oRoot.Nodes.Count > 0) _treeSymbols.Nodes.Add(oRoot);
            oRoot.Expand();   // верхние уровни видны сразу (символы — по желанию клика)
            _treeSymbols.EndUpdate();

            _lblCount.Text = "показано " + nShownTotal.ToString(CultureInfo.InvariantCulture) +
                " из " + _lstEntries.Count.ToString(CultureInfo.InvariantCulture);
            HighlightCurrentSymbol();
        }

        /// <summary>Рекурсивный рендер узла каталога (rev.14.2; rev.14.3 —
        /// индексно-рекурсивный фильтр): Kind=Symbol — лист (Tag = имя; FindLeaf
        /// ищет по Tag), остальные ветки БЕЗ Tag; текст узла = Name + « (N)»
        /// (N — число ПОКАЗАННЫХ листьев subtree). Фильтр: лист совпадает по
        /// SearchIndex (имя + FD-поля + SYMB_DESC, substring, OrdinalIgnoreCase);
        /// ветка, чей SearchIndex сам совпал (например, FD-имя/описание),
        /// показывает ВСЕХ детей — фильтр для детей рестартует на пустой;
        /// иначе пустые ветки скрыты; возврат — число листьев.</summary>
        private int RenderCatalogNode(SymbolCatalogNode oCatNode, string strFilter,
            TreeNodeCollection oDest)
        {
            if (oCatNode == null) return 0;
            bool bLeaf = string.Compare(oCatNode.Kind, SymbolCatalog.KIND_SYMBOL,
                StringComparison.Ordinal) == 0;
            string strHaystack = oCatNode.SearchIndex ?? oCatNode.Name;
            bool bSelfMatch = strFilter.Length > 0 &&
                strHaystack.IndexOf(strFilter, StringComparison.OrdinalIgnoreCase) >= 0;
            if (bLeaf)
            {
                if (strFilter.Length > 0 && !bSelfMatch) return 0;   // пустой фильтр — все листья
                TreeNode oLeaf = new TreeNode(oCatNode.Name);
                oLeaf.Tag = oCatNode.Name;
                oDest.Add(oLeaf);
                return 1;
            }
            TreeNode oBranch = new TreeNode(oCatNode.Name);
            string strChildFilter = bSelfMatch ? string.Empty : strFilter;
            int nShown = 0;
            foreach (SymbolCatalogNode oChild in oCatNode.Children)
            {
                nShown += RenderCatalogNode(oChild, strChildFilter, oBranch.Nodes);
            }
            if (nShown == 0) return 0;   // ветка без листьев — скрыта; self-match с 0
            // детей не различим от пустой (поддерева нет — скрывать нечего)
            oBranch.Text = oCatNode.Name + " (" +
                nShown.ToString(CultureInfo.InvariantCulture) + ")";
            oDest.Add(oBranch);
            return nShown;
        }

        /// <summary>Подсветка текущего символа (_strSelectedSymbol) в дереве:
        /// точное совпадение листа (БЕЗ trim — урок п.33) — разворот пути +
        /// выделение (AfterSelect применит карточку/превью — идемпотентно). Нет
        /// листа — дерево остаётся без подсветки (имя вне дерева — OK-валидация
        /// скажет). Символ может быть корректно скрыт фильтром поиска — это не
        /// ошибка: состояние сохранилось, OK-валидация идёт по _lstEntries.</summary>
        private void HighlightCurrentSymbol()
        {
            if (string.IsNullOrEmpty(_strSelectedSymbol)) return;
            TreeNode oLeaf = FindLeaf(_treeSymbols.Nodes, _strSelectedSymbol);
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
        /// Tag ставится ТОЛЬКО листьям (RenderCatalogNode) — ветки/корню Tag нет,
        /// «(N)»-суффиксы дерева карточке не мешают.</summary>
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
        /// урок п.33; rev.14.2 R5: имя — в _strSelectedSymbol, БЕЗ текстового поля).
        /// rev.14.3 Task 5 (дедуп): лист найден — ТОЛЬКО разворот+выделение,
        /// карточку/превью применит AfterSelect → ApplySelection (единожды);
        /// явный apply — только когда листа в дереве НЕТ (имя вне перечисления /
        /// скрыто фильтром): карточка по имени + превью по каскаду (OK-валидация
        /// проверит принадлежность).</summary>
        private void SelectSymbolPrechoice(string strName)
        {
            if (string.IsNullOrEmpty(strName)) return;
            _strSelectedSymbol = strName;
            TreeNode oLeaf = FindLeaf(_treeSymbols.Nodes, strName);
            if (oLeaf != null)
            {
                if (_treeSymbols.SelectedNode != oLeaf)
                {
                    TreeNode oWalk = oLeaf;
                    while (oWalk != null)
                    {
                        oWalk.Expand();
                        oWalk = oWalk.Parent;
                    }
                    _treeSymbols.SelectedNode = oLeaf;   // AfterSelect → ApplySelection единожды
                }
                return;   // applied через AfterSelect (или уже был выделен)
            }
            UpdateCardByEntry(strName);
            RefreshPreviewGridByEntry(strName);
        }

        // --- превью-сетка (DrawingService; R3/R4/R5 + фиксированные 8 клеток) ---

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

        /// <summary>Пересборка превью-сетки по ИМЕНИ символа (rev.14.2): запись
        /// ищется в _lstEntries; пустые Library/Name — сброс + ClearCard.
        /// rev.14.3 Task 5: после сборки (в т.ч. при полном отказе рендера —
        /// клетки уже стоят, повторный apply не нужен) ключ _strPreviewGridKey
        /// запоминается для гейта ApplySelection.</summary>
        private void RefreshPreviewGridByEntry(string strName)
        {
            string strLib = _txtLibrary.Text;
            if (string.IsNullOrEmpty(strLib) || string.IsNullOrEmpty(strName))
            {
                // Fix-минор r14.1: при сбросе превью карточка тоже очищается.
                DisposePreviewGrid();
                ClearCard();
                _strPreviewGridKey = string.Empty;   // сетки нет — ключ гасится
                return;
            }
            SymbolEntry oEntry = FindEntryObj(strName);
            BuildPreviewGrid(strLib, strName, oEntry);
            _strPreviewGridKey = strName;   // сетка построена для этого имени
        }

        /// <summary>Построение ВСЕХ 8 клеток A–H (rev.14.2, R3+спека): внешний
        /// Panel чёрный (enabled) / серый (disabled, Color.FromArgb(56,56,56)); ВНУТРЕННЯЯ
        /// панель рисования Dock=Fill добавляется ПЕРВОЙ, Label буквы Dock=Bottom
        /// (18px) добавляется ВТОРОЙ — docking обрабатывается в обратном порядке,
        /// метка НЕ накрывает область рисования (fix RC-2: раньше Paint получал всю
        /// клетку, а метка снизу её занимала). У каждой клетки СВОЙ DrawingService;
        /// Reset() перед созданием списка + SetDefaultViewport (гипотеза центра
        /// R4). Отсутствующие варианты — disabled (без display list): фон серый,
        /// буква серая, клик игнор. Клик по enabled — выделение (спека §16):
        /// рамка на Paint + цвет буквы; слоты H/V НЕ трогаются. Отказ рендера
        /// клетки не ломает остальные и выбор символа.</summary>
        private void BuildPreviewGrid(string strLib, string strName, SymbolEntry oEntry)
        {
            DisposePreviewGrid();
            _nSelectedCell = -1;
            _bPathLogged = false;   // путь успеха — один раз на пересборку (шум [DSPROBE])
            _tlpPreviewGrid.SuspendLayout();
            int nFailed = 0;   // отказ РЕНДЕРА enabled-клеток
            int nEnabledTotal = 0;
            for (int i = 0; i < 8; i++)
            {
                string strLetter = ((char)('A' + i)).ToString(CultureInfo.InvariantCulture);
                bool bEnabled = IsVariantEnabled(oEntry, i);
                if (bEnabled) nEnabledTotal++;

                Panel oCell = new Panel();
                oCell.BackColor = bEnabled ? Color.Black : Color.FromArgb(56, 56, 56);
                oCell.BorderStyle = BorderStyle.FixedSingle;
                oCell.Dock = DockStyle.Fill;
                oCell.Margin = new Padding(1);

                // ВНУТРЕННЯЯ панель рисования: Dock=Fill, добавляется ПЕРВОЙ —
                // docking обрабатывается в обратном порядке (последний добавленный
                // размещается первым) → метка снизу отъездит свой нижний пояс,
                // рисование НЕ накроется (fix RC-2). Класс DrawPanel — CS1540-фикс:
                // ResizeRedraw protected, чужому экземпляру недоступен.
                Panel oDraw = new DrawPanel();
                oDraw.Dock = DockStyle.Fill;
                oDraw.BackColor = oCell.BackColor;
                oCell.Controls.Add(oDraw);

                Label oLetter = new Label();
                oLetter.Text = strLetter;
                oLetter.BackColor = oCell.BackColor;
                oLetter.ForeColor = bEnabled ? Color.White : Color.FromArgb(160, 160, 160);
                oLetter.TextAlign = ContentAlignment.MiddleCenter;
                oLetter.Height = 18;
                // Fix ревью rev.14.2 (Important): Dock БЫЛ ПОТЕРЯН — метка без
                // закрепления остаётся в (0,0) за непрозрачной панелью рисования
                // (буквы невидимы). Dock=Bottom: docking обрабатывается в обратном
                // порядке добавления — метка (добавлена ВТОРОЙ) резервирует нижние
                // 18px, панель рисования (Dock=Fill, добавлена ПЕРВОЙ) занимает
                // остаток; области не пересекаются (fix RC-2 работает).
                oLetter.Dock = DockStyle.Bottom;
                oCell.Controls.Add(oLetter);

                DrawingService oDs = null;
                bool bReady = false;
                if (bEnabled)
                {
                    try
                    {
                        oDs = new DrawingService();
                        ApplyPreviewSettings(oDs);
                        bReady = BuildCellList(oDs, oEntry, strLib, strName, i, strLetter);
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
                }
                _lstPreviewCells.Add(oCell);
                _lstCellDrawPanels.Add(oDraw);
                _lstCellLetters.Add(oLetter);
                _lstPreviewServices.Add(oDs);
                _lstCellReady.Add(bReady);
                _lstCellEnabled.Add(bEnabled);

                // Paint ВНУТРЕННЕЙ панели: DrawDisplayList(oArgs, ClientRectangle
                // ВНУТРЕННЕЙ — fix RC-2; не вся клетка, метка больше не накрывает
                // графику) + рамка выделения поверх. Захват индекса — C#5 делегат.
                int iCell = i;
                oDraw.Paint += delegate(object oSender, PaintEventArgs oArgs)
                {
                    if (iCell >= _lstPreviewServices.Count) return;
                    DrawingService oCellDs = _lstPreviewServices[iCell];
                    if (oCellDs != null && _lstCellReady[iCell])
                    {
                        try
                        {
                            oCellDs.DrawDisplayList(oArgs, oDraw.ClientRectangle);
                        }
                        catch (Exception oEx)
                        {
                            // Отказ рендера — ProbeWarn, выбор символа не ломаем.
                            ProbeWarn("[DSPROBE] DrawDisplayList клетка " +
                                ((char)('A' + iCell)).ToString(CultureInfo.InvariantCulture) +
                                " — " + oEx.GetType().Name + ": " + oEx.Message);
                        }
                    }
                    if (_nSelectedCell == iCell && _lstCellEnabled[iCell])
                    {
                        try
                        {
                            Rectangle rcSel = oDraw.ClientRectangle;
                            rcSel.Inflate(-2, -2);
                            using (Pen oPenSel = new Pen(Color.Gold, 2f))
                            {
                                oArgs.Graphics.DrawRectangle(oPenSel, rcSel);
                            }
                        }
                        catch { /* рамка — украшение; отказ не мешает */ }
                    }
                };
                // Клик по рисуемой области / букве enabled клетки — выделение
                // варианта (визуально); слоты H/V НЕ меняются (спека).
                oDraw.MouseClick += delegate { SelectPreviewCell(iCell); };
                oLetter.Click += delegate { SelectPreviewCell(iCell); };
                _tlpPreviewGrid.Controls.Add(oCell, i % 4, i / 4);
            }
            _tlpPreviewGrid.ResumeLayout();
            // Сводка пересборки вместо по-клеточных успехов (ревью rev.14.2): один
            // [DSPROBE]-итог на выделение символа; отказы клеток — WARN выше.
            ProbeInfo("[DSPROBE] сетка: готово " +
                (nEnabledTotal - nFailed).ToString(CultureInfo.InvariantCulture) + " из " +
                nEnabledTotal.ToString(CultureInfo.InvariantCulture) + " enabled / 8 клеток");
            if (nEnabledTotal > 0 && nFailed == nEnabledTotal)
                SetStatus("Превью недоступно: все клетки не отрисованы (см. [DSPROBE] в логе).");
        }

        /// <summary>Строгий три-состояний гейт клетки i (0..7), решение пользователя
        /// (summary п.93): запись есть → делегация SymbolCatalog.IsCellEnabled —
        /// bVariantsKnown=false (сбой чтения Variants) или известный ПУСТОЙ список
        /// вариантов (0 шт.) → ВСЕ клетки disabled; известный список варианта →
        /// только реальные VariantNr. «Неизвестно → есть только A» ЗАПРЕЩЕНО.
        /// Записи нет (oEntry null, предвыборка вне библиотеки) — false.</summary>
        private static bool IsVariantEnabled(SymbolEntry oEntry, int i)
        {
            if (oEntry == null) return false;
            return SymbolCatalog.IsCellEnabled(oEntry.bVariantsKnown, oEntry.lstVariantNrs, i);
        }

        /// <summary>Панель рисования клетки с перерисовкой при ресайзе
        /// (CS1540-фикс, ревью/прогон rev.14.2: ResizeRedraw — protected член
        /// Control, через экземпляр ЧУЖОГО типа недоступен; внутри подкласса —
        /// можно. DrawDisplayList «fit keeping aspect ratio» на каждый Paint,
        /// при ресайзе без ResizeRedraw картинка не перемасштабируется).</summary>
        private class DrawPanel : Panel
        {
            public DrawPanel()
            {
                SetStyle(ControlStyles.ResizeRedraw, true);
            }
        }

        /// <summary>Display list ОДНОЙ клетки (готовность + каскад): Reset() →
        /// (1) живой SymbolVariant из A2 (KB CreateDisplayList(SymbolVariant));
        /// (2) типизированно new Symbol(SymbolLibrary)+new SymbolVariant (rev.14.1);
        /// (3) каскад by-strings (CreateDisplayList(String,String,Int32,Project)).
        /// true — клетка готова к отрисовке. Вызывается ТОЛЬКО для enabled-клеток
        /// (caller-гейт bEnabled — display list disabled-клетке не нужен); успех
        /// пути печатается ОДИН раз на пересборку сетки (_bPathLogged) — иначе
        /// до 32 [DSPROBE]-строк на каждое выделение (шум, ревью rev.14.2).</summary>
        private bool BuildCellList(DrawingService oDs, SymbolEntry oEntry, string strLib,
            string strName, int iVariant, string strLetter)
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
            bool bCreated = false;
            Exception oErr = null;
            // (1) Живой объект варианта из A2 — KB-перегрузка CreateDisplayList(SymbolVariant).
            object oVariantObject = FindVariantObject(oEntry, iVariant);
            if (oVariantObject != null)
            {
                oErr = TryDisplayListVariantObject(oDs, oVariantObject, strLetter);
                bCreated = oErr == null;
            }
            // (2) Типизированная сборка заново (как rev.14.1; также для B).
            if (!bCreated)
            {
                oErr = TryDisplayListTyped(oDs, strName, iVariant, strLetter);
                bCreated = oErr == null;
            }
            // (3) By-strings (rev.14.0, HE_Display).
            if (!bCreated)
            {
                oErr = TryDisplayListByStrings(oDs, strName, strLib, iVariant, strLetter);
                bCreated = oErr == null;
            }
            if (!bCreated)
            {
                ProbeWarn("[DSPROBE] вариант " + strLetter + " не отрисован: " +
                    oErr.GetType().Name + ": " + oErr.Message);
                return false;
            }
            TrySetDefaultViewport(oDs);
            return true;
        }

        /// <summary>Отыскать ЖИВОЙ объект SymbolVariant записи по VariantNr
        /// (A2-перечисление сохраняет пары VariantNr/объект).</summary>
        private static object FindVariantObject(SymbolEntry oEntry, int iVariant)
        {
            if (oEntry == null) return null;
            for (int i = 0; i < oEntry.lstVariantNrs.Count; i++)
            {
                if (oEntry.lstVariantNrs[i] == iVariant &&
                    i < oEntry.lstVariantObjects.Count)
                {
                    object oVariant = oEntry.lstVariantObjects[i];
                    if (oVariant != null) return oVariant;
                }
            }
            return null;
        }

        /// <summary>Путь 0 (rev.14.2, A2-первичный): CreateDisplayList(SymbolVariant)
        /// на ЖИВОМ объекте из Symbol.Variants — KB-доказанная перегрузка; сборка
        /// новых объектов не нужна. Отказ — исключение (каскад продолжится).</summary>
        private Exception TryDisplayListVariantObject(DrawingService oDs,
            object oVariantObject, string strLetter)
        {
            SymbolVariant oSymbolVariant = oVariantObject as SymbolVariant;
            if (oSymbolVariant == null)
            {
                Exception oNot = new InvalidOperationException(
                    "сохранённый объект варианта не SymbolVariant");
                ProbeWarn("[DSPROBE] клетка " + strLetter + " — " + oNot.Message);
                return oNot;
            }
            try
            {
                oDs.CreateDisplayList(oSymbolVariant);
                if (!_bPathLogged)
                {
                    _bPathLogged = true;
                    ProbeInfo("[DSPROBE] CreateDisplayList(SymbolVariant) — ок " +
                        "(живой объект из Symbol.Variants, клетка " + strLetter +
                        "; дальнейшие успехи клеток не печатаются)");
                }
                return null;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] CreateDisplayList(SymbolVariant-объект) — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return oEx;
            }
        }

        /// <summary>Путь 1 (первичный при отсутствии живого объекта): DataModel
        /// MasterData Symbol(oSymbolLibrary, strName) → SymbolVariant(oSymbol,
        /// nVariant) → CreateDisplayList(SymbolVariant) — все шаги KB-доказаны.
        /// Объект библиотеки — из _lstLibraryObjects (тот же, что использует цепочки
        /// A2/B); не выбран — путь недоступен (исключение-заглушка, [DSPROBE]).</summary>
        private Exception TryDisplayListTyped(DrawingService oDs, string strName,
            int nVariant, string strLetter)
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
                if (!_bPathLogged)
                {
                    _bPathLogged = true;
                    ProbeInfo("[DSPROBE] CreateDisplayList(SymbolVariant) — ок " +
                        "(типизированный путь: Symbol(SymbolLibrary) + " +
                        "SymbolVariant(Symbol,Int32), клетка " + strLetter +
                        "; дальнейшие успехи клеток не печатаются)");
                }
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
        /// Перегрузка с RepresentationType сознательно НЕ пробуется (не доказана —
        /// одна гипотеза за прогон). Оба отказа — последнее исключение.</summary>
        private Exception TryDisplayListByStrings(DrawingService oDs, string strName,
            string strLib, int nVariant, string strLetter)
        {
            try
            {
                oDs.CreateDisplayList(strName, strLib, nVariant, _oProject);
                if (!_bPathLogged)
                {
                    _bPathLogged = true;
                    ProbeInfo("[DSPROBE] CreateDisplayList(String,String,Int32,Project)" +
                        " — ок (имя библ: «" + strLib + "», клетка " + strLetter +
                        "; дальнейшие успехи клеток не печатаются)");
                }
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
                if (!_bPathLogged)
                {
                    _bPathLogged = true;
                    ProbeInfo("[DSPROBE] CreateDisplayList(String,String,Int32,Project)" +
                        " — ок (пустое имя библ, паттерн HE_Display, клетка " +
                        strLetter + "; дальнейшие успехи клеток не печатаются)");
                }
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
        /// безвредна. Успех НЕ печатается (шум: 8 клеток на выделение, ревью
        /// rev.14.2); отказ — WARN (не поле отказа рендера: список уже создан).</summary>
        private void TrySetDefaultViewport(DrawingService oDs)
        {
            try
            {
                oDs.SetDefaultViewport();
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] SetDefaultViewport — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        /// <summary>Выделение клетки превью (спека §16): только enabled-клетка;
        /// хранит индекс (_nSelectedCell); перерисовка — Invalidate панелей рисования
        /// (Paint рисует золотую рамку) + цвет букв; слоты H/V НЕ меняются.</summary>
        private void SelectPreviewCell(int iCellIndex)
        {
            if (iCellIndex < 0 || iCellIndex >= _lstCellEnabled.Count) return;
            if (!_lstCellEnabled[iCellIndex]) return;   // disabled / отсутствующий вариант
            if (_nSelectedCell == iCellIndex) return;   // уже выделена
            _nSelectedCell = iCellIndex;
            for (int i = 0; i < _lstCellLetters.Count; i++)
            {
                Label oLetter = _lstCellLetters[i];
                if (oLetter != null)
                {
                    oLetter.ForeColor = (i == _nSelectedCell)
                        ? Color.Gold
                        : (_lstCellEnabled[i] ? Color.White : Color.FromArgb(160, 160, 160));
                }
                Panel oDraw = _lstCellDrawPanels[i];
                if (oDraw != null)
                {
                    try { oDraw.Invalidate(); }
                    catch { /* контрол уже отсоединён — не мешает */ }
                }
            }
        }

        /// <summary>Сброс сетки: Dispose ВСЕХ сервисов + очистка всех параллельных
        /// списков клеток (OnFormClosing и каждая пересборка). Внутренние панели и
        /// буквы — дети клеток, dispose'ятся рекурсивно вместе с ними.</summary>
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
            _lstCellDrawPanels.Clear();
            _lstCellLetters.Clear();
            _lstCellReady.Clear();
            _lstCellEnabled.Clear();
            _nSelectedCell = -1;
            _tlpPreviewGrid.Controls.Clear();
            _tlpPreviewGrid.Invalidate();
        }

        // --- карточка ---

        /// <summary>Карточка превью (rev.14.2 R4): имя (как есть); категория =
        /// FD.Name выбранной записи (или «—» — Fd не сопоставился/пустое имя);
        /// описание = SYMB_DESC записи, иначе FD.Description, иначе «—».</summary>
        private void UpdateCardByEntry(string strName)
        {
            SymbolEntry oEntry = FindEntryObj(strName);
            string strCategoryText = null;
            string strDescriptionText = null;
            if (oEntry != null)
            {
                if (oEntry.Fd != null && !string.IsNullOrEmpty(oEntry.Fd.Name))
                    strCategoryText = oEntry.Fd.Name;
                if (!string.IsNullOrEmpty(oEntry.Description))
                    strDescriptionText = oEntry.Description;
                else if (oEntry.Fd != null && !string.IsNullOrEmpty(oEntry.Fd.Description))
                    strDescriptionText = oEntry.Fd.Description;
            }
            _lblPreviewName.Text = "Имя: " +
                (string.IsNullOrEmpty(strName) ? "—" : strName);
            _lblPreviewCategory.Text = "Категория: " +
                (string.IsNullOrEmpty(strCategoryText) ? "—" : strCategoryText);
            _lblPreviewDescription.Text = "Описание: " +
                (string.IsNullOrEmpty(strDescriptionText) ? "—" : strDescriptionText);
        }

        /// <summary>Сброс карточки в прочерки (фикс r14.1: вместе со сбросом
        /// превью). Вызывается из RefreshPreviewGridByEntry при пустых строках и
        /// RefreshSymbols при смене библиотеки.</summary>
        private void ClearCard()
        {
            _lblPreviewName.Text = "Имя: —";
            _lblPreviewCategory.Text = "Категория: —";
            _lblPreviewDescription.Text = "Описание: —";
        }

        /// <summary>Поиск записи (точное Ordinal) по имени — для карточки/OK-
        /// валидации (rev.14.2: дубли имён сохраняются — первый побеждает).</summary>
        private int FindEntryIndex(string strName)
        {
            if (string.IsNullOrEmpty(strName)) return -1;
            for (int i = 0; i < _lstEntries.Count; i++)
            {
                SymbolEntry oEntry = _lstEntries[i];
                if (oEntry != null &&
                    string.Compare(oEntry.Name, strName, StringComparison.Ordinal) == 0)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Объект записи по имени (null — нет такой; карточка прочерками
        /// «категория/описание», превью — каскад по строкам).</summary>
        private SymbolEntry FindEntryObj(string strName)
        {
            int iIndex = FindEntryIndex(strName);
            if (iIndex >= 0) return _lstEntries[iIndex];
            return null;
        }

        private void SetStatus(string strText)
        {
            _lblStatus.Text = strText;
        }

        // --- слоты вариантов ---

        /// <summary>Слоты — ComboBox букв A-H (всегда 8; rev.14.1 R6). Буква =
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

        // --- Layout (rev.14.2: 2 колонки ~50%/50%; паттерн MainDialog) ---

        private static Label LabelOf(string strText)
        {
            Label oLabel = new Label();
            oLabel.Text = strText;
            oLabel.AutoSize = true;
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

        /// <summary>Строка с КОНКРЕТНОЙ высотой (превью-сетка фиксированной
        /// высоты: 2 ряда по ~170 — как решено для 8 клеток).</summary>
        private static void AddRowFixed(TableLayoutPanel oPanel, Control oControl, int nHeight)
        {
            oPanel.RowCount++;
            oPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, nHeight));
            oControl.Dock = DockStyle.Fill;
            oPanel.Controls.Add(oControl, 0, oPanel.RowCount - 1);
        }

        /// <summary>Layout (rev.14.2, R3): контент — TableLayoutPanel 2 колонки
        /// (50/50), слева — библиотека/список/поиск/дерево 5 уровней/счётчик (поле
        /// «Имя выбранного символа» УДАЛЕНО — R5); справа — превью-сетка 8 клеток
        /// (2×170) и карточка; внизу — слоты H/V (комбо букв), статус, кнопки.
        /// ClientSize 1120×800.</summary>
        private void BuildLayout(Button btnOk, Button btnCancel)
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12, 8, 12, 8);
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // Контент: 2 колонки 50/50 (rev.14.2: 1120 ширины — колонки поровну).
            TableLayoutPanel oContent = new TableLayoutPanel();
            oContent.ColumnCount = 2;
            oContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            oContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            oContent.Dock = DockStyle.Fill;
            oContent.AutoSize = true;

            // ЛЕВО: библиотека/список/поиск/дерево/счётчик.
            TableLayoutPanel oLeft = new TableLayoutPanel();
            oLeft.Dock = DockStyle.Fill;
            oLeft.ColumnCount = 1;
            oLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            AddRow(oLeft, LabelOf("Библиотека (проекта; имя можно ввести вручную):"), false);
            AddRow(oLeft, _txtLibrary, true);
            _lstLibraries.Height = 110;
            _lstLibraries.HorizontalScrollbar = true;
            AddRow(oLeft, _lstLibraries, true);
            AddRow(oLeft, LabelOf("Поиск символа (подстрока):"), false);
            AddRow(oLeft, _txtSearch, true);
            AddRow(oLeft, LabelOf("Дерево: Trade → Area → Категория → Группа → " +
                "Определение функции → символ; двойной клик — выбрать:"), false);
            _treeSymbols.Height = 340;
            // HSCROLL у TreeView автоматический (свойства HorizontalScrollbar нет — CS1061).
            AddRow(oLeft, _treeSymbols, true);
            AddRow(oLeft, _lblCount, false);

            // ПРАВО: превью-сетка (8 клеток A-H, дискретные 2×170) + карточка.
            TableLayoutPanel oRight = new TableLayoutPanel();
            oRight.Dock = DockStyle.Fill;
            oRight.ColumnCount = 1;
            oRight.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            AddRow(oRight, LabelOf("Превью (A–H; выделение — клик по клетке):"), false);
            _tlpPreviewGrid.ColumnCount = 4;
            for (int iCol = 0; iCol < 4; iCol++)
                _tlpPreviewGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            _tlpPreviewGrid.RowCount = 2;
            for (int iRow = 0; iRow < 2; iRow++)
                _tlpPreviewGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 170f));
            _tlpPreviewGrid.Dock = DockStyle.Fill;
            _tlpPreviewGrid.BackColor = Color.Black;
            AddRowFixed(oRight, _tlpPreviewGrid, 352);
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
            _lblStatus.MaximumSize = new Size(1050, 0);
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.EServices.Ged;

namespace MyEplanActions
{
    /// <summary>SPIKE-2 (throwaway), H-4v2: производный InsertInteraction от нативной
    /// вставки символа — «диалог+размещение» синхронны (факт spike-1), а размещённые
    /// объекты доступны в OnSuccess. УДАЛИТЬ ПОСЛЕ РАЗБОРА ПРОГОНА (файл + хук 1c в
    /// AnalyzeAction.RunUi + гейт AddInConfiguration.SpikeSymbolPick).
    /// Brief: .superpowers/sdd/plan_stage8/task-h4v2-spike2-brief.md
    /// Продакшн UX (после фактов): кнопка «Символ через EPLAN…» в MainDialog с
    /// предвыбором в настройки; браузер H-4 — запасной. Вне скоупа спайка: кнопка,
    /// запись настроек, ОУ (FUNC_FULLNAME) размещённого — не читаем.
    /// Механика — verbatim-пример пользователя для 2.9 (атрибут [Interaction], базовый
    /// XEGedIaInsertSymRef, base.OnSuccess-first). KB-факты API 2.9 (цитировать
    /// граблю нельзя: блок [Code] — артефакт скрейпера):
    /// - InsertInteraction ctor() — www.eplan.help/.../api/2.9/Eplan.EplApi.EServicesu~
    ///   Eplan.EplApi.EServices.Ged.InsertInteraction~_ctor.html; Interaction.OnSuccess
    ///   (InteractionContext) — ...EServices.Ged.Interaction~OnSuccess.html;
    ///   InsertInteraction.InsertedItems — «Returns placements inserted by the
    ///   interaction» (...InsertInteraction~InsertedItems.html).
    /// - ВАЖНО (резолв брифа): в verbatim-примере — InsertedPlacements, в KB-снапшоте
    ///   2.9 — InsertedItems, PlacedObject нет вовсе. Оба имени читаются ТОЛЬКО
    ///   reflection-пробой с this.GetType() (порядок InsertedPlacements→InsertedItems),
    ///   ПРЯМЫХ вызовов этих свойств в коде НЕТ (CS1061 исключён независимо от того,
    ///   кто прав). Какое имя существует рантаймно — вопрос 2 спайка (факт в CollectedVia).
    /// - Function.SymbolVariant — KB-доказан (...DataModelu~Eplan.EplApi.DataModel.
    ///   Function~SymbolVariant.html) — читается напрямую.
    /// - InteractionContext: методы GetContextParameter/SetContextParameter, наследует
    ///   AddParameter (...InteractionContext_members.html); ИМЕНА параметров неизвестны
    ///   → дамп членов reflection + пробы GetContextParameter по именам из дампа (тоже
    ///   reflection — сигнатуры перегрузок не доказаны).
     /// Статика-буфер (PickDump/PlacedCount/CollectedVia/PlacedObjects) — OnSuccess не
     /// «вернуть», читаем из AnalyzeAction; с SPIKE-6 сброса в хуке не было (буфер
     /// наполнялся РУЧНОЙ вставкой символа до прогона), с SPIKE-7 сброс ВОЗВРАЩЁН —
     /// буфер держит захваты от НАШЕГО запуска ([PICK-EXEC7]), а история ручной вставки
     /// живёт в interaction_probe.log с timestamps. Буфер не потокозащищён —
     /// однопоточный UI-сценарий допустим.
    /// Провалы проб — строки в буфер, исключений наружу из OnSuccess не выпускаем.
    /// SPIKE-5 (rev.13.7): факты rev.13.4–13.6 — атрибутные имена
    /// TERMINAL_STRIP_PICK_SPIKE/…2 и имя класса «SymbolPickInteraction» дали
    /// Execute=False; системное «XEGedIaInsertSymRef» → True, диалог открылся,
    /// размещение состоялось (механизм запуска из-под нашего действия ЖИВ), но наш
    /// OnSuccess не вызван. Гипотеза SPIKE-5: регистрация проходит ТОЛЬКО по
    /// override-паттерну (Name==NameOfBaseInteraction==системное имя; docs eplan.help
    /// 2.9 Interactions.html, рабочий боевой пример EasyEPLANner XMIaInsertMacro) —
    /// атрибут ниже переведён на override-паттерн. РИСК: пока аддин загружен, ВСЕ
    /// вставки символа в EPLAN идут через наш OnSuccess — base.OnSuccess ПЕРВЫМ
    /// (штатная вставка не ломается), захват пассивен (статика-буфер); уборка
    /// с SPIKE-7 снова активна (размещение из хука — проба); С SPIKE-8
    /// IsAutorestartEnabled=false ГЛОБАЛЬНО — обычная вставка символа в EPLAN тоже
    /// не зацикливается (одиночное размещение).
    /// SPIKE-6 (rev.13.8): факт rev.13.7 — запуск системного имени 'XEGedIaInsertSymRef'
    /// дал True (диалог + размещение), но наш OnSuccess НЕ вызван даже при override-
    /// атрибуте (Name==NameOfBaseInteraction, Ordinal=50, Prio=20) — маршрутизации в
    /// класс нет. Гипотезы SPIKE-6: (1) сборка при старте EPLAN НЕ сканируется на
    /// InteractionAttribute — класс вообще не инстанцируется/не регистрируется;
    /// (2) override маршрутизирует только ВНУТРЕННИЕ события EPLAN (ручная вставка
    /// символа пользователем), а запуск экшеном по имени идёт в ядро напрямую (наводка
    /// EasyEPLANner: их override срабатывает на внутреннюю интеракцию). Пробы —
    /// файловые, append-only: .cctor/.ctor/OnStart/OnSuccess пишутся в
    /// interaction_probe.log (метод Probe), читается хуком
    /// AnalyzeAction.RunSymbolPickSpike; статика-буфер наполняется РУЧНОЙ вставкой
     /// символа ДО прогона. Визуальный канал проверки интеракций — окно Ctrl+\ в EPLAN
     /// (последние действия/интеракции).
     /// SPIKE-7 (rev.13.9): факты SPIKE-6 — ручная вставка маршрутизируется в класс
     /// (OnStart/OnSuccess), InsertedPlacements (Public) 1 эл., тройка напрямую
     /// (SymbolLibraryName/SymbolName/VariantNr), placed = EObjects.Cable, после
     /// OnSuccess — авторестарт OnStart; XGedStartInteractionAction bypass-ит override.
     /// Гипотеза SPIKE-7: GUI-экшен XEGActionInsertSymRef маршрутизируется через
     /// override (запуск из хука, пробы/захват/уборка восстановлены).
     /// SPIKE-8 (rev.13.10): гипотеза SPIKE-7 подтверждена прогоном (GUI-экшен
     /// маршрутизируется через override — OnStart в пробах). Факт п.82: Execute
     /// GUI-экшена ВОЗВРАЩАЕТСЯ ДО завершения размещения — символ живёт на курсоре,
     /// интеракция асинхронна относительно нашего действия (после отмены MainDialog
     /// вставка жива). Docs 2.9 (eplan.help, Interaction class, проверено 26.09):
     /// OnCancel() — «Is called after abort of interaction»; OnStop() — «called
     /// before an interaction stops, it is called after OnSuccess() or after
     /// OnCancel()» = ЕДИНЫЙ терминатор обеих веток; IsAutorestartEnabled —
     /// «Returns true, if interaction should restart after stop» (virtual get-only).
     /// Цель SPIKE-8: цикл ожидания хука до OnStop — обе ветки (размещение и
     /// отмена) терминируются одним сигналом StopSignaled; autorestart выключен
     /// override'ом (факт rev.13.8: раньше был повторный OnStart и требовался Esc) —
     /// одиночное размещение завершает интеракцию без Esc.
     /// SPIKE-9 (rev.13.11): факт п.84 — после размещения EPLAN открывает ДИАЛОГ
     /// СВОЙСТВ символа: пауза OnSuccess enter → base завершён = 2.56 с (диалог
     /// живёт ВНУТРИ base.OnSuccess; пользователь закрыл OK, и только потом
     /// пришёл OnStop). Диалог + неочевидность пробного размещения = лишние
     /// действия. Гипотеза SPIKE-9: в режиме захвата (флаг CaptureActive) НЕ
     /// вызывать base.OnSuccess — диалог не появится, а тройка из
     /// InsertedPlacements и размещённый объект останутся доступны (для
     /// [PICK-CLEAN] Remove). Флаг нужен, чтобы обычная (вне кнопки) вставка
     /// символа осталась штатной — с диалогом. Риски (проверяются прогоном):
     /// без base.OnSuccess размещение может не финализироваться — индикаторы
     /// в логе: [PICK-CLEAN] удалено 0/1, IsValid=false, PlacedObjects пуст
     /// при StopSignaled=True. Подсказка пользователю — PromptForStatusLine
     /// в OnStart (KB 2.9: свойство Interaction «Prompt for status line»,
     /// публичное settable string — www.eplan.help/.../api/2.9/Eplan.EplApi.
     /// EServicesu~Eplan.EplApi.EServices.Ged.Interaction~PromptForStatusLine.
     /// html; паттерн присваивания — docs-пример Interactions.html
     /// this.PromptForStatusLine = "select Terminals" в OnStart). Вне
     /// CaptureActive — полностью штатное поведение (base.OnSuccess
     /// вызывается, диалог на месте).</summary>
    [InteractionAttribute(Name = "XEGedIaInsertSymRef",
                          NameOfBaseInteraction = "XEGedIaInsertSymRef",
                          Ordinal = 50,
                          Prio = 20)]
    public class SymbolPickInteraction : InsertInteraction
    {
        /// <summary>Дамп фактов прогона (строки для [PICK-DUMP]); кап защиты лога ниже.</summary>
        public static readonly List<string> PickDump = new List<string>();
        /// <summary>Число элементов собранной коллекции размещённого.</summary>
        public static int PlacedCount = 0;
        /// <summary>Через какое имя коллекции собрано (fact вопроса 2) или диагноз отказа.</summary>
        public static string CollectedVia = "<не собрано>";
        /// <summary>Размещённые Placement'ы для уборки [PICK-CLEAN] (Remove по IsValid).</summary>
        public static readonly List<Placement> PlacedObjects = new List<Placement>();
        /// <summary>SPIKE-8 (rev.13.10): сигнал завершения интеракции — сбрасывается
        /// хуком AnalyzeAction.RunSymbolPickSpike перед запуском GUI-экшена,
        /// выставляется в OnStop (единый терминатор обеих веток по docs 2.9: после
        /// OnSuccess ИЛИ OnCancel). volatile — цикл ожидания хука обязан увидеть
        /// запись (C#5 допускает volatile на static bool).</summary>
        public static volatile bool StopSignaled = false;

        /// <summary>SPIKE-9 (rev.13.11): РЕЖИМ ЗАХВАТА. Включается хуком/кнопкой
        /// (AnalyzeAction.RunSymbolPickSpike) перед запуском GUI-экшена, снимается
        /// после цикла ожидания (OnSuccess уже отработал в режиме захвата; дампы/
        /// уборка флаг не используют). В режиме: OnSuccess НЕ вызывает base —
        /// диалог свойств после размещения не открывается (факт п.84: диалог
        /// внутри base.OnSuccess, пауза 2.56 с), захват тройки и размещения для
        /// [PICK-CLEAN] — обычным кодом. ВНЕ режима — полностью штатное поведение:
        /// base.OnSuccess вызывается, обычная (не из-под кнопки) вставка символа
        /// идёт как раньше, с диалогом свойств. volatile — тот же паттерн, что
        /// StopSignaled.</summary>
        public static volatile bool CaptureActive = false;

        // Защита лога: больше этого числа строк в буфер не пишем (счётчик опущенных — в конце).
        private const int DUMP_CAP = 400;
        private static int s_nSuppressed = 0;

        /// <summary>SPIKE-6: проба статического конструктора — срабатывает при ПЕРВОМ
        /// касании класса (если EPLAN сканирует сборку на InteractionAttribute при
        /// загрузке — уже на старте). Факт — в interaction_probe.log (append-only).</summary>
        static SymbolPickInteraction()
        {
            Probe(".cctor (статический конструктор — класс затронут)");
        }

        /// <summary>SPIKE-6: проба инстанцирования класса (базовый InsertInteraction()
        /// есть — KB: .../Eplan.EplApi.EServices.Ged.InsertInteraction~_ctor.html).</summary>
        public SymbolPickInteraction()
        {
            Probe(".ctor (инстанцирование класса)");
        }

        /// <summary>SPIKE-6: файловая проба жизненного цикла (append-only, НИКОГДА не
        /// бросает наружу — весь метод под try/catch, при отказе молча глотаем,
        /// интеракцию не ломаем). Путь — первый доступный каталог из кандидатов (в том
        /// же порядке, что логгер действия: DiagnosticLogger.LOG_DIR_CANDIDATES + temp),
        /// файл "interaction_probe.log"; строки вида "HH:mm:ss.fff [IA-PROBE] <что>".
        /// Читается хуком AnalyzeAction.RunSymbolPickSpike ([IA-PROBE] в основном логе).</summary>
        private static void Probe(string strWhat)
        {
            try
            {
                // Кандидаты — единый источник DiagnosticLogger.LOG_DIR_CANDIDATES
                // (writer здесь и reader в AnalyzeAction не разойдутся) + %TEMP%.
                string[] arrDirs = new string[DiagnosticLogger.LOG_DIR_CANDIDATES.Length + 1];
                DiagnosticLogger.LOG_DIR_CANDIDATES.CopyTo(arrDirs, 0);
                arrDirs[arrDirs.Length - 1] = System.IO.Path.GetTempPath();
                string strDir = null;
                foreach (string strCandidate in arrDirs)
                {
                    if (System.IO.Directory.Exists(strCandidate)) { strDir = strCandidate; break; }
                }
                if (strDir == null) return;
                string strLine = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                    " [IA-PROBE] " + strWhat + Environment.NewLine;
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(strDir, "interaction_probe.log"), strLine);
            }
            catch (Exception)
            {
                // Отказ пробы молчалив: интеракцию не ломать (контракт SPIKE-6).
            }
        }

        /// <summary>SPIKE-6: проба СТАРТА интеракции — OnStart вызывается, если
        /// маршрутизация действительно попала в наш класс. Паттерн — docs-пример
        /// DeleteTerminalsInteraction/DerivedSymbolInsertInteraction (KB:
        /// .../Eplan.EplApi.EServices.Ged.Interaction~OnStart.html — «public virtual
        /// RequestCode OnStart(InteractionContext)»); RequestCode — из
        /// Eplan.EplApi.EServices.Ged (using есть). После пробы — base.OnStart
        /// (штатное поведение не ломаем). SPIKE-9 (rev.13.11): в режиме захвата
        /// ДО базовой пробы выставляется PromptForStatusLine (подсказка в строке
        /// состояния: размещение пробное и будет удалено) + отдельная строка-проба
        /// — лог различает режимы; базовая проба OnStart остаётся.</summary>
        public override RequestCode OnStart(InteractionContext pContext)
        {
            if (CaptureActive)
            {
                // SPIKE-9: KB 2.9 — Interaction.PromptForStatusLine, публичное
                // settable string (страница ...~Eplan.EplApi.EServices.Ged.
                // Interaction~PromptForStatusLine.html — «Prompt for status line»,
                // C++/CLI-сигнатура содержит void set(String^value); паттерн
                // присваивания — docs-пример Interactions.html в OnStart).
                this.PromptForStatusLine = "Выберите точку размещения: символ будет удалён автоматически (это выбор символа для отчёта)";
                Probe("OnStart capture-mode: PromptForStatusLine выставлен");
            }
            Probe("OnStart — интеракция ЗАПУЩЕНА (маршрутизация в наш класс)");
            return base.OnStart(pContext);
        }

        /// <summary>SPIKE-8 (rev.13.10): docs 2.9 (eplan.help,
        /// .../Eplan.EplApi.EServices.Ged.Interaction~IsAutorestartEnabled.html):
        /// «Returns true, if interaction should restart after stop» — virtual
        /// get-only. Override → false: убираем авторестарт (факт rev.13.8: после
        /// OnSuccess шёл повторный OnStart и требовался Esc); теперь одиночное
        /// размещение завершает интеракцию — OnStop приходит без Esc.</summary>
        public override bool IsAutorestartEnabled
        {
            get { return false; }
        }

        /// <summary>SPIKE-8 (rev.13.10): docs 2.9 (eplan.help,
        /// .../Eplan.EplApi.EServices.Ged.Interaction~OnCancel.html): «Is called
        /// after abort of interaction» (отмена/Esc) — сигнатура void OnCancel()
        /// подтверждена документацией. Только проба: сигнал завершения ставит
        /// OnStop (он вызывается после OnSuccess ИЛИ после OnCancel).</summary>
        public override void OnCancel()
        {
            Probe("OnCancel — отмена (abort)");
            base.OnCancel();
        }

        /// <summary>SPIKE-8 (rev.13.10): docs 2.9 (eplan.help,
        /// .../Eplan.EplApi.EServices.Ged.Interaction~OnStop.html): «called before
        /// an interaction stops, it is called after OnSuccess() or after OnCancel()»
        /// — ЕДИНЫЙ терминатор обеих веток. Порядок: проба → StopSignaled=true →
        /// base: флаг ДО base, чтобы цикл ожидания хука вышел гарантированно
        /// (base.OnStop по докам не бросает; даже если бросит — флаг уже стоит).</summary>
        public override void OnStop()
        {
            Probe("OnStop — остановка (итог)");
            SymbolPickInteraction.StopSignaled = true;
            base.OnStop();
        }

        /// <summary>SPIKE-3: логика захвата ВЫНЕСЕНА в общий статический CaptureOnSuccess
        /// (SPIKE-5 rev.13.7: второй класс удалён — факт rev.13.5 в summary.md). Шаблон из
        /// KB-базы (EasyEPLANner InsertMacrosInteraction.cs, 2.9): base.OnSuccess ПЕРВЫМ.
        /// SPIKE-6 (rev.13.8): файловые пробы входа и завершения base — факт вызова
        /// (или невызова) OnSuccess при РУЧНОЙ вставке символа. SPIKE-9 (rev.13.11):
        /// в режиме захвата (CaptureActive) base.OnSuccess ПРОПУСКАЕТСЯ — он открывает
        /// диалог свойств размещённого символа (факт п.84: пауза 2.56 с внутри base,
        /// OnStop только после закрытия диалога); пробный символ будет удалён — его
        /// свойства не нужны. Вне режима — как раньше (base первым, штатный диалог).</summary>
        public override void OnSuccess(InteractionContext result)
        {
            Probe("OnSuccess enter");
            if (CaptureActive)
            {
                // SPIKE-9: base.OnSuccess НЕ вызываем — он открывает диалог свойств
                // (факт п.84: пауза 2.56 с на диалог). Пробный символ будет удалён,
                // его свойства не нужны. Риск (проверяется прогоном): без base размещение
                // может не финализироваться — см. [PICK-CLEAN]/IsValid в логе.
                Probe("OnSuccess capture-mode: base ПРОПУЩЕН");
                CaptureOnSuccess(this, result);
            }
            else
            {
                base.OnSuccess(result);
                Probe("OnSuccess: base завершён");
                CaptureOnSuccess(this, result);
            }
        }

        /// <summary>Общий захватчик (SPIKE-3): весь свой код под try/catch, сбои — строки
        /// в буфер; наружу исключений не выпускаем. Какой класс захватил — Buf-строка +
        /// префикс CollectedVia (факт «1-й vs 2-й вариант базового имени»).</summary>
        public static void CaptureOnSuccess(InsertInteraction oIa, InteractionContext result)
        {
            string strClass = oIa.GetType().Name;
            s_nSuppressed = 0;
            CollectedVia = "класс " + strClass + " — коллекция не собрана";
            Buf("OnSuccess от класса: " + strClass);
            try
            {
                // (1) Коллекция размещённого — reflection-проба двух имён (KB vs пример).
                System.Array arrInserted = ReadInsertedCollection(oIa);
                if (arrInserted == null)
                {
                    Buf("коллекция не получена ни по одному имени — см. пробы выше");
                }
                else
                {
                    int nIdx = 0;
                    foreach (object oItem in arrInserted)
                    {
                        nIdx++;
                        PlacedCount++;
                        if (oItem == null)
                        {
                            Buf("placed#" + Idx(nIdx) + ": null");
                            continue;
                        }
                        Buf("placed#" + Idx(nIdx) + ": " + oItem.GetType().FullName +
                            " | ToString: " + SafeToString(oItem));
                        Placement oPl = oItem as Placement;
                        if (oPl != null) PlacedObjects.Add(oPl);
                        else Buf("placed#" + Idx(nIdx) + ": не Placement — в уборку не берём");
                        // (2) Function.SymbolVariant — KB-доказан, прямое чтение; дамп свойств.
                        Function oFunc = oItem as Function;
                        if (oFunc != null) DumpSymbolVariant(oFunc, nIdx);
                        else Buf("placed#" + Idx(nIdx) + ": не Function — SymbolVariant не читали");
                    }
                }

                // (3) InteractionContext — вторая дорога чтения выбора: дамп членов +
                // reflection-пробы GetContextParameter по именам из дампа и типовым.
                DumpContext(result);
            }
            catch (Exception oEx)
            {
                Buf("OnSuccess fatally: " + oEx.GetType().Name + ": " + oEx.Message);
            }
            if (s_nSuppressed > 0)
                Buf("<кап " + Idx(DUMP_CAP) + ", опущено строк: " + Idx(s_nSuppressed) + ">");
        }

        // --- пробы коллекции ---

        /// <summary>Reflection-проба «InsertedPlacements» → «InsertedItems» (порядок по
        /// брифу) с oIa.GetType() (SPIKE-3: метод статичен — общий для классов 1/2;
        /// свойства НЕ доказаны согласованно (пример vs KB) —
        /// прямых обращений нет. SPIKE-2 fix-1 (Minor-correct): пример читает член из
        /// производного класса неквалифицированно — он может быть protected/protected
        /// internal, а дефолтный GetProperty — public-only (ложное «не найдено»). Поэтому:
        /// сначала public-проба, при промахе — иерархия oIa.GetType()→BaseType (cap 5)
        /// с Public|NonPublic|Instance; геттер — GetGetMethod(true) (открытый находит и
        /// public); владелец (DeclaringType) и binding — в CollectedVia
        /// («класс &lt;X&gt;: &lt;имя&gt;@&lt;тип&gt; binding=Public|NonPublic») — факт того же вопроса 2.
        /// Первый непустой не-null-массив — успех; отказы имён — строки в буфер.</summary>
        private static System.Array ReadInsertedCollection(InsertInteraction oIa)
        {
            string strClass = oIa.GetType().Name;
            string[] arrNames = new string[] { "InsertedPlacements", "InsertedItems" };
            foreach (string strName in arrNames)
            {
                try
                {
                    bool bNonPublic = false;
                    PropertyInfo oProp = oIa.GetType().GetProperty(strName);
                    if (oProp == null)
                    {
                        Type oT = oIa.GetType();
                        for (int iLevel = 0; oProp == null && oT != null && iLevel < 5; iLevel++)
                        {
                            oProp = oT.GetProperty(strName,
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (oProp != null) bNonPublic = true;
                            else oT = oT.BaseType;
                        }
                    }
                    if (oProp == null)
                    {
                        Buf("проба «" + strName + "»: свойство не найдено (public и иерархия NonPublic, cap 5)");
                        continue;
                    }
                    MethodInfo oGet = oProp.GetGetMethod(true);
                    if (oGet == null)
                    {
                        Buf("проба «" + strName + "»: геттер недоступен (и NonPublic)");
                        continue;
                    }
                    string strOwner = (oProp.DeclaringType != null ? oProp.DeclaringType.Name : "?") +
                        " binding=" + (bNonPublic ? "NonPublic" : "Public");
                    object oValue = oGet.Invoke(oIa, null);
                    if (oValue == null)
                    {
                        Buf("проба «" + strName + "» @" + strOwner + ": вернула null");
                        CollectedVia = "класс " + strClass + ": " + strName + "@" + strOwner + "=null";
                        continue;
                    }
                    System.Array arr = oValue as System.Array;
                    if (arr == null)
                    {
                        CollectedVia = "класс " + strClass + ": " + strName + "@" + strOwner +
                            "=не-массив:" + oValue.GetType().FullName;
                        Buf("проба «" + strName + "»: " + CollectedVia);
                        continue;
                    }
                    CollectedVia = "класс " + strClass + ": " + strName + "@" + strOwner +
                        " (" + Idx(arr.Length) + " эл.)";
                    Buf("проба «" + strName + "»: успех — " + Idx(arr.Length) + " эл. @" + strOwner);
                    return arr;
                }
                catch (Exception oEx)
                {
                    Buf("проба «" + strName + "»: " + oEx.GetType().Name + ": " + oEx.Message);
                }
            }
            return null;
        }

        // --- Function.SymbolVariant ---

        /// <summary>SymbolVariant размещённой функции — KB-доказанное свойство (прямое
        /// чтение); reflection-дамп строковых/числовых/enum-свойств варианта (кап 40 на
        /// функцию) — facts для [PICK-ADAPT] (можно ли восстановить биб/имя/индекс).</summary>
        private static void DumpSymbolVariant(Function oFunc, int nIdx)
        {
            try
            {
                SymbolVariant oSv = oFunc.SymbolVariant;   // KB-доказан: прямое обращение
                if (oSv == null)
                {
                    Buf("placed#" + Idx(nIdx) + ": SymbolVariant=null (не символьная функция?)");
                    return;
                }
                Buf("placed#" + Idx(nIdx) + " SymbolVariant.ToString(): " + SafeToString(oSv) +
                    " | тип " + oSv.GetType().FullName);
                int nProps = 0;
                foreach (PropertyInfo oP in oSv.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (nProps >= 40)
                    {
                        Buf("placed#" + Idx(nIdx) + ": кап 40 свойств варианта");
                        break;
                    }
                    try
                    {
                        MethodInfo oGet = oP.GetGetMethod();
                        if (oGet == null || oP.GetIndexParameters().Length > 0) continue;
                        nProps++;
                        object oValue = oGet.Invoke(oSv, null);
                        if (oValue == null) continue;
                        Type oT = oValue.GetType();
                        if (oT == typeof(string) || oT.IsEnum || oT.IsPrimitive)
                            Buf("placed#" + Idx(nIdx) + " variant." + oP.Name + " = " + oValue.ToString());
                        else
                            Buf("placed#" + Idx(nIdx) + " variant." + oP.Name + " : " + oT.Name + " (не скаляр — не дампим)");
                    }
                    catch (Exception oEx)
                    {
                        Buf("placed#" + Idx(nIdx) + " variant." + oP.Name + " бросил " + oEx.GetType().Name);
                    }
                }
            }
            catch (Exception oEx)
            {
                Buf("placed#" + Idx(nIdx) + ": SymbolVariant бросил " + oEx.GetType().Name +
                    ": " + oEx.Message);
            }
        }

        // --- InteractionContext ---

        /// <summary>Дамп членов контекста (public instance: свойства с именами типов,
        /// методы без/1-аргументные) + reflection-пробы GetContextParameter по типовым и
        /// «похожим на параметры» именам. Имена параметров 2.9 НЕ доказаны — это вопрос 4
        /// спайка; сигнатуры GetContextParameter тоже (out-перегрузка vs string-перегрузка)
        /// — только GetMethod+Invoke. Кап 40 строк на контекст.</summary>
        private static void DumpContext(InteractionContext result)
        {
            try
            {
                if (result == null)
                {
                    Buf("context: null (OnSuccess без контекста)");
                    return;
                }
                Type oT = result.GetType();
                Buf("context тип: " + oT.FullName);
                int nLines = 0;
                List<string> lstProbeNames = new List<string>();
                foreach (PropertyInfo oP in oT.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (nLines >= 40) { Buf("context: кап 40 строк дампа"); break; }
                    nLines++;
                    Buf("ctx.prop " + oP.Name + " : " + oP.PropertyType.Name);
                    MethodInfo oGet = oP.GetGetMethod();
                    if (oGet != null && oP.PropertyType == typeof(string) &&
                        oP.GetIndexParameters().Length == 0)
                    {
                        try
                        {
                            object oValue = oGet.Invoke(result, null);
                            if (oValue != null && oValue.ToString().Length > 0)
                                Buf("ctx." + oP.Name + "='" + oValue.ToString() + "'");
                        }
                        catch (Exception oEx)
                        {
                            Buf("ctx." + oP.Name + " чтение бросило " + oEx.GetType().Name);
                        }
                    }
                    // имя похоже на параметр — кандидат в пробы GetContextParameter
                    if (oP.Name.IndexOf("Symbol", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        oP.Name.IndexOf("Variant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        oP.Name.IndexOf("Lib", StringComparison.OrdinalIgnoreCase) >= 0)
                        lstProbeNames.Add(oP.Name);
                }
                foreach (MethodInfo oM in oT.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (nLines >= 40) { Buf("context: кап 40 строк дампа"); break; }
                    nLines++;
                    Buf("ctx.method " + oM.Name + "(" + Idx(oM.GetParameters().Length) + " арг.) : " + oM.ReturnType.Name);
                }
                // Пробы GetContextParameter: типовые имена + из дампа (уникальные).
                // SPIKE-2 fix-1 (hygiene): кап 30 проб, обрезание — пометкой в дампе.
                string[] arrTyped = new string[] { "Name", "SymbolLibName", "SymbolId", "VariantId" };
                foreach (string strName in arrTyped)
                    if (lstProbeNames.IndexOf(strName) < 0) lstProbeNames.Add(strName);
                int nProbed = 0;
                foreach (string strName in lstProbeNames)
                {
                    if (nProbed >= 30)
                    {
                        Buf("ctx: кап 30 проб GetContextParameter — " +
                            Idx(lstProbeNames.Count - 30) + " имён обрезано по капу");
                        break;
                    }
                    nProbed++;
                    Buf("ctx.GetParameter('" + strName + "') → " + ReadContextParam(result, strName));
                }
            }
            catch (Exception oEx)
            {
                Buf("context дамп бросил " + oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        /// <summary>Reflection-проба GetContextParameter: сначала (string, out string),
        /// затем (string)→string; нет метода/бросил — маркер &lt;...&gt; (прямых вызовов нет —
        /// сигнатуры не доказаны).</summary>
        private static string ReadContextParam(object oCtx, string strName)
        {
            try
            {
                Type oT = oCtx.GetType();
                MethodInfo oOutM = oT.GetMethod("GetContextParameter",
                    new Type[] { typeof(string), typeof(string).MakeByRefType() });
                if (oOutM != null)
                {
                    object[] arrArgs = new object[] { strName, string.Empty };
                    object oRes = oOutM.Invoke(oCtx, arrArgs);
                    string strOut = arrArgs[1] == null ? string.Empty : arrArgs[1].ToString();
                    if (oRes is bool && !(bool)oRes && strOut.Length == 0) return "<не установлен>";
                    return "'" + strOut + "'";
                }
                MethodInfo oSimpleM = oT.GetMethod("GetContextParameter", new Type[] { typeof(string) });
                if (oSimpleM != null && oSimpleM.ReturnType == typeof(string))
                {
                    object oRes = oSimpleM.Invoke(oCtx, new object[] { strName });
                    return oRes == null ? "<null>" : "'" + oRes.ToString() + "'";
                }
                return "<GetContextParameter недоступен>";
            }
            catch (Exception oEx)
            {
                return "<бросил " + oEx.GetType().Name + ">";
            }
        }

        // --- буфер/формат (паттерн логов: InvariantCulture) ---

        private static void Buf(string strLine)
        {
            if (PickDump.Count < DUMP_CAP) PickDump.Add(strLine);
            else s_nSuppressed++;
        }

        private static string Idx(int n)
        {
            return n.ToString(CultureInfo.InvariantCulture);
        }

        private static string SafeToString(object oTarget)
        {
            try { return oTarget.ToString(); }
            catch (Exception oEx) { return "<ToString бросил " + oEx.GetType().Name + ">"; }
        }
    }
}

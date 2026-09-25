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
    /// «вернуть», читаем из AnalyzeAction после синхронного Execute; сброс перед запуском.
    /// Буфер не потокозащищён — однопоточный UI-сценарий спайка допустим.
    /// Провалы проб — строки в буфер, исключений наружу из OnSuccess не выпускаем.</summary>
    [InteractionAttribute(Name = "TERMINAL_STRIP_PICK_SPIKE",
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

        // Защита лога: больше этого числа строк в буфер не пишем (счётчик опущенных — в конце).
        private const int DUMP_CAP = 400;
        private static int s_nSuppressed = 0;

        /// <summary>Шаблон из KB-базы (EasyEPLANner InsertMacrosInteraction.cs, 2.9):
        /// base.OnSuccess ПЕРВЫМ, весь свой код — под try/catch, сбои — строки в буфер.</summary>
        public override void OnSuccess(InteractionContext result)
        {
            base.OnSuccess(result);
            s_nSuppressed = 0;
            try
            {
                // (1) Коллекция размещённого — reflection-проба двух имён (KB vs пример).
                System.Array arrInserted = ReadInsertedCollection();
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
        /// брифу) с this.GetType(): свойства НЕ доказаны согласованно (пример vs KB) —
        /// прямых обращений нет. SPIKE-2 fix-1 (Minor-correct): пример читает член из
        /// производного класса неквалифицированно — он может быть protected/protected
        /// internal, а дефолтный GetProperty — public-only (ложное «не найдено»). Поэтому:
        /// сначала public-проба, при промахе — иерархия this.GetType()→BaseType (cap 5)
        /// с Public|NonPublic|Instance; геттер — GetGetMethod(true) (открытый находит и
        /// public); владелец (DeclaringType) и binding — в CollectedVia
        /// («&lt;имя&gt;@&lt;тип&gt; binding=Public|NonPublic») — факт того же вопроса 2.
        /// Первый непустой не-null-массив — успех; отказы имён — строки в буфер.</summary>
        private System.Array ReadInsertedCollection()
        {
            string[] arrNames = new string[] { "InsertedPlacements", "InsertedItems" };
            foreach (string strName in arrNames)
            {
                try
                {
                    bool bNonPublic = false;
                    PropertyInfo oProp = this.GetType().GetProperty(strName);
                    if (oProp == null)
                    {
                        Type oT = this.GetType();
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
                    object oValue = oGet.Invoke(this, null);
                    if (oValue == null)
                    {
                        Buf("проба «" + strName + "» @" + strOwner + ": вернула null");
                        CollectedVia = strName + "@" + strOwner + "=null";
                        continue;
                    }
                    System.Array arr = oValue as System.Array;
                    if (arr == null)
                    {
                        CollectedVia = strName + "@" + strOwner + "=не-массив:" + oValue.GetType().FullName;
                        Buf("проба «" + strName + "»: " + CollectedVia);
                        continue;
                    }
                    CollectedVia = strName + "@" + strOwner + " (" + Idx(arr.Length) + " эл.)";
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

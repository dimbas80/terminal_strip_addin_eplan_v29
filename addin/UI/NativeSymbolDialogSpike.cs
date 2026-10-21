using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;

namespace MyEplanActions
{
    /// <summary>SPIKE (throwaway), H-4v2: вызов нативного диалога EPLAN «Вставить символ»
    /// (экшен XEGActionInsertSymRef) из-под аддина — ДО показа нашего MainDialog в UI-ветке.
    /// УДАЛИТЬ ПОСЛЕ РАЗБОРА ПРОГОНА (вердикт: нативный диалог vs наш SymbolBrowserDialog;
    /// до вердикта H-4-браузер/замеры/компенсация/DT — fallback-путь, не трогать).
    /// Brief: .superpowers/sdd/plan_stage8/task-h4v2-spike-brief.md
    /// Цель — три факта: (1) открывается ли диалог из UI-потока команды; (2) возвращает ли
    /// ctx параметры SymbolLibName/SymbolId/VariantId после закрытия (имена или ID — что
    /// именно); (3) [ACTDUMP] — какие «sym»/«insert»-экшены зарегистрированы.
    /// Механика вызова — verbatim-доказательство пользователя (бриф §«Доказательство»):
    /// new ActionManager() → FindAction("XEGActionInsertSymRef") → new ActionCallingContext()
    /// → action.Execute(ctx); все параметры опциональны — не заполняем, открывается полный
    /// диалог. ПРОЧТЕНИЕ ctx — только reflection-проба GetParameter (сигнатура-перегрузки
    /// (string,out string) vs (string)→string на 2.9 не доказаны — прямые вызовы запрещены
    /// ruling'ом). После Execute ничего, кроме ctx, не читаем (выделение/страницу НЕ трогать).
    /// README_spike (ожидания от прогона — задокументировано ruling'ом): после выбора символа
    /// EPLAN может перейти в РЕЖИМ РАЗМЕЩЕНИЯ на странице — это ожидаемое поведение спайка:
    /// нажимать Esc/отменить размещение; отменить и сам диалог выбора — тоже штатно (тогда
    /// параметры ctx пустые — честный ответ на вопрос 2). Предвыбор в настройки НЕ пишем:
    /// сначала facts (критерий успеха = полные [SYMDLG]-строки в логе).
    /// Провал спайка пайплайн не останавливает: весь Run — под try/catch с WARN/INFO и
    /// продолжением штатного прогона. Гейт — AddInConfiguration.SpikeNativeInsertSymbol
    /// (константы throwaway-блока тоже удалить вместе с файлом и хуком).</summary>
    public static class NativeSymbolDialogSpike
    {
        /// <summary>Имя нативного экшена «вставить символьную ссылку» — из verbatim-примера
        /// пользователя; регистр/написание — как в примере (источник истины).</summary>
        private const string SPIKE_ACTION = "XEGActionInsertSymRef";

        /// <summary>Точка входа: UI-ветка AnalyzeAction.RunUi СТРОГО до показа MainDialog
        /// (headless Run() не вызывается никогда — ruling R8 сохраняется). oProject — для
        /// дешёвой пробы отображения SymbolId→имя; null — проба пропускается.</summary>
        public static void Run(Project oProject, DiagnosticLogger log)
        {
            log.Log("[INFO] [SYMDLG] --- SPIKE H-4v2: нативный диалог «" + SPIKE_ACTION +
                "» до MainDialog (throwaway; отмена/Esc — штатно) ---");
            try
            {
                ActionManager oManager = new ActionManager();

                // Вопрос 3: [ACTDUMP] — до вызова диалога (лог есть даже если Execute бросит).
                if (AddInConfiguration.SpikeActionDump)
                    DumpActions(oManager, log);

                // Вопросы 1–2: вызов — паттерн verbatim-примера пользователя (доказательство
                // API: FindAction/Execute(ActionCallingContext) — var, типы не именуем).
                var oAction = oManager.FindAction(SPIKE_ACTION);
                if (oAction == null)
                {
                    log.Log("[INFO] [SYMDLG] FindAction(«" + SPIKE_ACTION +
                        "») вернул null — экшен не зарегистрирован, диалог не вызывали");
                    return;
                }

                var oCtx = new ActionCallingContext();
                // Параметры намеренно НЕ заполняем (бриф: «если не заполнять, откроется
                // полный диалог выбора символа»).
                oAction.Execute(oCtx);

                // Чтение — только ctx (GetParameter — reflection-проба, ReadCtxParam).
                string strLib = ReadCtxParam(oCtx, "SymbolLibName");
                string strId = ReadCtxParam(oCtx, "SymbolId");
                string strVar = ReadCtxParam(oCtx, "VariantId");
                log.Log("[INFO] [SYMDLG] SymbolLibName='" + strLib + "' SymbolId='" + strId +
                    "' VariantId='" + strVar + "'");
                if (!HasValue(strLib) && !HasValue(strId) && !HasValue(strVar))
                    log.Log("[INFO] [SYMDLG] пустой ctx — выбор не сделан (отмена диалога/" +
                        "размещения — честный ответ на вопрос 2)");

                // Одна дешёвая проба: SymbolId→имя символа (паттерн цепочки A браузера:
                // SymbolLibrary(проект,имя) + reflection-перебор «Symbols»; неудача — просто
                // лог «не найдено», ничего не блокирует, в настройки НЕ пишем).
                if (HasValue(strLib) && HasValue(strId))
                    TryMapSymbolId(oProject, strLib, strId, log);
            }
            catch (Exception oEx)
            {
                // WARN допустим ruling'ом («WARN/INFO-лог и продолжение»): спайк-сбой —
                // диагностический сигнал, пайплайн НЕ останавливаем (Fail не зовём).
                log.Warn("[SYMDLG] SPIKE бросил " + oEx.GetType().Name + ": " + oEx.Message +
                    " — продолжаем штатный прогон (спайк не критичен)");
            }
        }

        // --- [ACTDUMP] ---

        /// <summary>[ACTDUMP]: перечисление экшенов. Прямые имена методов ActionManager
        /// НЕ доказаны — только reflection-проба: сначала surface-дамп public-методов
        /// без параметров (INFO одной строкой — facts для следующего шага), затем проба
        /// каждого «*Action*»-безаргументного метода, возвращающего string[]/IEnumerable:
        /// первый непустой результат фильтруем по «sym»/«insert» (OrdinalIgnoreCase),
        /// кап 200 строк формата «[ACTDUMP] &lt;имя&gt;». Метода нет/всё бросило — INFO
        /// «[ACTDUMP] перечисление недоступно».</summary>
        private static void DumpActions(ActionManager oManager, DiagnosticLogger log)
        {
            try
            {
                MethodInfo[] arrMethods = oManager.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.Instance);
                List<string> lstSurface = new List<string>();
                for (int i = 0; i < arrMethods.Length; i++)
                {
                    if (arrMethods[i].GetParameters().Length != 0) continue;
                    if (lstSurface.IndexOf(arrMethods[i].Name) < 0)
                        lstSurface.Add(arrMethods[i].Name);
                }
                lstSurface.Sort(StringComparer.OrdinalIgnoreCase);
                log.Log("[INFO] [ACTDUMP] ActionManager методы (безарг.): " +
                    string.Join(", ", lstSurface.ToArray()));

                foreach (MethodInfo oM in arrMethods)
                {
                    if (oM.GetParameters().Length != 0) continue;
                    if (oM.Name.IndexOf("Action", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (oM.ReturnType != typeof(string[]) &&
                        !typeof(IEnumerable).IsAssignableFrom(oM.ReturnType)) continue;
                    object oRes;
                    try { oRes = oM.Invoke(oManager, null); }
                    catch (Exception oEx)
                    {
                        log.Log("[INFO] [ACTDUMP] «" + oM.Name + "()» бросил " +
                            oEx.GetType().Name + " — следующая проба");
                        continue;
                    }
                    string[] arrNames = ToStringNames(oRes);
                    if (arrNames == null || arrNames.Length == 0) continue;
                    DumpMatches(arrNames, log);
                    return;   // первая успешная проба — fact получен, повторных не надо
                }
                log.Log("[INFO] [ACTDUMP] перечисление недоступно (ни одна безаргументная «*Action*»-проба не дала список)");
            }
            catch (Exception oEx)
            {
                log.Log("[INFO] [ACTDUMP] перечисление недоступно: " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        /// <summary>Фильтр «sym»/«insert» (OrdinalIgnoreCase), кап 200 строк,
        /// формат [ACTDUMP] &lt;имя&gt;; итог — всего/сум-инсерт (InvariantCulture).</summary>
        private static void DumpMatches(string[] arrNames, DiagnosticLogger log)
        {
            int nMatched = 0;
            for (int i = 0; i < arrNames.Length; i++)
            {
                string strName = arrNames[i];
                if (string.IsNullOrEmpty(strName)) continue;
                if (strName.IndexOf("sym", StringComparison.OrdinalIgnoreCase) < 0 &&
                    strName.IndexOf("insert", StringComparison.OrdinalIgnoreCase) < 0) continue;
                log.Log("[INFO] [ACTDUMP] " + strName);
                nMatched++;
                if (nMatched >= 200)
                {
                    log.Log("[INFO] [ACTDUMP] кап 200 — дальше не печатаем");
                    break;
                }
            }
            log.Log("[INFO] [ACTDUMP] экшенов всего " +
                arrNames.Length.ToString(CultureInfo.InvariantCulture) +
                ", с «sym»/«insert» " + nMatched.ToString(CultureInfo.InvariantCulture) + ".");
        }

        /// <summary>string[] или IEnumerable → string[] через ToString() (BCL-рефлексия,
        /// без EPLAN-имён членов).</summary>
        private static string[] ToStringNames(object oRes)
        {
            if (oRes == null) return null;
            string[] arr = oRes as string[];
            if (arr != null) return arr;
            IEnumerable oSeq = oRes as IEnumerable;
            if (oSeq == null) return null;
            List<string> lst = new List<string>();
            foreach (object oItem in oSeq)
            {
                if (oItem != null) lst.Add(oItem.ToString());
            }
            return lst.ToArray();
        }

        // --- чтение ctx (reflection-проба GetParameter) ---

        /// <summary>Reflection-проба чтения параметра: сначала перегрузка
        /// GetParameter(string, out string) (2-арг с byref), затем GetParameter(string)→string.
        /// Делать ПРЯМОЙ вызов запрещено (сигнатура 2.9 не доказана — ruling брифа). Исход,
        /// где метод не найден или бросок — маркеры «&lt;...&gt;», которые HasValue() считает
        /// пустыми.
        /// bool=false при пустом out-значении — тоже «пусто» (параметр не установлен).</summary>
        private static string ReadCtxParam(object oCtx, string strName)
        {
            try
            {
                Type oType = oCtx.GetType();
                MethodInfo oOutM = oType.GetMethod("GetParameter",
                    new Type[] { typeof(string), typeof(string).MakeByRefType() });
                if (oOutM != null)
                {
                    object[] arrArgs = new object[] { strName, string.Empty };
                    object oRes = oOutM.Invoke(oCtx, arrArgs);
                    string strOut = arrArgs[1] == null ? string.Empty : arrArgs[1].ToString();
                    if (oRes is bool && !(bool)oRes && strOut.Length == 0) return string.Empty;
                    return strOut;
                }
                MethodInfo oSimpleM = oType.GetMethod("GetParameter", new Type[] { typeof(string) });
                if (oSimpleM != null && oSimpleM.ReturnType == typeof(string))
                {
                    object oRes = oSimpleM.Invoke(oCtx, new object[] { strName });
                    return oRes == null ? string.Empty : oRes.ToString();
                }
                return "<GetParameter недоступен>";
            }
            catch (Exception oEx)
            {
                return "<чтение бросило " + oEx.GetType().Name + ">";
            }
        }

        /// <summary>Непустое значение = длина &gt; 0 и не маркер «&lt;...&gt;» (osмысленный fact).</summary>
        private static bool HasValue(string strValue)
        {
            return !string.IsNullOrEmpty(strValue) && strValue[0] != '<';
        }

        // --- одна дешёвая проба SymbolId→имя (цепочка A паттерна браузера) ---

        /// <summary>Проба: новый SymbolLibrary(oProject, strLib) (паттерн доказан:
        /// CableSymbolCreator/SymbolSizeMeasurer) → reflection-перебор «Symbols» (цепочка A
        /// SymbolBrowserDialog: имена НЕ доказаны — GetGetMethod().Invoke, БЕЗ
        /// PropertyInfo.GetValue — легаси-CS0618 не плодим) → сравнение ToString() элемента
        /// со строкой SymbolId (факт: чем является ID — именем или числом). Не найдено/бросок —
        /// INFO «отображение не найдено», ничего не блокирует.</summary>
        private static void TryMapSymbolId(Project oProject, string strLib, string strId, DiagnosticLogger log)
        {
            if (oProject == null)
            {
                log.Log("[INFO] [SYMDLG] отображение SymbolId пропущено (проект null в спайке)");
                return;
            }
            try
            {
                SymbolLibrary oLibrary = new SymbolLibrary(oProject, strLib);
                PropertyInfo oProp = oLibrary.GetType().GetProperty("Symbols");
                if (oProp == null)
                {
                    log.Log("[INFO] [SYMDLG] отображение SymbolId='" + strId + "' не найдено " +
                        "(у DataModel SymbolLibrary нет свойства «Symbols» — проба)");
                    return;
                }
                MethodInfo oGet = oProp.GetGetMethod();
                if (oGet == null)
                {
                    log.Log("[INFO] [SYMDLG] отображение SymbolId='" + strId +
                        "' не найдено (геттер «Symbols» недоступен — проба)");
                    return;
                }
                object oValue = oGet.Invoke(oLibrary, null);
                System.Array arrItems = oValue as System.Array;
                if (arrItems == null)
                {
                    log.Log("[INFO] [SYMDLG] отображение SymbolId='" + strId +
                        "' не найдено («Symbols» вернул не-массив: " +
                        (oValue == null ? "null" : oValue.GetType().Name) + ")");
                    return;
                }
                foreach (object oItem in arrItems)
                {
                    if (oItem == null) continue;
                    string strName = ProbeName(oItem);
                    if (!string.IsNullOrEmpty(strName) && strName == strId)
                    {
                        log.Log("[INFO] [SYMDLG] SymbolId='" + strId + "' → символ '" + strName +
                            "' в библиотеке '" + strLib + "' (совпало с именем элемента)");
                        return;
                    }
                }
                log.Log("[INFO] [SYMDLG] отображение SymbolId='" + strId + "' в библиотеке '" +
                    strLib + "' не найдено (среди " +
                    arrItems.Length.ToString(CultureInfo.InvariantCulture) +
                    " имён элементов; вероятно ID числовой — это fact)");
            }
            catch (Exception oEx)
            {
                log.Log("[INFO] [SYMDLG] отображение SymbolId='" + strId +
                    "' не найдено (проба бросила " + oEx.GetType().Name + ": " + oEx.Message + ")");
            }
        }

        /// <summary>Имя элемента библиотеки: reflection Name→IdentifyingName (get-инвока),
        /// отказ — ToString(). Только чтение, без PropertyInfo.GetValue (CS0618).</summary>
        private static string ProbeName(object oItem)
        {
            try
            {
                PropertyInfo oProp = oItem.GetType().GetProperty("Name");
                if (oProp != null)
                {
                    MethodInfo oGet = oProp.GetGetMethod();
                    if (oGet != null)
                    {
                        object oValue = oGet.Invoke(oItem, null);
                        if (oValue != null && oValue.ToString().Length > 0) return oValue.ToString();
                    }
                }
                return oItem.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}

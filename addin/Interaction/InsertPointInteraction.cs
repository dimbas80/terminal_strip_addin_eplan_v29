using System;
using System.Collections.Generic;
using System.Globalization;
using Eplan.EplApi.Base;                 // PointD (KB 2.9: «PointD Structure»)
using Eplan.EplApi.DataModel;            // StorableObject, IsValid
using Eplan.EplApi.EServices.Ged;        // Interaction, RequestCode, InteractionContext, Position

namespace MyEplanActions
{
    /// <summary>Интеракция выбора точки вставки отчёта (Этап 8, H-5/H-6, rev.15.0;
    /// spec 2026-09-25-fase-h-ui-design.md §6): запускается из TryPickInsertPoint
    /// (AnalyzeAction.RunUi) экшеном XGedStartInteractionAction /Name:TSA_INSERT_POINT,
    /// ждёт ОДИН клик пользователя (RequestCode.Point), координату отдаёт через статику
    /// (CapturedX/CapturedY + Captured), рамка-призрак под курсором — SetStaticCursor
    /// (объект кладёт хук в PendingGhost ПОСЛЕ создания, ПОСЛЕ этого — запуск экшена:
    /// SetStaticCursor вызывается уже в OnStart интеракции). По паттерну
    /// addin/Interaction/SymbolPickInteraction.cs (файл-проба append-only, буфер-кап,
    /// try/catch на всех callback'ах — исключение наружу НЕ выпускать).
    /// KB-факты API 2.9 (www.eplan.help/en-us/infoportal/content/api/2.9/):
    /// - XGedStartInteractionAction — «starts an interaction of the graphical editor»,
    ///   Name = «name of the interaction which should be started», пример
    ///   «XGedStartInteractionAction /Name:XGedIaSelectRectangle» (страница
    ///   XGedStartInteractionAction .html; примечание «The action can be used only
    ///   interactively» — поэтому ПЕРЕД запуском ShowDialog уже вернул OK и модального
    ///   окна нет; поведение — гипотеза H-5, проверяется прогоном);
    /// - RequestCode — enum [Flags()], члены (страница ...Ged.RequestCode.html):
    ///   Nothing=0, Stop=1, Select=2, Point=16, Length=128, Angle=256,
    ///   Success=1024 («input of data was successful»), Abort=512 и др. Для нашeй
    ///   точечной интеракции: OnStart возвращает Success|Point — интеракция запущена
    ///   И ждёт координаты; ClickOnPoint возвращает Success — завершение с успехом;
    /// - Interaction.OnPoint(Position) — docs-пример: OnPoint читает
    ///   oPosition.FinalPosition, после нужных точек возвращает Success
    ///   (страница ...Ged.Interaction.html, класс MyInteraction);
    /// - PromptForStatusLine — публичное settable string (страница
    ///   ...Ged.Interaction~PromptForStatusLine.html; используется в
    ///   SymbolPickInteraction.OnStart, факт rev.13.11);
    /// - SetStaticCursor(StorableObject, PointD) — «Sets StorableObject, that will
    ///   be temporary drawn as Cursor Representation», pntStartPos = «The beginning
    ///   of the new coordinate system in which placement is been drawn», Remarks:
    ///   «Object can be transient (not stored in database)» (страница
    ///   ...Ged.Interaction~SetStaticCursor(StorableObject,PointD).html);
    /// - IsAutorestartEnabled=false — убирает авторестарт после OnStop
    ///   (факт rev.13.8, паттерн SymbolPickInteraction);
    /// - OnCancel — «Is called after abort of interaction»;
    ///   OnStop — «called before an interaction stops, it is called after
    ///   OnSuccess() or after OnCancel()» = ЕДИНЫЙ терминатор обеих веток
    ///   (факт SPIKE-11: OnStop пришёл в обоих сценариях прогона rev.13.13).
    /// Запуск-паттерн (страница ...Ged.Interaction.html, docs-пример KB):
    /// ActionManager().FindAction("XGedStartInteractionAction") →
    /// ActionCallingContext.AddParameter("Name", "...") → Execute(ctx) —
    /// подтверждён примером KB напрямую (не CLI-строкой).
    /// ИЗВЕСТНОЕ ОГРАНИЧЕНИЕ (M2.4, ревью rev.15.0; KB 2.9):
    /// OnDeactivate — «Is called after start of a new interaction with same or
    /// higher priority. In this case the current interaction is deactivated
    /// until the new interaction stops» (...Ged.Interaction~OnDeactivate.html);
    /// OnReactivate — «Is called after stop of the current interaction and this
    /// interaction is reactivated» (...Ged.Interaction~OnReactivate.html).
    /// Значит, при повторном «Создать» НОВЫЙ запуск TSA_INSERT_POINT
    /// деактивирует ещё активную предыдущую (OnStop старой НЕ придёт — её цикл
    /// ожидания уже вышел по таймауту с WARN «интеракция может остаться
    /// активной») и при стопе новой старая может РЕАКТИВИРОВАТЬСЯ (OnReactivate).
    /// Факт ловится пробой [IPING-PROBE]; приём в прогоне — пользователь
    /// закрывает предыдущую интеракцию кликом/Esc до следующего «Создать».</summary>
    [InteractionAttribute(Name = "TSA_INSERT_POINT")]
    public class InsertPointInteraction : Interaction
    {
        /// <summary>ЕДИНЫЙ терминатор (docs 2.9: OnStop — после OnSuccess ИЛИ OnCancel).
        /// volatile — цикл ожидания хука обязан увидеть запись (C#5 допускает
        /// volatile на static bool — паттерн SymbolPickInteraction.StopSignaled).</summary>
        public static volatile bool Done = false;
        /// <summary>OnPoint пришёл: координаты в CapturedX/CapturedY. Сознательно
        /// НЕ volatile (m4, ревью rev.15.0): события интеракции диспатчатся в тот же
        /// UI-поток (DoEvents в цикле ожидания хука), модель однопоточная.</summary>
        public static bool Captured = false;
        /// <summary>Пользователь отменил (Esc / abort); тоже сознательно неволатильна
        /// — та же однопоточная модель диспатчинга, что у Captured.</summary>
        public static bool Cancelled = false;
        /// <summary>Захваченная точка (FinalPosition в координатах страницы).</summary>
        public static double CapturedX = 0.0;
        public static double CapturedY = 0.0;
        /// <summary>Буфер диагностики (кап DUMP_CAP) — слив в лог хуком ([IPING-DUMP]).</summary>
        public static readonly List<string> IPingDump = new List<string>();
        /// <summary>Рамка-призрак: кладёт хук (TryPickInsertPoint) ПЕРЕД запуском
        /// экшена; OnStart пробует SetStaticCursor по IsValid. PolyLine : StorableObject.</summary>
        public static StorableObject PendingGhost = null;

        /// <summary>Единственный активный экземпляр интеракции (M2.1, ревью
        /// rev.15.0) — для TryClearCursor из хука при таймауте: курсорную
        /// отрисовку призрака нужно снять ДО RemoveGhost, иначе курсор продолжает
        /// рисовать уже удалённый PolyLine. Присваивается в OnStart (интеракция
        /// инстанцируется EPLAN — поле заполняется только из OnStart).</summary>
        private static InsertPointInteraction s_instance = null;

        /// <summary>M2.1: снять курсорную отрисовку призрака — KB 2.9, страница
        /// ...Ged.Interaction~ClearCursor.html: ClearCursor() — «Remove
        /// Cursor-Representation». Ничего не бросает наружу: instance может не
        /// существовать (интеракция никогда не стартовала) или ClearCursor может
        /// бросить на остановленной интеракции — деградация штатная (рамка
        /// останется под курсором до следующего клика/Esc, вреда нет).</summary>
        public static void TryClearCursor()
        {
            try
            {
                if (s_instance != null) s_instance.ClearCursor();
            }
            catch (Exception oEx)
            {
                Buf("[IPING-CURSOR] ClearCursor бросил " + oEx.GetType().Name +
                    ": " + oEx.Message + " — деградация штатная");
            }
        }

        // Защита лога (паттерн SymbolPickInteraction): больше строк в буфер не пишем.
        private const int DUMP_CAP = 400;
        private static int s_nSuppressed = 0;

        /// <summary>Сброс ВСЕЙ статики перед новым запуском (хук, шаг 6 брифа):
        /// флаги, координаты, буфер, PendingGhost (призрак держит вызывающий код
        /// в локальной переменной — Reset только чистит ссылку).</summary>
        public static void Reset()
        {
            Done = false;
            Captured = false;
            Cancelled = false;
            CapturedX = 0.0;
            CapturedY = 0.0;
            PendingGhost = null;
            IPingDump.Clear();
            s_nSuppressed = 0;
        }

        /// <summary>OnStart: подсказка в строке состояния + проба рамки-призрака под
        /// курсором (SetStaticCursor по PendingGhost; отказ — НЕ ломает интеракцию,
        /// деградация «промпт без рамки», спека §6), затем base.OnStart и
        /// Success|Point: интеракция запущена И ждёт координаты (KB члены enum;
        /// бит Point = «coordinates of point are needed», факт SPIKE-11:
        /// SymbolPickInteraction.cs:239 проверяла eBaseCode на Point). Эксперимент:
        /// если SetStaticCursor(призрак) отвергается рантаймом — [IPING-CURSOR] отказ
        /// в буфере/файле пробы, интеракция живёт без рамки.</summary>
        public override RequestCode OnStart(InteractionContext pContext)
        {
            // M2.1: единственный активный экземпляр — для TryClearCursor при таймауте.
            InsertPointInteraction.s_instance = this;
            // Подсказка — паттерн SymbolPickInteraction.OnStart / docs-пример
            // Interactions.html («this.PromptForStatusLine = "select Terminals"»).
            this.PromptForStatusLine = "Укажите точку вставки отчёта клеммника (Esc — отмена)";
            Probe("OnStart: PromptForStatusLine выставлен");

            if (InsertPointInteraction.PendingGhost != null)
            {
                bool bGhostValid = false;
                try { bGhostValid = InsertPointInteraction.PendingGhost.IsValid; }
                catch (Exception oValEx)
                {
                    Buf("[IPING-CURSOR] проверка IsValid бросила " + oValEx.GetType().Name +
                        ": " + oValEx.Message + " — рамка не ставится");
                }
                if (bGhostValid)
                {
                    try
                    {
                        // KB: SetStaticCursor(StorableObject, PointD) — 2-арг.
                        // перегрузка; pntStartPos — начало локальной системы
                        // координат размещения (0,0 = якорь в точке курсора).
                        PointD oZero = new PointD(0.0, 0.0);
                        this.SetStaticCursor(InsertPointInteraction.PendingGhost, oZero);
                        Buf("[IPING-CURSOR] призрак под курсором (valid=" + bGhostValid + ")");
                    }
                    catch (Exception oEx)
                    {
                        // Деградация штатная: интеракция работает БЕЗ рамки (спека §6).
                        Buf("[IPING-CURSOR] отказ: " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                }
                else
                {
                    Buf("[IPING-CURSOR] PendingGhost не IsValid — без рамки");
                }
            }
            else
            {
                Buf("[IPING-CURSOR] PendingGhost == null — без рамки (штатная деградация)");
            }

            Probe("OnStart: интеракция " + "TSA_INSERT_POINT запущена");
            RequestCode eBase = base.OnStart(pContext);
            Buf("eBase после base.OnStart: " + eBase + " (число=" + ((int)eBase).ToString(CultureInfo.InvariantCulture) + ")");
            // Success|Point: запущена + ждёт клик (KB enum: Success=1024, Point=16).
            return eBase | RequestCode.Point;
        }

        /// <summary>OnPoint: ОДИН клик — захват координат и ЗАВЕРШЕНИЕ интеракции
        /// возвращаемым кодом. Возврат RequestCode.Success — KB 2.9, страница
        /// ...Ged.RequestCode.html: Success = 1024, «input of data was successful»;
        /// docs-пример MyInteraction (...Ged.Interaction.html) возвращает Success
        /// из OnPoint после сбора точки — тот же контракт. Весь метод под try/catch
        /// (исключение наружу НЕ выпускать — паттерн SymbolPickInteraction; частично
        /// захваченное состояние сохраняем, всё равно возвращаем код завершения —
        /// иначе интеракцию не закрыть и цикл ожидания упрётся в таймаут).</summary>
        public override RequestCode OnPoint(Position pnt)
        {
            try
            {
                // Паттерн чтения — docs-пример: oPosition.FinalPosition (PointD).
                PointD oFinal = pnt.FinalPosition;
                InsertPointInteraction.CapturedX = oFinal.X;
                InsertPointInteraction.CapturedY = oFinal.Y;
                InsertPointInteraction.Captured = true;
                Buf("[IPING-POINT] x=" + CapturedX.ToString("F3", CultureInfo.InvariantCulture) +
                    " y=" + CapturedY.ToString("F3", CultureInfo.InvariantCulture));
                return RequestCode.Success;
            }
            catch (Exception oEx)
            {
                Buf("[IPING-POINT] исключение " + oEx.GetType().Name + ": " + oEx.Message +
                    " — точка НЕ установлена, интеракция всё равно завершается");
                return RequestCode.Success;
            }
        }

        /// <summary>OnSuccess: может не вызываться для точечной интеракции —
        /// диагностическая проба; base.OnSuccess под try/catch (штатная точка
        /// посадки EPLAN, но наш сценарий не зависит от неё).</summary>
        public override void OnSuccess(InteractionContext result)
        {
            Probe("OnSuccess enter");
            try { base.OnSuccess(result); Probe("OnSuccess: base завершён"); }
            catch (Exception oEx)
            {
                Buf("[IPING-SUCCESS] base бросил " + oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        /// <summary>OnCancel: «Is called after abort of interaction» (Esc —
        /// страница ...Ged.Interaction~OnCancel.html). Сигнал хуку — Cancelled.</summary>
        public override void OnCancel()
        {
            InsertPointInteraction.Cancelled = true;
            Probe("[IPING-CANCEL] отменено (Esc/abort)");
            base.OnCancel();
        }

        /// <summary>OnStop: «called before an interaction stops, it is called after
        /// OnSuccess() or after OnCancel()» — ЕДИНЫЙ терминатор (факт SPIKE-11).
        /// Флаг ДО base — цикл ожидания выходит гарантированно (паттерн
        /// SymbolPickInteraction.OnStop rev.13.10).</summary>
        public override void OnStop()
        {
            Probe("[IPING-STOP] остановка (итог)");
            InsertPointInteraction.Done = true;
            base.OnStop();
        }

        /// <summary>Без авторестарта (docs: «Returns true, if interaction should
        /// restart after stop»; факт rev.13.8 — без override шёл повторный OnStart
        /// и требовался Esc). Одиночный клик завершает интеракцию.</summary>
        public override bool IsAutorestartEnabled
        {
            get { return false; }
        }

        // --- пробы (паттерн SymbolPickInteraction.Probe, свой файл + тег [IA-POINT]) ---

        /// <summary>Файловая проба жизненного цикла (append-only, НИКОГДА не бросает
        /// наружу). Файл "insert_point_probe.log" — ОТДЕЛЬНЫЙ от
        /// interaction_probe.log спайка; строки «HH:mm:ss.fff [IA-POINT] <что>».
        /// Каталоги — те же кандидаты, что у логгера действия
        /// (DiagnosticLogger.LOG_DIR_CANDIDATES + %TEMP%; writer/reader не разойдутся).</summary>
        private static void Probe(string strWhat)
        {
            try
            {
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
                    " [IA-POINT] " + strWhat + Environment.NewLine;
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(strDir, "insert_point_probe.log"), strLine);
            }
            catch (Exception)
            {
                // Отказ пробы молчалив: интеракцию не ломать (контракт SPIKE-6).
            }
        }

        private static void Buf(string strLine)
        {
            Probe(strLine);
            if (IPingDump.Count < DUMP_CAP) IPingDump.Add(strLine);
            else s_nSuppressed++;
        }
    }
}

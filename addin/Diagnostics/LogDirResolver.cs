using System;
using System.Collections.Generic;
using Eplan.EplApi.DataModel;

namespace MyEplanActions
{
    /// <summary>rev.16.14 (04.10): каталог лога/файла настроек Add-in'а берётся
    /// из НАСТРОЕК EPLAN, а не из захардкоженного пути. Прежде путь был один на
    /// машину заказчика (каталог под сетевым диском, подтверждён прогонами
    /// Этапы 1), и на любой другой машине EPLAN лог уезжал в %TEMP% (или в чужую
    /// папку, если каталог записан вручную). Теперь первый кандидат — папка
    /// «Сценарии» ИЗ настроек EPLAN (Опции → Настройки → Пользователь →
    /// Управление → Каталоги → Сценарии) плюс подпапка
    /// AddInConfiguration.LogSubFolderName, а крайний случай — %TEMP%
    /// (решение заказчика 04.10: захардкоженные пути удалены из проекта целиком,
    /// других фолбэков нет).
    /// ОСНОВАНО НА KB 2.9: Eplan.EplApi.DataModel.ProjectManager.Paths → «Returns
    /// PathInfo object … default Eplan P8 paths», PathInfo.Scripts (get-only) →
    /// «Returns default Scripts directory», PathInfo.Dispose() есть.
    /// ПОЧЕМУ ОТДЕЛЬНЫЙ КЛАСС, А НЕ ПРЯМО В DiagnosticLogger: логгер входит в
    /// консольный раннер tests/build_tests.bat, где EPLAN-DLL нет, поэтому в нём
    /// не должно быть НИ ОДНОГО using Eplan.* (проверка сборкой — в комментарии к
    /// DiagnosticLogger.SetLogDirCandidates). Единственное обращение к EPLAN API в
    /// этой правке живёт здесь; логгер получает уже готовый список каталогов.
    /// BuildCandidates — ЧИСТАЯ функция (только string[]/List<string>), ею же
    /// нормализуются кандидаты на стороне AnalyzeAction.</summary>
    public static class LogDirResolver
    {
        /// <summary>rev.16.14: папка «Сценарии» из настроек EPLAN
        /// (ProjectManager.Paths.Scripts). КОНТРАКТ ОТКАЗА: наружу не выходит НИ
        /// ОДНОГО исключения, при отказе возвращается null — вызывающая сторона
        /// обязана продолжить прогон (фолбэк — %TEMP%), а бросок из резолвера
        /// пути лога уронил бы прогон целиком. PathInfo
        /// ВЛАДЕЕМЫЙ нами объект (ProjectManager.Paths создаёт новый PathInfo на
        /// каждый вызов, KB 2.9), поэтому Dispose обязателен и стоит в finally —
        /// иначе COM-объект живёт до конца сессии EPLAN. Отказ самого Dispose не
        /// влияет на результат (путь уже прочитан), поэтому он глотается.</summary>
        public static string ResolveScriptsPath()
        {
            PathInfo oPaths = null;
            try
            {
                ProjectManager oProjectManager = new ProjectManager();
                oPaths = oProjectManager.Paths;
                return oPaths.Scripts;
            }
            // Имя исключения не берём: отказ здесь — ШТАТНЫЙ исход (настроек
            // EPLAN может не быть), логировать его нечем (логгер получает путь
            // ОТ ЭТОГО ЖЕ вызова), наружу оно не идёт. Безымянный
            // catch (Exception) — как в InsertPointInteraction/GhostFrameCreator.
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (oPaths != null)
                {
                    try { oPaths.Dispose(); }
                    catch { }
                }
            }
        }

        /// <summary>rev.16.14: склейка кандидатов каталога — сначала
        /// arrPrimary (из настроек EPLAN), потом arrFallback (у лога и настроек
        /// это %TEMP%, у папки point/ — null, то есть её нет вовсе). ПОРЯДОК
        /// СОХРАНЁН: каталог выбирает первый
        /// СУЩЕСТВУЮЩИЙ, поэтому перестановка поменяла бы, куда пишется лог.
        /// null/пустые элементы пропускаются (например, пустой путь «Сценарии»),
        /// дубли отсекаются БЕЗ УЧЁТА РЕГИСТРА (OrdinalIgnoreCase: один и тот же
        /// каталог, пришедший из EPLAN и из фолбэка, не должен проверяться и
        /// создаваться дважды). Любой null-массив допустим — это нормальный вызов
        /// «только фолбэк»/«только первичные». Ни LINQ, ни var, ни авто-свойств:
        /// файл должен оставаться в духе остальных чистых модулей проекта.</summary>
        public static string[] BuildCandidates(string[] arrPrimary, string[] arrFallback)
        {
            List<string> lstDirs = new List<string>();
            AddUnique(lstDirs, arrPrimary);
            AddUnique(lstDirs, arrFallback);
            return lstDirs.ToArray();
        }

        /// <summary>rev.16.14: часть списка в lstDirs с пропуском null/пустых и
        /// дублей без учёта регистра. Отдельный метод от BuildCandidates — чтобы
        /// правило фильтра было написано один раз для обоих списков.</summary>
        private static void AddUnique(List<string> lstDirs, string[] arrDirs)
        {
            if (arrDirs == null)
                return;
            for (int i = 0; i < arrDirs.Length; ++i)
            {
                string strDir = arrDirs[i];
                if (string.IsNullOrEmpty(strDir))
                    continue;
                if (ContainsIgnoreCase(lstDirs, strDir))
                    continue;
                lstDirs.Add(strDir);
            }
        }

        /// <summary>rev.16.14: есть ли уже такой каталог в списке (регистр не
        /// значит). Ручной проход вместо List.Contains — тот сравнивает Ordinal, а
        /// нам нужен OrdinalIgnoreCase.</summary>
        private static bool ContainsIgnoreCase(List<string> lstDirs, string strDir)
        {
            for (int i = 0; i < lstDirs.Count; ++i)
            {
                if (string.Equals(lstDirs[i], strDir,
                    StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace MyEplanActions
{
    /// <summary>Схема набора отображения свойств из .emc (rev.16.2, прод-волна;
    /// решения пользователя 01.10.2026): File — имя *.emc БЕЗ пути, Variant —
    /// A2453 (вариант символа: Import набора чужого варианта — тихий no-op
    /// без исключения, вердикт spike S2 → проверка ОБЯЗАТЕЛЬНА), SchemeName —
    /// A2454 (имя СХЕМЫ для активации Selected; имя файла ≠ имени схемы —
    /// вердикт S1). Multi-schema валиден: несколько O155 на файл
    /// (ТР_вертик(2).emc — 2 схемы, обе вариант 0).</summary>
    public sealed class EmcSchemeInfo
    {
        public string File;
        public int Variant = -1;   // A2453; -1 — нечисловой/пустой (запись скипается)
        public string SchemeName;  // A2454

        /// <summary>rev.17 (fix review): ComboBox рисует элемент через ToString
        /// (DisplayMember по публичному ПОЛЮ SchemeName ненадёжен — TypeDescriptor
        /// поля не гарантирует) — переопределение гарантирует имя схемы A2454
        /// в списках профилей.</summary>
        public override string ToString()
        {
            return SchemeName;
        }
    }

    /// <summary>Каталог наборов отображения point/*.emc (rev.16.2): скан НА
    /// СТАРТЕ команды (решение 01.10 — до показа UI-диалога; фактически —
    /// при первой вставке BP, скан дёшев и заголовок журнала один). Каждый
    /// корневой узел <O155 ... A2453="вариант" A2454="имя"> — схема; узлы
    /// без A2453/A2454 и нечитаемые файлы пропускаются (WARN). Папка пуста —
    /// падения НЕТ: 0 схем, BP вставляются БЕЗ набора. Чистый скан — только
    /// System/IO/Regex (паттерн BreakPointResolver; DiagnosticLogger — уже в
    /// тест-раннере). PickScheme — выбор по имени файла (kind-mapping 6 констант Emc*) И
    /// варианту (Review Focus 4/5: отсутствует/чужой вариант → null → WARN,
    /// BP без набора). «Несколько кандидатов на вариант» прод-логики не
    /// задевает: kind-маппинг уникален, артефактные файлы (ТР_вертик(2))
    /// константами не ссылаются; UI-выбор — при проектировании UI.</summary>
    public static class EmcSchemeCatalog
    {
        private static readonly Regex O155TagRegex =
            new Regex("<O155\\b[^>]*>", RegexOptions.Compiled);

        /// <summary>Разбор ТЕКСТА .emc: каждый тег <O155 ...> — кандидат
        /// схемы; без валидных A2453 (int) / A2454 (непусто) — пропуск.
        /// strFile — имя файла БЕЗ пути (метка схемы; путь не зашиваем —
        /// логи формирует потребитель). null/пустой текст — 0 записей.</summary>
        public static List<EmcSchemeInfo> ParseContent(string strContent, string strFile)
        {
            List<EmcSchemeInfo> lst = new List<EmcSchemeInfo>();
            if (string.IsNullOrEmpty(strContent)) return lst;
            MatchCollection colTags = O155TagRegex.Matches(strContent);
            foreach (Match oMatch in colTags)
            {
                if (oMatch == null) continue;
                int nVariant = AttrInt(oMatch.Value, "A2453");
                string strSchemeName = AttrStr(oMatch.Value, "A2454");
                if (nVariant < 0 || string.IsNullOrEmpty(strSchemeName)) continue;
                EmcSchemeInfo oInfo = new EmcSchemeInfo();
                oInfo.File = strFile;
                oInfo.Variant = nVariant;
                oInfo.SchemeName = strSchemeName;
                lst.Add(oInfo);
            }
            return lst;
        }

        /// <summary>Чтение файла набора: непрочитан (исключение) — null
        /// (вызовчик WARN); прочитан — список схем 0..N (без O155 — 0).</summary>
        public static List<EmcSchemeInfo> ParseFile(string strFilePath)
        {
            if (string.IsNullOrEmpty(strFilePath)) return null;
            try
            {
                return ParseContent(File.ReadAllText(strFilePath),
                    Path.GetFileName(strFilePath));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Прод-вход: скан папки → схема на каждый O155 всех *.emc.
        /// Папка null/отсутствует/нечитаема — null (WARN; BP без набора).
        /// Файл нечитаем — WARN, остальной скан продолжается.</summary>
        public static List<EmcSchemeInfo> ParseDirectory(string strDirPath,
            DiagnosticLogger log)
        {
            if (string.IsNullOrEmpty(strDirPath) || !Directory.Exists(strDirPath))
            {
                if (log != null)
                    log.Warn("[BP] папка наборов point/ не найдена ('" +
                        (strDirPath ?? "<null>") + "') — BP вставляются БЕЗ набора");
                return null;
            }
            string[] arrFiles;
            try { arrFiles = Directory.GetFiles(strDirPath, "*.emc"); }
            catch (Exception oEx)
            {
                if (log != null)
                    log.Warn("[BP] скан point/ бросил " + oEx.GetType().Name +
                        ": " + oEx.Message + " — BP вставляются БЕЗ набора");
                return null;
            }
            List<EmcSchemeInfo> lst = new List<EmcSchemeInfo>();
            foreach (string strPath in arrFiles)
            {
                // .NET 3-буквенный «перехват» GetFiles: '*.emc' ловит и '*.emcx'
                // (8.3) — точный фильтр расширения.
                if (!string.Equals(Path.GetExtension(strPath), ".emc",
                    StringComparison.OrdinalIgnoreCase)) continue;
                List<EmcSchemeInfo> lstFile = ParseFile(strPath);
                if (lstFile == null || lstFile.Count == 0)
                {
                    if (log != null && lstFile == null)
                        log.Warn("[BP] файл набора не читается: '" + strPath + "'");
                    continue;
                }
                foreach (EmcSchemeInfo oInfo in lstFile) lst.Add(oInfo);
            }
            return lst;
        }

        /// <summary>Выбор схемы для вставки: по имени файла (kind-mapping —
        /// 6 констант Emc*) И варианту (A2453 == вставляемому). Не найдено —
        /// null (Review Focus 4/5: WARN, BP с дефолтным отображением).
        /// Сравнение имени — OrdinalIgnoreCase. Списки короткие (до 7 файлов)
        /// — линейный скан, отдельного индекса по вариантам НЕ строим
        /// (уничтожен как API без потребителя после ревью).</summary>
        public static EmcSchemeInfo PickScheme(List<EmcSchemeInfo> lstAll,
            string strFileName, int nVariant)
        {
            if (string.IsNullOrEmpty(strFileName) || lstAll == null) return null;
            foreach (EmcSchemeInfo oInfo in lstAll)
            {
                if (oInfo == null || oInfo.Variant != nVariant) continue;
                if (string.Equals(oInfo.File, strFileName,
                    StringComparison.OrdinalIgnoreCase)) return oInfo;
            }
            return null;
        }

        // --- приватные хелперы: int/string атрибут A{n}="v" из тега ---
        private static int AttrInt(string strTag, string strAttr)
        {
            int nFrom, nEnd;
            if (!TryAttrRange(strTag, strAttr, out nFrom, out nEnd)) return -1;
            int nValue;
            if (!int.TryParse(strTag.Substring(nFrom, nEnd - nFrom),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out nValue))
                return -1;
            return nValue;
        }

        private static string AttrStr(string strTag, string strAttr)
        {
            int nFrom, nEnd;
            if (!TryAttrRange(strTag, strAttr, out nFrom, out nEnd)) return null;
            return strTag.Substring(nFrom, nEnd - nFrom);
        }

        private static bool TryAttrRange(string strTag, string strAttr,
            out int nFrom, out int nEnd)
        {
            nFrom = -1;
            nEnd = -1;
            if (string.IsNullOrEmpty(strTag) || string.IsNullOrEmpty(strAttr))
                return false;
            string strSearch = strAttr + "=\"";
            int nAt = strTag.IndexOf(strSearch, StringComparison.Ordinal);
            if (nAt < 0) return false;
            nFrom = nAt + strSearch.Length;
            nEnd = strTag.IndexOf('"', nFrom);
            return nEnd > nFrom;
        }
    }
}

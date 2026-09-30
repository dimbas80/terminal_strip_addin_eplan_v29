using System;

namespace MyEplanActions
{
    /// <summary>Решение по одному кабелю (rev.16.x = fix round 4, механика
    /// источников/целей по справке EPLAN 20376/20377; спека
    /// docs/superpowers/specs/2026-09-30-blockprop-format-design.md §4.1):
    /// какую строку формата писать в свойство блока 20202[x] символа кабеля
    /// (или не писать). ЧИСТЫЙ модуль — без EPLAN-типов, компилируется и в
    /// тест-раннер (tests/build_tests.bat). Поля заполняет
    /// BlockPropMath.Decide. В строке формата: 20211,1 = источник кабеля,
    /// 20211,2 = цель кабеля (fmt1 = показать источник, fmt2 = показать
    /// цель).</summary>
    public sealed class BlockFormatDecision
    {
        public string FormatToWrite;  // null = не писать
        public string Reason;         // "our-source->fmt-target" | "our-target->fmt-source"
                                      // | "not-related->skip" | "cab-sides-unreadable->skip"
                                      // | "ambiguous-both-ends->skip" | "our-loc-empty->skip"
                                      // | "our-loc-conflict->skip" | "feature-off"
        public bool UserError;        // true = MessageBox-сценарий
        public string OurLoc;         // 1429 нашего клеммника (единое; null = конфликт клемм)
        public string CabSource;      // 20376 «Кабели: источник» (как прочитано; "" = пусто/«—»)
        public string CabTarget;      // 20377 «Кабели: цель» (как прочитано)
    }

    /// <summary>fix round 4 (rev.16.x): чистая таблица решений по формату блока
    /// символа кабеля. Семантика: у кабеля есть свойства «Кабели: источник»
    /// (№20376) и «Кабели: цель» (№20377) — строки с ПОЛНОЙ структурой концов
    /// (пример К190: 20376='=HII-1.1++ЯЧ67+#1-X2', 20377='=ТСН-1++ЯЧ17+#1-X2').
    /// Определяем, у какого конца кабеля наш клеммник отчёта: сверяем заданное
    /// пользователем поле клеммника («Место сборки», №1429; strOurLoc) со
    /// строками 20376/20377 (Contains, Ordinal). Содержится в 20376 → наш
    /// конец = ИСТОЧНИК → показываем ДРУГОЙ конец (цель) → строка 20211,2
    /// (fmt2). Содержится в 20377 → наш конец = ЦЕЛЬ → показываем источник →
    /// 20211,1 (fmt1). Кабель может быть «развёрнут» — раньше источник/цель
    /// женить по GetSourcesAndTargets было нельзя; теперь сторона НЕ нужна:
    /// женится только то, ЧТО показывать.
    /// Вход: strOurLoc — единое значение №1429 нашего клеммника (null =
    /// конфликт клемм; "" = поле не задано); strCabSource/strCabTarget —
    /// 20376/20377 кабеля ("" = пусто/«—»); strFmtSource = строка формата для
    /// показа ИСТОЧНИКА кабеля (20211,1), strFmtTarget = для ЦЕЛИ (20211,2).
    /// Порядок правил — первое совпавшее:
    /// a) одна/обе fmt-строки пусты → feature-off (не писать);
    /// b) strOurLoc == null → конфликт клемм нашего клеммника → skip,
    ///    UserError = true (MessageBox);
    /// c) strCabSource пуст ИЛИ strCabTarget пуст → стороны кабеля не
    ///    прочитались/пусты (K-кабели rev.9.3) → skip, БЕЗ UserError;
    /// d) strOurLoc пуст ("") → у клеммника поле не задано → skip;
    /// e) сверка Contains (Ordinal): обе стороны → ambiguous → skip; только
    ///    20376 → our-source->fmt-target; только 20377 →
    ///    our-target->fmt-source; ни одна → not-related->skip.
    /// Contains на пустом strOurLoc не бывает (правило d проверяет "" раньше).
    /// Прим. C#5/.NET 4: сверка через IndexOf(value, Ordinal) == 0..N ≥ 0
    /// (перегрузка Contains без StringComparison в .NET Framework 4 не
    /// принимает Ordinal).
    /// Потребитель — EPLAN-обвязка (спека §4.2, BlockFormatResolver).</summary>
    public static class BlockPropMath
    {
        public static BlockFormatDecision Decide(string strOurLoc, string strCabSource,
            string strCabTarget, string strFmtSource, string strFmtTarget)
        {
            // a) фича off: одна/обе строки формата пусты — не писать вовсе;
            //    входные значения возвращаются как есть.
            if (string.IsNullOrEmpty(strFmtSource) || string.IsNullOrEmpty(strFmtTarget))
            {
                return MkDecision(null, "feature-off", false, strOurLoc, strCabSource, strCabTarget);
            }
            // b) конфликт клемм нашего клеммника (≥2 разных непустых №1429) —
            //    MessageBox-сценарий (поле НЕ записывается).
            if (strOurLoc == null)
            {
                return MkDecision(null, "our-loc-conflict->skip", true, null, strCabSource, strCabTarget);
            }
            // c) 20376/20377 не прочитались/пусты — сторону определить нельзя → skip
            //    БЕЗ UserError (WARN в логе, [BLOCKFMT]).
            if (strCabSource.Length == 0 || strCabTarget.Length == 0)
            {
                return MkDecision(null, "cab-sides-unreadable->skip", false, strOurLoc, strCabSource, strCabTarget);
            }
            // d) у клеммника поле не задано ("") — сверка невозможна → skip.
            if (strOurLoc.Length == 0)
            {
                return MkDecision(null, "our-loc-empty->skip", false, strOurLoc, strCabSource, strCabTarget);
            }
            // e) сверка нашего места сборки с полными структурами концов
            //    (Contains, Ordinal).
            bool bSrc = strCabSource.IndexOf(strOurLoc, StringComparison.Ordinal) >= 0;
            bool bTgt = strCabTarget.IndexOf(strOurLoc, StringComparison.Ordinal) >= 0;
            if (bSrc && bTgt)
            {
                // оба конца «знают» наше место сборки — решать нечем.
                return MkDecision(null, "ambiguous-both-ends->skip", false, strOurLoc, strCabSource, strCabTarget);
            }
            if (bSrc)
            {
                // наш конец = источник → показываем другой конец (цель) → 20211,2.
                return MkDecision(strFmtTarget, "our-source->fmt-target", false, strOurLoc, strCabSource, strCabTarget);
            }
            if (bTgt)
            {
                // наш конец = цель → показываем источник → 20211,1.
                return MkDecision(strFmtSource, "our-target->fmt-source", false, strOurLoc, strCabSource, strCabTarget);
            }
            // кабель не связан с нашим клеммником.
            return MkDecision(null, "not-related->skip", false, strOurLoc, strCabSource, strCabTarget);
        }

        // Собирает решение (без object-инициализатора — стиль соседних файлов).
        private static BlockFormatDecision MkDecision(string strFormatToWrite, string strReason,
            bool bUserError, string strOurLoc, string strCabSource, string strCabTarget)
        {
            BlockFormatDecision oRes = new BlockFormatDecision();
            oRes.FormatToWrite = strFormatToWrite;
            oRes.Reason = strReason;
            oRes.UserError = bUserError;
            oRes.OurLoc = strOurLoc;
            oRes.CabSource = strCabSource;
            oRes.CabTarget = strCabTarget;
            return oRes;
        }
    }
}

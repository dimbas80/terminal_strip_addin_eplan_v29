using Eplan.EplApi.ApplicationFramework;

namespace MyEplanActions
{
    /// <summary>
    /// Точка загрузки Add-in Этапа 2 (план: plan_stage2.md, Задача 1).
    /// Порт add-in-класса из spike/TerminalStripReportSpike.cs без изменений логики.
    /// </summary>
    public class TerminalStripAddIn : IEplAddIn
    {
        public bool OnRegister(ref bool bLoadOnStart)
        {
            bLoadOnStart = true;
            return true;
        }

        public bool OnUnregister() { return true; }
        public bool OnInit() { return true; }
        public bool OnInitGui() { return true; }
        public bool OnExit() { return true; }
    }
}

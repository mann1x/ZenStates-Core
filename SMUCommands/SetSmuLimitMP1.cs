namespace ZenStates.Core.SMUCommands
{
    internal class SetSmuLimitMP1 : BaseSMUCommand
    {
        public SetSmuLimitMP1(SMU smu) : base(smu) { }
        public CmdResult Execute(uint cmd, uint arg = 0U)
        {
            if (CanExecute())
            {
                result.args[0] = arg * 1000;
                result.status = smu.SendMp1Command(cmd, ref result.args);
            }

            return base.Execute();
        }
    }
}

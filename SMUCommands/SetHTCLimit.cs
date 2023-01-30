namespace ZenStates.Core.SMUCommands
{
    internal class SetHTCLimit : BaseSMUCommand
    {
        public SetHTCLimit(SMU smu) : base(smu) { }
        public CmdResult Execute(uint cmd, uint arg = 0U)
        {
            if (CanExecute() && cmd != 0x0)
            {
                result.args[0] = arg;
                result.status = smu.SendRsmuCommand(cmd, ref result.args);
            }

            return base.Execute();
        }
    }
}

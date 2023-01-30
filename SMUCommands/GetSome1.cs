namespace ZenStates.Core.SMUCommands
{
    internal class GetSome1 : BaseSMUCommand
    {
        public GetSome1(SMU smu) : base(smu) { }
        public override CmdResult Execute()
        {
            if (CanExecute())
            {
                result.args[0] = 0x0;
                result.status = smu.SendMp1Command(smu.Mp1Smu.SMU_MSG_GetSome1Clock, ref result.args);
            }

            return base.Execute();
        }
    }
}

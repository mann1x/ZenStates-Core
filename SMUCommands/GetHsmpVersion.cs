namespace ZenStates.Core.SMUCommands
{
    internal class GetHsmpVersion : BaseSMUCommand
    {
        public GetHsmpVersion(SMU smu) : base(smu) { }
        public override CmdResult Execute()
        {
            if (CanExecute())
            {
                result.status = smu.SendHsmpCommand(smu.Hsmp.GetInterfaceVersion, ref result.args);
            }

            return base.Execute();  
        }
    }
}

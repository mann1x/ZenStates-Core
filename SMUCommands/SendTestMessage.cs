﻿namespace ZenStates.Core.SMUCommands
{
    internal class SendTestMessage : BaseSMUCommand
    {
        public SendTestMessage(SMU smu) : base(smu) { }
        public bool success = true;
        public override CmdResult Execute()
        {
            if (CanExecute())
            {
                result.args[0] = 1;
                result.status = smu.SendRsmuCommand(smu.Hsmp.SMU_MSG_TestMessage, ref result.args);

                if (result.status != SMU.Status.OK) success = false;
                if (result.args[0] != 2) success = false;
            }

            return base.Execute();
        }
    }
}

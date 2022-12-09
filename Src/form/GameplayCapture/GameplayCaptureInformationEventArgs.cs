using System;

namespace GameplayCapture
{
    public class GameplayCaptureInformationEventArgs : EventArgs
    {
        internal GameplayCaptureInformationEventArgs(string information)
        {
            Information = information;
        }

        public string Information { get; }
    }
}

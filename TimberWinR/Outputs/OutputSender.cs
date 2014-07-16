﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using TimberWinR.Inputs;

namespace TimberWinR.Outputs
{
    public abstract class OutputSender
    {
        public CancellationToken CancelToken { get; private set; }        
        private List<InputListener> _inputs;

        public OutputSender(CancellationToken cancelToken)
        {
            CancelToken = cancelToken;
            _inputs = new List<InputListener>();
        }

        public void Connect(InputListener listener)
        {
            listener.OnMessageRecieved += MessageReceivedHandler;            
        }

        protected abstract void MessageReceivedHandler(string jsonMessage);
    }
}

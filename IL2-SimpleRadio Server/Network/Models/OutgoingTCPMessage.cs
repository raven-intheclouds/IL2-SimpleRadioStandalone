﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Ciribob.IL2.SimpleRadio.Standalone.Common.Network;

namespace Ciribob.IL2.SimpleRadio.Standalone.Server.Network.Models
{
    public class OutgoingTCPMessage
    {
        public NetworkMessage NetworkMessage {
            get;
            set;
        }

        public List<Socket> SocketList { get; set; }

    }
}

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

//public class Uavlink_cmd_flyto_t
//{
//    public byte AllWP;
//    public int WPId;
//    public Uavlink_cmd_flyto_t()
//    {
//        AllWP = 1;
//        WPId = 0;
//    }

//    public void Encode(out byte[] _data)
//    {
//        byte[] data = new byte[7];
//        int index = 0;

//        BitConverter.GetBytes((UInt16)CommandId.UAVLINK_CMD_FLYTO).CopyTo(data, index);
//        BitConverter.GetBytes(AllWP).CopyTo(data, index += 2);
//        BitConverter.GetBytes(WPId).CopyTo(data, index += 1);
//        _data = data;
//    }
//}

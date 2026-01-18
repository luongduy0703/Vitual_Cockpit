using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public enum MessageId : byte
{
    UAVLINK_MSG_ID_STATE = 0x01,
    //UAVLINK_MSG_ID_GLOBAL_POSITION = 0x02,
    //UAVLINK_MSG_ID_LOCAL_POSITION = 0x03,
    //UAVLINK_MSG_SETWP = 0x04,
    UAVLINK_MSG_ID_COMMAND = 0x05,
    UAVLINK_MSG_ID_MANUAL_CONTROL = 0x06,
    //UAVLINK_MSG_ID_ROBOT_CONTROL = 0x07,
    UAVLINK_MSG_ID_POSITION_CONTROL = 0x08,
    UAVLINK_MSG_ID_POSITION_FEEDBACK = 0x09,
    UAVLINK_MSG_ID_DRONE_STATUS = 0x0A,
    UAVLINK_MSG_ID_RC_CHANNELS = 0x0B,
    UAVLINK_MSG_ID_VELOCITY_CONTROL = 0x0C,
    UAVLINK_MSG_ID_SERVO_CONTROL = 0x0D
}

public enum CommandId : UInt16
{
    UAVLINK_CMD_TAKEOFF = 22,
    UAVLINK_CMD_ARM = 23,
    UAVLINK_CMD_LAND = 24,
    //UAVLINK_CMD_FLYTO = 25,
    UAVLINK_CMD_SETMODE = 26,
    // Add offboard mode
    UAVLINK_CMD_POSITION_CONTROL_MODE = 27,
    UAVLINK_CMD_CIRCLE = 28,
    UAVLINK_CMD_TRAIN_TOGGLE = 29,
    UAVLINK_CMD_VX_OVERRIDE = 30
}

public class Uavlink_message_t
{

    public Uavlink_message_t()
    {

    }

    MessageId _msgid;
    private sbyte _lenPayload;
    private byte[] _payload;

    public MessageId Msgid
    {
        get { return _msgid; }
        set { this._msgid = value; }
    }
    public sbyte LenPayload
    {
        get { return _lenPayload; }
        set { this._lenPayload = value; }
    }
    public byte[] Payload
    {
        get { return _payload; }
        set { this._payload = value; }
    }

    public void Encode(out byte[] pack)
    {
        byte[] data = new byte[1 + 1 + _lenPayload];
        data[0] = (byte)_msgid;
        data[1] = (byte)_lenPayload;
        for (int i = 0; i < _lenPayload; i++)
        {
            data[i + 2] = _payload[i];
        }
        pack = data;
    }

    public void Decode(byte[] data)
    {
        _msgid = (MessageId)data[0];
        _lenPayload = (sbyte)data[1];
        _payload = new byte[_lenPayload];
        for (int i = 0; i < _lenPayload; i++)
        {
            _payload[i] = data[i + 2];
        }
    }
}


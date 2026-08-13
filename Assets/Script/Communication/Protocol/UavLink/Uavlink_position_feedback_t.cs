using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Uavlink_position_feedback_t
{
    private static int LENGTH = 13; // 1 byte + 3 floats (3*4=12) = 13

    private byte _success;
    private float _error_x;
    private float _error_y;
    private float _error_z;

    public byte success
    {
        get { return _success; }
        set { _success = value; }
    }
    public float error_x
    {
        get { return _error_x; }
        set { _error_x = value; }
    }
    public float error_y
    {
        get { return _error_y; }
        set { _error_y = value; }
    }

    public float error_z
    {
        get { return _error_z; }
        set { _error_z = value; }
    }

    public void Encode(out byte[] data)
    {
        data = new byte[LENGTH];
        int index = 0;

        data[index] = _success;
        BitConverter.GetBytes(_error_x).CopyTo(data, index += 1);
        BitConverter.GetBytes(_error_y).CopyTo(data, index += 4);
        BitConverter.GetBytes(_error_z).CopyTo(data, index += 4);
    }

    public void Decode(byte[] data)
    {
        int index = 0;

        _success = data[index];
        _error_x = BitConverter.ToSingle(data, index += 1);
        _error_y = BitConverter.ToSingle(data, index += 4);
        _error_z = BitConverter.ToSingle(data, index += 4);
    }
}

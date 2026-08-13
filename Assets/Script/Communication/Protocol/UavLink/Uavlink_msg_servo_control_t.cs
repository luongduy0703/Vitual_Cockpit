using System;

public class Uavlink_msg_servo_control_t
{
    private static int LENGTH = 20;

    public float servo0;
    public float servo1;
    public float servo2;
    public float servo3;
    public float servo4;

    public void Encode(out byte[] _data)
    {
        byte[] data = new byte[LENGTH];
        int index = 0;
        BitConverter.GetBytes(servo0).CopyTo(data, index);
        BitConverter.GetBytes(servo1).CopyTo(data, index += 4);
        BitConverter.GetBytes(servo2).CopyTo(data, index += 4);
        BitConverter.GetBytes(servo3).CopyTo(data, index += 4);
        BitConverter.GetBytes(servo4).CopyTo(data, index += 4);

        _data = data;
    }
}
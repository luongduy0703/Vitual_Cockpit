#!/usr/bin/env python3
"""
Test PCA9685 channels one at a time, straight over I2C.

Independent of ROS and of servos.yaml, so it answers the question the ROS
tools cannot: *which physical servo is wired to which channel*. Useful when
some joints move and others do not — usually because the arm is wired to
channels 0..5 while servos.yaml expects 0, 1, 4, 7, 9, 15.

  STOP THE ROS DRIVER FIRST, otherwise two processes fight over the bus:
      Ctrl+C the wicom_roboarm launch window

Usage:
    python3 channel_test.py --scan             # wiggle every channel in turn
    python3 channel_test.py -c 4 --sweep       # sweep one channel
    python3 channel_test.py -c 4 -a 120        # hold one channel at an angle
    python3 channel_test.py --off              # release everything
"""

import argparse
import math
import sys
import time

try:
    from smbus2 import SMBus
except ImportError:
    sys.exit("Thieu smbus2:  pip3 install smbus2")

MODE1, MODE2, PRESCALE, LED0_ON_L = 0x00, 0x01, 0xFE, 0x06
ALL_LED_ON_L, ALL_LED_ON_H = 0xFA, 0xFB
ALL_LED_OFF_L, ALL_LED_OFF_H = 0xFC, 0xFD
RESTART, SLEEP, ALLCALL, OUTDRV = 0x80, 0x10, 0x01, 0x04


class PCA9685:
    """Same register sequence as wicom_roboarm_unified_node."""

    def __init__(self, bus, address=0x40, osc=25_000_000):
        self._bus, self._addr, self._osc = bus, address, osc
        self._w(MODE2, OUTDRV)
        self._w(MODE1, ALLCALL)
        time.sleep(0.005)
        self._w(MODE1, self._r(MODE1) & ~SLEEP)
        time.sleep(0.005)

    def _w(self, reg, val):
        self._bus.write_byte_data(self._addr, reg, val & 0xFF)

    def _r(self, reg):
        return self._bus.read_byte_data(self._addr, reg)

    def set_freq(self, hz):
        prescale = int(math.floor(self._osc / (4096.0 * hz) - 1.0 + 0.5))
        old = self._r(MODE1)
        self._w(MODE1, (old & 0x7F) | SLEEP)
        self._w(PRESCALE, prescale)
        self._w(MODE1, old)
        time.sleep(0.005)
        self._w(MODE1, old | RESTART)

    def set_pwm(self, ch, on, off):
        base = LED0_ON_L + 4 * ch
        self._w(base + 0, on & 0xFF)
        self._w(base + 1, (on >> 8) & 0x0F)
        self._w(base + 2, off & 0xFF)
        self._w(base + 3, (off >> 8) & 0x0F)

    def set_off(self, ch):
        base = LED0_ON_L + 4 * ch
        self._w(base + 0, 0x00)
        self._w(base + 1, 0x00)
        self._w(base + 2, 0x00)
        self._w(base + 3, 0x10)

    def all_off(self):
        self._w(ALL_LED_ON_L, 0)
        self._w(ALL_LED_ON_H, 0)
        self._w(ALL_LED_OFF_L, 0)
        self._w(ALL_LED_OFF_H, 0x10)


def angle_to_counts(angle, pmin, pmax, period_us):
    angle = max(0.0, min(180.0, angle))
    pulse = pmin + (angle / 180.0) * (pmax - pmin)
    return max(0, min(4095, int(round(pulse / period_us * 4096.0))))


def main():
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('--bus', type=int, default=1)
    p.add_argument('--addr', type=lambda x: int(x, 0), default=0x40)
    p.add_argument('--freq', type=float, default=50.0)
    p.add_argument('--min-us', type=float, default=600.0,
                   help='pulse at 0 deg (mac dinh 600 cho an toan)')
    p.add_argument('--max-us', type=float, default=2400.0,
                   help='pulse at 180 deg')
    p.add_argument('-c', '--channel', type=int, help='kenh 0..15')
    p.add_argument('-a', '--angle', type=float, help='giu kenh o mot goc')
    p.add_argument('--sweep', action='store_true', help='quet 90-60-90-120-90')
    p.add_argument('--scan', action='store_true',
                   help='lan luot lac tung kenh 0..15 de tim servo nao o dau')
    p.add_argument('--channels', type=str, default='0-15',
                   help='pham vi cho --scan, vd "0-7" hoac "0,1,4,7,9,15"')
    p.add_argument('--hold', type=float, default=0.6, help='giay giua moi buoc')
    p.add_argument('--off', action='store_true', help='tat toan bo kenh')
    args = p.parse_args()

    def parse_channels(spec):
        out = []
        for part in spec.split(','):
            part = part.strip()
            if '-' in part:
                a, b = part.split('-')
                out.extend(range(int(a), int(b) + 1))
            elif part:
                out.append(int(part))
        return [c for c in out if 0 <= c <= 15]

    with SMBus(args.bus) as bus:
        pca = PCA9685(bus, args.addr)
        pca.set_freq(args.freq)

        def put(ch, angle):
            pca.set_pwm(ch, 0, angle_to_counts(angle, args.min_us,
                                               args.max_us, 1_000_000.0 / args.freq))

        if args.off:
            pca.all_off()
            print("Da tat toan bo kenh.")
            return

        if args.scan:
            chans = parse_channels(args.channels)
            print(f"Quet kenh {chans} — xung {args.min_us:.0f}-{args.max_us:.0f}us")
            print("Nhin xem servo NAO nhuc nhich o moi buoc.\n")
            for ch in chans:
                print(f"  → kenh {ch:2d} ... ", end='', flush=True)
                for a in (90, 65, 115, 90):
                    put(ch, a)
                    time.sleep(args.hold)
                pca.set_off(ch)
                print("xong")
            pca.all_off()
            print("\nHet. Kenh nao khong co servo nao nhuc nhich = chua cam, "
                  "sai day, hoac servo hong.")
            return

        if args.channel is None:
            p.error("can --channel, hoac --scan, hoac --off")

        if args.sweep:
            print(f"Quet kenh {args.channel}: 90 → 60 → 90 → 120 → 90")
            for a in (90, 60, 90, 120, 90):
                print(f"   {a}°")
                put(args.channel, a)
                time.sleep(args.hold * 1.5)
            return

        angle = args.angle if args.angle is not None else 90.0
        put(args.channel, angle)
        print(f"Kenh {args.channel} = {angle:.1f}°  "
              f"(giu nguyen; chay --off de tha long)")


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
"""
Run one servo continuously, with live speed and on/off control.

Built for bench-testing an MG90S: check it still turns, listen for the buzz of
a mechanical end stop, burn a new one in, or just prove a channel is alive.

Talks straight to the PCA9685 over I2C, so it does not need ROS — but for the
same reason the arm driver must be stopped first, or the two will fight over
the chip:
      Ctrl+C the wicom_roboarm launch window

Two modes:
  sweep  standard servo (MG90S 180°) swings back and forth between two angles
  spin   continuous-rotation servo (MG90S 360°) turns nonstop; pulse width
         sets speed, 1500µs is stop

Usage:
    python3 servo_spin.py -c 9
    python3 servo_spin.py -c 9 --mode spin --speed 40
"""

import argparse
import sys
import threading
import time

try:  # installed as part of the package
    from wicom_roboarm_ros2.channel_test import PCA9685
except ImportError:  # run straight from the source folder
    from channel_test import PCA9685

try:
    from smbus2 import SMBus
except ImportError:
    sys.exit("Thieu smbus2:  pip3 install smbus2")

HELP = """
─── Dieu khien ──────────────────────────────────────────────────────
  g / s          CHAY / DUNG  (dung = van giu vi tri)
  +  /  -        tang / giam toc do 10%        (+5 / -5 de buoc nho)
  <so>           dat toc do theo %             vd:  35
  d              dao chieu
  c <kenh>       doi sang kenh khac            vd:  c 9
  r <lo> <hi>    doi bien do quet (do)         vd:  r 30 150
  m              doi che do sweep <-> spin
  o              NGAT XUNG kenh nay (servo mem ra)
  ?              in lai bang nay
  q              thoat (tu ngat xung)
─────────────────────────────────────────────────────────────────────
"""


class ServoRunner:
    """Drives one channel from a background thread at a steady update rate."""

    UPDATE_HZ = 50.0
    STOP_US = 1500.0          # neutral pulse for a continuous-rotation servo
    MAX_SPIN_OFFSET_US = 500.0

    def __init__(self, pca, channel, mode, lo, hi, speed_pct,
                 min_us, max_us, period_us):
        self.pca = pca
        self.channel = channel
        self.mode = mode                 # 'sweep' | 'spin'
        self.lo, self.hi = lo, hi
        self.speed_pct = speed_pct       # 0..100
        self.min_us, self.max_us = min_us, max_us
        self.period_us = period_us

        self.running = False
        self.direction = 1
        self.angle = (lo + hi) / 2.0
        self._alive = True
        self._lock = threading.Lock()
        self._thread = threading.Thread(target=self._loop, daemon=True)
        self._thread.start()

    # ── low level ────────────────────────────────────────────────────

    def _write_us(self, pulse_us):
        counts = int(round(pulse_us / self.period_us * 4096.0))
        self.pca.set_pwm(self.channel, 0, max(0, min(4095, counts)))

    def _write_angle(self, angle):
        angle = max(0.0, min(180.0, angle))
        self._write_us(self.min_us + (angle / 180.0) * (self.max_us - self.min_us))

    # ── worker ───────────────────────────────────────────────────────

    def _loop(self):
        dt = 1.0 / self.UPDATE_HZ
        while self._alive:
            with self._lock:
                if self.running:
                    if self.mode == 'spin':
                        offset = (self.speed_pct / 100.0) * self.MAX_SPIN_OFFSET_US
                        self._write_us(self.STOP_US + self.direction * offset)
                    else:
                        # 100% = 180 deg/s, a sensible ceiling for a hobby servo
                        step = (self.speed_pct / 100.0) * 180.0 * dt
                        self.angle += self.direction * step
                        if self.angle >= self.hi:
                            self.angle, self.direction = self.hi, -1
                        elif self.angle <= self.lo:
                            self.angle, self.direction = self.lo, 1
                        self._write_angle(self.angle)
            time.sleep(dt)

    # ── controls ─────────────────────────────────────────────────────

    def start(self):
        with self._lock:
            self.running = True

    def stop(self):
        """Stop moving but keep holding position (spin servos need neutral)."""
        with self._lock:
            self.running = False
            if self.mode == 'spin':
                self._write_us(self.STOP_US)

    def release(self):
        with self._lock:
            self.running = False
            self.pca.set_off(self.channel)

    def set_speed(self, pct):
        with self._lock:
            self.speed_pct = max(0.0, min(100.0, pct))

    def flip(self):
        with self._lock:
            self.direction *= -1

    def set_channel(self, ch):
        with self._lock:
            self.pca.set_off(self.channel)
            self.channel = ch
            self.angle = (self.lo + self.hi) / 2.0

    def set_range(self, lo, hi):
        with self._lock:
            self.lo, self.hi = min(lo, hi), max(lo, hi)
            self.angle = max(self.lo, min(self.hi, self.angle))

    def toggle_mode(self):
        with self._lock:
            self.pca.set_off(self.channel)
            self.mode = 'spin' if self.mode == 'sweep' else 'sweep'

    def shutdown(self):
        self._alive = False
        self._thread.join(timeout=1.0)
        self.pca.set_off(self.channel)

    def status(self):
        state = "CHAY" if self.running else "DUNG"
        arrow = "→" if self.direction > 0 else "←"
        if self.mode == 'spin':
            extra = f"xung {self.STOP_US + self.direction * (self.speed_pct/100)*self.MAX_SPIN_OFFSET_US:.0f}us"
        else:
            extra = f"bien do {self.lo:.0f}-{self.hi:.0f}°, dang o {self.angle:.0f}°"
        return (f"[CH{self.channel} {self.mode} {state} {arrow} "
                f"toc do {self.speed_pct:.0f}%  {extra}]")


def main():
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('-c', '--channel', type=int, required=True, help='kenh 0..15')
    p.add_argument('--mode', choices=['sweep', 'spin'], default='sweep')
    p.add_argument('--speed', type=float, default=30.0, help='toc do ban dau %%')
    p.add_argument('--lo', type=float, default=30.0, help='goc thap (sweep)')
    p.add_argument('--hi', type=float, default=150.0, help='goc cao (sweep)')
    p.add_argument('--min-us', type=float, default=600.0, help='xung o 0 do')
    p.add_argument('--max-us', type=float, default=2400.0, help='xung o 180 do')
    p.add_argument('--bus', type=int, default=1)
    p.add_argument('--addr', type=lambda x: int(x, 0), default=0x40)
    p.add_argument('--freq', type=float, default=50.0)
    p.add_argument('--start', action='store_true', help='chay ngay khi mo')
    args = p.parse_args()

    with SMBus(args.bus) as bus:
        pca = PCA9685(bus, args.addr)
        pca.set_freq(args.freq)

        runner = ServoRunner(pca, args.channel, args.mode, args.lo, args.hi,
                             args.speed, args.min_us, args.max_us,
                             1_000_000.0 / args.freq)
        if args.start:
            runner.start()

        print(HELP)
        try:
            while True:
                try:
                    raw = input(f"{runner.status()} > ").strip().lower()
                except (EOFError, UnicodeDecodeError):
                    break
                if not raw:
                    continue

                parts = raw.split()
                head = parts[0]

                if head in ('q', 'quit', 'exit'):
                    break
                elif head in ('?', 'h', 'help'):
                    print(HELP)
                elif head in ('g', 'go', 'start'):
                    runner.start()
                elif head in ('s', 'stop'):
                    runner.stop()
                elif head in ('o', 'off'):
                    runner.release()
                    print("   → da ngat xung, servo mem ra")
                elif head == 'd':
                    runner.flip()
                elif head == 'm':
                    runner.toggle_mode()
                    print(f"   → che do: {runner.mode}")
                elif head.startswith(('+', '-')):
                    try:
                        delta = float(head) if len(head) > 1 else (
                            10.0 if head == '+' else -10.0)
                    except ValueError:
                        delta = 10.0 if head[0] == '+' else -10.0
                    runner.set_speed(runner.speed_pct + delta)
                elif head == 'c' and len(parts) == 2:
                    try:
                        ch = int(parts[1])
                        if 0 <= ch <= 15:
                            runner.set_channel(ch)
                        else:
                            print("   !! kenh phai trong 0..15")
                    except ValueError:
                        print("   !! kenh phai la so")
                elif head == 'r' and len(parts) == 3:
                    try:
                        runner.set_range(float(parts[1]), float(parts[2]))
                    except ValueError:
                        print("   !! vd: r 30 150")
                else:
                    try:
                        runner.set_speed(float(head))
                    except ValueError:
                        print(f"   !! khong hieu '{raw}' — go ? de xem lenh")
        except KeyboardInterrupt:
            pass
        finally:
            runner.shutdown()
            print("\nDa ngat xung kenh, thoat.")


if __name__ == '__main__':
    main()

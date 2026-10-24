#!/usr/bin/env python3
"""
Emergency OFF all PCA9685 channels.
Default: direct access (no mux). Use --mux flags if mux is present.
"""
import argparse
from smbus2 import SMBus

ALL_LED_OFF_H = 0xFD
ALL_LED_OFF_L = 0xFC
ALL_LED_ON_H  = 0xFB
ALL_LED_ON_L  = 0xFA

def select_mux_channel(bus, mux_addr, channel):
    if mux_addr is None or channel is None:
        return
    if not (0 <= channel <= 7):
        raise ValueError("Mux channel must 0–7")
    bus.write_byte(mux_addr, 1 << channel)

def main():
    p = argparse.ArgumentParser(description="Emergency OFF all PCA9685 channels.")
    p.add_argument("--bus", type=int, default=1, help="I2C bus number (default 1)")
    p.add_argument("--addr", type=lambda x: int(x, 0), default=0x40, help="PCA9685 I2C address (e.g., 0x40)")
    p.add_argument("--use-mux", action="store_true", help="Enable mux access (default: direct, no mux)")
    p.add_argument("--mux-addr", type=lambda x: int(x, 0), default=0x70,
                   help="Multiplexer address (only used with --use-mux)")
    p.add_argument("--mux-channel", type=int, default=2,
                   help="Multiplexer channel (only used with --use-mux)")
    args = p.parse_args()

    with SMBus(args.bus) as bus:
        if args.use_mux:
            select_mux_channel(bus, args.mux_addr, args.mux_channel)
        bus.write_byte_data(args.addr, ALL_LED_ON_L, 0)
        bus.write_byte_data(args.addr, ALL_LED_ON_H, 0)
        bus.write_byte_data(args.addr, ALL_LED_OFF_L, 0)
        bus.write_byte_data(args.addr, ALL_LED_OFF_H, 0x10)  # full off

    print("All PCA9685 channels OFF ({})".format(
        f"mux addr=0x{args.mux_addr:02X}, channel={args.mux_channel}" if args.use_mux else "direct (no mux)"
    ))

if __name__ == "__main__":
    main()
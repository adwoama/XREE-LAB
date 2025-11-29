#!/usr/bin/env python3
"""List all available VISA resources"""
import pyvisa

rm = pyvisa.ResourceManager()
print("Available VISA resources:")
resources = rm.list_resources()
if resources:
    for r in resources:
        print(f"  {r}")
else:
    print("  (none found)")

print(f"\nPyVISA backend: {rm}")

# Neoteric Glow Plug Controller

An intelligent Arduino-based glow plug controller for 6-cylinder diesel engines featuring individual plug monitoring, temperature-adaptive timing, and comprehensive fault indication.

![](board-v1.png)

## Features

### **Intelligent Heating Control**
- **Two-Phase Heating**: 5 seconds at 100% power, then reduced to 60% 
- **Temperature-Adaptive Duration**: Cold engines (15s total), Hot engines (8s total)
- **Staggered Startup**: 1-second delay between plugs to reduce electrical load
- **Individual Control**: Each glow plug operates independently

### **Advanced Monitoring**
- **Real-Time Current Sensing**: Individual current monitoring per cylinder using BTS50010 high-side switches
- **Temperature Estimation**: Calculates glow plug temperature from current draw
- **Fault Detection**: Over/undercurrent protection with automatic plug disable
- **Voltage Divider Input**: 4.7kΩ/1.5kΩ divider for Arduino ADC compatibility

### **Fault Indication**
- **LED Blinking Codes**: Visual indication of failed plugs (1 blink = plug 1, 2 blinks = plug 2, etc.)
- **Priority System**: Shows lowest numbered fault first when multiple faults exist
- **Non-Interfering**: Fault indication doesn't disrupt normal operation

## Schematic

[v1 Schematic is here](schematic-v1.pdf)

## Hardware Requirements

### **Microcontroller**
- Arduino-compatible board (Uno, Nano, etc.)
- 6 PWM-capable output pins
- 6 analog input pins

### **Power Switching**
- 6x BTS50010 high-side switches (or equivalent)
- Rated for 15A+ continuous current per channel

### **Current Sensing**
- Voltage divider: 4.7kΩ (R1) to Arduino input, 1.5kΩ (R2) to ground
- Connected to BTS50010 current sense outputs

### **Connections**
```
Arduino Pins:
- PWM Outputs: 3, 5, 6, 9, 10, 11 (to BTS50010 control inputs)
- Analog Inputs: A0, A1, A2, A3, A4, A5 (from voltage dividers)
- Built-in LED: Fault indication
```

## Operation Sequence

```mermaid
flowchart TD
    A[Boot Delay] --> B[Measure Initial Temperatures
    10% duty cycle for 200ms]
    B --> C{"Estimated Temp
    ≥ HOT_PLUG_TEMP_THRESHOLD (200°C)?"}
    C -- Yes --> D[Set duration =
    HOT_ENGINE_TOTAL_MS]
    C -- No --> E[Set duration =
    COLD_ENGINE_TOTAL_MS]
    D --> F[Wait plug# × STAGGER_DELAY_MS]
    E --> F
    F --> G[Phase 1: Full Power
    100% duty cycle
    for FULL_POWER_DURATION_MS]
    G --> H[Phase 2: Reduced Power
    REDUCED_DUTY_CYCLE
    for duration − FULL_POWER_DURATION_MS]
    H --> I[Shut Off Plug]
    I --> J{All Plugs
    Finished?}
    J -- No --> K[Continue monitoring
    other plugs]
    K --> J
    J -- Yes --> L[Enter Low Power Mode]
```

### **1. Boot Sequence (1 second)**
- Initialize all outputs to OFF
- Initialize inputs for current monitoring
- Prepare for temperature measurement

### **2. Temperature Measurement (0.25 seconds)**
- Simultaneously energize all plugs at 10% duty cycle for 200ms
- Measure current and estimate initial temperature for each plug
- Classify as "hot" (≥200°C) or "cold" (<200°C)
- Set appropriate heating duration per plug

### **3. Staggered Startup**
- Plug 1 starts immediately
- Plug 2 starts after 1 second
- Plug 3 starts after 2 seconds
- Continue pattern for remaining plugs

### **4. Two-Phase Heating**
**Phase 1 - Full Power (5 seconds):**
- 100% PWM duty cycle
- Maximum current draw per plug
- Rapid initial heating

**Phase 2 - Reduced Power (3-10 seconds):**
- 60% PWM duty cycle  
- Reduced power consumption
- Sustained heating to operating temperature

### **5. Completion**
- All plugs shut off individually based on their timing
- Enter low-power mode
- Continue fault monitoring and indication

## Troubleshooting

### Different Temperature

*Q: My glow plugs are running too cold (or too warm).  How do I adjust that?*

There are a few factors here.  

First is that we don't know ambient temeprature (a sensor in a furture board would probably be helpful) so we have an assumed ambient temp in `config.h` of `AMBIENT_TEMP` of 25C (77F).  If you're operating in the winter, for example, and it's 0C (32F) then the temp curve calculation will already be off, and the control loop will not run as long as you might want.  You can decrease that ambient temp value which, in turn, will change the hot v cold path the control loop follows. and may make the heating cycle longer.

Second is the `REDUCED_DUTY_CYCLE` value, which defaults to `0.6` (60%).  This is how long, in Phase 2, that the glow plug is Energized.  Think of it as "60% max heat".  By increasing this value, you will increase the heat output of the glow plugs for all of Phase 2.

Third is `COLD_ENGINE_TOTAL_MS` (and `HOT_ENGINE_TOTAL_MS`).  This is the time of the entire heat cycle (Phase 1 + Phase2).  By default `COLD_ENGINE_TOTAL_MS` is 15000, meaing that the entire heat cycle is 15 seconds, so subtract out `FULL_POWER_DURATION_MS` and that tells the duration to run at the reduced duty cycle.  The defaults for cold will be 5 seconds at full power, then 10 seconds at reduced.    You can get 5 seconds more of reduced power by increasing the `FULL_POWER_DURATION_MS` by 5000.

## License

MIT License - See main project LICENSE file for details.

© 2025 Chris Tacke
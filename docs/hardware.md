# Inventario de hardware: Raspberry Pi + IMAT HAT

Cada dato se clasifica como:

- **Confirmado**: aparece explícitamente en una fuente.
- **Deducido**: se obtiene razonablemente de datos confirmados.
- **Pendiente**: no hay evidencia suficiente.

## Fuentes

| Id | Documento | Qué aporta |
|----|-----------|------------|
| F1 | *Sistemas Electrónicos*, Tema 3 "Entradas y Salidas Digitales" (curso 2025/2026, ICAI) | Pinout de la Pi, límites eléctricos, bibliotecas, ejemplos con LEDs y pulsadores, render 3D (KiCad) de la IMAT HAT |

---

## 1. Raspberry Pi

### 1.1 Modelo

| Dato | Estado | Fuente / razonamiento |
|------|--------|-----------------------|
| Conector GPIO de 40 pines | Confirmado | F1, diap. 2 |
| Modelo Raspberry Pi 3 Model B | **Deducido** | La foto de F1 (diap. 2) muestra 4 USB y Ethernet, pero es una imagen ilustrativa |

### 1.2 Alimentación y límites eléctricos (Confirmado, F1 diap. 3)

| Pin | Límite |
|-----|--------|
| 3,3 V | 0,5 A máx. |
| 5 V | 1,5 A máx. |
| GPIO | 3,3 V, ~7 mA por pin |

**Advertencia:** conectar lógica de 5 V a un GPIO puede destruir la Pi.

### 1.3 Pinout del conector de 40 pines (Confirmado, F1 diap. 2)

| Físico | Función | Función | Físico |
|-------:|---------|---------|:-------|
| 1 | 3V3 | 5V | 2 |
| 3 | GPIO2 (I2C SDA) | 5V | 4 |
| 5 | GPIO3 (I2C SCL) | GND | 6 |
| 7 | GPIO4 | GPIO14 (UART TXD) | 8 |
| 9 | GND | GPIO15 (UART RXD) | 10 |
| 11 | GPIO17 | GPIO18 | 12 |
| 13 | GPIO27 | GND | 14 |
| 15 | GPIO22 | GPIO23 | 16 |
| 17 | 3V3 | GPIO24 | 18 |
| 19 | GPIO10 (SPI MOSI) | GND | 20 |
| 21 | GPIO9 (SPI MISO) | GPIO25 | 22 |
| 23 | GPIO11 (SPI SCLK) | GPIO8 (SPI CE0) | 24 |
| 25 | GND | GPIO7 (SPI CE1) | 26 |
| 27 | ID SD | ID SC | 28 |
| 29 | GPIO5 | GND | 30 |
| 31 | GPIO6 | GPIO12 | 32 |
| 33 | GPIO13 | GND | 34 |
| 35 | GPIO19 | GPIO16 | 36 |
| 37 | GPIO26 | GPIO20 | 38 |
| 39 | GND | GPIO21 | 40 |

Funciones compartidas (Confirmado):

- I2C: GPIO 2 y 3.
- SPI: GPIO 7 a 11.
- UART: GPIO 14 y 15.

**Nota para el simulador:** número físico ≠ número GPIO/BCM. Por ejemplo, GPIO7 es el pin físico 26.

---

## 2. Software

| Dato | Estado | Fuente |
|------|--------|--------|
| Se usan **RPi.GPIO** (bajo nivel) y **gpiozero** (alto nivel, orientada a componentes) | Confirmado | F1 diap. 6 |
| RPi.GPIO: `setmode(BCM/BOARD)`, `setup(pin, IN, PUD_UP)`, `setup(pin, OUT, initial=...)`, `input`, `output`, `cleanup` | Confirmado | F1 diap. 7 |
| gpiozero: numeración BCM; `Button` activa pull-up interna; `is_pressed`, `value`, `wait_for_press`, `when_pressed`, `when_released`; `LED.on/off/toggle/blink` (`blink()` a 0,5 Hz por defecto) | Confirmado | F1 diap. 9 |
| Estructura de programa: bucle de scan no bloqueante, o eventos + `pause()`; `try / except KeyboardInterrupt / finally` con liberación de pines | Confirmado | F1 diap. 4–5 |
| Biblioteca propia de la universidad para la HAT | Pendiente | No aparece en F1 |

---

## 3. IMAT HAT

### 3.1 Datos generales

| Dato | Estado | Fuente |
|------|--------|--------|
| Diseñada en KiCad, con logotipo de Comillas ICAI | Confirmado | Render en F1 diap. 2 |
| Se monta sobre el conector de 40 pines | Deducido | Conector hembra de 2×20 en la capa inferior del render |
| Revisión, esquema, manual | Pendiente | No disponibles |

### 3.2 Componentes con función y GPIO conocidos

| Componente | Cantidad | GPIO (BCM) | Lógica | Estado |
|------------|---------:|------------|--------|--------|
| LED | 8 | 20, 21, 22, 23, 24, 25, 26, 27 | Activo a nivel alto | Confirmado (F1, ejemplos diap. 15–21) |
| Pulsador | 4 | 7, 16, 17, 19 | Activo a nivel bajo, sin resistencia externa, con pull-up interna | Confirmado (F1 diap. 12, 17, 21) |

Comportamiento eléctrico de los pulsadores (Deducido de "pull-up interna" y "activo a nivel bajo"):

- En reposo, la pull-up lleva el pin a 3,3 V y se lee HIGH.
- Al pulsar, el pin queda unido a GND y se lee LOW.

Por tanto, `is_pressed == True` equivale a leer LOW.

### 3.3 Correspondencia con las referencias de la placa (Deducido)

| Referencia en el render | Interpretación | Por qué |
|-------------------------|----------------|---------|
| SW1–SW4 | Los 4 pulsadores | 4 pulsadores físicos, 4 GPIO de pulsador en F1 |
| D1–D8 con R1–R8 | Los 8 LEDs con su resistencia limitadora | 8 LEDs en serie con resistencia, 8 GPIO de LED en F1 |

Pendiente:

- Qué SWn corresponde a qué GPIO.
- Qué Dn corresponde a qué GPIO.
- El valor de R1–R8.

### 3.4 Componentes vistos en el render, función desconocida (Pendiente)

| Referencia | Qué se ve | Hipótesis (sin confirmar) |
|------------|-----------|---------------------------|
| RV1 | Potenciómetro / trimmer azul | Entrada analógica; necesitaría ADC externo (la Pi no tiene ADC) |
| U1 | Circuito integrado SMD | ¿ADC u otro periférico (I2C/SPI)? |
| U2 | Zócalo DIP vacío | Para un integrado insertable en alguna práctica |
| U3, U4 | Circuitos integrados | Desconocido |
| Q1 | Componente de 3 terminales | ¿Transistor? |
| 3 borneras verdes (lado derecho) | Conectores de tornillo | ¿Alimentación externa, motores o servos? |
| JP1, JP2, JP3 | Jumpers; serigrafía GPIO22, GPIO23 (JP1) y GPIO27 (JP2) | ¿Desconectar LEDs para reutilizar esos GPIO? |
| J2, J12, P1 y otras tiras de pines | Conectores | Desconocido |
| D9–D16 (cara inferior) | Diodos o LEDs | Desconocido |
| R10, R11, C5–C10 | Pasivos | Desconocido |

---

## 4. Cómo completar lo pendiente

1. **Esquema KiCad o manual de la IMAT HAT.** Resuelve casi todo lo pendiente.
2. **Siguientes temas de Sistemas Electrónicos** (PWM, ADC, buses, motores). Probablemente usan RV1, U1, U2 y las borneras.
3. **Foto real de la placa** en la que se lean las referencias de los chips.

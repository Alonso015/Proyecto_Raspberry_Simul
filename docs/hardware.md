# Inventario de hardware: Raspberry Pi + IMAT HAT

Cada dato se clasifica como:

- **Confirmado**: aparece explícitamente en una fuente.
- **Deducido**: se obtiene razonablemente de datos confirmados.
- **Pendiente**: no hay evidencia suficiente.

## Fuentes

Todas son de la asignatura *Sistemas Electrónicos* (ICAI, curso 2025/2026).

| Id | Documento | Qué aporta |
|----|-----------|------------|
| F1 | Diapositivas, Tema 3 "Entradas y Salidas Digitales" | Pinout de la Pi, límites eléctricos, bibliotecas, render 3D (KiCad) de la HAT |
| F2 | Práctica 3 "Entradas y salidas digitales (GPIO)" | LEDs y pulsadores de la HAT, conflicto GPIO7/SPI, MCP3008 |
| F3 | Práctica 4 "Temporizadores y PWM" | Servo y conector DIG (SERVO), L293D en U2, MOTOR 1, POWER (EXT) |
| F4 | Práctica 5 "Conversor A/D y acondicionamiento de sensores" | MCP3008, bloque ANALOG IN, potenciómetro en CH7, límite de corriente GPIO |
| F5 | Apuntes de la asignatura (`Apuntes_SE_IMAT.pdf`) | BCM2711, PWM hardware → motores, SPI0/SPI1 en la HAT, condensadores antirrebote |
| F6 | Diapositivas, Tema 4a "Temporizadores y PWM" | Clases `Servo` / `AngularServo`, `pigpio` |
| F7 | Fotografía de la placa real montada sobre la Pi (aportada por el alumno, 12/11/2026) | Serigrafía: correspondencia de pulsadores y LEDs con sus GPIO, C1–C4, F1, chip montado, zócalo vacío |

**Documento clave aún no disponible:** el esquema de la IMAT HAT, publicado en Moodle (sección de laboratorio, según F2).

---

## 1. Raspberry Pi

### 1.1 Modelo

| Dato | Estado | Fuente / razonamiento |
|------|--------|-----------------------|
| SoC BCM2711 | Confirmado | F5 (sección PWM por hardware) |
| Modelo **Raspberry Pi 4** | **Deducido** | El BCM2711 es el SoC de la Pi 4. La foto de F1 (aspecto de Pi 3B) es solo ilustrativa |
| Conector GPIO de 40 pines | Confirmado | F1 |
| Nombre de host del laboratorio `imat-se-XXX` | Confirmado | F5 (prompts del terminal) |

### 1.2 Alimentación y límites eléctricos

| Dato | Valor | Estado / fuente |
|------|-------|-----------------|
| Pin 3,3 V | 0,5 A máx. | Confirmado, F1 |
| Pin 5 V | 1,5 A máx. | Confirmado, F1 |
| Nivel lógico GPIO | 3,3 V | Confirmado, F1 |
| Corriente por GPIO | **Discrepancia**: F1 dice ~7 mA; F4 dice "no más de 4 mA" | Pendiente: decidir qué límite usar en el simulador (el más conservador es 4 mA) |

**Advertencias** (Confirmado):

- Conectar lógica de 5 V a un GPIO puede destruir la Pi (F1).
- Con fuentes externas, unir siempre la masa de la fuente con la de la Pi (F4).

### 1.3 Pinout del conector de 40 pines (Confirmado, F1)

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
- SPI0: GPIO 7 a 11.
- UART: GPIO 14 y 15.

**Nota para el simulador:** número físico ≠ número GPIO/BCM. Por ejemplo, GPIO7 es el pin físico 26.

### 1.4 Periféricos del SoC relevantes (Confirmado, F5)

| Periférico | Pines | Notas |
|------------|-------|-------|
| PWM hardware, canal 0 | GPIO12 o GPIO18 | En la HAT se usa GPIO18 (función 2) |
| PWM hardware, canal 1 | GPIO13 o GPIO19 | En la HAT se usa GPIO13 (función 4) |
| SPI0 | MOSI GPIO10, MISO GPIO9, SCLK GPIO11, CE0 GPIO8, CE1 GPIO7 | Se habilita con `raspi-config` |
| SPI1 | MOSI GPIO20, MISO GPIO19, SCLK GPIO21, CE0 GPIO18, CE1 GPIO17, CE2 GPIO16 | Se habilita con `dtoverlay=spi1-3cs` |

Configuraciones:

- PWM hardware: `dtoverlay=pwm-2chan,pin=18,func=2,pin2=13,func2=4` en `config.txt`.
- Si se activan a la vez el PWM y SPI1, falla el que se active en segundo lugar, porque comparten GPIO18.

---

## 2. Software

| Dato | Estado | Fuente |
|------|--------|--------|
| **RPi.GPIO** (bajo nivel): `setmode`, `setup(..., pull_up_down=PUD_UP/PUD_DOWN)`, `input`, `output`, `cleanup`, `add_event_detect(RISING/FALLING, callback)` | Confirmado | F1, F2 |
| **gpiozero** (alto nivel, numeración BCM): `LED`, `PWMLED`, `LEDBoard`, `Button` (pull-up por defecto; `is_pressed`, `value`, `when_pressed`, `when_released`, `wait_for_press`), `MCP3008` (`value` 0–1, `voltage`), `Servo`, `AngularServo`, `OutputDevice`, `InputDevice`, `PWMOutputDevice` | Confirmado | F1–F6 |
| Factoría de pines **pigpio** (`PiGPIOFactory`, requiere `sudo pigpiod`) para PWM sin jitter | Confirmado | F3, F5, F6 |
| **rpi-hardware-pwm** (`HardwarePWM(pwm_channel, hz)`, `start`, `stop`, `change_duty_cycle`, `change_frequency`) para motores DC. Factor de corrección de frecuencia 0,933 | Confirmado | F3, F5 |
| **spidev** para acceso SPI de bajo nivel | Confirmado | F5 |
| Temporización con `sleep` (polling) y sin bloquear con `threading.Timer` / `sched` | Confirmado | F3 |
| Estructura: bucle de scan no bloqueante, o eventos + `pause()`; `try / except KeyboardInterrupt / finally` | Confirmado | F1, F2 |
| Detección por nivel vs. por flanco (comparar valor actual con anterior, o callbacks) | Confirmado | F2 |
| Biblioteca propia de la universidad para la HAT | Pendiente; no aparece en ninguna fuente, solo bibliotecas estándar | — |

---

## 3. IMAT HAT

### 3.1 Datos generales

| Dato | Estado | Fuente |
|------|--------|--------|
| Diseñada en KiCad, con logotipo de Comillas ICAI | Confirmado | Render en F1 |
| Se monta sobre el conector de 40 pines | Deducido | Conector hembra 2×20 en la capa inferior del render |
| Parte de los componentes los sueldan los alumnos en la primera práctica (MCP3008, L293D en zócalo, pull-up de pulsadores opcional) | Confirmado | F2, F3, F4 |
| Revisión de la placa | Pendiente | — |

### 3.2 Entradas y salidas digitales integradas

| Componente | Cantidad | GPIO (BCM) | Circuito / lógica | Estado |
|------------|---------:|------------|-------------------|--------|
| LED | 8 | 20, 21, 22, 23, 24, 25, 26, 27 | Pull-down; activos a nivel alto | Confirmado (F2) |
| Pulsador | 4 | 7, 16, 17, 19 | Pull-up; activos a nivel bajo. La resistencia de pull-up no se suelda: se usa la interna | Confirmado (F2) |
| Condensador antirrebote en cada pulsador | 4 | Del pin del pulsador a GND | Filtra rebotes | Confirmado (F5) |

Correspondencia de referencias de la placa con los GPIO (Confirmado, serigrafía en F7):

| Pulsador | GPIO | | LED | GPIO | | LED | GPIO |
|----------|------|-|-----|------|-|-----|------|
| SW1 | GPIO7 | | D1 | GPIO20 | | D5 | GPIO24 |
| SW2 | GPIO16 | | D2 | GPIO21 | | D6 | GPIO25 |
| SW3 | GPIO17 | | D3 | GPIO22 | | D7 | GPIO26 |
| SW4 | GPIO19 | | D4 | GPIO23 | | D8 | GPIO27 |

- Cada LED Dn lleva su resistencia Rn (R1–R8).
- **C1–C4** están junto a los pulsadores. Deducido: son los 4 condensadores antirrebote de F5.

Comportamiento del pulsador (Deducido de "pull-up" y "activo a nivel bajo"):

| Estado | Nivel en el pin | `is_pressed` |
|--------|-----------------|--------------|
| En reposo | HIGH | `False` |
| Pulsado | LOW | `True` |

### 3.3 Conversor A/D: MCP3008 (Confirmado, F4, F5)

| Dato | Valor |
|------|-------|
| Canales | 8 (CH0–CH7) |
| Resolución | 10 bits (0–1023) |
| Referencia | 3,3 V (`value = 1` ↔ 3,3 V) |
| Bus | SPI0, chip enable CE0 (GPIO8) |
| Bloque en la placa | **ANALOG IN (MCP3008)** |
| Canales libres para el usuario | CH0–CH6 |
| CH7 | Ocupado por el **potenciómetro** de la placa |

Referencias en la placa:

- **RV1** es el potenciómetro de CH7. Deducido: es el único potenciómetro visible (F1, F7).
- **Encapsulado:** en F7 hay un chip DIP de 16 pines montado junto a RV1, y el zócalo inferior de 2×8 (U2, el del L293D) está vacío. Deducido: el chip montado es el MCP3008 (DIP-16), que se suelda en la primera práctica.
- Referencia exacta (¿U1?) y texto del chip: Pendiente.

### 3.4 Control de motores: L293D (Confirmado, F3, F5)

| Dato | Valor |
|------|-------|
| Chip | L293D (puente en H doble), insertado en el **zócalo U2**. En F7 el zócalo está vacío: se coloca solo cuando hace falta |
| MOTOR 1: velocidad | PWM hardware canal 0 → **GPIO18** |
| MOTOR 1: sentido | **GPIO4 y GPIO5**; solo uno a nivel alto a la vez |
| MOTOR 2: velocidad | PWM hardware canal 1 → **GPIO13** |
| MOTOR 2: sentido | Pendiente (F3 remite al esquema) |
| Conexión del motor | Bornera **MOTOR 1** / **MOTOR 2**, terminales 1Y y 2Y (la polaridad solo invierte el sentido) |
| Alimentación de los motores | Bornera **POWER (EXT)**; 9 V desde fuente de laboratorio en F3 |
| Frecuencia PWM típica | ≥ 20 kHz (fuera del rango audible); requiere PWM hardware |

Deducido: las **tres borneras verdes** del render son MOTOR 1, MOTOR 2 y POWER (EXT). Qué bornera es cuál, Pendiente.

### 3.5 Conector de servo: DIG (SERVO) (Confirmado, F3)

| Dato | Valor |
|------|-------|
| GPIO de control | **GPIO14** |
| Disposición del conector triple | Señal en el pin interior, **5 V** en el central, **GND** en el exterior |
| Colores del cable del servo | Blanco o amarillo = señal PWM; rojo = 5 V; negro = GND |
| Servos del laboratorio | Pulso de **0,5 ms (0°) a 2,5 ms (180°)**, periodo 20 ms (50 Hz) |
| En `AngularServo` por defecto | Rango −90° a 90° |

### 3.6 Conflictos de uso de pines

Son relevantes para la validación de conexiones del simulador (Confirmado, F2, F5).

| Conflicto | Consecuencia |
|-----------|--------------|
| GPIO7 = pulsador **y** SPI0 CE1 | Para usar el pulsador de GPIO7 hay que **deshabilitar SPI**; entonces el MCP3008 no funciona |
| GPIO18 = PWM motor 1 **y** SPI1 CE0 | En la HAT, SPI1 CE0 no está disponible |
| SPI1 usa GPIO16, 17, 19 (pulsadores) y GPIO20, 21 (LEDs) | Usar SPI1 interfiere con LEDs y pulsadores. Por los condensadores antirrebote, SPI1 se limita a unos **100 kHz** |
| La HAT solo ofrece 2 señales de selección SPI | Máximo 2 esclavos en SPI1 |

### 3.7 Componentes del render aún sin identificar (Pendiente)

| Referencia | Qué se ve | Hipótesis (sin confirmar) |
|------------|-----------|---------------------------|
| U1 | Integrado | ¿MCP3008? (ver §3.3) |
| U3 (con C9, C10), U4 | Integrados | ¿Regulador de tensión? |
| F1 | Junto a RV1 (F7) | Por la letra F, probablemente un fusible de protección de la alimentación |
| Q1 | Componente de 3 terminales | ¿Transistor? |
| JP1, JP2, JP3 | Jumpers; serigrafía GPIO22, GPIO23 (JP1) y GPIO27 (JP2) | ¿Desconectar LEDs para reutilizar esos GPIO? |
| J2, J12, P1 y otras tiras de pines | Conectores | ¿Uno de ellos es el DIG (SERVO)? |
| D9–D16 (cara inferior) | Diodos o LEDs | Desconocido |
| R10, R11, C5–C10 | Pasivos | Desconocido |
| Valor de R1–R8 | Resistencias de los LEDs | Desconocido |
| Etiquetas de borneras y bloques (ANALOG IN, DIG (SERVO)) | No se leen en F7 | Desconocido |

---

## 4. Componentes externos usados en el laboratorio

Se montan en la protoboard, no en la HAT. Son candidatos a componentes "añadibles" en el simulador (Confirmado, F3, F4 y proyecto de la asignatura).

| Componente | Uso | Conexión |
|------------|-----|----------|
| Pulsador externo en pull-up | Entrada digital | GPIO14 (F2) |
| Servomotor de posición | Actuador PWM 50 Hz | DIG (SERVO), GPIO14 |
| Motor DC | Actuador vía L293D | MOTOR 1 / MOTOR 2 |
| Fotorresistencia (LDR) GL5-05AP02 | Sensor de luz: R10 = 3–20 kΩ, oscuridad > 0,5 MΩ, γ ≈ 0,6 | Acondicionada → MCP3008 |
| Amplificador operacional MCP6002 | Acondicionamiento de la LDR (rail-to-rail, doble) | Protoboard |
| Darlington BDX33C + bombilla 12 V / 5 W | Actuador de potencia por PWM | GPIO14 + fuente externa de 12 V |
| Sensores FSR serie 400 | Fuerza (divisor de tensión) | MCP3008 |
| Sensor IR de distancia GP2Y0A21YK0F | Distancia / presencia | MCP3008 |
| Micrófono capacitivo | Sonido | MCP3008 |
| Buzzer | Alarma acústica por PWM | GPIO (en el proyecto, GPIO15) |

---

## 5. Cómo completar lo pendiente

1. **Esquema de la IMAT HAT de Moodle** (sección de laboratorio de Sistemas Electrónicos). Resuelve casi todo lo de la sección 3.7 y los pines de sentido del MOTOR 2.
2. **Fotos cenitales de cerca**: de las borneras y conectores (cuál es MOTOR 1, MOTOR 2, POWER, DIG (SERVO), ANALOG IN) y del texto del chip DIP montado.
3. **Límite de corriente GPIO**: decidir entre 4 mA y 7 mA para las validaciones del simulador.

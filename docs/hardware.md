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
| F8 | **Esquema oficial** "Tarjeta de expansión iMAT HAT", rev. v1.1, 30/11/2022 (KiCad, 2 hojas; aportado por el alumno, 17/11/2026) | Conexiones completas de la placa: valores de componentes, conectores, puentes, motores, servos, alimentación |

El esquema oficial (F8) ya está incorporado. Es la fuente de mayor rango para todo lo relativo a la HAT.

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
| Corriente por GPIO | **Discrepancia**: F1 dice ~7 mA; F4 dice "no más de 4 mA" | **Decidido para el simulador (17/11/2026): 7 mA** (valor de F1). Los 4 mA de F4 se descartan como umbral porque los LEDs de la propia HAT consumen ≈ 4,8 mA (§3.2) |

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

Fuente principal: **F8**, el esquema oficial. Casi todos los datos de esta sección pasan a estar **confirmados**. Donde F8 coincide con fuentes anteriores, se indica.

### 3.1 Datos generales (Confirmado, F8)

| Dato | Valor |
|------|-------|
| Nombre | "Tarjeta de expansión iMAT HAT", Comillas ICAI |
| Revisión | **v1.1**, 30/11/2022; KiCad 6.0.10 (`ImatHat.kicad_sch` + hoja `BackPower.kicad_sch`) |
| Conector a la Pi | **P1**, conector de 2×20 (pinout de §1.3). Para apilar otro HAT encima se puede usar un conector Samtec ESQ-120-14-T-D |
| ID_SD / ID_SC (pines 27 y 28) | Sin conectar: reservados para la EEPROM de identificación del HAT |
| Fijación | 4 orificios de montaje M2.5 (MK1–MK4) |
| Punto de prueba | **TP1**, conectado a GND, para la masa del osciloscopio |
| Puentes de soldadura | **JP1, JP2, JP3**: se cortan para liberar pines; se vuelven a unir con un punto de soldadura (§3.7) |

### 3.2 LEDs (Confirmado, F8; coincide con F2 y F7)

Circuito de cada LED:

```
GPIO ── Rn (270 Ω) ── ánodo Dn ── cátodo ── JP1 o JP2 ── GND
```

| LED | GPIO | Resistencia | Retorno a GND |
|-----|------|-------------|---------------|
| D1 | GPIO20 (SPI1_MOSI) | R1 = 270 Ω | JP1 |
| D2 | GPIO21 (SPI1_SCK) | R2 = 270 Ω | JP1 |
| D3 | GPIO22 | R3 = 270 Ω | JP1 |
| D4 | GPIO23 | R4 = 270 Ω | JP1 |
| D5 | GPIO24 | R5 = 270 Ω | JP2 |
| D6 | GPIO25 | R6 = 270 Ω | JP2 |
| D7 | GPIO26 | R7 = 270 Ω | JP2 |
| D8 | GPIO27 | R8 = 270 Ω | JP2 |

- Activos a nivel alto: con el GPIO en HIGH circula corriente del pin a GND a través de la resistencia y el LED.
- Los cátodos de D1–D4 van juntos a GND a través de **JP1**, y los de D5–D8 a través de **JP2**.
- El conector **J11** (8 pines) saca GPIO20–27 en orden (pin 1 = GPIO20 … pin 8 = GPIO27).

**Corriente de cada LED (Deducido).** Con 3,3 V en el pin y una tensión directa del LED de unos 2 V (supuesto):

I ≈ (3,3 − 2) / 270 ≈ **4,8 mA**

Con los supuestos del simulador (resistencia de salida del GPIO 40 Ω, LED con V_F = 2 V y R_S = 10 Ω) sale (3,3 − 2) / (40 + 270 + 10) ≈ **4,06 mA** (test T1 de la E3). En ambos casos es **más que los 4 mA** que F4 da como límite del GPIO, y menos que los 7 mA de F1. Como la propia placa trabaja así, los 4 mA deben entenderse como una recomendación conservadora, no como el umbral a partir del cual el pin se daña. Por eso el simulador usa 7 mA (§1.2).

### 3.3 Pulsadores (Confirmado, F8; coincide con F2, F5 y F7)

| Pulsador | GPIO | Condensador |
|----------|------|-------------|
| SW1 | GPIO7 (SPI0 CE1) | C1 = 100 nF a GND |
| SW2 | GPIO16 | C2 = 100 nF a GND |
| SW3 | GPIO17 | C3 = 100 nF a GND |
| SW4 | GPIO19 (SPI1_MISO) | C4 = 100 nF a GND |

- Cada pulsador une su GPIO con **GND** al pulsarlo.
- **No hay resistencias de pull-up en la placa:** se usa la interna. La nota del esquema lo dice: gpiozero configura los pulsadores con pull-up por defecto.
- Resultado: en reposo se lee HIGH y al pulsar se lee LOW (activo a nivel bajo).
- El conector **J3** (4 pines) saca GPIO7, 16, 17 y 19.

### 3.4 Conversor A/D: MCP3008 = U1 (Confirmado, F8; coincide con F4 y F5)

| Dato | Valor |
|------|-------|
| Referencia | **U1** = MCP3008. Resuelve la duda de F7 |
| Alimentación | Vdd (pin 16) y **Vref (pin 15) = 3,3 V**. Desacoplo: C5 = 1 µF, C6 = 100 nF |
| Masas | AGND (14) y DGND (9) a GND |
| SPI0 | CLK ← GPIO11, Dout → GPIO9 (MISO), Din ← GPIO10 (MOSI), CS/SHDN ← **GPIO8 (CE0)** |
| Entradas CH0–CH6 | Conector **J1** (8 pines), pines 1–7 |
| Entrada CH7 | Por el puente de 3 posiciones **JP3**: a **RV1** (potenciómetro de 10 kΩ entre 3,3 V y GND, con el cursor a CH7) o al pin 8 de J1 |

Posición de fábrica de JP3: CH7 conectado a RV1. Es **Deducido**: F4 y F5 dicen que el potenciómetro está en CH7.

### 3.5 Motores DC: L293 = U2 (Confirmado, F8)

| Señal del L293 | Pin | GPIO / conexión |
|----------------|-----|-----------------|
| 1A | 2 | **GPIO4** |
| 2A | 7 | **GPIO5** |
| EN1,2 | 1 | **GPIO18** (PWM0) |
| 3A | 10 | **GPIO6** |
| 4A | 15 | **GPIO12** |
| EN3,4 | 9 | **GPIO13** (PWM1) |
| 1Y, 2Y | 3, 6 | Bornera **J5 = Motor 1** |
| 3Y, 4Y | 11, 14 | Bornera **J6 = Motor 2** |
| VCC1 (lógica) | 16 | +5 V (desacoplo C7 = 100 nF) |
| VCC2 (motores) | 8 | **Vmot**, desde la bornera **J4 = alimentación de motores** (desacoplo C8 = 100 nF) |
| GND | 4, 5, 12, 13 | GND |

Notas:

- **Resuelve el pendiente:** los pines de sentido del MOTOR 2 son **GPIO6 y GPIO12**.
- **D9–D16** son diodos de protección (rueda libre) de las salidas. Según la nota del esquema, si se usa el L293**D** no hace falta montarlos, porque ese chip ya los lleva.
- Las borneras son enchufables, modelo WE 691351700002.
- Las tres borneras verdes de la foto (F7) son J4, J5 y J6. **Deducido.**

### 3.6 Conectores (Confirmado, F8)

| Conector | Pines | Uso |
|----------|-------|-----|
| **J7** | 1 = GPIO14 (TXD0), 2 = +5 V, 3 = GND | Servo |
| **J8** | 1 = GPIO15 (RXD0), 2 = +5 V, 3 = GND | Servo |
| **J9** | 1 = GPIO22, 2 = +5 V, 3 = GND | Servo |
| **J10** | 1 = GPIO23, 2 = +5 V, 3 = GND | Servo |
| **J2** | 1 = +3,3 V, 2 = GPIO3 (SCL1), 3 = GPIO2 (SDA1), 4 = GND | I2C |
| **J12** | 1 = +5 V, 2 = GND, 3 = +3,3 V, 4 = GND | Alimentación de circuitos externos |
| **J1** | CH0–CH6 (+ CH7 vía JP3) | Entradas analógicas (§3.4) |
| **J3** | GPIO7, 16, 17, 19 | Acceso a los pulsadores (§3.3) |
| **J11** | GPIO20–27 | Acceso a los LEDs (§3.2) |
| **J4 / J5 / J6** | — | Alimentación de motores / Motor 1 / Motor 2 (§3.5) |

**Servos: hay 4 conectores, no solo uno.**

- En la práctica 4 (F3) se usa el de **GPIO14**.
- El pin de señal es el 1. Encaja con F3 (señal en el pin interior, 5 V en el central, GND en el exterior). Es **Deducido** que el pin 1 es el interior.
- **J9 y J10 comparten GPIO22 y GPIO23 con los LEDs D3 y D4.** Para usar esos servos sin que los LEDs carguen el pin hay que cortar JP1, lo que apaga D1–D4.

**I2C, advertencia del esquema:** la Pi lleva internamente resistencias de pull-up a 3,3 V. Si se conecta un esclavo con pull-ups a 5 V, lo más probable es que la Pi se dañe.

### 3.7 Puentes de soldadura (Confirmado, F8)

| Puente | Qué desconecta al cortarlo | Para qué |
|--------|----------------------------|----------|
| JP1 | Retorno a GND de D1–D4 | Liberar GPIO20–23 (SPI1, servos J9/J10) |
| JP2 | Retorno a GND de D5–D8 | Liberar GPIO24–27 |
| JP3 (3 posiciones) | Elige qué va a CH7: RV1 o el pin 8 de J1 | Usar CH7 como entrada externa |

Corrección: la etiqueta "GPIO22, GPIO23" junto a JP1 en la serigrafía (F7) no significa que JP1 afecte solo a esos dos pines. Afecta a D1–D4.

### 3.8 Alimentación de respaldo, "BackPower" (Confirmado, F8 hoja 2)

**Montaje opcional, "solo para el proyecto".** Permite alimentar la Pi desde la fuente de los motores:

```
Vmot ── F1 (fusible) ── U3 (convertidor DC-DC) ── Q1 + U4 (diodo ideal) ── +5 V de la Pi
```

| Componente | Referencia | Función |
|------------|------------|---------|
| F1 | Fusible | Protege la entrada Vmot |
| U3 | OKX-T/5-D12N-C | Convertidor DC-DC. Salida ajustada con R9 = 1,472 kΩ (0,5 %, 100 ppm/°C). Desacoplo C9 y C10 = 10 µF. Pin On/Off sin conectar → siempre encendido |
| Q1 | DMG2305UX (MOSFET P; alternativa SI2305B-TP) | Diodo ideal: impide que la corriente vuelva hacia el convertidor |
| U4 | DMMT5401 (par de transistores) | Control del diodo ideal, con R10 = 10 kΩ y R11 = 47 kΩ |

Es **Deducido** que U3 da 5 V, porque alimenta el raíl +5 V de la Pi.

Para el simulador es irrelevante, salvo para explicar qué son F1, U3, U4 y Q1 si aparecen en el modelo visual de la placa.

### 3.9 Conflictos de uso de pines

Son relevantes para la validación del simulador. Se confirman con F8 y se añaden los nuevos.

| Conflicto | Consecuencia |
|-----------|--------------|
| GPIO7 = SW1 **y** SPI0 CE1 | Para usar SW1 hay que deshabilitar SPI; entonces el MCP3008 no funciona (F2) |
| GPIO18 = EN del Motor 1 **y** SPI1 CE0 | En la HAT, SPI1 CE0 no está disponible (F5) |
| SPI1 = GPIO19 (SW4), GPIO20 (D1), GPIO21 (D2), GPIO16 (SW2), GPIO17 (SW3) | Usar SPI1 interfiere con LEDs y pulsadores; además los condensadores C2–C4 lo limitan a unos 100 kHz (F5) |
| GPIO22 / GPIO23 = D3 / D4 **y** servos J9 / J10 | Para usar esos servos conviene cortar JP1 |
| GPIO12 = sentido del Motor 2 (4A) | Es también una salida posible de PWM0. Con el overlay de F3/F5, PWM0 sale por GPIO18, así que no hay choque |
| GPIO14 / GPIO15 = servos J7 / J8 **y** UART | Si la UART (consola serie) está activa, esos pines no están libres. Es **Deducido**; las fuentes no lo tratan |

### 3.10 Lo que sigue pendiente

| Dato | Estado |
|------|--------|
| Tensión directa y color de D1–D8 | Pendiente; el esquema solo dice "LED". Se usa ~2 V como supuesto |
| Posición de fábrica de JP3 | Deducido (RV1 a CH7), no escrito en el esquema |
| Qué pin físico del conector de servo es el "interior" | Deducido (pin 1 = señal), por F3 |
| Si en el laboratorio se monta la parte BackPower | Desconocido; F8 la marca como opcional |

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

Con el esquema (F8) quedan muy pocos datos abiertos (§3.10). Además:

1. **Tensión directa de D1–D8:** midiéndola o con el modelo del LED si aparece en Moodle.
